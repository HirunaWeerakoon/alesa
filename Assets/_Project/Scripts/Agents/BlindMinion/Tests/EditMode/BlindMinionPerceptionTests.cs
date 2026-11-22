using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    [TestFixture]
    public class BlindMinionPerceptionTests
    {
        #region Acoustic Occlusion Tests

        [Test]
        public void AcousticOcclusion_MaterialDampingLoss_CalculatesCorrectDecibelLoss()
        {
            // Drywall Partition has base loss 18 dB
            float drywallLoss = AcousticOcclusionEvaluator.GetMaterialDampingLoss(AcousticMaterialType.DrywallPartition, 0.5f);
            Assert.AreEqual(9.0f, drywallLoss, 0.001f);

            // Reinforced Concrete has base loss 35 dB
            float concreteLoss = AcousticOcclusionEvaluator.GetMaterialDampingLoss(AcousticMaterialType.ReinforcedConcrete, 2.0f);
            Assert.AreEqual(70.0f, concreteLoss, 0.001f);

            // Minimum thickness clamp: 0.01 clamped to 0.1
            float thinGlassLoss = AcousticOcclusionEvaluator.GetMaterialDampingLoss(AcousticMaterialType.GlassWindow, 0.01f);
            Assert.AreEqual(8.0f * 0.1f, thinGlassLoss, 0.001f);
        }

        [Test]
        public void AcousticOcclusion_PortalDiffraction_BoundaryAndApertureLosses()
        {
            // Fully open portal: zero diffraction loss
            float openLoss = AcousticOcclusionEvaluator.CalculatePortalDiffractionLoss(1.0f, 1.2f);
            Assert.AreEqual(0.0f, openLoss, 0.001f);

            // Fully closed portal: 20.0 dB sealed boundary loss
            float closedLoss = AcousticOcclusionEvaluator.CalculatePortalDiffractionLoss(0.0f, 1.2f);
            Assert.AreEqual(20.0f, closedLoss, 0.001f);

            // Half-open aperture: logarithmic diffraction attenuation
            float halfOpenLoss = AcousticOcclusionEvaluator.CalculatePortalDiffractionLoss(0.5f, 1.0f);
            float expectedAperture = (-10.0f * (float)Math.Log10(0.5f)) + 3.0f;
            Assert.AreEqual(expectedAperture, halfOpenLoss, 0.01f);
            Assert.IsTrue(halfOpenLoss > 0.0f && halfOpenLoss < 20.0f);
        }

        [Test]
        public void AcousticOcclusion_ComputeAttenuatedLoudness_FactorsDistanceAndMultiMaterialBarriers()
        {
            var source = new Vector3(0, 0, 0);
            var listener = new Vector3(10, 0, 0); // 10 metres away in X
            float initialLoudness = 60.0f; // Gunshot

            var barriers = new List<AcousticBarrier>
            {
                new AcousticBarrier(new Vector3(3, 0, 0), AcousticMaterialType.DrywallPartition, 0.5f), // 18 * 0.5 = 9.0 dB
                new AcousticBarrier(new Vector3(6, 0, 0), AcousticMaterialType.GlassWindow, 0.2f)       // 8 * 0.2 = 1.6 dB
            };

            // Total occlusion = distance (10) + drywall (9.0) + glass (1.6) + portal (0) = 20.6
            float totalOcclusion = AcousticOcclusionEvaluator.CalculateTotalOcclusion(source, listener, barriers, portalOpenFraction: 1.0f);
            Assert.AreEqual(10.6f, totalOcclusion, 0.001f);

            float perceived = AcousticOcclusionEvaluator.ComputeAttenuatedLoudness(initialLoudness, source, listener, barriers, portalOpenFraction: 1.0f);
            Assert.AreEqual(60.0f - 10.0f - 10.6f, perceived, 0.001f);
        }

        #endregion

        #region Noise Priority Queue Tests

        [Test]
        public void NoisePriorityQueue_OrdersByThreatScoreUnderLoad()
        {
            var queue = new NoisePriorityQueue(maxCapacity: 8);
            var minionPos = new Vector3(0, 0, 0);

            // Create various acoustic stimuli
            var footstep = new NoiseEvent(new Vector3(5, 0, 0), 8.0f, NoiseType.Footstep, 1.0f);
            var doorSlam = new NoiseEvent(new Vector3(10, 0, 0), 22.0f, NoiseType.DoorSlam, 1.2f);
            var gunshot = new NoiseEvent(new Vector3(20, 0, 0), 60.0f, NoiseType.Gunshot, 1.5f);
            var scream = new NoiseEvent(new Vector3(8, 0, 0), 30.0f, NoiseType.Scream, 1.6f);

            float footstepThreat = NoiseThreatEvaluator.CalculateThreatScore(footstep, 8.0f, minionPos);
            float doorThreat = NoiseThreatEvaluator.CalculateThreatScore(doorSlam, 22.0f, minionPos);
            float gunshotThreat = NoiseThreatEvaluator.CalculateThreatScore(gunshot, 40.0f, minionPos);
            float screamThreat = NoiseThreatEvaluator.CalculateThreatScore(scream, 25.0f, minionPos);

            queue.Enqueue(new PrioritizedNoise(footstep, 8.0f, footstepThreat, 1.0f, 1));
            queue.Enqueue(new PrioritizedNoise(gunshot, 40.0f, gunshotThreat, 1.5f, 2));
            queue.Enqueue(new PrioritizedNoise(doorThreat > 0 ? doorSlam : footstep, 22.0f, doorThreat, 1.2f, 3));
            queue.Enqueue(new PrioritizedNoise(scream, 25.0f, screamThreat, 1.6f, 4));

            Assert.AreEqual(4, queue.Count);

            // Dequeue should yield strictly descending threat scores
            float previousThreat = float.MaxValue;
            while (queue.TryDequeue(out var item))
            {
                Assert.IsTrue(item.ThreatScore <= previousThreat, "Items must be returned in descending threat order");
                previousThreat = item.ThreatScore;
            }
        }

        [Test]
        public void NoisePriorityQueue_EvictsLowestThreatWhenCapacityExceeded()
        {
            var queue = new NoisePriorityQueue(maxCapacity: 3);

            var eventLow = new NoiseEvent(Vector3.Zero, 5f, NoiseType.Footstep, 1f);
            var eventMed1 = new NoiseEvent(Vector3.Zero, 15f, NoiseType.Sprint, 1f);
            var eventMed2 = new NoiseEvent(Vector3.Zero, 20f, NoiseType.DoorSlam, 1f);
            var eventHigh = new NoiseEvent(Vector3.Zero, 60f, NoiseType.Gunshot, 1f);

            queue.Enqueue(new PrioritizedNoise(eventLow, 5f, 10f, 1f, 0));
            queue.Enqueue(new PrioritizedNoise(eventMed1, 15f, 30f, 1f, 0));
            queue.Enqueue(new PrioritizedNoise(eventMed2, 20f, 40f, 1f, 0));
            Assert.AreEqual(3, queue.Count);

            // Adding high threat must evict the lowest (threat 10)
            queue.Enqueue(new PrioritizedNoise(eventHigh, 60f, 100f, 1f, 0));
            Assert.AreEqual(3, queue.Count);

            Assert.IsTrue(queue.TryPeek(out var top));
            Assert.AreEqual(100f, top.ThreatScore, 0.001f);

            // Dequeue all and verify lowest (threat 10) is gone
            queue.TryDequeue(out _); // 100
            queue.TryDequeue(out _); // 40
            queue.TryDequeue(out var last); // 30
            Assert.AreEqual(30f, last.ThreatScore, 0.001f);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void NoisePriorityQueue_PruneExpired_RemovesStaleNoises()
        {
            var queue = new NoisePriorityQueue(maxCapacity: 5);
            var ev = new NoiseEvent(Vector3.Zero, 10f, NoiseType.Footstep, 0f);

            queue.Enqueue(new PrioritizedNoise(ev, 10f, 20f, timestamp: 2.0f, 0));
            queue.Enqueue(new PrioritizedNoise(ev, 10f, 50f, timestamp: 8.0f, 0));
            queue.Enqueue(new PrioritizedNoise(ev, 10f, 30f, timestamp: 9.5f, 0));

            // At t = 10.0 with maxAge = 3.0: items before 7.0 (i.e. t=2.0) should be purged
            queue.PruneExpired(currentTime: 10.0f, maxAge: 3.0f);
            Assert.AreEqual(2, queue.Count);

            Assert.IsTrue(queue.TryDequeue(out var item1));
            Assert.AreEqual(8.0f, item1.Timestamp, 0.001f);
            Assert.IsTrue(queue.TryDequeue(out var item2));
            Assert.AreEqual(9.5f, item2.Timestamp, 0.001f);
        }

        #endregion

        #region Sensory Memory Buffer Tests

        [Test]
        public void SensoryMemoryBuffer_CalculateAccumulatedExcitation_DecaysOverTime()
        {
            var buffer = new SensoryMemoryBuffer(capacity: 10);
            buffer.AddImpression(new Vector3(0, 0, 0), loudness: 20.0f, NoiseType.Sprint, timestamp: 2.0f, nodeId: 1);

            // At t = 2.0 (age = 0), excitation is at its peak
            float peak = buffer.CalculateAccumulatedExcitation(currentTime: 2.0f, halfLife: 5.0f);
            float expectedPeak = 20.0f * NoiseThreatEvaluator.GetCategoryWeight(NoiseType.Sprint);
            Assert.AreEqual(expectedPeak, peak, 0.01f);

            // At t = 7.0 (age = 5.0, exactly 1 half-life), excitation should halve
            float halfLifeDecay = buffer.CalculateAccumulatedExcitation(currentTime: 7.0f, halfLife: 5.0f);
            Assert.AreEqual(expectedPeak * 0.5f, halfLifeDecay, 0.01f);

            // At t = 12.0 (age = 10.0, 2 half-lives), excitation should be 25% of peak
            float quarterDecay = buffer.CalculateAccumulatedExcitation(currentTime: 12.0f, halfLife: 5.0f);
            Assert.AreEqual(expectedPeak * 0.25f, quarterDecay, 0.01f);
        }

        [Test]
        public void SensoryMemoryBuffer_TryGetAcousticHeading_CalculatesDirectionVector()
        {
            var buffer = new SensoryMemoryBuffer(capacity: 10);

            // Single impression cannot establish heading
            buffer.AddImpression(new Vector3(0, 0, 0), 10f, NoiseType.Footstep, timestamp: 1.0f, 0);
            Assert.IsFalse(buffer.TryGetAcousticHeading(1.1f, out _));

            // Second impression along +Z axis
            buffer.AddImpression(new Vector3(0, 0, 4.0f), 10f, NoiseType.Footstep, timestamp: 2.0f, 1);
            bool hasHeading = buffer.TryGetAcousticHeading(2.0f, out var direction);

            Assert.IsTrue(hasHeading);
            Assert.AreEqual(0.0f, direction.X, 0.01f);
            Assert.AreEqual(0.0f, direction.Y, 0.01f);
            Assert.AreEqual(1.0f, direction.Z, 0.01f);
        }

        #endregion

        #region Suspicion Decay Curve Tests

        [Test]
        public void SuspicionDecayCurve_LinearAndExponential_Boundaries()
        {
            // Linear Profile
            float linMid = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 15.0f, duration: 30.0f, DecayProfile.Linear);
            Assert.AreEqual(0.5f, linMid, 0.001f);

            float linZero = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 35.0f, duration: 30.0f, DecayProfile.Linear);
            Assert.AreEqual(0.0f, linZero, 0.001f);

            float linNeg = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: -1.0f, duration: 30.0f, DecayProfile.Linear);
            Assert.AreEqual(1.0f, linNeg, 0.001f);

            // Exponential Profile
            float expStart = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 0.0f, duration: 30.0f, DecayProfile.Exponential);
            Assert.AreEqual(1.0f, expStart, 0.001f);

            float expLater = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 15.0f, duration: 30.0f, DecayProfile.Exponential);
            Assert.IsTrue(expLater < 0.5f && expLater > 0.0f);
        }

        [Test]
        public void SuspicionDecayCurve_SigmoidalTension_MaintainsPlateauBeforeRollOff()
        {
            // Sigmoidal Tension maintains high alertness during early inspection
            float tEarly = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 3.0f, duration: 30.0f, DecayProfile.SigmoidalTension);
            float tLinearEarly = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 3.0f, duration: 30.0f, DecayProfile.Linear);

            // Early tension is retained significantly higher than naive linear decline
            Assert.IsTrue(tEarly > 0.95f, $"Early tension was {tEarly}, expected > 0.95");
            Assert.IsTrue(tEarly > tLinearEarly);

            // At duration completion, alertness drops to 0
            float tEnd = SuspicionDecayCurve.Evaluate(1.0f, elapsedSilence: 30.0f, duration: 30.0f, DecayProfile.SigmoidalTension);
            Assert.AreEqual(0.0f, tEnd, 0.001f);
        }

        [Test]
        public void SuspicionDecayCurve_ComputeReactionDelay_AcceleratesUnderAlertness()
        {
            float baseReaction = 0.6f;

            // Zero alertness: full reaction time
            float delayCalm = SuspicionDecayCurve.ComputeReactionDelay(baseReaction, alertness: 0.0f, maxSpeedup: 0.5f);
            Assert.AreEqual(0.6f, delayCalm, 0.001f);

            // Peak alertness: 50% accelerated reaction delay
            float delayAlert = SuspicionDecayCurve.ComputeReactionDelay(baseReaction, alertness: 1.0f, maxSpeedup: 0.5f);
            Assert.AreEqual(0.3f, delayAlert, 0.001f);
        }

        #endregion

        #region Minion Alert Relay Tests

        [Test]
        public void MinionAlertRelay_PreventsInfiniteBroadcastLoopsViaCooldown()
        {
            var bus = new NoiseBus();
            var relay = new MinionAlertRelay(bus) { ScreechRelayCooldown = 4.0f };
            int broadcastCount = 0;
            relay.ScreechRelayed += _ => broadcastCount++;

            // Initial broadcast succeeds
            bool first = relay.TryBroadcastScreech(minionId: 1, position: new Vector3(10, 0, 0), loudness: 30.0f, currentTime: 1.0f);
            Assert.IsTrue(first);
            Assert.AreEqual(1, broadcastCount);

            // Immediate secondary attempt within cooldown window is suppressed (prevents screech ripple storms)
            bool spam = relay.TryBroadcastScreech(minionId: 1, position: new Vector3(10, 0, 0), loudness: 30.0f, currentTime: 2.5f);
            Assert.IsFalse(spam);
            Assert.AreEqual(1, broadcastCount);

            // Attempt after cooldown expires succeeds
            bool postCooldown = relay.TryBroadcastScreech(minionId: 1, position: new Vector3(10, 0, 0), loudness: 30.0f, currentTime: 5.5f);
            Assert.IsTrue(postCooldown);
            Assert.AreEqual(2, broadcastCount);
        }

        [Test]
        public void MinionAlertRelay_CalculateGroupSuspicion_ProximityAndDecay()
        {
            var bus = new NoiseBus();
            var relay = new MinionAlertRelay(bus) { AlertDecayWindow = 10.0f };

            // Register heard scream from neighbor at (10, 0, 0)
            relay.RegisterHeardScreech(new NoiseEvent(new Vector3(10, 0, 0), 30f, NoiseType.Scream, time: 2.0f, sourceId: 2));

            // Close listener at (12, 0, 0) - distance 2m
            float nearSuspicion = relay.CalculateGroupSuspicion(new Vector3(12, 0, 0), currentTime: 2.0f);

            // Far listener at (50, 0, 0) - distance 40m
            float farSuspicion = relay.CalculateGroupSuspicion(new Vector3(50, 0, 0), currentTime: 2.0f);

            Assert.IsTrue(nearSuspicion > farSuspicion, "Near listener should experience higher group suspicion");
            Assert.IsTrue(nearSuspicion > 0.0f);

            // After AlertDecayWindow elapses (t = 15.0, elapsed = 13.0 > 10.0), suspicion must clear
            float expiredSuspicion = relay.CalculateGroupSuspicion(new Vector3(12, 0, 0), currentTime: 15.0f);
            Assert.AreEqual(0.0f, expiredSuspicion, 0.001f);
        }

        #endregion
    }
}
