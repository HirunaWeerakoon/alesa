using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.Graph;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Candidate tactical approach waypoint evaluated for investigative navigation.
    /// </summary>
    public readonly struct InvestigativeWaypoint
    {
        public readonly Vector3 Position;
        public readonly float TacticalScore;
        public readonly float DistanceFromMinion;
        public readonly float AngleOffsetDegrees;

        public InvestigativeWaypoint(Vector3 position, float tacticalScore, float distanceFromMinion, float angleOffsetDegrees)
        {
            Position = position;
            TacticalScore = tacticalScore;
            DistanceFromMinion = distanceFromMinion;
            AngleOffsetDegrees = angleOffsetDegrees;
        }
    }

    /// <summary>
    /// Generates tactical approach vectors, search offsets, and flanking waypoints around acoustic loci.
    /// Evaluates candidate search angles to clear corridor corners and room blind-spots instead of
    /// walking in a naive straight line directly onto the perceived point.
    /// </summary>
    public static class InvestigativeWaypointGenerator
    {
        /// <summary>
        /// Generates ranked approach candidate waypoints surrounding a target noise location.
        /// Samples multiple approach angles and offsets to provide tactical flank paths.
        /// </summary>
        /// <param name="minionPosition">Current position of the investigating agent.</param>
        /// <param name="targetLocus">Acoustic epicenter or LKP.</param>
        /// <param name="standoffDistance">Desired offset from target (e.g. 1.5 - 3.0 m).</param>
        /// <param name="sampleAngles">Number of radial approach samples to evaluate.</param>
        public static List<InvestigativeWaypoint> GenerateApproachWaypoints(
            Vector3 minionPosition,
            Vector3 targetLocus,
            float standoffDistance = 2.0f,
            int sampleAngles = 8)
        {
            var candidates = new List<InvestigativeWaypoint>();
            Vector3 toTarget = targetLocus - minionPosition;
            float distToTarget = FlatMath.Distance(minionPosition, targetLocus);

            if (distToTarget < 0.1f)
            {
                candidates.Add(new InvestigativeWaypoint(targetLocus, 1.0f, 0f, 0f));
                return candidates;
            }

            float baseAngle = (float)Math.Atan2(toTarget.Z, toTarget.X);
            float angleStep = (float)(2.0 * Math.PI / Math.Max(4, sampleAngles));

            for (int i = 0; i < sampleAngles; i++)
            {
                float angle = baseAngle + (i - sampleAngles / 2) * angleStep;
                float angleDegrees = (float)((i - sampleAngles / 2) * (180.0 / Math.PI) * angleStep);

                // Compute candidate offset position
                float offsetX = (float)Math.Cos(angle) * standoffDistance;
                float offsetZ = (float)Math.Sin(angle) * standoffDistance;
                Vector3 candidatePos = targetLocus + new Vector3(offsetX, 0f, offsetZ);

                float distFromMinion = FlatMath.Distance(minionPosition, candidatePos);

                // Tactical score: balance directness with flanking coverage
                // Direct approach has angle penalty to prevent rushing into potential ambushes
                float anglePenalty = Math.Abs(angleDegrees) / 180.0f;
                float proximityScore = 1.0f / (1.0f + 0.1f * distFromMinion);
                float tacticalScore = proximityScore * (1.0f - 0.3f * anglePenalty);

                candidates.Add(new InvestigativeWaypoint(candidatePos, tacticalScore, distFromMinion, angleDegrees));
            }

            // Rank highest tactical score first
            candidates.Sort((a, b) => b.TacticalScore.CompareTo(a.TacticalScore));
            return candidates;
        }

        /// <summary>
        /// Identifies corner clearance waypoints when approaching a junction or doorway.
        /// Generates peeking coordinates that maximize view into the perpendicular hallway.
        /// </summary>
        public static Vector3 CalculateCornerClearanceOffset(Vector3 corridorDirection, Vector3 cornerNodePosition, float clearanceOffset = 1.2f)
        {
            // Perpendicular lateral normal in horizontal plane (X-Z)
            Vector3 lateral = Vector3.Normalize(new Vector3(-corridorDirection.Z, 0f, corridorDirection.X));
            return cornerNodePosition + lateral * clearanceOffset;
        }

        /// <summary>
        /// Filters generated waypoints against the maze graph to ensure only reachable points
        /// near valid navigable nodes are retained.
        /// </summary>
        public static List<InvestigativeWaypoint> FilterReachableWaypoints(
            MazeGraph graph,
            IEnumerable<InvestigativeWaypoint> waypoints,
            float maxAcceptableGraphDistance = 2.5f)
        {
            var valid = new List<InvestigativeWaypoint>();
            if (graph == null) return valid;

            foreach (var wp in waypoints)
            {
                int nearestNode = graph.NearestNode(wp.Position);
                if (nearestNode < 0) continue;

                float distToGraph = FlatMath.Distance(wp.Position, graph[nearestNode].Position);
                if (distToGraph <= maxAcceptableGraphDistance)
                {
                    valid.Add(wp);
                }
            }

            return valid;
        }
    }
}
