# Agent 1 (Alesa) — Test Execution Evidence Matrix

**Date Recorded:** 13 November 2026  
**Environment:** macOS (Darwin arm64), .NET SDK 10.0.302, standalone console harness  
**Test Suite:** `Tests/Alesa.Logic.Checks`  
**Execution Command:** `dotnet run --project Tests/Alesa.Logic.Checks`

---

## Acceptance Test Execution Log

| Test ID | Scenario / Verification | Expected Result | Actual Result | Status |
|---|---|---|---|:---:|
| **Test 01** | `MinHeap` priority queue ordering & duplicate tie-breaking | Items dequeued in ascending priority order; identical priorities resolved via FIFO insertion order. | Dequeued in exact order: A (2.1), B1 (5.0), B2 (5.0), C (10.5), D (15.0). Heap empty at end. | **PASS** |
| **Test 02** | `WaypointGraph` construction & revision version tracking | Adding nodes/edges, blocking/unblocking edges increments `Version`. Redundant state changes do not increment `Version`. | Graph version incremented v0 -> v1 -> v2 -> v3. Traversal cost became PositiveInfinity on block. Redundant block ignored. Unblock incremented version. | **PASS** |
| **Test 03** | A* optimal corridor selection | Upper route (cost 10) chosen over lower detour (cost 30). | Route returned: `[S -> A -> Exit]` with total cost exactly 10.0. | **PASS** |
| **Test 04** | Dynamic corridor blocking & replanning | When upper corridor `A -> Exit` is blocked (simulating Boss presence), A* reroutes through lower corridor `B`. | Detour returned: `[S -> B -> Exit]` with total cost 30.0. Block reason recorded. | **PASS** |
| **Test 05** | Corridor unblocking restores shortest path | When upper corridor `A -> Exit` is cleared/unblocked, subsequent A* run restores the optimal path. | Restored path: `[S -> A -> Exit]` with cost 10.0. | **PASS** |
| **Test 06** | Disconnected graph / No-path safety | When goal is unreachable, returns `RouteStatus.NoPath` without throwing exceptions. | Returned `RouteResult.Status = NoPath`, `TotalCost = PositiveInfinity`, `NodeCount = 0`, descriptive message. | **PASS** |
| **Test 07** | Empty graph & invalid node IDs | Non-existent node IDs or empty graph handled gracefully. | Returned `EmptyGraph`, `StartNodeNotFound`, `GoalNodeNotFound` safely. Zero unhandled exceptions. | **PASS** |
| **Test 08** | Start equals goal trivial case | Start node is destination. | Returned single-node path `[S]` with cost 0.0. | **PASS** |
| **Test 09** | Cyclic graph termination & loop avoidance | Graph with triangular and cross cycles terminates without infinite loops. | Terminated in $O((V+E) \log V)$ time; returned optimal loopless path of cost 20.0 with no repeated nodes. | **PASS** |
| **Test 10** | Risk penalty weighting influences route | Equal length corridors with supernatural risk penalty causes A* to pick lower-risk corridor. | Correctly picked North (cost 10) initially, then flipped to South (cost 20) when North's risk penalty exceeded South's. | **PASS** |
| **Test 11** | Temporary exclusions without graph mutation | A* pathfinding with excluded edges set bypasses edges without altering graph version. | Route detoured to B; graph version remained unchanged (foundation for Day 3 Yen's K-Shortest Paths). | **PASS** |

---

## Summary
- **Tests Executed:** 11
- **Passed:** 11
- **Failed:** 0
- **Unity-Dependent Tests (NavMeshAgent, Audio, Subtitles, Scene Gizmos):**

