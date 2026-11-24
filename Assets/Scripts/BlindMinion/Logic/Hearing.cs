using System;
using System.Numerics;

namespace Alesa.BlindMinion
{
    /// <summary>
    /// First hearing model: loudness fades by one unit per metre of straight-line distance.
    ///
    /// Known limitation: sound goes straight through walls, so a minion can hear a footstep in
    /// the next corridor as if the wall weren't there. The next version will send sound along
    /// the corridors instead (graph + Dijkstra).
    /// </summary>
    public static class Hearing
    {
        /// <summary>How loud <paramref name="noise"/> is at <paramref name="listener"/>; ≤ 0 means silent.</summary>
        public static float Perceived(NoiseEvent noise, Vector3 listener) =>
            noise.Loudness - FlatDistance(noise.Position, listener);

        /// <summary>Distance on the floor plane; a noise at head height shouldn't count as further away.</summary>
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
