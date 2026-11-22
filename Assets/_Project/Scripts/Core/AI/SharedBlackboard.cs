using System;
using System.Collections.Generic;

namespace Alesa.Core.AI
{
    /// <summary>
    /// Facts agents publish for each other, so no agent assembly references another.
    /// Alesa publishes her recommended route; the Director reads it and asks the boss to appear.
    /// </summary>
    public sealed class SharedBlackboard
    {
        private static readonly int[] EmptyRoute = Array.Empty<int>();

        /// <summary>Node ids of the route Alesa is currently recommending, player's position first.</summary>
        public IReadOnlyList<int> RecommendedRoute { get; private set; } = EmptyRoute;

        /// <summary>Alternative routes (2nd, 3rd shortest) Alesa is aware of.</summary>
        public IReadOnlyList<IReadOnlyList<int>> AlternativeRoutes { get; private set; } =
            Array.Empty<IReadOnlyList<int>>();

        public bool EscapePhaseActive { get; set; }
        public bool BossActive { get; set; }

        /// <summary>Graph node the boss currently occupies or is heading to; -1 when absent.</summary>
        public int BossNode { get; set; } = -1;

        public event Action RouteChanged;

        /// <summary>Raised by the Director with the chosen trigger node id.</summary>
        public event Action<int> BossAppearanceRequested;

        public void PublishRoutes(IReadOnlyList<int> recommended, IReadOnlyList<IReadOnlyList<int>> alternatives)
        {
            RecommendedRoute = recommended ?? EmptyRoute;
            AlternativeRoutes = alternatives ?? Array.Empty<IReadOnlyList<int>>();
            RouteChanged?.Invoke();
        }

        public void RequestBossAppearance(int triggerNodeId) => BossAppearanceRequested?.Invoke(triggerNodeId);
    }
}
