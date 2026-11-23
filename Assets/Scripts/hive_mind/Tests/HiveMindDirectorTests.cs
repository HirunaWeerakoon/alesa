using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.HiveMind.Tests
{
    public class HiveMindDirectorTests
    {
        private static MazeGraph Corridor(int nodes = 13)
        {
            var g = new MazeGraph();
            for (int i = 0; i < nodes; i++) g.AddNode(new Vector3(i * 5f, 0f, 0f), NodeTag.None, "C" + i);
            for (int i = 0; i < nodes - 1; i++) g.Connect(i, i + 1);
            return g;
        }

        private static HiveMindDirector Make(out SharedBlackboard board, out MazeGraph graph)
        {
            graph = Corridor();
            board = new SharedBlackboard();
            return new HiveMindDirector(graph, board);
        }

        /// <summary>Drives the director for a stretch of game time at a realistic tick rate.</summary>
        private static float Run(HiveMindDirector d, PacingInputs inputs, int playerNode,
            float from, float seconds, float step = 0.1f)
        {
            float t = from;
            for (float e = 0f; e < seconds; e += step)
            {
                t += step;
                d.Tick(t, step, inputs, playerNode);
            }
            return t;
        }

        // ---------------------------------------------------------------- wiring

        [Test]
        public void StartsWithNoPlanAndNoMinions()
        {
            var d = Make(out _, out _);
            Assert.IsFalse(d.CurrentAmbush.IsValid);
            Assert.AreEqual(0, d.DesiredMinionCount);
        }

        [Test]
        public void NoiseReachesTheHeatMap()
        {
            var d = Make(out _, out _);
            d.Observe(new NoiseEvent(new Vector3(25f, 0f, 0f), 30f, NoiseType.Gunshot, 0f, 0));
            d.Tick(0f, 0.1f, PacingInputs.Calm, 0);

            Assert.Greater(d.Heat.HottestHeat, 0f, "A gunshot should have left the hive something to work with");
            Assert.AreEqual(5, d.Heat.HottestNode, "Heard at x = 25, which is node 5");
        }

        [Test]
        public void HeatFeedsIntensityWithoutTheCallerSupplyingIt()
        {
            var d = Make(out _, out _);
            // Caller passes Calm, which has Heat = 0. The director must overwrite it from its own map.
            d.Observe(new NoiseEvent(Vector3.Zero, 40f, NoiseType.Gunshot, 0f, 0));
            Run(d, PacingInputs.Calm, 0, 0f, 5f);

            Assert.Greater(d.Intensity.Intensity, 0f, "Being located should register as pressure");
        }

        // ---------------------------------------------------------------- staging

        [Test]
        public void StagesAnAmbushDuringBuildUp()
        {
            var d = Make(out _, out _);
            d.Tick(0.1f, 0.1f, PacingInputs.Calm, 0);

            Assert.IsTrue(d.CurrentAmbush.IsValid, "BuildUp should produce a plan straight away");
            Assert.AreNotEqual(0, d.CurrentAmbush.Node, "And never on top of the player");
        }

        [Test]
        public void AmbushIsAnnounced()
        {
            var d = Make(out _, out _);
            var staged = new List<AmbushCandidate>();
            d.AmbushStaged += staged.Add;

            d.Tick(0.1f, 0.1f, PacingInputs.Calm, 0);
            Assert.AreEqual(1, staged.Count, "The adapter needs to be told where to put them");
        }

        [Test]
        public void AmbushMovesWithThePlayer()
        {
            var d = Make(out _, out _) ;
            d.RestageIntervalSeconds = 5f;
            d.Tick(0.1f, 0.1f, PacingInputs.Calm, 0);
            int first = d.CurrentAmbush.Node;

            // Player has walked a long way east; the old spot is behind them now.
            float t = Run(d, PacingInputs.Calm, 10, 0.1f, 8f);
            Assert.Greater(d.CurrentAmbush.Node, first,
                "A plan made for where the player was is no plan at all");
            Assert.Greater(t, 8f);
        }

        [Test]
        public void NoStagingWhileResting()
        {
            var d = Make(out _, out _);
            d.Pacing.SustainSeconds = 1f;
            d.Pacing.RelaxSeconds = 60f;

            // Drive to a peak with a minion in the player's face, then take it away.
            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            float t = Run(d, pressed, 0, 0f, 6f);
            t = Run(d, PacingInputs.Calm, 0, t, 40f);

            Assert.AreEqual(PacingState.Relax, d.Pacing.State);
            Assert.AreEqual(0, d.DesiredMinionCount, "Rest must mean rest");
            Assert.IsFalse(d.CurrentAmbush.IsValid, "And the old plan should have been retired");
        }

        [Test]
        public void PrefersAheadOfThePlayerOnAlesasRoute()
        {
            var d = Make(out SharedBlackboard board, out _);
            d.Ambush.RouteWeight = 10f;   // make the route dominate so the assertion is unambiguous
            d.Ambush.MinDistance = 5f;

            // Player at node 6; Alesa is routing them east.
            board.PublishRoutes(new[] { 6, 7, 8, 9, 10, 11, 12 }, null);
            d.Tick(0.1f, 0.1f, PacingInputs.Calm, 6);

            Assert.IsTrue(d.CurrentAmbush.OnRoute, "The hive should get ahead of the player");
            Assert.Greater(d.CurrentAmbush.Node, 6, "Ahead means east of node 6 here");
        }

        // ---------------------------------------------------------------- pressure

        [Test]
        public void MinionCountFollowsThePacingBudget()
        {
            var d = Make(out _, out _);
            d.MaxActiveMinions = 4;
            d.Pacing.SustainSeconds = 5f;

            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            Run(d, pressed, 0, 0f, 4f);

            Assert.AreEqual(PacingState.SustainPeak, d.Pacing.State);
            Assert.AreEqual(4, d.DesiredMinionCount, "A peak should field everything available");
        }

        [Test]
        public void PacingChangesAreAnnounced()
        {
            var d = Make(out _, out _);
            d.Pacing.SustainSeconds = 1f;
            var seen = new List<PacingState>();
            d.PacingChanged += seen.Add;

            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            Run(d, pressed, 0, 0f, 4f);

            Assert.Contains(PacingState.SustainPeak, seen, "Music and lighting hang off these");
        }

        // ---------------------------------------------------------------- boss

        [Test]
        public void BossIsNotRequestedBeforeTheEscape()
        {
            var d = Make(out SharedBlackboard board, out _);
            int requested = -1;
            board.BossAppearanceRequested += n => requested = n;

            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            Run(d, pressed, 0, 0f, 10f);

            Assert.AreEqual(-1, requested, "The boss belongs to the escape, not the whole game");
        }

        [Test]
        public void BossIsNotRequestedWhileThePlayerIsCalm()
        {
            var d = Make(out SharedBlackboard board, out _);
            board.EscapePhaseActive = true;
            int requested = -1;
            board.BossAppearanceRequested += n => requested = n;

            Run(d, PacingInputs.Calm, 0, 0f, 10f);
            Assert.AreEqual(-1, requested, "It should escalate something, not interrupt a quiet moment");
        }

        [Test]
        public void BossIsRequestedOnceUnderPressureDuringTheEscape()
        {
            var d = Make(out SharedBlackboard board, out _);
            board.EscapePhaseActive = true;
            var requests = new List<int>();
            board.BossAppearanceRequested += requests.Add;

            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            Run(d, pressed, 0, 0f, 15f);

            Assert.AreEqual(1, requests.Count, "Exactly one request, however long the pressure lasts");
            Assert.GreaterOrEqual(requests[0], 0, "And it must name a real node");
        }

        // ---------------------------------------------------------------- lifecycle

        [Test]
        public void UnknownPlayerNode_DoesNotThrow()
        {
            var d = Make(out _, out _);
            Assert.DoesNotThrow(() => d.Tick(0.1f, 0.1f, PacingInputs.Calm, -1));
            Assert.IsFalse(d.CurrentAmbush.IsValid);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var d = Make(out SharedBlackboard board, out _);
            board.EscapePhaseActive = true;
            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 1f;
            d.Observe(new NoiseEvent(Vector3.Zero, 40f, NoiseType.Gunshot, 0f, 0));
            Run(d, pressed, 0, 0f, 10f);

            d.Reset();

            Assert.AreEqual(0f, d.Heat.TotalHeat);
            Assert.AreEqual(0f, d.Intensity.Intensity);
            Assert.AreEqual(PacingState.BuildUp, d.Pacing.State);
            Assert.AreEqual(0, d.DesiredMinionCount);
            Assert.IsFalse(d.CurrentAmbush.IsValid);
        }

        [Test]
        public void ARunThroughTheWholeCycleStaysCoherent()
        {
            // An end-to-end sanity run: pressure, peak, release, rest, and back to build up.
            var d = Make(out _, out _);
            d.Pacing.SustainSeconds = 3f;
            d.Pacing.RelaxSeconds = 10f;

            var pressed = PacingInputs.Calm;
            pressed.NearestThreatDistance = 2f;

            // A minion two metres away drives intensity past the 0.85 threshold in about 1.3 s,
            // so three seconds in we should be holding the peak but not yet past the 3 s sustain.
            float t = Run(d, pressed, 4, 0f, 3f);
            Assert.AreEqual(PacingState.SustainPeak, d.Pacing.State, "Pressure should reach a peak");

            t = Run(d, pressed, 4, t, 3f);
            Assert.AreEqual(PacingState.PeakFade, d.Pacing.State, "The peak should be released");

            t = Run(d, PacingInputs.Calm, 4, t, 30f);
            Assert.AreEqual(PacingState.BuildUp, d.Pacing.State, "And the cycle should come round again");
            Assert.AreEqual(1, d.Pacing.CompletedCycles);
        }
    }
}
