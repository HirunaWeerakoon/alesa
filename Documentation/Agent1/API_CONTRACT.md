# Agent 1 (Alesa) — Inter-Agent Integration Contracts

## 1. Principles of Collaboration

To ensure smooth group integration without causing merge conflicts or breaking other teammates' code:
1. **Zero Direct Script Editing:** Agent 1 does not alter `PossessedBoss.cs`, `AmbushNode.cs`, `AcousticBus.cs`, or `PlayerGameplay.cs`.
2. **Contract via Public Methods & Events:** All interaction occurs through clean C# public methods, C# actions/events, or standard UnityEvents.
3. **Graceful Degradation:** If Agent 3 or Agent 4 are not yet active or configured in a scene, Agent 1 operates safely with standalone fallback mechanisms (e.g. manual Inspector test toggles).

---

## 2. Integration Interfaces

### A. Lifecycle Activation (Nathan rescues Alesa)

**Triggering Script:** `Assets/Scripts/DialpadKeypad.cs` or `Assets/Scripts/PlayerGameplay.cs`  
**Agent 1 Receiver:** `AlesaLifecycleController.cs`

```csharp
// Subscribes to DialpadKeypad.onAlesaFreed UnityEvent in Unity Inspector
// or hooks into PlayerGameplay state
public class AlesaLifecycleController : MonoBehaviour
{
    public bool IsAlesaActive { get; private set; }

    // Invoked when keypad unlocks or player frees Alesa
    public void ActivateAfterRescue()
    {
        IsAlesaActive = true;
        // Wakes up AlesaBrain from Dormant to Following
    }
}
```

### B. Hazard & Obstruction Contract (Boss / Ambush / Doors)

**Triggering Script:** Agent 3 (`PossessedBoss`), Agent 4 (`AmbushNode` / Encounter Director), or Door Triggers  
**Agent 1 Receiver:** `AlesaRouteBlocker.cs` / `AlesaNavigator.cs`

```csharp
public interface IAlesaRouteBlocker
{
    /// <summary>
    /// Blocks or unblocks a specific corridor edge in the hospital navigation graph.
    /// </summary>
    /// <param name="fromNodeId">Starting node ID of the corridor edge.</param>
    /// <param name="toNodeId">Ending node ID of the corridor edge.</param>
    /// <param name="isBlocked">True to block, false to restore.</param>
    /// <param name="reason">Descriptive reason (e.g. "Possessed Boss Occupying", "Collapsed Wall").</param>
    /// <param name="bidirectional">Whether both directions are blocked (default true for standard corridors).</param>
    void SetCorridorBlocked(string fromNodeId, string toNodeId, bool isBlocked, string reason, bool bidirectional = true);

    /// <summary>
    /// Temporarily applies a risk penalty to a corridor (e.g., enemy sounds or flickering lights).
    /// </summary>
    void SetCorridorRisk(string fromNodeId, string toNodeId, float riskPenalty, bool bidirectional = true);
}
```

### C. Recommended Route Notification (Outward Contract)

**Emitter:** `AlesaNavigator.cs`  
**Receivers (Optional Listeners):** Agent 4 (Encounter Director can plan ambushes along Nathan's expected path), Debug Gizmos, UI Minimap.

```csharp
public struct RouteUpdateInfo
{
    public string StartNodeId;
    public string ExitNodeId;
    public IReadOnlyList<string> PrimaryPathNodeIds;
    public float PrimaryPathCost;
    public int AlternativePathsCount;
    public int GraphRevision;
}

public class AlesaNavigator : MonoBehaviour
{
    // Event fired whenever the recommended escape route changes
    public event System.Action<RouteUpdateInfo> OnRecommendedRouteChanged;
}
```

---

## 3. Mock & Standalone Fallbacks for Testing

To allow isolated testing before Agent 3 and Agent 4 complete their implementations:
- An `AlesaMockHazardTrigger` component allows testing corridor invalidation in the editor via hotkeys (e.g., press `[B]` to simulate Boss blocking corridor A->B).
- An Inspector button triggers `ActivateAfterRescue()` for instant companion testing.

