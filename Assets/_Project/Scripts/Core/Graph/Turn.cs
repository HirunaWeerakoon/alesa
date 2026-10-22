using System;
using System.Numerics;

namespace Alesa.Core.Graph
{
    public enum TurnDirection { Straight, Left, Right, Back }

    /// <summary>
    /// Classifies a move relative to the way someone is facing, on the horizontal (XZ) plane.
    /// Alesa uses it for her call-outs; the Director uses it to reject triggers behind the player.
    /// Unity is left-handed with +Y up, so a positive Y cross product means "to the right".
    /// </summary>
    public static class Turn
    {
        /// <param name="straightConeDegrees">Half-angle treated as "straight ahead".</param>
        public static TurnDirection Classify(Vector3 forward, Vector3 from, Vector3 to,
            float straightConeDegrees = 30f)
        {
            var f = Flatten(forward);
            var d = Flatten(to - from);
            if (f == Vector3.Zero || d == Vector3.Zero) return TurnDirection.Straight;

            float angle = SignedAngleDegrees(f, d);
            float abs = Math.Abs(angle);
            if (abs <= straightConeDegrees) return TurnDirection.Straight;
            if (abs >= 180f - straightConeDegrees) return TurnDirection.Back;
            return angle > 0 ? TurnDirection.Right : TurnDirection.Left;
        }

        /// <summary>Signed angle from a to b around +Y, in degrees, range (-180, 180].</summary>
        public static float SignedAngleDegrees(Vector3 a, Vector3 b)
        {
            a = Flatten(a);
            b = Flatten(b);
            float dot = Vector3.Dot(a, b);
            float crossY = a.Z * b.X - a.X * b.Z;
            return (float)(Math.Atan2(crossY, dot) * 180.0 / Math.PI);
        }

        public static Vector3 Flatten(Vector3 v)
        {
            var flat = new Vector3(v.X, 0f, v.Z);
            float len = flat.Length();
            return len < 1e-5f ? Vector3.Zero : flat / len;
        }
    }
}
