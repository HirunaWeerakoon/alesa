using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    [TestFixture]
    public class BlindMinionIntegrationTests
    {
        private sealed class MockWorldQuery : IWorldQuery
        {
            public float Time { get; set; }
            public Vector3 PlayerPosition { get; set; } = new Vector3(100, 0, 100);
            public Vector3 PlayerVelocity { get; set; } = Vector3.Zero;
            public Vector3 PlayerForward { get; set; } = Vector3.UnitZ;
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
            public float LightLevelAt(Vector3 position) => 0f;
        }

        private MazeGraph _graph;
        private MockWorldQuery _world;
        private NoiseBus _noiseBus;
        private SharedBlackboard _blackboard;
        private AIContext _context;

        private int _nodeA;
        private int _nodeB;
        private int _nodeC;
        private int _nodeIsolated;

        [SetUp]
        public void Setup()
        {
            _graph = new MazeGraph();
            // Corridor: A(0,0,0) <---> B(10,0,0) <---> C(20,0,0)
            _nodeA = _graph.AddNode(new Vector3(0, 0, 0), NodeTag.DeadEnd, "CorridorA").Id;
            _nodeB = _graph.AddNode(new Vector3(10, 0, 0), NodeTag.Junction, "CorridorB").Id;
            _nodeC = _graph.AddNode(new Vector3(20, 0, 0), NodeTag.DeadEnd, "CorridorC").Id;
            _graph.Connect(_nodeA, _nodeB);
            _graph.Connect(_nodeB, _nodeC);

            // Isolated room far away with no edges (unreachable)
            _nodeIsolated = _graph.AddNode(new Vector3(80, 0, 80), NodeTag.Room, "IsolatedRoom").Id;

            _world = new MockWorldQuery();
            _noiseBus = new NoiseBus();
            _blackboard = new SharedBlackboard();
            _context = new AIContext(_graph, _world, _noiseBus, _blackboard);
        }

        #region Headless FSM Cycle Integration Tests

        [Test]
        public void FSM_FullInvestigationCycle_DormantToAlertedToInvestigatingToSearchingToDormant()
        {
            using var brain = new BlindMinionBrain(_context)
            {
                CurrentPosition = _graph[_nodeA].Position,
                ReactionTime = 0.5f,
                SearchDuration = 2.0f
            };

            var stateHistory = new List<MinionState>();
            brain.StateChanged += s => stateHistory.Add(s);

            Assert.AreEqual(MinionState.Dormant, brain.State);

            // Step 1: Moderate noise (perceived = 10, between HearingThreshold 5 and HuntThreshold 25)
            // Emitted at Node B (10m away, loudness 20 -> perceived 10)
            _noiseBus.Emit(new NoiseEvent(_graph[_nodeB].Position, 20.0f, NoiseType.Footstep, 0f));

            Assert.AreEqual(MinionState.Alerted, brain.State);
            Assert.Contains(MinionState.Alerted, stateHistory);

            // Step 2: Reaction delay tick (reaction time = 0.5s)
            brain.Tick(0.2f);
            Assert.AreEqual(MinionState.Alerted, brain.State, "Must stay Alerted during reaction delay");

            brain.Tick(0.35f); // Total time in state > 0.5s
            Assert.AreEqual(MinionState.Investigating, brain.State, "Should transition to Investigating");

            // Step 3: Minion follows route towards destination
            Assert.IsTrue(brain.Intent.HasDestination);

            // Simulate minion stepping along corridor until reaching target (Node B)
            brain.CurrentPosition = _graph[_nodeB].Position;
            brain.Tick(0.1f);

            // Upon arriving at the noise epicenter, agent searches the area
            Assert.AreEqual(MinionState.Searching, brain.State, "Should transition to Searching upon arrival");

            // Step 4: Search duration timeout returns minion to Dormant
            brain.Tick(1.0f);
            Assert.AreEqual(MinionState.Searching, brain.State);

            brain.Tick(1.5f); // Total search time > 2.0s
            Assert.AreEqual(MinionState.Dormant, brain.State, "Should transition back to Dormant after search expires");
        }

        [Test]
        public void FSM_AlarmingGunshot_TriggersHuntAndScreamBroadcast()
        {
            using var brain = new BlindMinionBrain(_context)
            {
                CurrentPosition = _graph[_nodeA].Position,
                ReactionTime = 0.4f,
                HuntThreshold = 25.0f
            };

            bool screamFired = false;
            brain.Screamed += () => screamFired = true;

            // Emit violent gunshot (loudness 60)
            _noiseBus.Emit(new NoiseEvent(_graph[_nodeB].Position, 60.0f, NoiseType.Gunshot, 0f));
            Assert.AreEqual(MinionState.Alerted, brain.State);

            // Complete reaction delay to transition into Hunting
            brain.Tick(0.5f);
            Assert.AreEqual(MinionState.Hunting, brain.State);
            Assert.IsTrue(screamFired, "Hunting transition must trigger screech broadcast");
        }

        [Test]
        public void FSM_TouchContact_SwitchesToAttackingAndResumesHuntOnEscape()
        {
            using var brain = new BlindMinionBrain(_context)
            {
                CurrentPosition = new Vector3(0, 0, 0),
                TouchRange = 1.2f,
                AttackRange = 1.8f
            };

            int attackTriggerCount = 0;
            brain.Attacked += () => attackTriggerCount++;

            // Player touches the minion
            _world.PlayerPosition = new Vector3(0.5f, 0, 0); // distance 0.5m <= TouchRange
            brain.Tick(0.1f);

            Assert.AreEqual(MinionState.Attacking, brain.State);
            Assert.AreEqual(1, attackTriggerCount, "Initial touch attack should fire");

            // Player flees outside AttackRange * 1.5 (1.8 * 1.5 = 2.7m)
            _world.PlayerPosition = new Vector3(5.0f, 0, 0);
            brain.Tick(0.1f);

            Assert.AreEqual(MinionState.Hunting, brain.State, "Minion should hunt fleeing target position");
        }

        [Test]
        public void FSM_HuntTimeout_TransitionsToSearching()
        {
            using var brain = new BlindMinionBrain(_context)
            {
                CurrentPosition = _graph[_nodeA].Position,
                HuntMemory = 3.0f,
                ReactionTime = 0.1f
            };

            _noiseBus.Emit(new NoiseEvent(_graph[_nodeB].Position, 60.0f, NoiseType.Gunshot, 0f));
            brain.Tick(0.2f); // Alerted -> Hunting
            Assert.AreEqual(MinionState.Hunting, brain.State);

            // Tick past HuntMemory timeout without new acoustic stimuli
            brain.Tick(3.5f);
            Assert.AreEqual(MinionState.Searching, brain.State, "Hunt should expire into Searching after HuntMemory timeout");
        }

        #endregion

        #region LKP & Search Grid Reachability Fallbacks

        [Test]
        public void LKP_Tracker_UncertaintyInflationAndExtrapolation()
        {
            var tracker = new LastKnownPositionTracker
            {
                UncertaintyExpansionRate = 2.0f,
                MaxSearchRadius = 20.0f
            };

            var initialPos = new Vector3(10, 0, 0);
            var velocity = new Vector3(3, 0, 0); // moving at 3 m/s in +X
            tracker.Record(initialPos, time: 0.0f, initialUncertainty: 1.0f, velocity: velocity);

            Assert.IsTrue(tracker.HasLKP);

            // At t = 1.0s:
            // Centroid extrapolates: 10 + 3 * 1.0 = 13.0m
            // Uncertainty expands: 1.0 + 1.0 * 2.0 = 3.0m
            var centroid = tracker.GetEstimatedCentroid(currentTime: 1.0f);
            Assert.AreEqual(13.0f, centroid.X, 0.01f);

            float radius = tracker.GetCurrentUncertaintyRadius(currentTime: 1.0f);
            Assert.AreEqual(3.0f, radius, 0.01f);

            // Candidate at (14, 0, 0) is 1.0m from centroid -> inside uncertainty
            Assert.IsTrue(tracker.IsWithinUncertainty(new Vector3(14, 0, 0), currentTime: 1.0f));

            // Candidate at (25, 0, 0) is 12.0m from centroid -> outside uncertainty
            Assert.IsFalse(tracker.IsWithinUncertainty(new Vector3(25, 0, 0), currentTime: 1.0f));
        }

        [Test]
        public void SearchGrid_FindSearchNodes_ReturnsReachableNodesOnly()
        {
            // Search around Node B within radius 12m
            // Node A (10m away) and Node C (10m away) are connected and reachable
            // IsolatedRoom (80m away) is out of reach and disconnected
            var searchNodes = SearchPatternGenerator.FindSearchNodes(_graph, _graph[_nodeB].Position, searchRadius: 12.0f, maxNodes: 4);

            Assert.IsTrue(searchNodes.Contains(_nodeB));
            Assert.IsTrue(searchNodes.Contains(_nodeA));
            Assert.IsTrue(searchNodes.Contains(_nodeC));
            Assert.IsFalse(searchNodes.Contains(_nodeIsolated), "Isolated disconnected node must not be in search nodes");
        }

        [Test]
        public void InvestigativeWaypoints_FilterReachableWaypoints_FiltersDisconnectedPoints()
        {
            var minionPos = new Vector3(0, 0, 0);
            var targetLocus = new Vector3(10, 0, 0);

            // Generate tactical approach waypoints
            var rawWaypoints = InvestigativeWaypointGenerator.GenerateApproachWaypoints(minionPos, targetLocus, standoffDistance: 2.0f, sampleAngles: 8);
            Assert.AreEqual(8, rawWaypoints.Count);

            // Filter against graph (corridor nodes are at Y=0, Z=0)
            var validWaypoints = InvestigativeWaypointGenerator.FilterReachableWaypoints(_graph, rawWaypoints, maxAcceptableGraphDistance: 3.0f);

            Assert.IsTrue(validWaypoints.Count > 0);
            Assert.IsTrue(validWaypoints.Count <= rawWaypoints.Count);

            // None of the valid waypoints should be excessively far from any graph node
            foreach (var wp in validWaypoints)
            {
                int nearest = _graph.NearestNode(wp.Position);
                float dist = FlatMath.Distance(wp.Position, _graph[nearest].Position);
                Assert.IsTrue(dist <= 3.0f);
            }
        }

        #endregion

        #region Combat Decision Arbiter Integration Tests

        [Test]
        public void CombatDecisionArbiter_ClosingVelocity_EvaluatesStrikeVsLungeVsStandby()
        {
            var arbiter = new CombatDecisionArbiter
            {
                MeleeStrikeReach = 1.8f,
                LungeTriggerReach = 3.2f,
                BaseAttackCooldown = 1.5f
            };

            var minionPos = new Vector3(0, 0, 0);

            // Case 1: Target within MeleeReach (1.5m), closing in fast (-2 m/s relative, towards minion)
            var targetPosNear = new Vector3(1.5f, 0, 0);
            var closingVelocity = new Vector3(-2.0f, 0, 0); // towards minion

            var decisionNear = arbiter.EvaluateAttack(minionPos, targetPosNear, closingVelocity, currentTime: 1.0f);
            Assert.IsTrue(decisionNear.ShouldAttack);
            Assert.AreEqual(AttackIntentType.StandardMeleeStrike, decisionNear.IntentType);
            Assert.IsTrue(decisionNear.EstimatedHitProbability > 0.5f, "Closing velocity should yield high hit probability");

            // Case 2: Target in outer reach (2.5m) between 1.8m and 3.2m -> Leaping Lunge
            var targetPosMid = new Vector3(2.5f, 0, 0);
            var decisionMid = arbiter.EvaluateAttack(minionPos, targetPosMid, closingVelocity, currentTime: 3.0f);
            Assert.IsTrue(decisionMid.ShouldAttack);
            Assert.AreEqual(AttackIntentType.LeapingLunge, decisionMid.IntentType);

            // Case 3: Target beyond reach (5.0m) -> Standby
            var targetPosFar = new Vector3(5.0f, 0, 0);
            var decisionFar = arbiter.EvaluateAttack(minionPos, targetPosFar, closingVelocity, currentTime: 5.0f);
            Assert.IsFalse(decisionFar.ShouldAttack);
            Assert.AreEqual(AttackIntentType.None, decisionFar.IntentType);
        }

        [Test]
        public void CombatDecisionArbiter_FrenzyEscalation_ReducesCooldownAndTriggersFlurry()
        {
            var arbiter = new CombatDecisionArbiter
            {
                MeleeStrikeReach = 2.0f,
                BaseAttackCooldown = 1.5f,
                FrenzyCooldownMultiplier = 0.6f,
                FrenzyDuration = 4.0f
            };

            var minionPos = Vector3.Zero;
            var targetPos = new Vector3(1.5f, 0, 0);

            // Register 2 successful consecutive hits to enter Frenzy
            arbiter.RegisterHit(currentTime: 1.0f);
            arbiter.RegisterHit(currentTime: 2.0f);

            Assert.IsTrue(arbiter.IsFrenzied(currentTime: 2.5f));

            // In frenzy mode within reach, attack type escalates to FrenzyFlurry
            var frenzyDecision = arbiter.EvaluateAttack(minionPos, targetPos, Vector3.Zero, currentTime: 2.5f);
            Assert.IsTrue(frenzyDecision.ShouldAttack);
            Assert.AreEqual(AttackIntentType.FrenzyFlurry, frenzyDecision.IntentType);

            // Desired attack cadence in frenzy is shortened (1.5 * 0.6 = 0.9s)
            Assert.AreEqual(0.9f, frenzyDecision.DesiredAttackCadence, 0.01f);
        }

        #endregion
    }
}
