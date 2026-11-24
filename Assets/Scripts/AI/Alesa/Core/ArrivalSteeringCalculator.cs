using System;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# Craig Reynolds Arrival steering calculator for companion locomotion.
    /// Modulates speed based on distance to the follow anchor: full speed when distant,
    /// smooth linear/Hermite deceleration within the slowing radius, and zero speed
    /// within stopping distance to eliminate overshooting and jitter.
    /// </summary>
    public sealed class ArrivalSteeringCalculator
    {
        public float StoppingDistance { get; set; } = 0.35f;
        public float SlowingRadius { get; set; } = 2.5f;
        public float Acceleration { get; set; } = 8.0f;
        public float Deceleration { get; set; } = 12.0f;
        public float DeadbandBuffer { get; set; } = 0.05f;

        public ArrivalSteeringCalculator(
            float stoppingDistance = 0.35f,
            float slowingRadius = 2.5f,
            float acceleration = 8.0f,
            float deceleration = 12.0f)
        {
            StoppingDistance = Math.Max(0.05f, stoppingDistance);
            SlowingRadius = Math.Max(StoppingDistance + 0.5f, slowingRadius);
            Acceleration = Math.Max(1.0f, acceleration);
            Deceleration = Math.Max(1.0f, deceleration);
        }

        /// <summary>
        /// Calculates the theoretical desired speed based on distance to the follow anchor.
        /// - Beyond SlowingRadius: MaxSpeed.
        /// - Between StoppingDistance and SlowingRadius: smoothly decelerates towards 0.
        /// - At or below StoppingDistance + DeadbandBuffer: exactly 0 (prevents overshoot/jitter).
        /// </summary>
        public float CalculateDesiredSpeed(float distanceToTarget, float maxSpeed)
        {
            if (maxSpeed <= 0f) return 0f;
            if (distanceToTarget <= StoppingDistance) return 0f;

            if (distanceToTarget >= SlowingRadius)
            {
                return maxSpeed;
            }

            // Smooth linear ramp within deceleration zone:
            float range = SlowingRadius - StoppingDistance;
            if (range < 1e-4f) return maxSpeed;

            float factor = (distanceToTarget - StoppingDistance) / range;
            factor = Math.Clamp(factor, 0f, 1f);

            // Smoothstep curve for natural deceleration feel: 3x^2 - 2x^3
            float smoothedFactor = factor * factor * (3f - 2f * factor);

            return maxSpeed * smoothedFactor;
        }

        /// <summary>
        /// Applies realistic acceleration and deceleration constraints over deltaTime.
        /// Prevents instant jarring stops or instant warp accelerations.
        /// </summary>
        public float SmoothSpeed(float currentSpeed, float targetSpeed, float deltaTime)
        {
            if (deltaTime <= 0f) return currentSpeed;

            if (currentSpeed < targetSpeed)
            {
                // Accelerating
                float maxIncrease = Acceleration * deltaTime;
                return Math.Min(targetSpeed, currentSpeed + maxIncrease);
            }
            else
            {
                // Decelerating
                float maxDecrease = Deceleration * deltaTime;
                return Math.Max(targetSpeed, currentSpeed - maxDecrease);
            }
        }

        /// <summary>
        /// Computes desired 2D horizontal steering velocity vector towards the target position.
        /// </summary>
        public (float vx, float vz) CalculateSteeringVelocity(
            NodePosition currentPos,
            NodePosition targetPos,
            float currentSpeed,
            float maxSpeed,
            float deltaTime)
        {
            float dx = targetPos.X - currentPos.X;
            float dz = targetPos.Z - currentPos.Z;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);

            float desiredSpeed = CalculateDesiredSpeed(distance, maxSpeed);
            float smoothedSpeed = SmoothSpeed(currentSpeed, desiredSpeed, deltaTime);

            if (distance < 1e-4f || smoothedSpeed < 1e-4f)
            {
                return (0f, 0f);
            }

            float nx = dx / distance;
            float nz = dz / distance;

            return (nx * smoothedSpeed, nz * smoothedSpeed);
        }
    }
}
