using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;

namespace Alesa.Agents.BlindMinion
{
    public enum MinionState { Dormant, Alerted, Investigating, Hunting, Searching, Attacking }

    /// <summary>
    /// Agent 2 – Blind infected minion that hunts by sound.
    ///
    /// Perception: every noise is propagated through the maze graph by <see cref="AcousticModel"/>.
    /// The minion only hears what reaches it along corridors, so walls block sound and closed
    /// doors muffle it.
    ///
    /// Navigation: the minion does not walk straight at the noise. It retraces the path the
    /// sound took (Dijkstra predecessor chain), node by node, toward where it came from.
    /// A blocked door stops an investigating minion. A hunting minion re-routes around it with A*.
    ///
    /// Decision: a finite state machine
    ///   Dormant → Alerted (reaction delay) → Investigating | Hunting → Searching → Dormant
    ///   any → Attacking when the player is within reach (it is blind, not numb).
    /// Quiet noises lead to Investigating at walking pace. Gunshots or loud noises lead to Hunting at a sprint.
    ///
    /// Chain reaction: a minion screams when it starts hunting, and the scream is just another noise,
    /// so one gunshot can ripple through a ward.
    /// </summary>
    public sealed class BlindMinionBrain : IMobileBrain, IDisposable
    {
        private readonly AIContext _ctx;
        private readonly List<int> _route = new List<int>();

        // --- Tunables (exposed on the Unity adapter) ---
        public float HearingThreshold { get; set; } = 5f;
        public float HuntThreshold { get; set; } = 25f;
        public float ReactionTime { get; set; } = 0.6f;
        public float HuntMemory { get; set; } = 20f; // safety timeout: long enough to finish a detour round the loop
        public float SearchDuration { get; set; } = 4f;
        public float InvestigateSpeed { get; set; } = 1.8f;
        public float HuntSpeed { get; set; } = 5.5f;
        public float ArriveRadius { get; set; } = 1.0f;
        public float TouchRange { get; set; } = 1.2f;
        public float AttackRange { get; set; } = 1.8f;
        public float AttackCooldown { get; set; } = 1.5f;

        /// <summary>
        /// Loudness of the scream a minion lets out when it starts hunting. Other minions hear it like
        /// any other noise: close ones (perceived ≥ HuntThreshold) hunt and scream in turn, further ones
        /// only investigate. The ripple dies out by itself because loudness falls with distance and
        /// each minion screams once per hunt.
        /// </summary>
        public float ScreamLoudness { get; set; } = 30f;

        /// <summary>
        /// At or above this perceived loudness the minion knows exactly where the noise was. Below it,
        /// its guess is off by up to <see cref="MaxHearingError"/> metres, scaled by how faint it was:
        /// error radius = MaxHearingError × (1 − perceived / ClearHearingLoudness).
        /// A gunshot pinpoints you; a footstep two rooms away only says "somewhere over there".
        /// </summary>
        public float ClearHearingLoudness { get; set; } = 20f;
        public float MaxHearingError { get; set; } = 6f;

        /// <summary>
        /// Memory: hearing anything sets <see cref="Alertness"/> to 1, which then fades to 0 over this many
        /// seconds. While alert, the minion hears quieter sounds (threshold × (1 − <see cref="AlertSensitivity"/> × alertness))
        /// and reacts faster (reaction × (1 − <see cref="AlertReactionSpeedup"/> × alertness)).
        /// Sneaking past a minion you just disturbed is much harder than sneaking past a sleeping one.
        /// </summary>
        public float AlertnessMemory { get; set; } = 30f;
        public float AlertSensitivity { get; set; } = 0.6f;
        public float AlertReactionSpeedup { get; set; } = 0.5f;

        /// <summary>Seconds without getting closer to a target before treating it as reached (unreachable guess, blocked doorway).</summary>
        public float StuckTimeout { get; set; } = 2f;

        public AcousticModel Acoustics { get; } = new AcousticModel();

        /// <summary>Unique per minion, stamped on its screams so it never reacts to its own voice.</summary>
        public int Id { get; }
        private static int _nextId = 1;

        // --- State ---
        public MinionState State { get; private set; } = MinionState.Dormant;
        public MovementIntent Intent { get; private set; }

        /// <summary>Set every frame by the Unity adapter (or by tests).</summary>
        public Vector3 CurrentPosition { get; set; }

        public SoundField LastSound { get; private set; }
        public float LastPerceivedLoudness { get; private set; } = float.NegativeInfinity;
        public IReadOnlyList<int> Route => _route;
        public int RouteIndex { get; private set; }
        public bool HasFinalTarget { get; private set; }
        public Vector3 FinalTarget { get; private set; }

        /// <summary>0 = calm, 1 = just heard something. Decays linearly over <see cref="AlertnessMemory"/>.</summary>
        public float Alertness { get; private set; }

        public float EffectiveHearingThreshold => HearingThreshold * (1f - AlertSensitivity * Alertness);

        /// <summary>How far off (metres) the minion's guess of the last noise's position could be.</summary>
        public float HearingErrorRadius { get; private set; }

