using System;
using Alesa.Core.AI;
using Alesa.Core.Graph;

namespace Alesa.HiveMind
{
    /// <summary>
    /// The hive mind itself: one brain deciding how many minions should exist, where they should be
    /// waiting, and when the boss should appear.
    ///
    /// It owns the four pieces and does nothing clever on its own, which is the point — each part
    /// is separately testable and separately tunable:
    ///
    ///   <see cref="Heat"/>     what the hive believes about the player, from sound alone
    ///   <see cref="Intensity"/> how much pressure the player is actually under
    ///   <see cref="Pacing"/>    the build-up / peak / fade / rest cycle that gives the game rhythm
    ///   <see cref="Ambush"/>    where an encounter should be staged
    ///
    /// The director is deliberately omniscient about the player's position while the minions are
    /// not. That asymmetry is what lets the hive seem to anticipate the player without any
    /// individual minion cheating: the director puts them somewhere plausible, and from then on
    /// they hunt purely by ear.
    ///
    /// Pure C#; no engine types. The Unity adapter feeds it and acts on its events.
    /// </summary>
    public sealed class HiveMindDirector
    {
        public HiveHeatMap Heat { get; }
        public IntensityModel Intensity { get; }
        public PacingStateMachine Pacing { get; }
        public AmbushScorer Ambush { get; }

        private readonly MazeGraph _graph;
        private readonly SharedBlackboard _blackboard;

        /// <summary>Most minions allowed at full pressure. The pacing budget scales this down.</summary>
        public int MaxActiveMinions { get; set; } = 4;

        /// <summary>
        /// How often a staged-but-unreached ambush is reconsidered. The player may have turned
        /// around, in which case waiting in the old spot forever is just an empty corridor.
        /// </summary>
        public float RestageIntervalSeconds { get; set; } = 12f;

        /// <summary>Intensity the player must be under before the boss is worth unleashing.</summary>
        public float BossIntensityThreshold { get; set; } = 0.6f;

        /// <summary>How many minions should be alive right now, after the pacing budget.</summary>
        public int DesiredMinionCount { get; private set; }

        /// <summary>Where the hive currently intends to be waiting; invalid when it has no plan.</summary>
        public AmbushCandidate CurrentAmbush { get; private set; } = AmbushCandidate.None;

        /// <summary>Raised when a new staging point is chosen. The adapter moves or spawns minions.</summary>
        public event Action<AmbushCandidate> AmbushStaged;

        /// <summary>Raised when the tension cycle moves on. Useful for music and lighting stingers.</summary>
        public event Action<PacingState> PacingChanged;

        private float _lastStageTime;
        private bool _hasStaged;
        private bool _bossRequested;

        public HiveMindDirector(MazeGraph graph, SharedBlackboard blackboard)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));

            Heat = new HiveHeatMap(graph);
            Intensity = new IntensityModel();
            Pacing = new PacingStateMachine();
            Ambush = new AmbushScorer();

            // Re-stage the moment a new build up begins, rather than waiting out the interval.
            Pacing.StateChanged += OnPacingChanged;
        }

        /// <summary>Forwards a noise into the hive's memory. Wired to the NoiseBus by the adapter.</summary>
        public void Observe(NoiseEvent noise) => Heat.Observe(noise);

        /// <summary>
        /// One decision cycle. Called a few times a second, not every frame: none of this needs
        /// frame precision, and the Dijkstra floods inside are not free.
        /// </summary>
        /// <param name="now">Game time in seconds.</param>
        /// <param name="deltaTime">Time since the previous call.</param>
        /// <param name="inputs">What the director can see about the player's situation.</param>
        /// <param name="playerNode">Graph node the player is nearest to, or -1 if unknown.</param>
        public void Tick(float now, float deltaTime, PacingInputs inputs, int playerNode)
        {
            // Order matters. Heat first, because intensity reads it; pacing last, because it reads
            // intensity. Running these out of order would make every signal a frame stale.
            Heat.Tick(now);
            inputs.Heat = Heat.HottestHeat;
            Intensity.Tick(inputs, deltaTime);
            Pacing.Tick(Intensity.Intensity, deltaTime);

            DesiredMinionCount = (int)Math.Round(Pacing.PressureBudget * MaxActiveMinions);

            if (playerNode >= 0) UpdateStaging(now, playerNode);
            UpdateBoss(playerNode);
        }

        /// <summary>
        /// Chooses or re-chooses where the hive waits. Only during BuildUp: staging mid-peak would
        /// drop minions into a fight already in progress, and staging during Relax would undo the
        /// rest the player was promised.
        /// </summary>
        private void UpdateStaging(float now, int playerNode)
        {
            if (Pacing.State != PacingState.BuildUp)
            {
                // Leaving BuildUp retires the plan, so the next build up starts fresh.
                if (Pacing.State == PacingState.Relax) CurrentAmbush = AmbushCandidate.None;
                return;
            }

            bool due = !_hasStaged || now - _lastStageTime >= RestageIntervalSeconds;
            if (!due) return;

            var chosen = Ambush.Best(_graph, Heat, _blackboard.RecommendedRoute, playerNode, now);
            _lastStageTime = now;
            _hasStaged = true;

            // Nowhere acceptable right now: keep the previous plan rather than teleporting the
            // ambush to a bad spot. The player walking on will produce a better option shortly.
            if (!chosen.IsValid) return;
            if (chosen.Node == CurrentAmbush.Node) return;

            CurrentAmbush = chosen;
            Ambush.NoteUsed(chosen.Node, now);
            AmbushStaged?.Invoke(chosen);
        }

        /// <summary>
        /// Asks for the boss once the escape is under way and the player is already under real
        /// pressure. Gating on intensity rather than a timer means the boss arrives as an
        /// escalation of something, instead of interrupting a quiet moment.
        /// </summary>
        private void UpdateBoss(int playerNode)
        {
            if (_bossRequested || _blackboard.BossActive) return;
            if (!_blackboard.EscapePhaseActive) return;
            if (Intensity.Intensity < BossIntensityThreshold) return;

            // Prefer a node tagged as a boss trigger ahead of the player; fall back to the ambush
            // point, and finally to wherever the player is.
            int trigger = CurrentAmbush.IsValid ? CurrentAmbush.Node : playerNode;
            if (trigger < 0) return;

            _bossRequested = true;
            _blackboard.RequestBossAppearance(trigger);
        }

        private void OnPacingChanged(PacingState state)
        {
            // A fresh build up should pick a new spot immediately rather than inheriting the last.
            if (state == PacingState.BuildUp) _hasStaged = false;
            PacingChanged?.Invoke(state);
        }

        /// <summary>Full reset. Used on player death and scene reload.</summary>
        public void Reset()
        {
            Heat.Clear();
            Intensity.Reset();
            Pacing.Reset();
            Ambush.Clear();
            CurrentAmbush = AmbushCandidate.None;
            DesiredMinionCount = 0;
            _hasStaged = false;
            _bossRequested = false;
            _lastStageTime = 0f;
        }
    }
}
