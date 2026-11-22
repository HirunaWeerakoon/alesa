using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alesa.Core.Motion
{
    /// <summary>
    /// Geometry on a path polyline (the NavMesh corners), on the horizontal plane.
    /// </summary>
    public static class PathFollower
    {
        /// <summary>
        /// Pure-pursuit "carrot": project <paramref name="position"/> onto the path, then walk
        /// <paramref name="lookahead"/> metres further along it. Steering at this point instead of at the
        /// next corner makes the agent start turning before the corner, so corners come out rounded.
        /// </summary>
        public static Vector3 LookaheadPoint(IReadOnlyList<Vector3> path, Vector3 position, float lookahead)
        {
            if (path.Count == 0) return position;
            if (path.Count == 1) return path[0];

            Closest(path, position, out int segment, out float t);
            float remaining = lookahead;
            var from = Lerp(path[segment], path[segment + 1], t);
            for (int i = segment; i < path.Count - 1; i++)
            {
                var to = path[i + 1];
                float length = FlatDistance(from, to);
                if (length >= remaining) return from + (to - from) * (remaining / length);
                remaining -= length;
                from = to;
            }
            return path[path.Count - 1];
        }

        /// <summary>Distance along the path from the projection of <paramref name="position"/> to the end.</summary>
        public static float RemainingDistance(IReadOnlyList<Vector3> path, Vector3 position)
        {
            if (path.Count == 0) return 0f;
            if (path.Count == 1) return FlatDistance(position, path[0]);

            Closest(path, position, out int segment, out float t);
            var from = Lerp(path[segment], path[segment + 1], t);
            float total = 0f; // measured from the projection: sideways offset from the path isn't progress
            for (int i = segment; i < path.Count - 1; i++)
            {
                total += FlatDistance(from, path[i + 1]);
                from = path[i + 1];
            }
            return total;
        }

        /// <summary>
        /// The next interior corner ahead of <paramref name="position"/>: distance to it along the path and how
        /// sharply the path turns there (0° = straight on, 180° = U-turn). False if no corner remains.
        /// </summary>
        public static bool NextCorner(IReadOnlyList<Vector3> path, Vector3 position, out float distance, out float turnDegrees)
        {
            distance = 0f;
            turnDegrees = 0f;
            if (path.Count < 3) return false;

            Closest(path, position, out int segment, out float t);
            int corner = segment + 1;
            if (corner >= path.Count - 1) return false;

            distance = FlatDistance(Lerp(path[segment], path[segment + 1], t), path[corner]);
            var inDir = Flat(path[corner] - path[corner - 1]);
            var outDir = Flat(path[corner + 1] - path[corner]);
            if (inDir.LengthSquared() < 1e-8f || outDir.LengthSquared() < 1e-8f) return false;
            float cos = Math.Clamp(Vector3.Dot(Vector3.Normalize(inDir), Vector3.Normalize(outDir)), -1f, 1f);
            turnDegrees = (float)(Math.Acos(cos) * 180.0 / Math.PI);
            return true;
        }

        /// <summary>Segment index and parameter (0..1) of the closest point on the path.</summary>
        public static void Closest(IReadOnlyList<Vector3> path, Vector3 position, out int segment, out float t)
        {
            segment = 0;
            t = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = Flat(path[i]);
                var ab = Flat(path[i + 1]) - a;
                float lenSq = ab.LengthSquared();
                float u = lenSq < 1e-8f ? 0f : Math.Clamp(Vector3.Dot(Flat(position) - a, ab) / lenSq, 0f, 1f);
                float d = Vector3.DistanceSquared(Flat(position), a + ab * u);
                if (d < best - 1e-6f)
                {
                    best = d;
                    segment = i;
                    t = u;
                }
            }
        }

        public static float FlatDistance(Vector3 a, Vector3 b) => Vector3.Distance(Flat(a), Flat(b));

        private static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0f, v.Z);

        private static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    }
}
