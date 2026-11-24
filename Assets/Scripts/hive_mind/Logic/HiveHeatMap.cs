using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;

namespace Alesa.HiveMind
{
    /// <summary>
    /// The hive's shared memory of where the player has been making noise.
    ///
    /// Every node of the <see cref="MazeGraph"/> carries a "heat" value in 0..1. A noise floods
    /// outward through the graph with Dijkstra, depositing heat that falls off with acoustic
    /// distance, and all heat then decays exponentially so old information fades on its own.
    ///
    /// This is deliberately NOT the player's actual position. It is what the hive could plausibly
    /// have inferred from sound alone, which is why a player who stops moving becomes genuinely
    /// hard to find, and why sprinting past a junction leaves a trail the director can exploit.
    ///
    /// Pure C# with no UnityEngine reference, so it is fully unit-testable without a scene.
    /// </summary>
    public sealed class HiveHeatMap
    {
        private readonly MazeGraph _graph;
        private readonly float[] _heat;

        /// <summary>Noises waiting to be folded in. Drained on <see cref="Tick"/>.</summary>
        private readonly List<NoiseEvent> _pending = new List<NoiseEvent>();

        private float _lastTickTime;
        private bool _started;

        /// <summary>
        /// Acoustic cost per edge. Injected so the director attenuates sound exactly the way the
        /// minions hear it; if these two disagreed, the hive would stage ambushes based on noises
        /// its own minions never heard. The Unity adapter passes the minions' AcousticModel here.
        /// </summary>
        public EdgeCost AcousticCost { get; set; }

        /// <summary>
        /// Seconds for heat to halve. Exponential rather than linear, so the fade rate is
        /// proportional to what is left: a gunshot and a footstep both lose half their remaining
        /// heat in the same time, which keeps pacing predictable regardless of magnitude.
        /// </summary>
        public float HalfLifeSeconds { get; set; } = 8f;

        /// <summary>
        /// Noises quieter than this are ignored entirely. Crouch-walking should be genuinely safe,
        /// and it keeps the per-noise Dijkstra off the hot path for sounds that cannot matter.
        /// </summary>
        public float MinLoudness { get; set; } = 1.5f;

        /// <summary>
        /// Upper bound on Dijkstra floods per tick. Footsteps fire several times a second and each
        /// flood allocates two arrays the width of the graph. When more arrive than this, the
        /// loudest win, because a gunshot tells the hive far more than the footsteps around it.
        /// </summary>
        public int MaxNoisesPerTick { get; set; } = 4;

        public HiveHeatMap(MazeGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _heat = new float[graph.Count];
        }

        /// <summary>Heat at a node, 0 (silent) to 1 (the player was just loudly here).</summary>
        public float HeatAt(int node) => (uint)node < (uint)_heat.Length ? _heat[node] : 0f;

