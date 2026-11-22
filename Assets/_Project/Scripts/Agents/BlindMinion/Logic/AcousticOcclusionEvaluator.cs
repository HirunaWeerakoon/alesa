using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Material classifications for acoustic barriers within hospital architecture.
    /// Values represent baseline acoustic Transmission Loss (dB / loudness units).
    /// </summary>
    public enum AcousticMaterialType
    {
        Air = 0,
        GlassWindow = 8,
        WoodenDoor = 14,
        DrywallPartition = 18,
        HeavyFireDoor = 25,
        ReinforcedConcrete = 35
    }

    /// <summary>
    /// Represents an acoustic obstacle or barrier situated between sound source and listener.
    /// </summary>
    public readonly struct AcousticBarrier
    {
        public readonly Vector3 Position;
        public readonly AcousticMaterialType Material;
        public readonly float Thickness;

        public AcousticBarrier(Vector3 position, AcousticMaterialType material, float thickness = 0.2f)
        {
            Position = position;
            Material = material;
            Thickness = Math.Max(0.05f, thickness);
        }
    }

    /// <summary>
    /// Computes spatial sound occlusion, material transmission loss, and doorway diffraction.
    /// Simulates how physical obstacles muffle or redirect acoustic waveforms without invoking
    /// graphics rendering or physics raycasts.
    /// </summary>
    public static class AcousticOcclusionEvaluator
    {
        /// <summary>
        /// Transmission loss multiplier for a given material.
        /// Returns the decibel / loudness deduction per unit thickness.
        /// </summary>
        public static float GetMaterialDampingLoss(AcousticMaterialType material, float thickness = 1.0f)
        {
            float baseLoss = (float)material;
            return baseLoss * Math.Max(0.1f, thickness);
        }

        /// <summary>
        /// Computes portal diffraction attenuation. When sound passes through an open or ajar doorway,
        /// sound bends into adjacent corridors with frequency and aperture dependent loss.
        /// </summary>
        /// <param name="doorOpenFraction">0.0 = fully closed, 1.0 = wide open.</param>
        /// <param name="apertureWidth">Width of the portal in metres.</param>
        public static float CalculatePortalDiffractionLoss(float doorOpenFraction, float apertureWidth = 1.0f)
        {
            float clampedOpen = Math.Min(1.0f, Math.Max(0.0f, doorOpenFraction));
            if (clampedOpen >= 0.95f) return 0f; // wide open: negligible diffraction loss

            // Closed or ajar door: rapid transmission drop with non-linear acoustic leakage
            if (clampedOpen <= 0.05f) return 20.0f; // sealed boundary loss

            // Slit/ajar diffraction curve: log-scaled attenuation
            float effectiveAperture = clampedOpen * Math.Max(0.2f, apertureWidth);
            return (float)(-10.0 * Math.Log10(effectiveAperture / Math.Max(0.2f, apertureWidth))) + 3.0f;
        }

        /// <summary>
        /// Calculates total acoustic transmission loss along a direct line-of-hearing.
        /// Factors in distance spread, intermediate barriers, and doorway apertures.
        /// </summary>
        public static float CalculateTotalOcclusion(
            Vector3 source,
            Vector3 listener,
            IEnumerable<AcousticBarrier> intermediateBarriers,
            float portalOpenFraction = 1.0f)
        {
            float distance = FlatMath.Distance(source, listener);
            float totalLoss = 0f;

            if (intermediateBarriers != null)
            {
                foreach (var barrier in intermediateBarriers)
                {
                    totalLoss += GetMaterialDampingLoss(barrier.Material, barrier.Thickness);
                }
            }

            totalLoss += CalculatePortalDiffractionLoss(portalOpenFraction);
            return totalLoss;
        }

        /// <summary>
        /// Evaluates attenuated loudness perceived by listener after passing through intervening barriers.
        /// </summary>
        public static float ComputeAttenuatedLoudness(
            float initialLoudness,
            Vector3 source,
            Vector3 listener,
            IEnumerable<AcousticBarrier> barriers,
            float portalOpenFraction = 1.0f)
        {
            float dist = FlatMath.Distance(source, listener);
            float totalLoss = CalculateTotalOcclusion(source, listener, barriers, portalOpenFraction);
            return initialLoudness - dist - totalLoss;
        }
    }
}
