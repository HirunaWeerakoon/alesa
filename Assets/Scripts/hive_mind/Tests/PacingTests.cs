using System.Collections.Generic;
using NUnit.Framework;

namespace Alesa.HiveMind.Tests
{
    public class IntensityModelTests
    {
        private static PacingInputs ThreatAt(float metres)
        {
            var i = PacingInputs.Calm;
            i.NearestThreatDistance = metres;
            return i;
        }

        /// <summary>Runs the model for <paramref name="seconds"/> in small steps, as the game would.</summary>
        private static void Run(IntensityModel model, PacingInputs inputs, float seconds, float step = 0.05f)
        {
            for (float t = 0f; t < seconds; t += step) model.Tick(inputs, step);
        }

        [Test]
        public void StartsCalm()
        {
            Assert.AreEqual(0f, new IntensityModel().Intensity);
        }

        [Test]
        public void NothingHappening_StaysCalm()
        {
            var model = new IntensityModel();
            Run(model, PacingInputs.Calm, 10f);
            Assert.AreEqual(0f, model.Intensity);
        }

        [Test]
        public void NearbyThreat_RaisesIntensity()
        {
            var model = new IntensityModel();
            Run(model, ThreatAt(2f), 3f);
            Assert.Greater(model.Intensity, 0.8f, "A minion two metres away should be near-maximum pressure");
        }

        [Test]
        public void DistantThreat_ContributesNothing()
        {
            var model = new IntensityModel { ThreatRange = 25f };
            Run(model, ThreatAt(30f), 5f);
            Assert.AreEqual(0f, model.Intensity, "Beyond the threat range there is no pressure");
        }

        [Test]
        public void Threat_PressureScalesWithCloseness()
        {
            var near = new IntensityModel();
            var far = new IntensityModel();
            Run(near, ThreatAt(5f), 5f);
            Run(far, ThreatAt(20f), 5f);
            Assert.Greater(near.Intensity, far.Intensity, "Closer should be more frightening");
        }

        [Test]
        public void IntensityRisesFasterThanItFalls()
        {
            // This asymmetry is the whole point: fear must outlast its cause.
            var model = new IntensityModel { RiseSeconds = 1.5f, FallSeconds = 12f };

            Run(model, ThreatAt(0f), 2f);
            float peak = model.Intensity;
            Assert.Greater(peak, 0.9f, "Should have reached near maximum within two seconds");

            // Threat gone. The same elapsed time must remove far less than it added.
            Run(model, PacingInputs.Calm, 2f);
            Assert.Greater(model.Intensity, 0.6f, "Two seconds of calm should not undo two seconds of terror");
        }

        [Test]
        public void BossActive_KeepsTheGameTense()
        {
            var model = new IntensityModel();
            var inputs = PacingInputs.Calm;
            inputs.BossActive = true;
            Run(model, inputs, 5f);

            Assert.GreaterOrEqual(model.Intensity, 0.79f, "While the boss hunts, nothing is calm");
        }

        [Test]
        public void HeatAlone_IsOnlyMildPressure()
        {
            var model = new IntensityModel();
            var inputs = PacingInputs.Calm;
            inputs.Heat = 1f; // perfectly located, but nothing has arrived yet
            Run(model, inputs, 5f);

            Assert.Greater(model.Intensity, 0.4f, "Being hunted should be felt");
            Assert.LessOrEqual(model.Intensity, 0.51f, "But being hunted is not the same as being caught");
        }

        [Test]
        public void LowHealth_RaisesTheStakes()
        {
            var model = new IntensityModel();
            var inputs = PacingInputs.Calm;
            inputs.PlayerHealthFraction = 0.1f;
            Run(model, inputs, 5f);

            Assert.Greater(model.Intensity, 0.5f, "Nearly dead is stressful even in an empty corridor");
        }

        [Test]
        public void Firing_CountsAsPressure()
        {
            var model = new IntensityModel();
            var inputs = PacingInputs.Calm;
            inputs.PlayerFiring = true;
            Run(model, inputs, 5f);

            Assert.GreaterOrEqual(model.Intensity, 0.59f);
        }

        [Test]
        public void Intensity_StaysWithinRange()
        {
            var model = new IntensityModel();
            var inputs = ThreatAt(0f);
            inputs.BossActive = true;
            inputs.PlayerHealthFraction = 0f;
            inputs.PlayerFiring = true;
            inputs.Heat = 1f;
            Run(model, inputs, 20f);

            Assert.LessOrEqual(model.Intensity, 1f, "Everything at once must still cap at 1");
            Run(model, PacingInputs.Calm, 120f);
            Assert.GreaterOrEqual(model.Intensity, 0f, "And must never go negative");
        }

        [Test]
        public void Reset_ReturnsToCalm()
        {
            var model = new IntensityModel();
            Run(model, ThreatAt(1f), 3f);
            model.Reset();
            Assert.AreEqual(0f, model.Intensity);
        }
    }

    public class PacingStateMachineTests
    {
        private static void Run(PacingStateMachine sm, float intensity, float seconds, float step = 0.1f)
        {
            for (float t = 0f; t < seconds; t += step) sm.Tick(intensity, step);
        }

