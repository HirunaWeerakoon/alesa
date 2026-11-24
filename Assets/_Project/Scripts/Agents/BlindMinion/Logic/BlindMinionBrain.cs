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
        /// Distance at which the minion feels the player's presence and hunts without hearing a
        /// noise. Keep it short: hearing should still be how it usually finds you. 0 disables it.
        /// </summary>
        public float PresenceRange { get; set; } = 6f;

        /// <summary>
        /// When false, no noise — however loud — escalates past <see cref="MinionState.Investigating"/>,
        /// so the minion walks to a gunshot instead of sprinting at it. Only feeling the player
        /// (<see cref="PresenceRange"/>) or being shot at still makes it run. Defaults to the
        /// original behaviour so existing tests and scenes are unchanged.
        /// </summary>
        public bool HuntOnLoudNoise { get; set; } = true;

        /// <summary>
        /// Radius of the ring this minion aims for around the player instead of the player's exact
        /// position. Several minions all pathing to one point end up standing inside each other;
        /// each taking its own angle on a ring keeps them apart and surrounds the player instead of
        /// queueing behind one another. 0 walks straight at the player.
        /// </summary>
        public float AttackStandoff { get; set; }

        /// <summary>Degrees around the player this minion approaches from. The adapter spreads these.</summary>
        public float AttackAngle { get; set; }

        /// <summary>Where to stand to reach the player, which is not the same as where the player is.</summary>
        private Vector3 StandPoint(Vector3 player)
        {
            if (AttackStandoff <= 0f) return player;
            double radians = AttackAngle * Math.PI / 180.0;
            return player + new Vector3(
                (float)Math.Cos(radians) * AttackStandoff,
                0f,
                (float)Math.Sin(radians) * AttackStandoff);
        }

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

        // ------------------------------------------------------------------ search & memory
        // Everything below is off by default, so a brain built without these set behaves exactly as it
        // always did (stand still for SearchDuration, linear alertness, newest noise wins). The Unity
        // adapter switches them on.

        /// <summary>Where the target was last believed to be, and how unsure that is. Fed by hearing and by feeling the player.</summary>
        public LastKnownPositionTracker LastKnown { get; } = new LastKnownPositionTracker();

        /// <summary>Short-term memory of recent noises. Repeated sounds add up even when each is faint.</summary>
        public SensoryMemoryBuffer Memory { get; } = new SensoryMemoryBuffer();

        /// <summary>
        /// Searching places visited after losing the trail, nearest the last known position first.
        /// 0 keeps the original behaviour: stand still for <see cref="SearchDuration"/>.
        /// </summary>
        public int SweepNodeCount { get; set; }

        /// <summary>Longest a sweep may last before the minion gives up and goes dormant.</summary>
        public float SweepDuration { get; set; } = 18f;

        /// <summary>Seconds spent looking around at each place in a sweep.</summary>
        public float SweepDwellSeconds { get; set; } = 1.2f;

        /// <summary>Smallest area searched, however sure the minion was where the noise came from.</summary>
        public float SweepMinRadius { get; set; } = 4f;

        /// <summary>Shape of the alertness cool-down. Linear is the original behaviour.</summary>
        public DecayProfile AlertDecayProfile { get; set; } = DecayProfile.Linear;

        /// <summary>
        /// Accumulated noise (see <see cref="SensoryMemoryBuffer.CalculateAccumulatedExcitation"/>) at which an
        /// investigation becomes a hunt: a run of footsteps is worse than one. 0 turns it off.
        /// </summary>
        public float ExcitationHuntThreshold { get; set; }
        public float ExcitationHalfLife { get; set; } = 6f;

        /// <summary>
        /// When true, a noise far less threatening than the one being followed does not pull the minion off it.
        /// It is remembered and checked once the current lead goes cold.
        /// </summary>
        public bool PrioritizeThreats { get; set; }

        /// <summary>A new noise must score at least this fraction of the current lead to take over.</summary>
        public float PreemptRatio { get; set; } = 0.6f;

        /// <summary>Seconds a lead keeps its priority; after that any noise may replace it.</summary>
        public float ThreatHoldSeconds { get; set; } = 8f;

        /// <summary>Seconds a remembered, ignored noise stays worth investigating.</summary>
        public float BacklogSeconds { get; set; } = 15f;

        /// <summary>The places the current sweep is visiting, in order. Empty outside a sweep.</summary>
        public IReadOnlyList<int> SweepNodes => _sweep;

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

        private float _alertPeak;
        private float _alertAt = float.NegativeInfinity;
        private float _currentThreat;
        private float _currentThreatAt = float.NegativeInfinity;
        private bool _replayingBacklog;
        private readonly NoisePriorityQueue _backlog = new NoisePriorityQueue(8);

        private readonly List<int> _sweep = new List<int>();
        private readonly List<int> _sweepPath = new List<int>();
        private int _sweepNext;
        private int _sweepPathIndex;
        private bool _searchStarted;
        private float _dwellUntil;
        private Vector3 _dwellLook;

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
            if (AlertnessMemory > 0f) Alertness = SuspicionDecayCurve.Evaluate(_alertPeak, _clock - _alertAt, AlertnessMemory, AlertDecayProfile);

            var player = _ctx.World.PlayerPosition;
            float playerDistance = FlatMath.Distance(CurrentPosition, player);
            float reach = State == MinionState.Dormant ? TouchRange : AttackRange;
            if (State != MinionState.Attacking && playerDistance <= reach) Enter(MinionState.Attacking);
            else if (State != MinionState.Attacking) SensePlayer(player, playerDistance);

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
                    TickSearching();
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

            float threat = NoiseThreatEvaluator.CalculateThreatScore(noise, perceived, CurrentPosition);
            if (!_replayingBacklog) Memory.AddImpression(noise.Position, perceived, noise.Type, _clock, heardThrough);

            // Already following something far more alarming? Don't drop it for a footstep: note this one
            // for later and stay on edge.
            bool following = State == MinionState.Investigating || State == MinionState.Hunting || State == MinionState.Alerted;
            if (PrioritizeThreats && following && !_replayingBacklog
                && _clock - _currentThreatAt < ThreatHoldSeconds && threat < _currentThreat * PreemptRatio)
            {
                _backlog.Enqueue(new PrioritizedNoise(noise, perceived, threat, _clock, heardThrough));
                RaiseAlertness();
                return;
            }

            LastSound = sound;
            LastPerceivedLoudness = perceived;
            float alertnessBefore = Alertness;
            RaiseAlertness();
            _lastHeardAt = _clock;
            _currentThreat = threat;
            _currentThreatAt = _clock;
            Vector3 guess = GuessSourcePosition(noise.Position, perceived);
            BuildRoute(sound, heardThrough, guess);
            LastKnown.Record(guess, _clock, Math.Max(1f, HearingErrorRadius));

            bool excited = ExcitationHuntThreshold > 0f
                && Memory.CalculateAccumulatedExcitation(_clock, ExcitationHalfLife) >= ExcitationHuntThreshold;
            bool alarming = HuntOnLoudNoise && (noise.Type == NoiseType.Gunshot || perceived >= HuntThreshold || excited);
            var desired = alarming || State == MinionState.Hunting ? MinionState.Hunting : MinionState.Investigating;

            switch (State)
            {
                case MinionState.Dormant:
                    _stateAfterAlert = desired;
                    _reactionDelay = SuspicionDecayCurve.ComputeReactionDelay(ReactionTime, alertnessBefore, AlertReactionSpeedup);
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

        /// <summary>
        /// Blind, not oblivious. Inside <see cref="PresenceRange"/> the minion feels the player —
        /// breath, warmth, the floor shifting — and hunts them directly without waiting for a noise.
        /// </summary>
        /// <remarks>
        /// Without this a minion that has lost its lead stands still while the player walks up and
        /// stares at it, because hearing is the only sense it has and standing still is silent.
        /// The range is deliberately short so the hearing model, not this, is what normally finds
        /// the player. Set <see cref="PresenceRange"/> to 0 to turn it off.
        /// </remarks>
        private void SensePlayer(Vector3 player, float playerDistance)
        {
            if (PresenceRange <= 0f || playerDistance > PresenceRange) return;

            _lastHeardAt = _clock;
            HearingErrorRadius = 0f;
            HasFinalTarget = true;
            LastKnown.Record(player, _clock, 1f, _ctx.World.PlayerVelocity);

            // Chase the player's live position: clear the acoustic route, which points at where a
            // noise was, not at where they are now. Aim for this minion's own slot on the ring
            // around them, not the player's exact spot, or every minion converges on one point.
            Vector3 stand = StandPoint(player);
            if (FlatMath.Distance(FinalTarget, stand) > ArriveRadius)
            {
                _route.Clear();
                RouteIndex = 0;
                FinalTarget = stand;
                ResetProgress();
            }

            if (State != MinionState.Hunting) Enter(MinionState.Hunting);
        }

        /// <summary>
        /// Drops whatever it was doing and runs at the player's current position. A bullet cracking
        /// past tells a blind thing exactly where you are, so this bypasses the acoustic route
        /// entirely. Safe to call every frame while charging: it just refreshes the target.
        /// </summary>
        public void HuntPlayer()
        {
            if (State == MinionState.Attacking) return;

            Vector3 stand = StandPoint(_ctx.World.PlayerPosition);
            if (FlatMath.Distance(FinalTarget, stand) > ArriveRadius || !HasFinalTarget)
            {
                _route.Clear();
                RouteIndex = 0;
                FinalTarget = stand;
                ResetProgress();
            }

            HasFinalTarget = true;
            HearingErrorRadius = 0f;
            RaiseAlertness();
            _lastHeardAt = _clock;
            LastKnown.Record(_ctx.World.PlayerPosition, _clock, 1f, _ctx.World.PlayerVelocity);
            if (State != MinionState.Hunting) Enter(MinionState.Hunting);
        }

        private void TickAttack(Vector3 player, float playerDistance)
        {
            if (playerDistance > AttackRange * 1.5f)
            {
                // Lost contact: it felt where the player was, so hunt that spot.
                LastKnown.Record(player, _clock, 1f, _ctx.World.PlayerVelocity);
                _route.Clear();
                RouteIndex = 0;
                FinalTarget = StandPoint(player);
                HasFinalTarget = true;
                HearingErrorRadius = 0f;
                ResetProgress();
                _lastHeardAt = _clock;
                Enter(MinionState.Hunting);
                FollowRoute(HuntSpeed);
                return;
            }

            // AttackRange is where it commits to the player, not where it stops walking: halting
            // there left the minion standing an arm's length away staring. It keeps closing until
            // TouchRange, and only swings once it is actually in contact.
            if (playerDistance > TouchRange)
            {
                Intent = MovementIntent.MoveTo(StandPoint(player), HuntSpeed);
                return;
            }

            Intent = LookTowards(player);
            if (_clock - _lastAttackAt < AttackCooldown) return;
            _lastAttackAt = _clock;
            Attacked?.Invoke();
        }

        // ------------------------------------------------------------------ search & memory behaviour

        /// <summary>Hearing or feeling the target sets alertness to full; <see cref="SuspicionDecayCurve"/> brings it back down.</summary>
        private void RaiseAlertness()
        {
            _alertPeak = 1f;
            _alertAt = _clock;
            Alertness = 1f;
        }

        private void ResetSweep()
        {
            _sweep.Clear();
            _sweepPath.Clear();
            _sweepNext = 0;
            _sweepPathIndex = 0;
            _searchStarted = false;
            _dwellUntil = float.NegativeInfinity;
        }

        /// <summary>
        /// Lost the trail. Original behaviour is to stand still; with <see cref="SweepNodeCount"/> set, first check
        /// any noise ignored earlier, then visit places around where the target was last believed to be.
        /// </summary>
        private void TickSearching()
        {
            if (SweepNodeCount <= 0)
            {
                Intent = MovementIntent.Idle;
                if (TimeInState >= SearchDuration) Enter(MinionState.Dormant);
                return;
            }

            if (!_searchStarted)
            {
                _searchStarted = true;
                if (TryResumeBacklog()) return;
                BuildSweep();
            }

            if (TimeInState >= SweepDuration) { Enter(MinionState.Dormant); return; }

            if (_clock < _dwellUntil)
            {
                Intent = LookTowards(_dwellLook);
                return;
            }

            while (true)
            {
                if (_sweepPathIndex >= _sweepPath.Count)
                {
                    if (_sweepNext >= _sweep.Count)
                    {
                        Intent = MovementIntent.Idle;
                        Enter(MinionState.Dormant);
                        return;
                    }
                    if (!StartLeg(_sweep[_sweepNext++])) continue;
                }

                int node = _sweepPath[_sweepPathIndex];
                var nodePos = Graph[node].Position;
                float distance = FlatMath.Distance(CurrentPosition, nodePos);
                if (distance > ArriveRadius && !Stuck(nodePos, distance))
                {
                    Intent = MovementIntent.MoveTo(nodePos, InvestigateSpeed);
                    return;
                }

                _sweepPathIndex++;
                if (_sweepPathIndex >= _sweepPath.Count)
                {
                    // Reached the place: look around before moving on.
                    _dwellUntil = _clock + SweepDwellSeconds;
                    double angle = _sweepNext * 2.1;
                    _dwellLook = nodePos + new Vector3((float)Math.Cos(angle) * 3f, 0f, (float)Math.Sin(angle) * 3f);
                    Intent = LookTowards(_dwellLook);
                    return;
                }
            }
        }

        /// <summary>Picks the places to check: those nearest where the target probably is, then ordered as a short walk.</summary>
        private void BuildSweep()
        {
            Vector3 center = LastKnown.HasLKP ? LastKnown.GetEstimatedCentroid(_clock) : CurrentPosition;
            float radius = Math.Min(LastKnown.MaxSearchRadius, Math.Max(SweepMinRadius, LastKnown.GetCurrentUncertaintyRadius(_clock)));

            // The recent noises moved in some direction; start the search a little further that way.
            if (Memory.TryGetAcousticHeading(_clock, out var heading)) center += heading * (radius * 0.4f);

            var candidates = SearchPatternGenerator.FindSearchNodes(Graph, center, radius, SweepNodeCount + 2);
            var pending = new List<int>();
            foreach (int node in candidates)
            {
                // The place it is already standing is not worth walking to.
                if (FlatMath.Distance(CurrentPosition, Graph[node].Position) > ArriveRadius && pending.Count < SweepNodeCount)
                    pending.Add(node);
            }

            // Greedy nearest-next order from where it stands, so it walks a tour rather than zig-zagging.
            var from = CurrentPosition;
            while (pending.Count > 0)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < pending.Count; i++)
                {
                    float d = FlatMath.Distance(from, Graph[pending[i]].Position);
                    if (d < bestDistance) { bestDistance = d; best = i; }
                }
                _sweep.Add(pending[best]);
                from = Graph[pending[best]].Position;
                pending.RemoveAt(best);
            }
        }

        /// <summary>Plans the walk to <paramref name="goalNode"/> along passable corridors. False when there is no way there.</summary>
        private bool StartLeg(int goalNode)
        {
            _sweepPath.Clear();
            _sweepPathIndex = 0;
            int start = Graph.NearestNode(CurrentPosition);
            if (start < 0) return false;
            var path = GraphSearch.AStar(Graph, start, goalNode);
            if (!path.Found) return false;
            _sweepPath.AddRange(path.Nodes);
            FinalTarget = Graph[goalNode].Position;
            HasFinalTarget = true;
            ResetProgress();
            return true;
        }

        /// <summary>A noise set aside earlier is checked once the current lead has gone cold.</summary>
        private bool TryResumeBacklog()
        {
            _backlog.PruneExpired(_clock, BacklogSeconds);
            if (!_backlog.TryDequeue(out var next)) return false;

            _replayingBacklog = true;
            try { OnNoise(next.Noise); }
            finally { _replayingBacklog = false; }
            return State != MinionState.Searching;
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
            if (next == MinionState.Searching) ResetSweep();
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