        /// <summary>Total heat across the graph. Pacing reads this as "how exposed is the player".</summary>
        public float TotalHeat
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < _heat.Length; i++) sum += _heat[i];
                return sum;
            }
        }

        /// <summary>Node the hive believes the player is nearest; -1 when it has lost them.</summary>
        public int HottestNode { get; private set; } = -1;

        /// <summary>Heat at <see cref="HottestNode"/>; 0 when the hive has no idea.</summary>
        public float HottestHeat { get; private set; }

        /// <summary>Queues a noise. Cheap on purpose: the flood happens in <see cref="Tick"/>.</summary>
        public void Observe(NoiseEvent noise)
        {
            // Only the player and the world are worth tracking. Minions stamp their own source id,
            // so this stops the hive chasing the echo of its own screeching.
            if (noise.SourceId != 0) return;
            if (!(noise.Loudness >= MinLoudness)) return; // also rejects NaN
            _pending.Add(noise);
        }

        /// <summary>
        /// Advances the map to <paramref name="now"/>: decays what is there, then folds in the
        /// noises queued since the last call.
        /// </summary>
        public void Tick(float now)
        {
            if (!_started)
            {
                // First tick establishes the clock; decaying from time zero would wipe everything.
                _started = true;
                _lastTickTime = now;
            }

            float dt = now - _lastTickTime;
            if (dt > 0f) Decay(dt);
            _lastTickTime = now;

            DrainPending();
            RecomputeHottest();
        }

        /// <summary>Multiplies all heat by 0.5^(dt/halfLife) - the exponential decay curve.</summary>
        private void Decay(float dt)
        {
            if (HalfLifeSeconds <= 0f) { Array.Clear(_heat, 0, _heat.Length); return; }

            float factor = (float)Math.Pow(0.5, dt / HalfLifeSeconds);
            for (int i = 0; i < _heat.Length; i++)
            {
                if (_heat[i] <= 0f) continue;              // already cold, nothing to do
                _heat[i] *= factor;
                if (_heat[i] < 0.001f) _heat[i] = 0f;      // snap to zero so cold nodes stay cheap
            }
        }

        private void DrainPending()
        {
            if (_pending.Count == 0) return;

            // More noises than we will process: keep the loudest, since those carry the most
            // information and reach furthest. Descending sort, then take MaxNoisesPerTick.
            if (_pending.Count > MaxNoisesPerTick)
                _pending.Sort((a, b) => b.Loudness.CompareTo(a.Loudness));

            int count = Math.Min(_pending.Count, MaxNoisesPerTick);
            for (int i = 0; i < count; i++) Deposit(_pending[i]);
            _pending.Clear();
        }

        /// <summary>Floods one noise through the graph and adds the heat it leaves behind.</summary>
        private void Deposit(NoiseEvent noise)
        {
            var seeds = new List<(int node, float cost)>();
            CollectSeeds(noise.Position, seeds);
            if (seeds.Count == 0) return;

            float weight = Weight(noise.Type);
            if (weight <= 0f) return;

            // maxCost = Loudness, so the flood stops exactly where the sound becomes inaudible.
            // Every node it reaches therefore has Cost < Loudness.
            var field = GraphSearch.DijkstraFromSeeds(
                _graph, seeds, AcousticCost ?? DefaultAcousticCost(), noise.Loudness);

            for (int n = 0; n < _heat.Length; n++)
            {
                if (!field.Reached(n)) continue;

                // Normalised so absolute loudness does not matter: 1 right at the source, falling
                // to 0 at the edge of earshot. Without this a gunshot would saturate every node it
                // touched, erasing any sense of direction.
                float strength = 1f - field.Cost[n] / noise.Loudness;
                if (strength <= 0f) continue;

                // Saturating add, not a replace: two noises in the same corridor should reinforce,
                // but heat must stay in 0..1 so the pacing thresholds mean something stable.
                _heat[n] = Math.Min(1f, _heat[n] + strength * weight);
            }
        }

        /// <summary>
        /// The node nearest the noise plus its immediate neighbours, each seeded with its real
        /// distance from the noise. A gunshot halfway down a corridor is between two nodes, so
        /// seeding only the nearest would place it at one end and skew every direction estimate.
        /// </summary>
        private void CollectSeeds(Vector3 position, List<(int node, float cost)> into)
        {
            int nearest = _graph.NearestNode(position);
            if (nearest < 0) return;

            into.Add((nearest, FlatDistance(position, _graph[nearest].Position)));
            foreach (var edge in _graph[nearest].Neighbours)
            {
                if (edge.HasClosedDoor) continue;   // sound does not leak round a shut door at full strength
                int other = edge.Other(nearest);
                into.Add((other, FlatDistance(position, _graph[other].Position)));
            }
        }

        private void RecomputeHottest()
        {
            int best = -1;
            float bestHeat = 0f;
            for (int i = 0; i < _heat.Length; i++)
            {
                if (_heat[i] <= bestHeat) continue;
                bestHeat = _heat[i];
                best = i;
            }
            HottestNode = best;
            HottestHeat = bestHeat;
        }

        /// <summary>
        /// How much heat each kind of sound is worth. A gunshot is unambiguous and carries far, so
        /// it pins the player down; a single footstep is weak evidence and should not, on its own,
        /// bring the hive down on someone moving carefully.
        /// </summary>
        public static float Weight(NoiseType type)
        {
            switch (type)
            {
                case NoiseType.Gunshot: return 1.0f;
                case NoiseType.DoorSlam: return 0.7f;
                case NoiseType.Trolley: return 0.7f;
                case NoiseType.ThrownObject: return 0.5f;
                case NoiseType.Sprint: return 0.6f;
                case NoiseType.Footstep: return 0.3f;
                // A scream is a minion's own noise. It reaches here only if something mis-stamps
                // the source id, and it must never be read as evidence about the player.
                case NoiseType.Scream: return 0f;
                default: return 0.3f;
            }
        }

        /// <summary>
        /// Fallback attenuation matching the minions' AcousticModel defaults, used when nothing
        /// injects <see cref="AcousticCost"/>. Costs are in "metres of reach consumed".
        /// </summary>
        public EdgeCost DefaultAcousticCost() => (edge, from) =>
        {
            float cost = edge.Length;
            if (edge.HasClosedDoor) cost += 20f;                              // a shut door is most of a corridor
            if (_graph[edge.Other(from)].Neighbours.Count > 2) cost += 1.5f;  // the wave splits at a junction
            return cost;
        };

        /// <summary>Horizontal distance. Node heights and emitter heights differ and must not count.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Wipes all heat. Used when the hive loses the player completely, or on reload.</summary>
        public void Clear()
        {
            Array.Clear(_heat, 0, _heat.Length);
            _pending.Clear();
            HottestNode = -1;
            HottestHeat = 0f;
        }
    }
}