        [Test]
        public void StartsInBuildUp()
        {
            Assert.AreEqual(PacingState.BuildUp, new PacingStateMachine().State);
        }

        [Test]
        public void HighIntensity_ReachesThePeak()
        {
            var sm = new PacingStateMachine();
            Run(sm, 0.9f, 0.5f);
            Assert.AreEqual(PacingState.SustainPeak, sm.State);
        }

        [Test]
        public void LowIntensity_StaysInBuildUp()
        {
            var sm = new PacingStateMachine();
            Run(sm, 0.2f, 30f);
            Assert.AreEqual(PacingState.BuildUp, sm.State, "Without pressure there is nothing to peak");
        }

        [Test]
        public void Peak_IsHeldThenFades()
        {
            var sm = new PacingStateMachine { SustainSeconds = 4f };
            Run(sm, 0.9f, 0.5f);
            Assert.AreEqual(PacingState.SustainPeak, sm.State);

            Run(sm, 0.9f, 2f);
            Assert.AreEqual(PacingState.SustainPeak, sm.State, "The peak should still be held at two seconds");

            Run(sm, 0.9f, 3f);
            Assert.AreEqual(PacingState.PeakFade, sm.State, "And released after four");
        }

        [Test]
        public void CalmingDown_ReachesRelax()
        {
            var sm = new PacingStateMachine { SustainSeconds = 1f, CalmThreshold = 0.3f };
            Run(sm, 0.9f, 2f);                     // peak and sustain
            Assert.AreEqual(PacingState.PeakFade, sm.State);

            Run(sm, 0.1f, 0.5f);                   // player got away
            Assert.AreEqual(PacingState.Relax, sm.State);
        }

        [Test]
        public void StuckAtMediumIntensity_StillReachesRelax()
        {
            // The safety valve. A player hiding with intensity parked just above CalmThreshold
            // must not be able to switch the director off forever.
            var sm = new PacingStateMachine { SustainSeconds = 1f, CalmThreshold = 0.3f, MaxFadeSeconds = 10f };
            Run(sm, 0.9f, 2f);
            Assert.AreEqual(PacingState.PeakFade, sm.State);

            Run(sm, 0.5f, 12f);                    // never calms below the threshold
            Assert.AreEqual(PacingState.Relax, sm.State, "MaxFadeSeconds must force the quiet phase");
        }

        [Test]
        public void Relax_EndsAndCountsTheCycle()
        {
            var sm = new PacingStateMachine { SustainSeconds = 1f, RelaxSeconds = 5f };
            Run(sm, 0.9f, 2f);
            Run(sm, 0.1f, 0.5f);
            Assert.AreEqual(PacingState.Relax, sm.State);
            Assert.AreEqual(0, sm.CompletedCycles);

            Run(sm, 0.1f, 6f);
            Assert.AreEqual(PacingState.BuildUp, sm.State, "A full cycle should start the next build up");
            Assert.AreEqual(1, sm.CompletedCycles);
        }

        [Test]
        public void PressureBudget_IsZeroWhileResting()
        {
            var sm = new PacingStateMachine { SustainSeconds = 1f };
            Run(sm, 0.9f, 2f);
            Run(sm, 0.1f, 0.5f);

            Assert.AreEqual(PacingState.Relax, sm.State);
            Assert.AreEqual(0f, sm.PressureBudget, "Relax must be genuinely quiet, or it is not a rest");
        }

        [Test]
        public void PressureBudget_IsFullAtThePeak()
        {
            var sm = new PacingStateMachine();
            Run(sm, 0.9f, 0.5f);
            Assert.AreEqual(1f, sm.PressureBudget);
        }

        [Test]
        public void PressureBudget_RampsDuringBuildUp()
        {
            var sm = new PacingStateMachine();
            sm.Tick(0.1f, 0.1f);
            float early = sm.PressureBudget;

            Run(sm, 0.1f, 5f);
            Assert.Greater(sm.PressureBudget, early, "Pressure should build, not arrive all at once");
            Assert.Greater(early, 0f, "But a build up is never completely empty");
        }

        [Test]
        public void StateChanges_AreAnnounced()
        {
            var sm = new PacingStateMachine { SustainSeconds = 1f, RelaxSeconds = 2f };
            var seen = new List<PacingState>();
            sm.StateChanged += seen.Add;

            Run(sm, 0.9f, 2f);   // BuildUp -> SustainPeak -> PeakFade
            Run(sm, 0.1f, 3f);   // PeakFade -> Relax -> BuildUp

            CollectionAssert.AreEqual(
                new[] { PacingState.SustainPeak, PacingState.PeakFade, PacingState.Relax, PacingState.BuildUp },
                seen,
                "The cycle should be announced in order");
        }

        [Test]
        public void ForceBuildUp_CutsTheRestShort()
        {
            var sm = new PacingStateMachine { SustainSeconds = 1f, RelaxSeconds = 60f };
            Run(sm, 0.9f, 2f);
            Run(sm, 0.1f, 0.5f);
            Assert.AreEqual(PacingState.Relax, sm.State);

            // The player walked into a scripted beat; the director must not mute it.
            sm.ForceBuildUp();
            Assert.AreEqual(PacingState.BuildUp, sm.State);
            Assert.AreEqual(1, sm.CompletedCycles);
        }
    }
}
