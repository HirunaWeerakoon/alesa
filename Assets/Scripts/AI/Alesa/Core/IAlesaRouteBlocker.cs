namespace Alesa.Core
{
    /// <summary>
    /// Inter-agent contract interface for environmental hazards and corridor obstructions.
    /// Implemented by Agent 1 to receive external block/risk events from Agent 3 (PossessedBoss)
    /// and Agent 4 (Encounter Director) without direct script modification or tight coupling.
    /// </summary>
    public interface IAlesaRouteBlocker
    {
        /// <summary>
        /// Blocks or unblocks a specific corridor edge in the hospital navigation graph.
        /// </summary>
        void SetCorridorBlocked(string fromNodeId, string toNodeId, bool isBlocked, string reason, bool bidirectional = true);

        /// <summary>
        /// Applies a risk penalty to a corridor (e.g. supernatural presence, flickering lights).
        /// </summary>
        void SetCorridorRisk(string fromNodeId, string toNodeId, float riskPenalty, bool bidirectional = true);
    }
}
