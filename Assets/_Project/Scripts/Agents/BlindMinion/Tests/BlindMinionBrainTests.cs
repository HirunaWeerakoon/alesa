using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    /// <summary>
    /// Test level (metres, top view, +Z up the page):
    ///
    ///   T(10,10)          R(20,10)  room behind a door
    ///      |                 ‖  door
    ///   S(0,0) ── A(10,0) ── B(20,0) ── C(30,0)
    ///
    /// A and B are junctions (3 neighbours) so sound entering them loses JunctionLoss (1.5).
    /// </summary>
    public class BlindMinionBrainTests
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
        private FakeWorld _world;
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

            _world = new FakeWorld();
            _noise = new NoiseBus();
            _ctx = new AIContext(_graph, _world, _noise, new SharedBlackboard());
        }

        private BlindMinionBrain MinionAt(int node) =>
            new BlindMinionBrain(_ctx) { CurrentPosition = _graph[node].Position };

        private void Emit(Vector3 at, float loudness, NoiseType type) =>
            _noise.Emit(new NoiseEvent(at, loudness, type, 0f));

        private void SetDoor(bool closed)
        {
            var door = _graph.FindEdge(B, R);
            door.HasClosedDoor = closed;
            door.Traversable = !closed;
        }

        /// <summary>Teleports the minion to wherever it wants to go, as a perfect NavMesh would.</summary>
        private static void WalkOneStep(BlindMinionBrain brain, float dt = 0.1f)
        {
            brain.Tick(dt);
            if (brain.Intent.HasTarget) brain.CurrentPosition = brain.Intent.Target;
        }

        [Test]
        public void Gunshot_TwoCorridorsAway_AlertsThenHunts()
        {
            var minion = MinionAt(C);
            Emit(_graph[S].Position, 60f, NoiseType.Gunshot);

            Assert.AreEqual(MinionState.Alerted, minion.State);
            // 60 − (10 S→A + 10 A→B + 1.5 junction + 10 B→C) = 28.5
            Assert.AreEqual(28.5f, minion.LastPerceivedLoudness, 1e-3f);

            minion.Tick(1f);
            Assert.AreEqual(MinionState.Hunting, minion.State);
        }

        [Test]
        public void QuietFootstep_FarAway_IsNotHeard()
        {
            var minion = MinionAt(C);
            Emit(_graph[S].Position, 8f, NoiseType.Footstep);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void NearbyFootstep_Investigates()
        {
            var minion = MinionAt(A);
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);
            Assert.AreEqual(MinionState.Investigating, minion.State);
        }

        [Test]
        public void ClosedDoor_MufflesNoise()
        {
            SetDoor(closed: true);
            var minion = MinionAt(R);
            Emit(_graph[B].Position, 30f, NoiseType.DoorSlam);
            Assert.AreEqual(MinionState.Dormant, minion.State, "30 − 10 m − 20 door loss = 0, below threshold");
        }

        [Test]
        public void OpenDoor_LetsSameNoiseThrough()
        {
            SetDoor(closed: false);
            var minion = MinionAt(R);
            Emit(_graph[B].Position, 30f, NoiseType.DoorSlam);
            Assert.AreEqual(MinionState.Alerted, minion.State);
            Assert.AreEqual(20f, minion.LastPerceivedLoudness, 1e-3f);
        }

        [Test]
        public void FollowsCorridorAroundCorner_NotStraightThroughWall()
        {
            var minion = MinionAt(T);
            Emit(_graph[S].Position, 40f, NoiseType.Footstep);

            minion.Tick(1f);   // reaction delay
            minion.Tick(0.1f); // first movement decision

            Assert.AreEqual(MinionState.Investigating, minion.State);
            Assert.IsTrue(minion.Intent.HasTarget);
            Assert.AreEqual(_graph[A].Position, minion.Intent.Target, "should head for the corner at A, not cut diagonally to S");
        }

        [Test]
        public void ReachesNoise_SearchesThenGoesDormant()
        {
            var minion = MinionAt(A);
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            minion.Tick(1f);

            for (int i = 0; i < 20 && minion.State != MinionState.Searching; i++) WalkOneStep(minion);
            Assert.AreEqual(MinionState.Searching, minion.State);

            minion.Tick(minion.SearchDuration + 0.1f);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void LockedDoor_NoWayRound_HuntingMinionWaitsAtDoor()
        {
            SetDoor(closed: true);
            var minion = MinionAt(R);
            Emit(_graph[B].Position, 60f, NoiseType.Gunshot);
            minion.Tick(1f);

            for (int i = 0; i < 5; i++) WalkOneStep(minion);

            Assert.AreEqual(MinionState.Searching, minion.State);
            Assert.AreEqual(_graph[R].Position, minion.CurrentPosition);
        }

        [Test]
        public void HuntingMinionScreams_WakingANeighbourThatMissedTheOriginalNoise()
        {
            var near = MinionAt(S);
            var far = MinionAt(C);
            near.ScreamLoudness = 40f;
            int nearScreams = 0, farScreams = 0;
            near.Screamed += () => nearScreams++;
            far.Screamed += () => farScreams++;

            // Door slam 30 at S: near hears 30 (≥ 25 → hunt); S→C costs 31.5, so far hears nothing.
            Emit(_graph[S].Position, 30f, NoiseType.DoorSlam);
            Assert.AreEqual(MinionState.Dormant, far.State, "far minion shouldn't hear the door");

            near.Tick(1f); // reaction over → Hunting → scream 40 at S → far hears 40 − 31.5 = 8.5
            Assert.AreEqual(MinionState.Hunting, near.State);
            Assert.AreEqual(1, nearScreams);
            Assert.AreEqual(MinionState.Alerted, far.State, "the scream should carry to the far minion");
            Assert.AreEqual(8.5f, far.LastPerceivedLoudness, 1e-3f);

            far.Tick(1f);
            Assert.AreEqual(MinionState.Investigating, far.State, "8.5 is curious, not alarming");
            Assert.AreEqual(0, farScreams, "only hunters scream, so the ripple fades out");
        }

        [Test]
        public void ScreamDuringReaction_DoesNotReplaceTheGunshotLead()
        {
            // Both hear the gunshot at S. The near one reacts first and screams while the far one is still Alerted.
            var near = MinionAt(A);
            var far = MinionAt(C);
            Emit(_graph[S].Position, 60f, NoiseType.Gunshot);
            Assert.AreEqual(MinionState.Alerted, far.State);

            near.Tick(1f); // near → Hunting → screams at A, which far can hear (30 − 20 = 10)
            Assert.AreEqual(MinionState.Hunting, near.State);

            Assert.AreEqual(28.5f, far.LastPerceivedLoudness, 1e-3f, "far should still be working from the gunshot");
            Assert.AreEqual(_graph[S].Position, far.FinalTarget, "and still be heading for where the shot was");
            far.Tick(1f);
            Assert.AreEqual(MinionState.Hunting, far.State);
        }

        [Test]
        public void MinionIgnoresItsOwnScream_AndScreamsOncePerHunt()
        {
            var minion = MinionAt(A);
            int screams = 0;
            minion.Screamed += () => screams++;

            Emit(_graph[S].Position, 60f, NoiseType.Gunshot); // 10 m away, so it is still running there
            for (int i = 0; i < 10; i++) minion.Tick(0.2f);

            Assert.AreEqual(MinionState.Hunting, minion.State);
            Assert.AreEqual(1, screams);
            Assert.AreEqual(50f, minion.LastPerceivedLoudness, 1e-3f, "its own scream must not overwrite what it heard");
        }

        [Test]
        public void LoudNoise_IsPinpointedExactly()
        {
            var minion = MinionAt(C);
            Emit(_graph[S].Position, 60f, NoiseType.Gunshot); // heard at 28.5 ≥ 20

            Assert.AreEqual(0f, minion.HearingErrorRadius);
            Assert.AreEqual(_graph[S].Position, minion.FinalTarget);
        }

        [Test]
        public void FaintNoise_OnlyGivesARoughDirection()
        {
            var noise = new Vector3(8, 0, 0);
            var minion = MinionAt(A);
            Emit(noise, 8f, NoiseType.Footstep); // heard at 6 → error 6 × (1 − 6/20) = 4.2 m

            Assert.AreEqual(4.2f, minion.HearingErrorRadius, 1e-3f);
            float miss = Vector3.Distance(noise, minion.FinalTarget);
            Assert.Greater(miss, 0f, "a faint noise should not be pinpointed");
            Assert.LessOrEqual(miss, 4.2f + 1e-3f);
        }

        [Test]
        public void FaintNoise_GuessesDifferWithSeed()
        {
            var noise = new Vector3(8, 0, 0);
            var a = new BlindMinionBrain(_ctx, seed: 1) { CurrentPosition = _graph[A].Position };
            var b = new BlindMinionBrain(_ctx, seed: 2) { CurrentPosition = _graph[A].Position };
            Emit(noise, 8f, NoiseType.Footstep);
            Assert.AreNotEqual(a.FinalTarget, b.FinalTarget, "two minions hearing the same footstep search different spots");
        }

        [Test]
        public void UnreachableGuess_MinionGivesUpInsteadOfFreezing()
        {
            var minion = MinionAt(A);
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);

            // Never move it (as if the NavMesh can't get any closer) and let time pass.
            for (int i = 0; i < 40; i++) minion.Tick(0.1f);

            Assert.AreEqual(MinionState.Searching, minion.State, "no progress for StuckTimeout → treat as reached");
        }

        /// <summary>Wakes the minion with a footstep, then lets it give up and fall asleep where it stands.</summary>
        private void DisturbThenWait(BlindMinionBrain minion, float seconds)
        {
            Emit(new Vector3(8, 0, 0), 8f, NoiseType.Footstep);
            for (float t = 0f; t < seconds; t += 0.1f) minion.Tick(0.1f);
        }

        [Test]
        public void RecentlyDisturbedMinion_HearsQuieterSounds()
        {
            var calm = MinionAt(A);
            Emit(new Vector3(8, 0, 0), 5.5f, NoiseType.Footstep); // perceived 3.5 < 5
            Assert.AreEqual(MinionState.Dormant, calm.State, "a calm minion misses it");
            calm.Dispose();

            var twitchy = MinionAt(A);
            DisturbThenWait(twitchy, 10f);
            Assert.AreEqual(MinionState.Dormant, twitchy.State, "back asleep after searching");
            // alertness 1 − 10/30 ≈ 0.67 → threshold 5 × (1 − 0.6 × 0.67) ≈ 3.0
            Assert.AreEqual(3.0f, twitchy.EffectiveHearingThreshold, 0.05f);

            Emit(new Vector3(8, 0, 0), 5.5f, NoiseType.Footstep);
            Assert.AreEqual(MinionState.Alerted, twitchy.State, "the same faint step now wakes it");
        }

        [Test]
        public void Alertness_FadesBackToCalm()
        {
            var minion = MinionAt(A);
            DisturbThenWait(minion, 31f);
            Assert.AreEqual(0f, minion.Alertness);
            Assert.AreEqual(minion.HearingThreshold, minion.EffectiveHearingThreshold);

            Emit(new Vector3(8, 0, 0), 5.5f, NoiseType.Footstep);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void RecentlyDisturbedMinion_ReactsFaster()
        {
            var calm = MinionAt(C);
            Emit(_graph[S].Position, 60f, NoiseType.Gunshot);
            calm.Tick(0.45f);
            Assert.AreEqual(MinionState.Alerted, calm.State, "calm reaction is 0.6 s");
            calm.Dispose();

            var twitchy = MinionAt(A);
            DisturbThenWait(twitchy, 10f);
            twitchy.CurrentPosition = _graph[C].Position;
            Emit(_graph[S].Position, 60f, NoiseType.Gunshot);
            twitchy.Tick(0.45f); // reaction 0.6 × (1 − 0.5 × 0.67) = 0.4 s
            Assert.AreEqual(MinionState.Hunting, twitchy.State);
        }

        [Test]
        public void PlayerTouchingDormantMinion_GetsAttacked()
        {
            var minion = MinionAt(S);
            int hits = 0;
            minion.Attacked += () => hits++;
            _world.PlayerPosition = new Vector3(0.5f, 0, 0);

            minion.Tick(0.1f);
            minion.Tick(0.1f); // within cooldown: still one hit

            Assert.AreEqual(MinionState.Attacking, minion.State);
            Assert.AreEqual(1, hits);
        }
    }
}
