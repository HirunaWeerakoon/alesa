using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.HiveMind.Tests
{
    /// <summary>
    /// Unit tests for <see cref="HiveHeatMap"/>. No scene, no models, no NavMesh: the heat map is
    /// pure C# over a graph, which is exactly why the hive mind can be finished before any enemy
    /// art exists.
    /// </summary>
    public class HiveHeatMapTests
    {
        /// <summary>
        /// A straight corridor of six nodes, 5 m apart along +X, with a side branch off node 2.
        ///
        ///   0---1---2---3---4---5        (y = 0, z = 0)
        ///           |
        ///           6                     (z = 5)
        /// </summary>
        private static MazeGraph Corridor(out MazeNode branch)
        {
            var g = new MazeGraph();
            for (int i = 0; i < 6; i++) g.AddNode(new Vector3(i * 5f, 0f, 0f), NodeTag.None, "C" + i);
            for (int i = 0; i < 5; i++) g.Connect(i, i + 1);

            branch = g.AddNode(new Vector3(10f, 0f, 5f), NodeTag.Room, "Branch");
            g.Connect(2, branch.Id);
            return g;
        }

        private static NoiseEvent Noise(float x, float loudness, NoiseType type = NoiseType.Gunshot) =>
            new NoiseEvent(new Vector3(x, 0f, 0f), loudness, type, 0f, 0);

        // ---------------------------------------------------------------- deposit

        [Test]
        public void Noise_DepositsHeatAtItsOrigin()
        {
            var map = new HiveHeatMap(Corridor(out _));
            map.Observe(Noise(0f, 30f));
            map.Tick(0f);

            Assert.Greater(map.HeatAt(0), 0f, "The node at the noise should be hot");
            Assert.AreEqual(0, map.HottestNode, "The hive should believe the player is at node 0");
        }

        [Test]
        public void Heat_FallsOffWithDistance()
        {
            var map = new HiveHeatMap(Corridor(out _));
            map.Observe(Noise(0f, 40f));
            map.Tick(0f);

            // Each node is 5 m further down the corridor, so each must be strictly cooler.
            for (int i = 1; i < 5; i++)
                Assert.Less(map.HeatAt(i), map.HeatAt(i - 1),
                    "Node " + i + " is further from the noise than node " + (i - 1));
        }

        [Test]
        public void Heat_StopsAtTheEdgeOfEarshot()
        {
            var map = new HiveHeatMap(Corridor(out _));
            // 12 m of reach cannot cross 15 m of corridor to node 3.
            map.Observe(Noise(0f, 12f));
            map.Tick(0f);

            Assert.Greater(map.HeatAt(1), 0f, "Node 1 at 5 m is within earshot");
            Assert.AreEqual(0f, map.HeatAt(4), "Node 4 at 20 m is well out of earshot");
            Assert.AreEqual(0f, map.HeatAt(5), "Node 5 at 25 m is well out of earshot");
        }

        [Test]
        public void ClosedDoor_MufflesWhatIsBehindIt()
        {
            var open = Corridor(out _);
            var shut = Corridor(out _);
            // Same graph twice; in one of them the door between node 3 and node 4 is closed.
            shut.FindEdge(3, 4).HasClosedDoor = true;

            var a = new HiveHeatMap(open);
            var b = new HiveHeatMap(shut);
            a.Observe(Noise(0f, 60f));
            b.Observe(Noise(0f, 60f));
            a.Tick(0f);
            b.Tick(0f);

            Assert.AreEqual(a.HeatAt(3), b.HeatAt(3), 1e-4f, "In front of the door nothing changes");
            Assert.Less(b.HeatAt(4), a.HeatAt(4), "Behind a shut door the player should be harder to hear");
        }

        // ---------------------------------------------------------------- decay

        [Test]
        public void Heat_HalvesAfterOneHalfLife()
        {
            var map = new HiveHeatMap(Corridor(out _)) { HalfLifeSeconds = 8f };
            map.Observe(Noise(0f, 30f));
            map.Tick(0f);
            float initial = map.HeatAt(0);

            map.Tick(8f);
            Assert.AreEqual(initial * 0.5f, map.HeatAt(0), 0.01f, "One half-life should remove half the heat");

            map.Tick(16f);
            Assert.AreEqual(initial * 0.25f, map.HeatAt(0), 0.01f, "Two half-lives should leave a quarter");
        }

        [Test]
        public void Heat_ReachesZeroEventually()
        {
            var map = new HiveHeatMap(Corridor(out _)) { HalfLifeSeconds = 8f };
            map.Observe(Noise(0f, 30f));
            map.Tick(0f);

            map.Tick(200f); // 25 half-lives
            Assert.AreEqual(0f, map.TotalHeat, "The hive must genuinely forget, not keep a trace forever");
            Assert.AreEqual(-1, map.HottestNode, "With no heat there is no believed position");
        }

        [Test]
        public void FirstTick_DoesNotDecayFromTimeZero()
        {
            // Entering play mode at t = 300 s must not wipe the first noise with 300 s of decay.
            var map = new HiveHeatMap(Corridor(out _)) { HalfLifeSeconds = 8f };
            map.Observe(Noise(0f, 30f));
            map.Tick(300f);

            Assert.Greater(map.HeatAt(0), 0f, "A late first tick should not retroactively decay");
        }

        // ---------------------------------------------------------------- filtering

        [Test]
        public void MinionOwnNoise_IsIgnored()
        {
            var map = new HiveHeatMap(Corridor(out _));
            // SourceId 7 is some minion screeching; the hive must not chase itself.
            map.Observe(new NoiseEvent(Vector3.Zero, 40f, NoiseType.Scream, 0f, 7));
            map.Tick(0f);

            Assert.AreEqual(0f, map.TotalHeat);
        }

        [Test]
        public void QuietNoise_IsIgnored()
        {
            var map = new HiveHeatMap(Corridor(out _)) { MinLoudness = 1.5f };
            map.Observe(Noise(0f, 1f, NoiseType.Footstep)); // crouch-walking
            map.Tick(0f);

            Assert.AreEqual(0f, map.TotalHeat, "Moving carefully should be genuinely safe");
        }

        [Test]
        public void NaNLoudness_IsRejected()
        {
            var map = new HiveHeatMap(Corridor(out _));
            map.Observe(Noise(0f, float.NaN));
            map.Tick(0f);

            Assert.AreEqual(0f, map.TotalHeat);
        }

        // ---------------------------------------------------------------- weighting

        [Test]
        public void Gunshot_LeavesMoreHeatThanAFootstep()
        {
            var shot = new HiveHeatMap(Corridor(out _));
            var step = new HiveHeatMap(Corridor(out _));
            shot.Observe(Noise(0f, 20f, NoiseType.Gunshot));
            step.Observe(Noise(0f, 20f, NoiseType.Footstep));
            shot.Tick(0f);
            step.Tick(0f);

            Assert.Greater(shot.HeatAt(0), step.HeatAt(0),
                "At equal loudness a gunshot is far stronger evidence than a footstep");
        }

        [Test]
        public void Heat_NeverExceedsOne()
        {
            var map = new HiveHeatMap(Corridor(out _));
            // Twenty gunshots in the same spot inside one tick.
            for (int i = 0; i < 20; i++) map.Observe(Noise(0f, 60f));
            map.Tick(0f);

            for (int n = 0; n < 6; n++)
                Assert.LessOrEqual(map.HeatAt(n), 1f, "Heat must stay normalised for pacing thresholds");
        }

        [Test]
        public void RepeatedNoise_Reinforces()
        {
            var map = new HiveHeatMap(Corridor(out _)) { HalfLifeSeconds = 100f };
            map.Observe(Noise(0f, 20f, NoiseType.Footstep));
            map.Tick(0f);
            float once = map.HeatAt(0);

            map.Observe(Noise(0f, 20f, NoiseType.Footstep));
            map.Tick(0.1f); // barely any decay
            Assert.Greater(map.HeatAt(0), once, "A second footstep in the same place should add confidence");
        }

        // ---------------------------------------------------------------- tracking

        [Test]
        public void HottestNode_FollowsThePlayer()
        {
            var map = new HiveHeatMap(Corridor(out _)) { HalfLifeSeconds = 2f };
            map.Observe(Noise(0f, 25f));
            map.Tick(0f);
            Assert.AreEqual(0, map.HottestNode, "Player heard at the west end");

            // Player moves east and fires again; the old heat has had 20 s to fade.
            map.Observe(Noise(25f, 25f));
            map.Tick(20f);
            Assert.AreEqual(5, map.HottestNode, "The belief should have moved to the east end");
        }

        [Test]
        public void Branch_GetsHeatFromTheCorridor()
        {
            var g = Corridor(out MazeNode branch);
            var map = new HiveHeatMap(g);
            map.Observe(Noise(10f, 30f)); // right at the junction
            map.Tick(0f);

            Assert.Greater(map.HeatAt(branch.Id), 0f, "Sound should turn the corner into the side room");
        }

        // ---------------------------------------------------------------- budget

        [Test]
        public void OnlyTheLoudestNoisesAreProcessedInOneTick()
        {
            var map = new HiveHeatMap(Corridor(out _)) { MaxNoisesPerTick = 1 };
            // A quiet footstep at the west end, a louder gunshot at the east end, same tick.
            // 20 m of reach cannot cross the ~26.5 m of attenuation back to node 0, so if node 0
            // ends up hot it can only be because the footstep was processed too.
            map.Observe(Noise(0f, 10f, NoiseType.Footstep));
            map.Observe(Noise(25f, 20f, NoiseType.Gunshot));
            map.Tick(0f);

            Assert.AreEqual(0f, map.HeatAt(0), "The footstep should have been dropped from the budget");
            Assert.Greater(map.HeatAt(5), 0f, "The gunshot is the one worth spending the budget on");
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            var map = new HiveHeatMap(Corridor(out _));
            map.Observe(Noise(0f, 40f));
            map.Tick(0f);
            Assert.Greater(map.TotalHeat, 0f);

            map.Clear();
            Assert.AreEqual(0f, map.TotalHeat);
            Assert.AreEqual(-1, map.HottestNode);
        }

        [Test]
        public void InjectedAcousticCost_IsUsed()
        {
            var map = new HiveHeatMap(Corridor(out _))
            {
                // A world where sound dies ten times faster than normal.
                AcousticCost = (edge, from) => edge.Length * 10f
            };
            map.Observe(Noise(0f, 30f));
            map.Tick(0f);

            // Nodes adjacent to the noise are seeded with their real distance, so node 1 is still
            // reached. Node 2 is the first that has to pay the injected edge cost: 5 m of seed plus
            // 50 of edge is past the 30 of reach, where the default model would have reached it.
            Assert.Greater(map.HeatAt(1), 0f, "Seeded neighbours are reached by distance, not edge cost");
            Assert.AreEqual(0f, map.HeatAt(2), "At ten times the loss the sound dies before node 2");
        }
    }
}
