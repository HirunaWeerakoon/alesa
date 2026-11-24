using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    /// <summary>
    /// The search-and-memory behaviours: sweeping after losing the trail, remembering the last known
    /// position, noise adding up, threat priority with a backlog, and the alertness cool-down curve.
    /// All of them are opt-in on the brain, so the original behaviour is covered by the older tests.
    ///
    /// Level (metres, top view):
    ///
    ///   T(10,10)          R(20,10)
    ///      |                 |
    ///   S(0,0) ── A(10,0) ── B(20,0) ── C(30,0)
    /// </summary>
    public class BlindMinionSearchMemoryTests
    {
        private sealed class FakeWorld : IWorldQuery
        {
            public float Time { get; set; }
            public Vector3 PlayerPosition { get; set; } = new Vector3(100, 0, 100);
            public Vector3 PlayerVelocity { get; set; }
            public Vector3 PlayerForward { get; set; } = Vector3.UnitZ;
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
            public float LightLevelAt(Vector3 position) => 0f;
        }

        private MazeGraph _graph;
        private NoiseBus _noise;
        private AIContext _ctx;
        private int S, A, B, C, T, R;

        [SetUp]
        public void Build()
        {
            _graph = new MazeGraph();
            S = _graph.AddNode(new Vector3(0, 0, 0), NodeTag.DeadEnd, "S").Id;
            A = _graph.AddNode(new Vector3(10, 0, 0), NodeTag.Junction, "A").Id;
            B = _graph.AddNode(new Vector3(20, 0, 0), NodeTag.Junction, "B").Id;
            C = _graph.AddNode(new Vector3(30, 0, 0), NodeTag.DeadEnd, "C").Id;
            T = _graph.AddNode(new Vector3(10, 0, 10), NodeTag.DeadEnd, "T").Id;
            R = _graph.AddNode(new Vector3(20, 0, 10), NodeTag.Room, "R").Id;
            _graph.Connect(S, A);
            _graph.Connect(A, B);
            _graph.Connect(B, C);
            _graph.Connect(A, T);
            _graph.Connect(B, R);

            _noise = new NoiseBus();
            _ctx = new AIContext(_graph, new FakeWorld(), _noise, new SharedBlackboard());
        }

        private BlindMinionBrain MinionAt(int node) =>
            new BlindMinionBrain(_ctx) { CurrentPosition = _graph[node].Position };

        private void Emit(Vector3 at, float loudness, NoiseType type) =>
            _noise.Emit(new NoiseEvent(at, loudness, type, 0f));

        /// <summary>Teleports the minion to wherever it wants to go, as a perfect NavMesh would.</summary>
        private static void Step(BlindMinionBrain brain, float dt = 0.1f)
        {
            brain.Tick(dt);
            if (brain.Intent.HasTarget) brain.CurrentPosition = brain.Intent.Target;
        }

        private static void WalkUntil(BlindMinionBrain brain, MinionState state, int maxSteps = 200)
        {
            for (int i = 0; i < maxSteps && brain.State != state; i++) Step(brain);
        }

        private BlindMinionBrain SweepingMinionAtA()
        {
            var minion = MinionAt(A);
            minion.SweepNodeCount = 3;
            minion.SweepDuration = 60f;
            minion.SweepDwellSeconds = 0.2f;
            minion.SweepMinRadius = 12f;
            return minion;
        }

        // ------------------------------------------------------------------ search sweep

        [Test]
        public void Sweep_VisitsSeveralPlaces_ThenGoesDormant()
        {
            var minion = SweepingMinionAtA();
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);
            WalkUntil(minion, MinionState.Searching);
            Assert.AreEqual(MinionState.Searching, minion.State);

            var visited = new HashSet<int>();
            for (int i = 0; i < 600 && minion.State == MinionState.Searching; i++)
            {
                Step(minion);
                if (!minion.Intent.HasTarget) continue;
                for (int n = 0; n < _graph.Count; n++)
                    if (minion.Intent.Target == _graph[n].Position) visited.Add(n);
            }

            Assert.AreEqual(MinionState.Dormant, minion.State, "a sweep ends in sleep");
            Assert.GreaterOrEqual(visited.Count, 2, "it should have checked more than one place");
        }

        [Test]
        public void Sweep_Off_StandsStillWhileSearching()
        {
            var minion = MinionAt(A);
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);
            WalkUntil(minion, MinionState.Searching);

            minion.Tick(0.1f);
            Assert.AreEqual(MinionState.Searching, minion.State);
            Assert.IsFalse(minion.Intent.HasTarget, "the original search is standing still");
            Assert.AreEqual(0, minion.SweepNodes.Count);
        }

        [Test]
        public void Sweep_GivesUpAfterSweepDuration()
        {
            var minion = SweepingMinionAtA();
            minion.SweepDuration = 0.5f;
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);
            WalkUntil(minion, MinionState.Searching);

            minion.Tick(1f);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void Sweep_HearingSomethingAgain_BreaksOffTheSearch()
        {
            var minion = SweepingMinionAtA();
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);
            WalkUntil(minion, MinionState.Searching);
            minion.Tick(0.1f);

            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            Assert.AreEqual(MinionState.Investigating, minion.State, "a new noise ends the search");
        }

        [Test]
        public void Sweep_NeverWalksThroughALockedDoor()
        {
            var door = _graph.FindEdge(B, R);
            door.HasClosedDoor = true;
            door.Traversable = false;

            var minion = MinionAt(B);
            minion.SweepNodeCount = 4;
            minion.SweepMinRadius = 15f;
            minion.SweepDuration = 60f;
            minion.SweepDwellSeconds = 0.1f;
            Emit(_graph[B].Position + new Vector3(0, 0, 3), 20f, NoiseType.Footstep);
            minion.Tick(1f);
            WalkUntil(minion, MinionState.Searching);

            for (int i = 0; i < 600 && minion.State == MinionState.Searching; i++)
            {
                Step(minion);
                Assert.IsFalse(minion.Intent.HasTarget && minion.Intent.Target == _graph[R].Position,
                    "the room behind the locked door cannot be reached on foot");
            }
        }

        // ------------------------------------------------------------------ last known position

        [Test]
        public void HearingANoise_RecordsLastKnownPosition()
        {
            var minion = MinionAt(A);
            Assert.IsFalse(minion.LastKnown.HasLKP);

            Emit(new Vector3(8, 0, 0), 30f, NoiseType.Footstep);

            Assert.IsTrue(minion.LastKnown.HasLKP);
            Assert.Less(FlatMath.Distance(minion.LastKnown.Position, new Vector3(8, 0, 0)), 1f);
        }

        // ------------------------------------------------------------------ noise adds up

        [Test]
        public void RepeatedNoise_EscalatesInvestigationToHunt()
        {
            var minion = MinionAt(A);
            minion.ExcitationHuntThreshold = 30f;

            Emit(new Vector3(8, 0, 0), 20f, NoiseType.Footstep);
            minion.Tick(1f);
            Assert.AreEqual(MinionState.Investigating, minion.State, "one footstep is only worth a look");

            Emit(new Vector3(8, 0, 0), 20f, NoiseType.Footstep);
            Assert.AreEqual(MinionState.Hunting, minion.State, "two in a row add up to a hunt");
        }

        [Test]
        public void RepeatedNoise_StaysAnInvestigation_WhenEscalationIsOff()
        {
            var minion = MinionAt(A);

            Emit(new Vector3(8, 0, 0), 20f, NoiseType.Footstep);
            minion.Tick(1f);
            Emit(new Vector3(8, 0, 0), 20f, NoiseType.Footstep);

            Assert.AreEqual(MinionState.Investigating, minion.State);
        }

        // ------------------------------------------------------------------ threat priority

        [Test]
        public void FaintNoise_DoesNotPullMinionOffAGunshot()
        {
            var minion = MinionAt(A);
            minion.PrioritizeThreats = true;

            Emit(new Vector3(8, 0, 0), 60f, NoiseType.Gunshot);
            minion.Tick(1f);
            var target = minion.FinalTarget;

            Emit(_graph[C].Position, 40f, NoiseType.Footstep);

            Assert.AreEqual(target, minion.FinalTarget, "a footstep is not worth leaving a gunshot for");
        }

        [Test]
        public void FaintNoise_StillWinsWhenPrioritisingIsOff()
        {
            var minion = MinionAt(A);

            Emit(new Vector3(8, 0, 0), 60f, NoiseType.Gunshot);
            minion.Tick(1f);
            Emit(_graph[C].Position, 40f, NoiseType.Footstep);

            Assert.Less(FlatMath.Distance(minion.FinalTarget, _graph[C].Position), 1f, "original behaviour: newest noise wins");
        }

        [Test]
        public void IgnoredNoise_IsCheckedOnceTheLeadGoesCold()
        {
            var minion = MinionAt(A);
            minion.PrioritizeThreats = true;
            minion.SweepNodeCount = 3;
            minion.SweepDuration = 60f;

            Emit(new Vector3(8, 0, 0), 60f, NoiseType.Gunshot);
            minion.Tick(1f);
            Emit(_graph[C].Position, 40f, NoiseType.Footstep); // ignored for now, remembered

            WalkUntil(minion, MinionState.Searching);
            Assert.AreEqual(MinionState.Searching, minion.State, "it reaches the gunshot and finds nothing");

            minion.Tick(0.1f);
            Assert.AreEqual(MinionState.Investigating, minion.State, "now it goes to see about the footstep");
            Assert.Less(FlatMath.Distance(minion.FinalTarget, _graph[C].Position), 1f);
        }

        // ------------------------------------------------------------------ alertness curve

        [Test]
        public void SigmoidalAlertness_StaysHigherMidDecay_ThanLinear()
        {
            var linear = MinionAt(A);
            var sigmoidal = MinionAt(A);
            linear.AlertnessMemory = sigmoidal.AlertnessMemory = 10f;
            linear.AlertDecayProfile = DecayProfile.Linear;
            sigmoidal.AlertDecayProfile = DecayProfile.SigmoidalTension;

            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            linear.Tick(4f);
            sigmoidal.Tick(4f);

            Assert.AreEqual(0.6f, linear.Alertness, 1e-3f);
            Assert.Greater(sigmoidal.Alertness, linear.Alertness, "tension holds before it rolls off");

            linear.Tick(6.5f);
            sigmoidal.Tick(6.5f);
            Assert.AreEqual(0f, linear.Alertness);
            Assert.AreEqual(0f, sigmoidal.Alertness, "both are calm again once the memory has run out");
        }
    }
}