        public event Action<MinionState> StateChanged;
        public event Action Attacked;

        /// <summary>Raised when this minion screams (adapter plays the audio).</summary>
        public event Action Screamed;

        private float _clock;
        private float _stateEnteredAt;
        private float _lastHeardAt;
        private float _lastAttackAt = float.NegativeInfinity;
        private MinionState _stateAfterAlert;
        private float _reactionDelay;
        private int _routeEndNode = -1;
        private readonly Random _random;
        private Vector3 _progressTarget = new Vector3(float.NaN);
        private float _progressBest;
        private float _progressSince;

        private MazeGraph Graph => _ctx.Graph;

        /// <param name="seed">Seed for hearing error; fixed in tests, random in game.</param>
        public BlindMinionBrain(AIContext ctx, int seed = 1)
        {
            _ctx = ctx;
            Id = _nextId++;
            _random = new Random(seed);
            _ctx.Noise.NoiseEmitted += OnNoise;
        }

        public void Dispose() => _ctx.Noise.NoiseEmitted -= OnNoise;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;
            if (AlertnessMemory > 0f) Alertness = Math.Max(0f, Alertness - deltaTime / AlertnessMemory);

            var player = _ctx.World.PlayerPosition;
            float playerDistance = FlatMath.Distance(CurrentPosition, player);
            float reach = State == MinionState.Dormant ? TouchRange : AttackRange;
            if (State != MinionState.Attacking && playerDistance <= reach) Enter(MinionState.Attacking);

            switch (State)
            {
                case MinionState.Dormant:
                    Intent = MovementIntent.Idle;
                    break;

                case MinionState.Alerted:
                    Intent = LookTowards(CurrentWaypoint());
                    if (TimeInState >= _reactionDelay) Enter(_stateAfterAlert);
                    break;

                case MinionState.Investigating:
                    FollowRoute(InvestigateSpeed);
                    break;

                case MinionState.Hunting:
                    if (_clock - _lastHeardAt > HuntMemory)
                    {
                        Enter(MinionState.Searching);
                        break;
                    }
                    FollowRoute(HuntSpeed);
                    break;

                case MinionState.Searching:
                    Intent = MovementIntent.Idle;
                    if (TimeInState >= SearchDuration) Enter(MinionState.Dormant);
                    break;

                case MinionState.Attacking:
                    TickAttack(player, playerDistance);
                    break;
            }
        }

        private float TimeInState => _clock - _stateEnteredAt;

        // ------------------------------------------------------------------ perception

        private void OnNoise(NoiseEvent noise)
        {
            if (State == MinionState.Attacking) return;
            if (noise.SourceId == Id) return; // its own scream
            // A scream is second-hand news: it only helps a minion with no lead of its own. A hunter, or one
            // still reacting to the original noise (Alerted), already knows more than the screamer's position.
            if (noise.Type == NoiseType.Scream && (State == MinionState.Hunting || State == MinionState.Alerted)) return;

            var sound = Acoustics.Propagate(Graph, noise);
            int heardThrough = -1;
            float perceived = sound != null
                ? sound.PerceivedAt(Graph, CurrentPosition, out heardThrough)
                : noise.Loudness - FlatMath.Distance(CurrentPosition, noise.Position); // no graph: open-air fallback

            if (perceived < EffectiveHearingThreshold) return;

            LastSound = sound;
            LastPerceivedLoudness = perceived;
            float alertnessBefore = Alertness;
            Alertness = 1f;
            _lastHeardAt = _clock;
            BuildRoute(sound, heardThrough, GuessSourcePosition(noise.Position, perceived));

            bool alarming = noise.Type == NoiseType.Gunshot || perceived >= HuntThreshold;
            var desired = alarming || State == MinionState.Hunting ? MinionState.Hunting : MinionState.Investigating;

            switch (State)
            {
                case MinionState.Dormant:
                    _stateAfterAlert = desired;
                    _reactionDelay = ReactionTime * (1f - AlertReactionSpeedup * alertnessBefore);
                    Enter(MinionState.Alerted);
                    break;
                case MinionState.Alerted:
                    if (desired == MinionState.Hunting) _stateAfterAlert = MinionState.Hunting;
                    break;
                default:
                    Enter(desired);
                    break;
            }
        }

        /// <summary>The faint-sound guess: a point within the error radius around the true source.</summary>
        private Vector3 GuessSourcePosition(Vector3 truePosition, float perceived)
        {
            float clarity = Math.Min(1f, Math.Max(0f, perceived / ClearHearingLoudness));
            HearingErrorRadius = MaxHearingError * (1f - clarity);
            if (HearingErrorRadius <= 0f) return truePosition;

            double angle = _random.NextDouble() * 2.0 * Math.PI;
            float distance = HearingErrorRadius * (float)(0.5 + 0.5 * _random.NextDouble());
            return truePosition + new Vector3((float)Math.Cos(angle) * distance, 0f, (float)Math.Sin(angle) * distance);
        }

