using System;
using System.Collections.Generic;
using System.Linq;

namespace Alesa.Core
{
    /// <summary>
    /// Immutable result containing ranked alternative routes calculated by Yen's K-Shortest Paths algorithm.
    /// Routes are sorted in non-decreasing order of total traversal cost.
    /// </summary>
    public sealed class MultiRouteResult
    {
        public RouteStatus Status { get; }
        public IReadOnlyList<RouteResult> Routes { get; }
        public int GraphVersion { get; }
        public string Message { get; }

        public int Count => Routes?.Count ?? 0;
        public bool HasRoutes => Count > 0 && Routes[0].IsValid;

        /// <summary>
        /// The primary (optimal) shortest route, or null if no valid path exists.
        /// </summary>
        public RouteResult BestRoute => HasRoutes ? Routes[0] : null;

        private MultiRouteResult(RouteStatus status, IReadOnlyList<RouteResult> routes, int graphVersion, string message)
        {
            Status = status;
            Routes = routes ?? Array.Empty<RouteResult>();
            GraphVersion = graphVersion;
            Message = message ?? string.Empty;
        }

        public static MultiRouteResult Success(IReadOnlyList<RouteResult> routes, int graphVersion)
        {
            if (routes == null || routes.Count == 0 || !routes[0].IsValid)
                throw new ArgumentException("Success result requires at least one valid route.", nameof(routes));

            return new MultiRouteResult(RouteStatus.Valid, routes, graphVersion, $"Successfully found {routes.Count} route(s).");
        }

        public static MultiRouteResult Failure(RouteStatus status, string message, int graphVersion)
        {
            if (status == RouteStatus.Valid)
                throw new ArgumentException("Cannot create failure result with RouteStatus.Valid.", nameof(status));

            return new MultiRouteResult(status, Array.Empty<RouteResult>(), graphVersion, message);
        }

        public RouteResult GetRoute(int index)
        {
            if (Routes != null && index >= 0 && index < Routes.Count)
                return Routes[index];
            return null;
        }

        public override string ToString()
        {
            if (!HasRoutes)
                return $"MultiRouteResult: {Status} | Message='{Message}' (v{GraphVersion})";

            var summary = string.Join(" | ", Routes.Select((r, idx) => $"[#{idx + 1}: Cost={r.TotalCost:F1}, Hops={r.NodeCount}]"));
            return $"MultiRouteResult: {Count} Route(s) -> {summary} (v{GraphVersion})";
        }
    }
}
