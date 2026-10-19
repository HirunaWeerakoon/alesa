using System.Numerics;
using NUnit.Framework;

namespace Alesa.BlindMinion.Tests
{
    public class BlindMinionBrainTests
    {
        private NoiseBus _bus;

        [SetUp]
        public void CreateBus() => _bus = new NoiseBus();

        private BlindMinionBrain MinionAt(float x, float z) =>
            new BlindMinionBrain(_bus) { CurrentPosition = new Vector3(x, 0f, z) };

        private void Emit(float x, float z, float loudness, NoiseType type = NoiseType.Footstep) =>
            _bus.Emit(new NoiseEvent(new Vector3(x, 0f, z), loudness, type));

        [Test]
        public void Hearing_LosesOneUnitPerMetre_OnTheFloorPlane()
        {
            var gunshotAtHeadHeight = new NoiseEvent(new Vector3(0f, 1.6f, 0f), 30f, NoiseType.Gunshot);
            Assert.AreEqual(20f, Hearing.Perceived(gunshotAtHeadHeight, new Vector3(10f, 0f, 0f)), 1e-4f);
        }

        [Test]
        public void StartsAsleep()
        {
            var minion = MinionAt(0f, 0f);
            Assert.AreEqual(MinionState.Dormant, minion.State);
            Assert.IsFalse(minion.WantsToMove);
        }

        [Test]
        public void QuietFarNoise_IsIgnored()
        {
            var minion = MinionAt(0f, 0f);
            Emit(10f, 0f, 8f); // 8 − 10 m = −2
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void HeardNoise_AlertsThenInvestigatesTheSpot()
        {
            var minion = MinionAt(0f, 0f);
            Emit(2f, 0f, 8f); // 8 − 2 m = 6 ≥ 5

            Assert.AreEqual(MinionState.Alerted, minion.State);
            Assert.AreEqual(6f, minion.LastPerceivedLoudness, 1e-4f);
            Assert.IsFalse(minion.WantsToMove, "still reacting");

            minion.Tick(0.7f);
            Assert.AreEqual(MinionState.Investigating, minion.State);
            Assert.IsTrue(minion.WantsToMove);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), minion.NoisePosition);
        }

        [Test]
        public void ReachingTheSpot_SearchesThenSleepsAgain()
        {
            var minion = MinionAt(0f, 0f);
            Emit(2f, 0f, 8f);
            minion.Tick(0.7f);

            minion.CurrentPosition = new Vector3(2f, 0f, 0f); // walked there
            minion.Tick(0.1f);
            Assert.AreEqual(MinionState.Searching, minion.State);

            minion.Tick(minion.SearchDuration + 0.1f);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }

        [Test]
        public void NewNoiseWhileSearching_SendsItInvestigatingAgain()
        {
            var minion = MinionAt(0f, 0f);
            Emit(2f, 0f, 8f);
            minion.Tick(0.7f);
            minion.CurrentPosition = new Vector3(2f, 0f, 0f);
            minion.Tick(0.1f);

            Emit(4f, 0f, 8f);
            Assert.AreEqual(MinionState.Investigating, minion.State);
            Assert.AreEqual(new Vector3(4f, 0f, 0f), minion.NoisePosition);
        }

        [Test]
        public void StopsListening_AfterDispose()
        {
            var minion = MinionAt(0f, 0f);
            minion.Dispose();
            Emit(1f, 0f, 50f, NoiseType.Gunshot);
            Assert.AreEqual(MinionState.Dormant, minion.State);
        }
    }
}