        /// <summary>Route = the sound's own path, walked backwards from where the minion heard it.</summary>
        private void BuildRoute(SoundField sound, int heardThrough, Vector3 noisePosition)
        {
            _route.Clear();
            RouteIndex = 0;
            FinalTarget = noisePosition;
            HasFinalTarget = true;
            _routeEndNode = -1;
            ResetProgress();
            if (sound == null || heardThrough < 0) return;

            for (int n = heardThrough; n != -1; n = sound.TowardSource(n)) _route.Add(n);
            _routeEndNode = _route[_route.Count - 1];

            // Already past the first node toward the second? Don't walk back to it.
            if (_route.Count >= 2)
            {
                var first = Graph[_route[0]].Position;
                var second = Graph[_route[1]].Position;
                if (FlatMath.Distance(CurrentPosition, second) < FlatMath.Distance(first, second)) RouteIndex = 1;
            }
        }

        // ------------------------------------------------------------------ movement

        private void FollowRoute(float speed)
        {
            while (RouteIndex < _route.Count)
            {
                int node = _route[RouteIndex];
                var nodePos = Graph[node].Position;
                float toNode = FlatMath.Distance(CurrentPosition, nodePos);
                if (toNode > ArriveRadius && !Stuck(nodePos, toNode))
                {
                    Intent = MovementIntent.MoveTo(nodePos, speed);
                    return;
                }

                if (RouteIndex + 1 < _route.Count)
                {
                    var edge = Graph.FindEdge(node, _route[RouteIndex + 1]);
                    if (edge != null && !edge.Traversable)
                    {
                        if (TryRerouteAround(node)) continue;
                        Enter(MinionState.Searching); // stuck at the door, listening
                        Intent = MovementIntent.Idle;
                        return;
                    }
                }
                RouteIndex++;
            }

            float toFinal = FlatMath.Distance(CurrentPosition, FinalTarget);
            if (HasFinalTarget && toFinal > ArriveRadius && !Stuck(FinalTarget, toFinal))
            {
                Intent = MovementIntent.MoveTo(FinalTarget, speed);
                return;
            }

            Enter(MinionState.Searching); // reached where the noise was; nothing here
            Intent = MovementIntent.Idle;
        }

        /// <summary>
        /// True when the minion hasn't got 0.25 m closer to <paramref name="target"/> for
        /// <see cref="StuckTimeout"/> seconds. A fuzzy guess can land inside a wall or behind a closed door,
        /// and the NavMesh will park the agent at the nearest reachable point instead. Without this
        /// the minion would stand there investigating forever.
        /// </summary>
        private void ResetProgress() => _progressTarget = new Vector3(float.NaN);

        private bool Stuck(Vector3 target, float distance)
        {
            if (target != _progressTarget)
            {
                _progressTarget = target;
                _progressBest = distance;
                _progressSince = _clock;
                return false;
            }
            if (distance < _progressBest - 0.25f)
            {
                _progressBest = distance;
                _progressSince = _clock;
            }
            return _clock - _progressSince > StuckTimeout;
        }

        /// <summary>
        /// Sound passes through a locked door; feet don't. A curious minion gives up at the door,
        /// a hunting one finds a walkable way round with A*.
        /// </summary>
        private bool TryRerouteAround(int fromNode)
        {
            if (State != MinionState.Hunting || _routeEndNode < 0) return false;
            var path = GraphSearch.AStar(Graph, fromNode, _routeEndNode);
            if (!path.Found) return false;

            _route.Clear();
            _route.AddRange(path.Nodes);
            RouteIndex = 0;
            return true;
        }

        private void TickAttack(Vector3 player, float playerDistance)
        {
            if (playerDistance > AttackRange * 1.5f)
            {
                // Lost contact: it felt where the player was, so hunt that spot.
                _route.Clear();
                RouteIndex = 0;
                FinalTarget = player;
                HasFinalTarget = true;
                HearingErrorRadius = 0f;
                ResetProgress();
                _lastHeardAt = _clock;
                Enter(MinionState.Hunting);
                FollowRoute(HuntSpeed);
                return;
            }

            Intent = LookTowards(player);
            if (_clock - _lastAttackAt < AttackCooldown) return;
            _lastAttackAt = _clock;
            Attacked?.Invoke();
        }

        private Vector3 CurrentWaypoint()
        {
            if (RouteIndex < _route.Count) return Graph[_route[RouteIndex]].Position;
            return HasFinalTarget ? FinalTarget : CurrentPosition;
        }

        private static MovementIntent LookTowards(Vector3 point) =>
            new MovementIntent { HasLookAt = true, LookAt = point };

        private void Enter(MinionState next)
        {
            if (State == next) return;
            var previous = State;
            State = next;
            _stateEnteredAt = _clock;
            StateChanged?.Invoke(next);

            // Losing touch with the player (Attacking > Hunting) continues the same hunt: no new scream.
            if (next == MinionState.Hunting && previous != MinionState.Attacking) Scream();
        }

        private void Scream()
        {
            Screamed?.Invoke();
            _ctx.Noise.Emit(new NoiseEvent(CurrentPosition, ScreamLoudness, NoiseType.Scream, _clock, Id));
        }
    }
}
