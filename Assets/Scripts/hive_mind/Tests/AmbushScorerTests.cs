using System.Numerics;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.HiveMind.Tests
{
    public class AmbushScorerTests
    {
        /// <summary>
        /// A long corridor, 5 m between nodes, with a dead-end side room off node 4.
        ///
        ///   0---1---2---3---4---5---6---7---8---9---10---11---12
        ///                   |
        ///                   13 (side room)
        /// </summary>
        private static MazeGraph LongCorridor()
        {
            var g = new MazeGraph();
            for (int i = 0; i <= 12; i++) g.AddNode(new Vector3(i * 5f, 0f, 0f), NodeTag.None, "C" + i);
            for (int i = 0; i < 12; i++) g.Connect(i, i + 1);

            var side = g.AddNode(new Vector3(20f, 0f, 6f), NodeTag.Room, "Side");
            g.Connect(4, side.Id);
            return g;
        }

        [Test]
        public void PicksSomewhereAtTheIdealDistance()
        {
            var g = LongCorridor();
            // Player at node 0; ideal 20 m is node 4, and nothing else is weighted.
            var scorer = new AmbushScorer { IdealDistance = 20f, MinDistance = 8f, HeatWeight = 0f, RouteWeight = 0f };
            var best = scorer.Best(g, null, null, 0, 0f);

            Assert.IsTrue(best.IsValid);
            Assert.AreEqual(4, best.Node, "20 m from the player is node 4");
        }

        [Test]
        public void NeverStagesOnTopOfThePlayer()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer { MinDistance = 12f };
            var all = scorer.Score(g, null, null, 0, 0f);

            foreach (var c in all)
                Assert.GreaterOrEqual(c.Distance, 12f,
                    "Node " + c.Node + " is close enough that the player would see it assemble");
        }

        [Test]
        public void NeverStagesBeyondReach()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer { MaxDistance = 30f, MinDistance = 5f };
            var all = scorer.Score(g, null, null, 0, 0f);

            foreach (var c in all)
                Assert.LessOrEqual(c.Distance, 30f, "Node " + c.Node + " is past MaxDistance");
        }

        [Test]
        public void PrefersAheadOnTheRouteOverBehind()
        {
            var g = LongCorridor();
            // Player is at node 6, walking east. Route runs 6 -> 7 -> 8 -> 9 -> 10.
            var route = new[] { 6, 7, 8, 9, 10 };
            var scorer = new AmbushScorer
            {
                IdealDistance = 15f,   // node 3 (behind) and node 9 (ahead) are both 15 m away
                MinDistance = 5f,
                HeatWeight = 0f,
                RouteWeight = 1f
            };

            var all = scorer.Score(g, null, route, 6, 0f);
            float ahead = ScoreOf(all, 9);
            float behind = ScoreOf(all, 3);

            Assert.Greater(ahead, behind,
                "At identical distance, getting ahead of the player must beat following them");
        }

        [Test]
        public void RouteBehindThePlayer_GetsNoBonus()
        {
            var g = LongCorridor();
            // The route includes ground already covered: 0 -> 1 -> ... -> 10, player now at 6.
            var route = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var scorer = new AmbushScorer { MinDistance = 5f, HeatWeight = 0f };

            var all = scorer.Score(g, null, route, 6, 0f);

            foreach (var c in all)
                if (c.Node < 6)
                    Assert.IsFalse(c.OnRoute, "Node " + c.Node + " is route they have already walked");
        }

        [Test]
        public void HeatPullsTheAmbushTowardThePlayersLikelyPath()
        {
            var g = LongCorridor();
            var heat = new HiveHeatMap(g);
            // The player was heard loudly near node 8 (x = 40).
            heat.Observe(new Alesa.Core.AI.NoiseEvent(
                new Vector3(40f, 0f, 0f), 30f, Alesa.Core.AI.NoiseType.Gunshot, 0f, 0));
            heat.Tick(0f);

            var withHeat = new AmbushScorer { HeatWeight = 2f, RouteWeight = 0f, IdealDistance = 20f, MinDistance = 5f };
            var ignoringHeat = new AmbushScorer { HeatWeight = 0f, RouteWeight = 0f, IdealDistance = 20f, MinDistance = 5f };

            var a = withHeat.Best(g, heat, null, 0, 0f);
            var b = ignoringHeat.Best(g, null, null, 0, 0f);

            Assert.Greater(a.Distance, b.Distance,
                "Heat around node 8 should drag the chosen spot further east than distance alone would");
        }

        [Test]
        public void RecentlyUsedSpots_AreAvoided()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer
            {
                IdealDistance = 20f,
                MinDistance = 8f,
                HeatWeight = 0f,
                RouteWeight = 0f,
                RecentUseWeight = 5f,
                RecentUseMemorySeconds = 60f
            };

            var first = scorer.Best(g, null, null, 0, 0f);
            Assert.AreEqual(4, first.Node);

            scorer.NoteUsed(first.Node, 0f);
            var second = scorer.Best(g, null, null, 0, 1f);

            Assert.AreNotEqual(first.Node, second.Node,
                "Ambushing the same corner twice running makes the hive readable");
        }

        [Test]
        public void UsedSpots_BecomeAcceptableAgainOverTime()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer { RecentUseMemorySeconds = 60f };
            scorer.NoteUsed(4, 0f);

            Assert.AreEqual(1f, scorer.RecentUse(4, 0f), 0.01f, "Just used");
            Assert.AreEqual(0.5f, scorer.RecentUse(4, 30f), 0.01f, "Half forgotten");
            Assert.AreEqual(0f, scorer.RecentUse(4, 60f), 0.01f, "Fully forgotten");
            Assert.AreEqual(0f, scorer.RecentUse(4, 120f), 0.01f, "Still forgotten later");
        }

        [Test]
        public void UnusableNodes_AreVetoed()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer
            {
                IdealDistance = 20f,
                MinDistance = 8f,
                HeatWeight = 0f,
                RouteWeight = 0f,
                // Node 4 is in plain sight; the Unity adapter would veto it like this.
                IsUnusable = node => node == 4
            };

            var best = scorer.Best(g, null, null, 0, 0f);
            Assert.AreNotEqual(4, best.Node, "A vetoed node must never be chosen, however well it scores");
            Assert.IsTrue(best.IsValid, "But a veto must not leave the hive with nowhere to go");
        }

        [Test]
        public void SideRoom_IsAValidStagingPoint()
        {
            var g = LongCorridor();
            var scorer = new AmbushScorer { MinDistance = 5f, MaxDistance = 60f };
            var all = scorer.Score(g, null, null, 0, 0f);

            bool found = false;
            foreach (var c in all) if (c.Node == 13) found = true;
            Assert.IsTrue(found, "A dead-end room off the corridor is exactly where something should wait");
        }

        [Test]
        public void NoGraph_ReturnsNothingRatherThanThrowing()
        {
            var scorer = new AmbushScorer();
            Assert.IsFalse(scorer.Best(new MazeGraph(), null, null, 0, 0f).IsValid);
            Assert.IsFalse(scorer.Best(LongCorridor(), null, null, 999, 0f).IsValid,
                "An out-of-range player node must not throw");
        }

        [Test]
        public void EverythingTooClose_ReturnsNoCandidate()
        {
            var g = LongCorridor();
            // Nothing in the graph is 500 m away, so there is simply nowhere acceptable.
            var scorer = new AmbushScorer { MinDistance = 500f };
            Assert.IsFalse(scorer.Best(g, null, null, 0, 0f).IsValid,
                "The director must be able to learn that now is not the moment");
        }

        [Test]
        public void Clear_ForgetsUsedSpots()
        {
            var scorer = new AmbushScorer();
            scorer.NoteUsed(4, 0f);
            scorer.Clear();
            Assert.AreEqual(0f, scorer.RecentUse(4, 0f));
        }

        private static float ScoreOf(System.Collections.Generic.List<AmbushCandidate> all, int node)
        {
            foreach (var c in all) if (c.Node == node) return c.Score;
            Assert.Fail("Node " + node + " was not among the candidates");
            return 0f;
        }
    }
}
