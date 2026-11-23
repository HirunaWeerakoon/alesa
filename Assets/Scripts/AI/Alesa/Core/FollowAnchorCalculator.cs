using System;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# geometric calculator for companion trailing anchor positioning.
    /// Computes safe formation coordinates behind Nathan, enforces personal space buffers,
    /// and applies exponential smoothing to prevent target jitter during sharp turns.
    /// </summary>
    public sealed class FollowAnchorCalculator
    {
        public float PreferredDistance { get; set; } = 1.75f;
        public float LateralOffset { get; set; } = 0.0f;
        public float MinSeparation { get; set; } = 1.0f;

        public FollowAnchorCalculator(float preferredDistance = 1.75f, float minSeparation = 1.0f, float lateralOffset = 0.0f)
        {
            PreferredDistance = Math.Max(0.5f, preferredDistance);
            MinSeparation = Math.Max(0.3f, minSeparation);
            LateralOffset = lateralOffset;
        }

        /// <summary>
        /// Calculates the ideal trailing anchor point behind the player based on position and forward heading.
        /// Target = PlayerPosition - (PlayerForward * PreferredDistance) + (Perpendicular * LateralOffset)
        /// </summary>
        public NodePosition CalculateIdealAnchor(NodePosition playerPos, NodePosition playerForward)
        {
            float fx = playerForward.X;
            float fz = playerForward.Z;
            float mag = (float)Math.Sqrt(fx * fx + fz * fz);

            if (mag < 1e-4f)
            {
                // Fallback if player forward vector is zero
                return new NodePosition(playerPos.X, playerPos.Y, playerPos.Z - PreferredDistance);
            }

            fx /= mag;
            fz /= mag;

            // Trailing position: opposite of player facing direction
            float targetX = playerPos.X - fx * PreferredDistance;
            float targetZ = playerPos.Z - fz * PreferredDistance;

            // Optional lateral offset (right-perpendicular vector on X-Z plane: (fz, -fx))
            if (Math.Abs(LateralOffset) > 1e-4f)
            {
                targetX += fz * LateralOffset;
                targetZ += -fx * LateralOffset;
            }

            return new NodePosition(targetX, playerPos.Y, targetZ);
        }

        /// <summary>
        /// Enforces personal space buffer. If candidate anchor is too close to the player,
        /// pushes it outward to at least MinSeparation.
        /// </summary>
        public NodePosition ClampPersonalSpace(NodePosition playerPos, NodePosition candidateAnchor)
        {
            float dist = playerPos.FlatDistanceTo(candidateAnchor);
            if (dist < MinSeparation)
            {
                if (dist > 1e-4f)
                {
                    float factor = MinSeparation / dist;
                    float dx = (candidateAnchor.X - playerPos.X) * factor;
                    float dz = (candidateAnchor.Z - playerPos.Z) * factor;
                    return new NodePosition(playerPos.X + dx, playerPos.Y, playerPos.Z + dz);
                }

                // If exactly overlapping, push backward along Z
                return new NodePosition(playerPos.X, playerPos.Y, playerPos.Z - MinSeparation);
            }

            return candidateAnchor;
        }

        /// <summary>
        /// Applies exponential damping to anchor position to prevent whip-around jitter when Nathan spins quickly.
        /// </summary>
        public NodePosition SmoothAnchor(NodePosition currentAnchor, NodePosition targetAnchor, float deltaTime, float smoothSpeed = 8.0f)
        {
            if (deltaTime <= 0f) return targetAnchor;

            float t = 1.0f - (float)Math.Exp(-smoothSpeed * deltaTime);
            float x = currentAnchor.X + (targetAnchor.X - currentAnchor.X) * t;
            float y = currentAnchor.Y + (targetAnchor.Y - currentAnchor.Y) * t;
            float z = currentAnchor.Z + (targetAnchor.Z - currentAnchor.Z) * t;
            return new NodePosition(x, y, z);
        }
    }
}
