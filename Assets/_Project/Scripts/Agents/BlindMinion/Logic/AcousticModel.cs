using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// How sound travels through the hospital. Instead of straight-line distance (which would let
    /// minions hear through solid walls), a noise floods outward along the maze graph with Dijkstra,
    /// losing loudness as it goes:
    ///   • 1 unit per metre of corridor travelled (<see cref="MazeEdge.Length"/>)
    ///   • <see cref="ClosedDoorLoss"/> when it passes a closed or locked door
    ///   • <see cref="JunctionLoss"/> each time it enters a junction, where the wave splits
    ///     between branches
    /// Loudness units are "metres of open-air reach", so a footstep of 8 is heard up to 8 m away
    /// down a straight empty corridor.
    /// </summary>
    public sealed class AcousticModel
    {
        public float ClosedDoorLoss { get; set; } = 20f;
        public float JunctionLoss { get; set; } = 1.5f;

        public EdgeCost CostFor(MazeGraph graph) => (edge, from) =>
        {
            float cost = edge.Length;
            if (edge.HasClosedDoor) cost += ClosedDoorLoss;
            if (graph[edge.Other(from)].Neighbours.Count > 2) cost += JunctionLoss;
            return cost;
        };

        /// <returns>The sound field, or null when the level has no maze graph.</returns>
        public SoundField Propagate(MazeGraph graph, NoiseEvent noise)
        {
            var seeds = new List<(int node, float cost)>();
            CollectNearbyNodes(graph, noise.Position, seeds);
            if (seeds.Count == 0) return null;

            var field = GraphSearch.DijkstraFromSeeds(graph, seeds, CostFor(graph), noise.Loudness);
            return new SoundField(noise, field);
        }

        /// <summary>
        /// The node nearest to <paramref name="position"/> plus its neighbours that are not behind a
        /// closed door, each paired with its flat distance. A point mid-corridor is "between" two nodes,
        /// so taking only the nearest one would overestimate the distance in one direction.
        /// </summary>
        public static void CollectNearbyNodes(MazeGraph graph, Vector3 position, List<(int node, float cost)> into)
        {
            int nearest = graph.NearestNode(position);
            if (nearest < 0) return;
            into.Add((nearest, FlatMath.Distance(position, graph[nearest].Position)));
            foreach (var edge in graph[nearest].Neighbours)
            {
                if (edge.HasClosedDoor) continue;
                int other = edge.Other(nearest);
                into.Add((other, FlatMath.Distance(position, graph[other].Position)));
            }
        }
    }

    /// <summary>One noise, propagated through the graph.</summary>
    public sealed class SoundField
    {
        public NoiseEvent Noise { get; }
        public CostField Field { get; }

        public SoundField(NoiseEvent noise, CostField field)
        {
            Noise = noise;
            Field = field;
        }

        public float LoudnessAtNode(int node) =>
            Field.Reached(node) ? Noise.Loudness - Field.Cost[node] : float.NegativeInfinity;

        /// <summary>
        /// Loudness a listener at <paramref name="listener"/> perceives, and the node it is heard
        /// through (where the listener should start walking along the sound's path).
        /// </summary>
        public float PerceivedAt(MazeGraph graph, Vector3 listener, out int heardThroughNode)
        {
            var candidates = new List<(int node, float cost)>();
            AcousticModel.CollectNearbyNodes(graph, listener, candidates);

            heardThroughNode = -1;
            float best = float.NegativeInfinity;
            foreach (var (node, distance) in candidates)
            {
                float loudness = LoudnessAtNode(node) - distance;
                if (loudness <= best) continue;
                best = loudness;
                heardThroughNode = node;
            }
            return best;
        }

        /// <summary>Next node toward the sound's origin; -1 once at a seed node.</summary>
        public int TowardSource(int node) => Field.Previous[node];
    }

    public static class FlatMath
    {
        /// <summary>Horizontal distance; agents' pivots and graph nodes sit at different heights.</summary>
        public static float Distance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)System.Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
