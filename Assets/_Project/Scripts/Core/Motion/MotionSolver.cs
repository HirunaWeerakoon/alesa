using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alesa.Core.Motion
{
    public struct MotionSettings
    {
        /// <summary>m/s² when speeding up / slowing down.</summary>
        public float Acceleration;
        public float Deceleration;

        /// <summary>Max yaw rate (°/s) while walking or running.</summary>
        public float TurnSpeed;

        /// <summary>Heading error (°) above which a slow agent stops and turns on the spot first.</summary>
        public float TurnInPlaceAngle;

        /// <summary>Yaw rate (°/s) when turning on the spot.</summary>
        public float TurnInPlaceSpeed;

        /// <summary>Pure-pursuit lookahead (m). Larger = rounder, wider corners.</summary>
        public float Lookahead;

        /// <summary>Distance (m) from the end of the path that counts as arrived.</summary>
        public float StoppingDistance;

        /// <summary>
        /// Speed kept through a corner: maxSpeed × (1 − CornerSlowdown × turnAngle/180°).
        /// 0 = take every corner flat out; 0.6 = a 90° corner at 70 %, a U-turn at 40 %.
        /// </summary>
        public float CornerSlowdown;

        public static MotionSettings Default => new MotionSettings
        {
            Acceleration = 8f,
            Deceleration = 12f,
            TurnSpeed = 300f,
            TurnInPlaceAngle = 75f,
            TurnInPlaceSpeed = 220f,
            Lookahead = 1.5f,
            StoppingDistance = 0.3f,
            CornerSlowdown = 0.6f,
        };
    }

    /// <summary>
    /// Turns a path (NavMesh corners) into believable body motion, one step per frame:
    ///   • steer at a lookahead point on the path (pure pursuit) so corners are rounded
    ///   • yaw is rate-limited, so the agent can never snap round
    ///   • if the path starts behind a slow agent it stops and turns on the spot first
    ///   • speed ramps with acceleration limits and brakes kinematically: v ≤ √(v_target² + 2·a·d),
    ///     both for the next corner (slower for sharper corners) and for the end of the path, so it
    ///     slows *before* a corner and stops exactly at the end without overshooting
    /// Pure C#: the Unity motor only feeds it the path and applies the result.
    /// Heading is Unity yaw in degrees: 0 = +Z, 90 = +X (clockwise seen from above).
    /// </summary>
    public sealed class MotionSolver
    {
        private const float TurnInPlaceExitAngle = 15f;
        private const float TurnInPlaceMaxSpeed = 0.5f;
        private const float FaceTolerance = 5f;

        public MotionSettings Settings;

        public Vector3 Position;
        public float Heading;
        public float Speed { get; private set; }

        /// <summary>Yaw rate this step (°/s), positive = turning right. Drives turn animations.</summary>
        public float AngularVelocity { get; private set; }

        public bool TurningInPlace { get; private set; }
        public bool Arrived { get; private set; } = true;
        public float RemainingDistance { get; private set; }

        public Vector3 Forward => ForwardOf(Heading);

        public MotionSolver(MotionSettings settings, Vector3 position, float heading)
        {
            Settings = settings;
            Position = position;
            Heading = heading;
        }

        /// <param name="path">Path corners, first = roughly the current position. Empty = no path.</param>
        /// <param name="maxSpeed">Speed the brain asked for.</param>
        /// <param name="lookAt">Optional point to face once there is nowhere to go.</param>
        public void Step(IReadOnlyList<Vector3> path, float maxSpeed, Vector3? lookAt, float dt)
        {
            if (dt <= 0f) return;
            var s = Settings;
            AngularVelocity = 0f;

            bool hasPath = path != null && path.Count >= 2;
            RemainingDistance = hasPath ? PathFollower.RemainingDistance(path, Position) : 0f;
            Arrived = !hasPath || RemainingDistance <= s.StoppingDistance;

            Vector3? face = null;
            float targetSpeed = 0f;
            if (!Arrived)
            {
                face = PathFollower.LookaheadPoint(path, Position, s.Lookahead);
                targetSpeed = maxSpeed;
            }
            else if (lookAt.HasValue)
            {
                face = lookAt;
            }

            if (face.HasValue && PathFollower.FlatDistance(Position, face.Value) > 0.05f)
            {
                float error = DeltaAngle(Heading, YawTowards(Position, face.Value));
                float absError = Math.Abs(error);

                // Hysteresis: start turning on the spot above TurnInPlaceAngle, keep going until nearly aligned.
                if (Arrived) TurningInPlace = absError > FaceTolerance;
                else if (TurningInPlace) TurningInPlace = absError > TurnInPlaceExitAngle;
                else TurningInPlace = absError > s.TurnInPlaceAngle && Speed < TurnInPlaceMaxSpeed;

                float rate = TurningInPlace ? s.TurnInPlaceSpeed : s.TurnSpeed;
                float turn = Math.Clamp(error, -rate * dt, rate * dt);
                Heading = NormalizeYaw(Heading + turn);
                AngularVelocity = turn / dt;

                if (TurningInPlace) targetSpeed = 0f;
            }
            else
            {
                TurningInPlace = false;
            }

            if (!Arrived)
            {
                // Anticipatory braking for the next corner...
                if (PathFollower.NextCorner(path, Position, out float cornerDistance, out float cornerAngle))
                {
                    float cornerSpeed = maxSpeed * (1f - s.CornerSlowdown * cornerAngle / 180f);
                    // Pure pursuit starts turning one lookahead before the corner, so be slow by then.
                    targetSpeed = Math.Min(targetSpeed, BrakingLimit(cornerSpeed, cornerDistance - s.Lookahead, s.Deceleration));
                }
                // ...and for the end of the path.
                float brakingRoom = Math.Max(0f, RemainingDistance - s.StoppingDistance);
                targetSpeed = Math.Min(targetSpeed, BrakingLimit(0f, brakingRoom, s.Deceleration));
            }

            float rateLimit = (targetSpeed > Speed ? s.Acceleration : s.Deceleration) * dt;
            Speed = Math.Max(0f, Speed + Math.Clamp(targetSpeed - Speed, -rateLimit, rateLimit));
            Position += Forward * (Speed * dt);
        }

        /// <summary>Fastest speed from which you can still slow to <paramref name="speedThere"/> within <paramref name="distance"/>.</summary>
        public static float BrakingLimit(float speedThere, float distance, float deceleration) =>
            (float)Math.Sqrt(speedThere * speedThere + 2f * deceleration * Math.Max(0f, distance));

        public static Vector3 ForwardOf(float yawDegrees)
        {
            double r = yawDegrees * Math.PI / 180.0;
            return new Vector3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
        }

        public static float YawTowards(Vector3 from, Vector3 to) =>
            (float)(Math.Atan2(to.X - from.X, to.Z - from.Z) * 180.0 / Math.PI);

        /// <summary>Shortest signed difference b − a in degrees, range (−180, 180].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        private static float NormalizeYaw(float yaw)
        {
            yaw %= 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }
    }
}
