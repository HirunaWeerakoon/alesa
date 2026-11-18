using System;
using System.Collections.Generic;
using System.Linq;
using Alesa.Core;

namespace Alesa.Logic.Checks
{
    public static class GraphAndAStarChecks
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"[ASSERTION FAILED]: {message}");
            }
        }

        public static void RunAll()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(" ALESA Agent 1: Standalone Core & A* Test Suite  ");
            Console.WriteLine("==================================================");

            Test01_MinHeapOrderingAndTieBreaking();
            Test02_GraphCreationAndVersionTracking();
            Test03_AStarOptimalPathSelection();
            Test04_DynamicCorridorBlockingAndReplanning();
            Test05_CorridorUnblockingRestoresShortestPath();
            Test06_NoPathDisconnectedGraphHandledSafely();
            Test07_EmptyGraphAndInvalidNodeIds();
            Test08_StartEqualsGoalTrivialCase();
            Test09_CyclicGraphTerminationAndLoopAvoidance();
            Test10_RiskPenaltyInfluencesPathSelection();
            Test11_TemporaryExclusionsWithoutGraphMutation();
            Test12_SceneWaypointTopologyAndRouteVerification();
            Test13_DynamicEdgeManagementAndSafetyChecks();
            Test14_YenKShortestPathsComprehensiveScenarios();
            Test15_AlesaRoutePlannerDynamicReplanningAndEvents();

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(" ALL 15 TESTS PASSED SUCCESSFULLY!                ");
            Console.WriteLine("==================================================");
        }

        private static void Test01_MinHeapOrderingAndTieBreaking()
        {
            Console.Write("Test 01: MinHeap ordering and tie-breaking... ");

            var heap = new MinHeap<string, float>();
            heap.Enqueue("C", 10.5f);
            heap.Enqueue("A", 2.1f);
            heap.Enqueue("D", 15.0f);
            heap.Enqueue("B1", 5.0f);
            heap.Enqueue("B2", 5.0f);

            Assert(heap.Count == 5, "Heap count should be 5.");
            Assert(heap.Peek() == "A", "Peek should return minimum element 'A'.");

            Assert(heap.Dequeue() == "A", "First dequeue must be 'A'.");
            Assert(heap.Dequeue() == "B1", "Duplicate priority must preserve FIFO order ('B1' first).");
            Assert(heap.Dequeue() == "B2", "Duplicate priority must preserve FIFO order ('B2' second).");
            Assert(heap.Dequeue() == "C", "Next dequeue must be 'C'.");
            Assert(heap.Dequeue() == "D", "Last dequeue must be 'D'.");
            Assert(heap.IsEmpty, "Heap should now be empty.");

            Console.WriteLine("PASS");
        }

        private static void Test02_GraphCreationAndVersionTracking()
        {
            Console.Write("Test 02: Graph creation & version increments... ");

            var graph = new WaypointGraph();
            int v0 = graph.Version;

            graph.AddNode("N1", new NodePosition(0, 0, 0));
            Assert(graph.Version > v0, "Adding node must increment version.");
            int v1 = graph.Version;

            graph.AddNode("N2", new NodePosition(10, 0, 0));
            graph.AddEdge("N1", "N2", 10f, bidirectional: true);
            Assert(graph.Version > v1, "Adding edge must increment version.");
            int v2 = graph.Version;

            Assert(graph.NodeCount == 2, "Graph must have 2 nodes.");
            Assert(graph.GetOutgoingEdges("N1").Count == 1, "N1 must have 1 outgoing edge.");
            Assert(graph.GetOutgoingEdges("N2").Count == 1, "N2 must have 1 outgoing edge (bidirectional).");

            bool blocked = graph.SetEdgeBlocked("N1", "N2", true, "Boss nearby");
            Assert(blocked, "Blocking edge should return true.");
            Assert(graph.Version > v2, "Blocking edge must increment version.");
            int v3 = graph.Version;

            var edge = graph.GetOutgoingEdges("N1").First();
            Assert(edge.IsBlocked, "Edge must be marked blocked.");
            Assert(float.IsPositiveInfinity(edge.TraversalCost), "Blocked edge must have infinite cost.");
            Assert(!edge.IsTraversable, "Blocked edge must not be traversable.");

            // Setting same state should not increment version
            bool redundantBlock = graph.SetEdgeBlocked("N1", "N2", true);
            Assert(!redundantBlock, "Redundant block should return false.");
            Assert(graph.Version == v3, "Redundant block must not increment version.");

            graph.SetEdgeBlocked("N1", "N2", false);
            Assert(graph.Version > v3, "Unblocking edge must increment version.");

            Console.WriteLine("PASS");
        }

        private static void Test03_AStarOptimalPathSelection()
        {
            Console.Write("Test 03: A* optimal corridor selection... ");

            // Graph topology:
            // S (0,0) -> A (10,0) -> Exit (20,0) [Upper corridor: cost = 5 + 5 = 10]
            // S (0,0) -> B (10,-10) -> Exit (20,0) [Lower detour: cost = 15 + 15 = 30]
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("A", new NodePosition(10, 0, 0));
            graph.AddNode("B", new NodePosition(10, 0, -10));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "A", 5f);
            graph.AddEdge("A", "Exit", 5f);
            graph.AddEdge("S", "B", 15f);
            graph.AddEdge("B", "Exit", 15f);

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(graph, "S", "Exit");

            Assert(result.IsValid, "Route must be valid.");
            Assert(result.Status == RouteStatus.Valid, "Status must be Valid.");
            Assert(Math.Abs(result.TotalCost - 10f) < 1e-4f, $"Expected cost 10, got {result.TotalCost}.");
            Assert(result.NodeCount == 3, "Path must have 3 nodes.");
            Assert(result.NodeIds[0] == "S" && result.NodeIds[1] == "A" && result.NodeIds[2] == "Exit",
                $"Expected [S, A, Exit], got [{string.Join(", ", result.NodeIds)}].");

            Console.WriteLine("PASS");
        }

        private static void Test04_DynamicCorridorBlockingAndReplanning()
        {
            Console.Write("Test 04: Dynamic corridor blocking and replanning... ");

            // Same graph as Test 03: Upper route (cost 10) vs Lower route (cost 30)
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("A", new NodePosition(10, 0, 0));
            graph.AddNode("B", new NodePosition(10, 0, -10));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "A", 5f);
            graph.AddEdge("A", "Exit", 5f);
            graph.AddEdge("S", "B", 15f);
            graph.AddEdge("B", "Exit", 15f);

            var pathfinder = new AStarPathfinder();

            // Simulate Agent 3 / Boss blocking upper corridor A -> Exit
            graph.SetEdgeBlocked("A", "Exit", true, "Possessed Boss present in corridor");

            var resultAfterBlock = pathfinder.FindPath(graph, "S", "Exit");

            Assert(resultAfterBlock.IsValid, "Should find alternative route through B.");
            Assert(Math.Abs(resultAfterBlock.TotalCost - 30f) < 1e-4f, $"Expected cost 30, got {resultAfterBlock.TotalCost}.");
            Assert(resultAfterBlock.NodeIds[1] == "B", "Route must detour through node B.");

            Console.WriteLine("PASS");
        }

        private static void Test05_CorridorUnblockingRestoresShortestPath()
        {
            Console.Write("Test 05: Unblocking corridor restores optimal path... ");

            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("A", new NodePosition(10, 0, 0));
            graph.AddNode("B", new NodePosition(10, 0, -10));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "A", 5f);
            graph.AddEdge("A", "Exit", 5f);
            graph.AddEdge("S", "B", 15f);
            graph.AddEdge("B", "Exit", 15f);

            var pathfinder = new AStarPathfinder();

            // Block and replan (detours to B)
            graph.SetEdgeBlocked("A", "Exit", true, "Debris");
            var detoured = pathfinder.FindPath(graph, "S", "Exit");
            Assert(detoured.NodeIds[1] == "B", "Must detour while blocked.");

            // Unblock (debris cleared) and replan
            graph.SetEdgeBlocked("A", "Exit", false);
            var restored = pathfinder.FindPath(graph, "S", "Exit");

            Assert(restored.IsValid, "Restored route must be valid.");
            Assert(Math.Abs(restored.TotalCost - 10f) < 1e-4f, "Cost must be restored to 10.");
            Assert(restored.NodeIds[1] == "A", "Restored route must use corridor A again.");

            Console.WriteLine("PASS");
        }

        private static void Test06_NoPathDisconnectedGraphHandledSafely()
        {
            Console.Write("Test 06: Disconnected graph / NoPath handling... ");

            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("Island1", new NodePosition(5, 0, 0));
            graph.AddNode("Exit", new NodePosition(50, 0, 0));

            graph.AddEdge("S", "Island1", 5f);
            // No edge connecting Island1 to Exit!

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(graph, "S", "Exit");

            Assert(!result.IsValid, "Result must not be valid.");
            Assert(result.Status == RouteStatus.NoPath, "Status must be NoPath.");
            Assert(float.IsPositiveInfinity(result.TotalCost), "Total cost must be PositiveInfinity.");
            Assert(result.NodeCount == 0, "NodeIds must be empty.");
            Assert(!string.IsNullOrEmpty(result.Message), "Message must explain reason.");

            Console.WriteLine("PASS");
        }

        private static void Test07_EmptyGraphAndInvalidNodeIds()
        {
            Console.Write("Test 07: Empty graph and invalid node IDs... ");

            var emptyGraph = new WaypointGraph();
            var pathfinder = new AStarPathfinder();

            var emptyResult = pathfinder.FindPath(emptyGraph, "S", "Exit");
            Assert(emptyResult.Status == RouteStatus.EmptyGraph, "Status must be EmptyGraph.");

            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("Exit", new NodePosition(10, 0, 0));

            var missingStart = pathfinder.FindPath(graph, "NonExistent", "Exit");
            Assert(missingStart.Status == RouteStatus.StartNodeNotFound, "Must report StartNodeNotFound.");

            var missingGoal = pathfinder.FindPath(graph, "S", "NonExistent");
            Assert(missingGoal.Status == RouteStatus.GoalNodeNotFound, "Must report GoalNodeNotFound.");

            var nullStart = pathfinder.FindPath(graph, null, "Exit");
            Assert(nullStart.Status == RouteStatus.StartNodeNotFound, "Null start must be handled safely.");

            Console.WriteLine("PASS");
        }

        private static void Test08_StartEqualsGoalTrivialCase()
        {
            Console.Write("Test 08: Start equals goal trivial case... ");

            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(5, 0, 5));

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(graph, "S", "S");

            Assert(result.IsValid, "Result must be valid.");
            Assert(result.TotalCost == 0f, "Cost to stay at same node must be 0.");
            Assert(result.NodeCount == 1 && result.NodeIds[0] == "S", "Path must contain only the start node.");

            Console.WriteLine("PASS");
        }

        private static void Test09_CyclicGraphTerminationAndLoopAvoidance()
        {
            Console.Write("Test 09: Cyclic graph termination & loop avoidance... ");

            // Complex graph with multiple cycles:
            // S <-> C1 <-> C2 <-> S (triangle loop)
            // C2 <-> C3 <-> Exit
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("C1", new NodePosition(5, 0, 2));
            graph.AddNode("C2", new NodePosition(10, 0, 0));
            graph.AddNode("C3", new NodePosition(15, 0, -2));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "C1", 6f);
            graph.AddEdge("C1", "C2", 6f);
            graph.AddEdge("C2", "S", 10f); // Cycle back to S
            graph.AddEdge("C2", "C3", 5f);
            graph.AddEdge("C3", "Exit", 5f);
            graph.AddEdge("C3", "C1", 12f); // Another cross-cycle

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(graph, "S", "Exit");

            Assert(result.IsValid, "Should find path through cyclic graph.");
            // Shortest: S -> C2 (10) -> C3 (5) -> Exit (5) = 20
            // vs S -> C1 (6) -> C2 (6) -> C3 (5) -> Exit (5) = 22
            Assert(Math.Abs(result.TotalCost - 20f) < 1e-4f, $"Expected 20, got {result.TotalCost}");
            Assert(result.NodeIds.Count == new HashSet<string>(result.NodeIds).Count, "Path must contain no duplicate nodes (loopless).");

            Console.WriteLine("PASS");
        }

        private static void Test10_RiskPenaltyInfluencesPathSelection()
        {
            Console.Write("Test 10: Risk penalty weighting alters route... ");

            // Two corridors with identical physical distance (10m):
            // North: S -> N -> Exit (10m base, 0 risk)
            // South: S -> M -> Exit (10m base, 15 risk penalty due to supernatural noise)
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("North", new NodePosition(10, 0, 5));
            graph.AddNode("South", new NodePosition(10, 0, -5));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "North", 5f);
            graph.AddEdge("North", "Exit", 5f);

            graph.AddEdge("S", "South", 5f, bidirectional: true, riskPenalty: 10f);
            graph.AddEdge("South", "Exit", 5f, bidirectional: true, riskPenalty: 10f);

            var pathfinder = new AStarPathfinder();
            var result = pathfinder.FindPath(graph, "S", "Exit");

            Assert(result.IsValid, "Result must be valid.");
            Assert(result.NodeIds[1] == "North", "A* must prefer the safe North corridor.");
            Assert(Math.Abs(result.TotalCost - 10f) < 1e-4f, "Cost should be 10.");

            // Now make North very dangerous (e.g. Boss approaching)
            graph.SetEdgeRisk("S", "North", 30f);
            graph.SetEdgeRisk("North", "Exit", 30f);

            var resultAfterRiskShift = pathfinder.FindPath(graph, "S", "Exit");
            Assert(resultAfterRiskShift.NodeIds[1] == "South", "A* should now divert to South corridor despite its base risk.");

            Console.WriteLine("PASS");
        }

        private static void Test11_TemporaryExclusionsWithoutGraphMutation()
        {
            Console.Write("Test 11: Temporary exclusions (Yen's algorithm foundation)... ");

            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("A", new NodePosition(10, 0, 0));
            graph.AddNode("B", new NodePosition(10, 0, -10));
            graph.AddNode("Exit", new NodePosition(20, 0, 0));

            graph.AddEdge("S", "A", 5f);
            graph.AddEdge("A", "Exit", 5f);
            graph.AddEdge("S", "B", 15f);
            graph.AddEdge("B", "Exit", 15f);

            int versionBefore = graph.Version;
            var pathfinder = new AStarPathfinder();

            // Temporarily exclude edge (S, A) without mutating graph
            var excludedEdges = new HashSet<(string from, string to)> { ("S", "A") };
            var result = pathfinder.FindPath(graph, "S", "Exit", excludedEdges: excludedEdges);

            Assert(result.IsValid, "Route must be valid via B.");
            Assert(result.NodeIds[1] == "B", "Must use alternate corridor B.");
            Assert(graph.Version == versionBefore, "Graph version must not change when using temporary exclusions.");

            Console.WriteLine("PASS");
        }

        private static void Test12_SceneWaypointTopologyAndRouteVerification()
        {
            Console.Write("Test 12: Scene waypoint topology & dynamic detour verification... ");

            // Mirror exact 10-node layout authored in AlesaAgentTest.unity
            var graph = new WaypointGraph();
            graph.AddNode("WP_Start", new NodePosition(0f, 0.5f, -14f));
            graph.AddNode("WP_T_Junction", new NodePosition(0f, 0.5f, -4f), isJunction: true);

            // West Corridor (Shorter)
            graph.AddNode("WP_West_Entry", new NodePosition(-4.5f, 0.5f, -4f));
            graph.AddNode("WP_West_Mid", new NodePosition(-4.5f, 0.5f, 2f));
            graph.AddNode("WP_West_Exit", new NodePosition(-4.5f, 0.5f, 8f));

            // East Corridor (Longer detour)
            graph.AddNode("WP_East_Entry", new NodePosition(6f, 0.5f, -4f));
            graph.AddNode("WP_East_Mid", new NodePosition(6f, 0.5f, 2f));
            graph.AddNode("WP_East_Exit", new NodePosition(6f, 0.5f, 8f));

            // North Junction & Exit
            graph.AddNode("WP_North_Junction", new NodePosition(0f, 0.5f, 8f), isJunction: true);
            graph.AddNode("WP_Exit", new NodePosition(0f, 0.5f, 15f));

            // Wire identical corridor connections
            graph.AddEdge("WP_Start", "WP_T_Junction", bidirectional: true);
            graph.AddEdge("WP_T_Junction", "WP_West_Entry", bidirectional: true);
            graph.AddEdge("WP_T_Junction", "WP_East_Entry", bidirectional: true);

            graph.AddEdge("WP_West_Entry", "WP_West_Mid", bidirectional: true);
            graph.AddEdge("WP_West_Mid", "WP_West_Exit", bidirectional: true);
            graph.AddEdge("WP_West_Exit", "WP_North_Junction", bidirectional: true);

            graph.AddEdge("WP_East_Entry", "WP_East_Mid", bidirectional: true);
            graph.AddEdge("WP_East_Mid", "WP_East_Exit", bidirectional: true);
            graph.AddEdge("WP_East_Exit", "WP_North_Junction", bidirectional: true);

            graph.AddEdge("WP_North_Junction", "WP_Exit", bidirectional: true);

            var pathfinder = new AStarPathfinder();

            // 1. Initial State: West corridor must be chosen because 38.0m < 41.0m
            var initialRoute = pathfinder.FindPath(graph, "WP_Start", "WP_Exit");
            Assert(initialRoute.IsValid, "Initial route must be valid.");
            Assert(Math.Abs(initialRoute.TotalCost - 38.0f) < 0.1f, $"Expected West route cost ~38.0m, got {initialRoute.TotalCost:F2}");
            Assert(initialRoute.NodeIds.Contains("WP_West_Mid"), "Initial route must traverse West corridor.");

            // 2. Simulate Boss/Hazard blocking West Corridor
            graph.SetEdgeBlocked("WP_West_Mid", "WP_West_Exit", true, "Boss Ambush");
            var detouredRoute = pathfinder.FindPath(graph, "WP_Start", "WP_Exit");
            Assert(detouredRoute.IsValid, "Detour route must be valid.");
            Assert(Math.Abs(detouredRoute.TotalCost - 41.0f) < 0.1f, $"Expected East route cost ~41.0m, got {detouredRoute.TotalCost:F2}");
            Assert(detouredRoute.NodeIds.Contains("WP_East_Mid"), "Route must divert through East corridor.");

            // 3. Block both corridors -> NoPath safe return
            graph.SetEdgeBlocked("WP_East_Mid", "WP_East_Exit", true, "Secondary Hazard");
            var blockedRoute = pathfinder.FindPath(graph, "WP_Start", "WP_Exit");
            Assert(!blockedRoute.IsValid, "Blocked route must report invalid.");
            Assert(blockedRoute.Status == RouteStatus.NoPath, "Status must be NoPath.");
            Assert(float.IsPositiveInfinity(blockedRoute.TotalCost), "Cost must be infinity.");

            // 4. Restore corridors -> returns to optimal West route
            graph.SetEdgeBlocked("WP_West_Mid", "WP_West_Exit", false);
            graph.SetEdgeBlocked("WP_East_Mid", "WP_East_Exit", false);
            var restoredRoute = pathfinder.FindPath(graph, "WP_Start", "WP_Exit");
            Assert(restoredRoute.IsValid, "Restored route must be valid.");
            Assert(restoredRoute.NodeIds.Contains("WP_West_Mid"), "Restored route must use West corridor again.");

            Console.WriteLine("PASS");
        }

        private static void Test13_DynamicEdgeManagementAndSafetyChecks()
        {
            Console.Write("Test 13: Dynamic edge queries, directed blocking & atomic resets... ");

            var graph = new WaypointGraph();
            graph.AddNode("N1", new NodePosition(0, 0, 0));
            graph.AddNode("N2", new NodePosition(10, 0, 0));
            graph.AddNode("N3", new NodePosition(20, 0, 0));

            graph.AddEdge("N1", "N2", 10f, bidirectional: true);
            graph.AddEdge("N2", "N3", 10f, bidirectional: true);

            // 1. Query existing edges
            Assert(graph.TryGetEdge("N1", "N2", out var edge12), "Must find edge N1->N2.");
            Assert(edge12.BaseLength == 10f, "Base length must be 10.");
            Assert(edge12.EdgeKey == "N1->N2", "EdgeKey must match 'N1->N2'.");
            Assert(graph.IsEdgeTraversable("N1", "N2"), "N1->N2 must be traversable.");
            Assert(!graph.IsEdgeBlocked("N1", "N2"), "N1->N2 must not be blocked.");

            // 2. Query non-existent or invalid edges safely
            Assert(!graph.TryGetEdge("N1", "NonExistent", out _), "Querying unknown target must return false.");
            Assert(!graph.TryGetEdge(null, "N2", out _), "Null fromId must return false.");
            Assert(!graph.TryGetEdge("N1", null, out _), "Null toId must return false.");
            Assert(!graph.IsEdgeTraversable("N1", "NonExistent"), "Nonexistent edge is not traversable.");
            Assert(!graph.IsEdgeBlocked("N1", "NonExistent"), "Nonexistent edge is not blocked.");

            // 3. Blocking non-existent edges safely returns false without modifying Version
            int vBefore = graph.Version;
            Assert(!graph.SetEdgeBlocked("N1", "NonExistent", true), "Blocking unknown edge returns false.");
            Assert(!graph.SetEdgeBlocked(null, "N2", true), "Null fromId returns false.");
            Assert(!graph.SetEdgeRisk("N1", null, 5f), "Null toId returns false.");
            Assert(graph.Version == vBefore, "Version must not change on invalid edge operations.");

            // 4. Directed vs Bidirectional blocking
            // Block ONLY directed forward edge N1->N2 (bidirectional: false)
            graph.SetEdgeBlocked("N1", "N2", true, "One-way hazard", bidirectional: false);
            Assert(graph.IsEdgeBlocked("N1", "N2"), "N1->N2 must be blocked.");
            Assert(!graph.IsEdgeTraversable("N1", "N2"), "N1->N2 must not be traversable.");
            Assert(!graph.IsEdgeBlocked("N2", "N1"), "Backward edge N2->N1 must remain unblocked.");
            Assert(graph.IsEdgeTraversable("N2", "N1"), "Backward edge N2->N1 must remain traversable.");
            Assert(edge12.BaseLength == 10f, "Original BaseLength must be preserved even when blocked.");

            // 5. GetBlockedEdges() tracking
            var blockedList = graph.GetBlockedEdges();
            Assert(blockedList.Count == 1, "Must be exactly 1 blocked edge.");
            Assert(blockedList[0].EdgeKey == "N1->N2", "Blocked edge must be N1->N2.");

            // Block N2->N3 bidirectionally
            graph.SetEdgeBlocked("N2", "N3", true, "Boss Presence", bidirectional: true);
            var blockedList2 = graph.GetBlockedEdges();
            Assert(blockedList2.Count == 3, "Must now be 3 blocked edges (N1->N2, N2->N3, N3->N2).");

            // 6. ResetAllBlockedEdges() atomic restoration
            int resetCount = graph.ResetAllBlockedEdges();
            Assert(resetCount == 3, $"Expected 3 edges reset, got {resetCount}.");
            Assert(graph.GetBlockedEdges().Count == 0, "No edges should remain blocked.");
            Assert(graph.IsEdgeTraversable("N1", "N2"), "N1->N2 must be restored.");
            Assert(graph.IsEdgeTraversable("N2", "N3"), "N2->N3 must be restored.");
            Assert(graph.IsEdgeTraversable("N3", "N2"), "N3->N2 must be restored.");

            // 7. Edge removal
            Assert(graph.RemoveEdge("N1", "N2", bidirectional: true), "Removing edge must return true.");
            Assert(!graph.TryGetEdge("N1", "N2", out _), "Removed edge must not exist.");
            Assert(!graph.TryGetEdge("N2", "N1", out _), "Removed reverse edge must not exist.");

            Console.WriteLine("PASS");
        }

        private static void Test14_YenKShortestPathsComprehensiveScenarios()
        {
            Console.Write("Test 14: Yen's K-Shortest Paths (K=3, ordering, loops & edge cases)... ");

            var yen = new YenKShortestPaths();

            // 1. Single valid route graph
            var singleGraph = new WaypointGraph();
            singleGraph.AddNode("S", new NodePosition(0, 0, 0));
            singleGraph.AddNode("A", new NodePosition(10, 0, 0));
            singleGraph.AddNode("Exit", new NodePosition(20, 0, 0));
            singleGraph.AddEdge("S", "A", 10f);
            singleGraph.AddEdge("A", "Exit", 10f);

            var singleResult = yen.FindKShortestPaths(singleGraph, "S", "Exit", k: 3);
            Assert(singleResult.HasRoutes && singleResult.Count == 1, "Graph with only 1 route must return exactly 1 route.");
            Assert(Math.Abs(singleResult.BestRoute.TotalCost - 20f) < 1e-4f, "Cost must be 20.0.");

            // 2. Multi-path graph with 4 distinct paths of known costs:
            // S -> Path 1 (via P1): 5 + 5 = 10
            // S -> Path 2 (via P2): 7 + 7 = 14
            // S -> Path 3 (via P3): 9 + 9 = 18
            // S -> Path 4 (via P4): 12 + 12 = 24
            var multiGraph = new WaypointGraph();
            multiGraph.AddNode("S", new NodePosition(0, 0, 0));
            multiGraph.AddNode("P1", new NodePosition(5, 0, 0));
            multiGraph.AddNode("P2", new NodePosition(5, 0, 5));
            multiGraph.AddNode("P3", new NodePosition(5, 0, -5));
            multiGraph.AddNode("P4", new NodePosition(5, 0, 10));
            multiGraph.AddNode("Exit", new NodePosition(10, 0, 0));

            multiGraph.AddEdge("S", "P1", 5f);
            multiGraph.AddEdge("P1", "Exit", 5f);

            multiGraph.AddEdge("S", "P2", 7f);
            multiGraph.AddEdge("P2", "Exit", 7f);

            multiGraph.AddEdge("S", "P3", 9f);
            multiGraph.AddEdge("P3", "Exit", 9f);

            multiGraph.AddEdge("S", "P4", 12f);
            multiGraph.AddEdge("P4", "Exit", 12f);

            int versionBefore = multiGraph.Version;

            // Request top K=3 paths
            var k3Result = yen.FindKShortestPaths(multiGraph, "S", "Exit", k: 3);
            Assert(k3Result.HasRoutes && k3Result.Count == 3, "Must return exactly 3 shortest routes.");
            Assert(multiGraph.Version == versionBefore, "Graph version must remain strictly untouched by Yen's algorithm.");

            // Verify strict ordering and correct costs
            Assert(Math.Abs(k3Result.Routes[0].TotalCost - 10f) < 1e-4f, $"Route 1 cost should be 10.0, got {k3Result.Routes[0].TotalCost}");
            Assert(Math.Abs(k3Result.Routes[1].TotalCost - 14f) < 1e-4f, $"Route 2 cost should be 14.0, got {k3Result.Routes[1].TotalCost}");
            Assert(Math.Abs(k3Result.Routes[2].TotalCost - 18f) < 1e-4f, $"Route 3 cost should be 18.0, got {k3Result.Routes[2].TotalCost}");

            // Verify paths are distinct (no duplicate candidate routes)
            var signatures = new HashSet<string>();
            for (int r = 0; r < k3Result.Count; r++)
            {
                string sig = string.Join("->", k3Result.Routes[r].NodeIds);
                Assert(signatures.Add(sig), $"Duplicate path signature detected: {sig}");
            }

            // 3. Request fewer paths than available (e.g. K=2)
            var k2Result = yen.FindKShortestPaths(multiGraph, "S", "Exit", k: 2);
            Assert(k2Result.Count == 2, "K=2 must return exactly 2 routes.");
            Assert(k2Result.Routes[0].TotalCost <= k2Result.Routes[1].TotalCost, "Routes must be in ascending cost order.");

            // 4. Request more paths than exist (K=10 on graph with 4 routes)
            var k10Result = yen.FindKShortestPaths(multiGraph, "S", "Exit", k: 10);
            Assert(k10Result.Count == 4, $"Should gracefully return all 4 available routes without error, got {k10Result.Count}.");
            Assert(Math.Abs(k10Result.Routes[3].TotalCost - 24f) < 1e-4f, "4th route cost must be 24.0.");

            // 5. Cyclic graph with complex cross-connections
            var cyclicGraph = new WaypointGraph();
            cyclicGraph.AddNode("S", new NodePosition(0, 0, 0));
            cyclicGraph.AddNode("C1", new NodePosition(5, 0, 2));
            cyclicGraph.AddNode("C2", new NodePosition(10, 0, 0));
            cyclicGraph.AddNode("C3", new NodePosition(15, 0, -2));
            cyclicGraph.AddNode("Exit", new NodePosition(20, 0, 0));

            cyclicGraph.AddEdge("S", "C1", 6f, bidirectional: true);
            cyclicGraph.AddEdge("C1", "C2", 6f, bidirectional: true);
            cyclicGraph.AddEdge("C2", "S", 10f, bidirectional: true); // Cycle back to S
            cyclicGraph.AddEdge("C2", "C3", 5f, bidirectional: true);
            cyclicGraph.AddEdge("C3", "Exit", 5f, bidirectional: true);
            cyclicGraph.AddEdge("C3", "C1", 12f, bidirectional: true); // Another cycle
            cyclicGraph.AddEdge("S", "C3", 16f, bidirectional: true);  // Direct branch

            var cyclicResult = yen.FindKShortestPaths(cyclicGraph, "S", "Exit", k: 3);
            Assert(cyclicResult.HasRoutes, "Must find paths in cyclic graph.");
            for (int r = 0; r < cyclicResult.Count; r++)
            {
                var pathNodes = cyclicResult.Routes[r].NodeIds;
                var set = new HashSet<string>(pathNodes);
                Assert(set.Count == pathNodes.Count, $"Candidate path {r + 1} contains cycles: [{string.Join(", ", pathNodes)}]");
            }

            // 6. Dynamic blockage interaction with Yen's algorithm
            multiGraph.SetEdgeBlocked("S", "P1", true, "Boss Blocking Path 1");
            var resultAfterBlock = yen.FindKShortestPaths(multiGraph, "S", "Exit", k: 3);
            Assert(resultAfterBlock.Count == 3, "Should return next 3 shortest remaining routes.");
            Assert(!resultAfterBlock.Routes.Any(r => r.NodeIds.Contains("P1")), "Blocked corridor P1 must be completely excluded from all alternatives.");
            Assert(Math.Abs(resultAfterBlock.Routes[0].TotalCost - 14f) < 1e-4f, "Path 2 (cost 14) is now the 1st shortest route.");
            Assert(Math.Abs(resultAfterBlock.Routes[1].TotalCost - 18f) < 1e-4f, "Path 3 (cost 18) is now the 2nd shortest route.");
            Assert(Math.Abs(resultAfterBlock.Routes[2].TotalCost - 24f) < 1e-4f, "Path 4 (cost 24) is now the 3rd shortest route.");

            // 7. Trivial case: Start == Goal
            var trivial = yen.FindKShortestPaths(multiGraph, "S", "S", k: 3);
            Assert(trivial.HasRoutes && trivial.Count == 1 && trivial.BestRoute.TotalCost == 0f, "Start equals goal must return 1-node path of cost 0.");

            // 8. Disconnected destination
            var disconnectedGraph = new WaypointGraph();
            disconnectedGraph.AddNode("S", new NodePosition(0, 0, 0));
            disconnectedGraph.AddNode("Island", new NodePosition(10, 0, 0));
            disconnectedGraph.AddNode("Exit", new NodePosition(50, 0, 0));
            disconnectedGraph.AddEdge("S", "Island", 10f);

            var noPathResult = yen.FindKShortestPaths(disconnectedGraph, "S", "Exit", k: 3);
            Assert(!noPathResult.HasRoutes && noPathResult.Status == RouteStatus.NoPath, "Disconnected exit must report NoPath safely.");

            Console.WriteLine("PASS");
        }

        private static void Test15_AlesaRoutePlannerDynamicReplanningAndEvents()
        {
            Console.Write("Test 15: AlesaRoutePlanner dynamic replanning, caching & events... ");

            // Graph topology:
            // S -> W1 -> W2 -> Exit (West: 5 + 5 + 5 = 15m)
            // S -> E1 -> E2 -> Exit (East: 7 + 7 + 7 = 21m)
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("W1", new NodePosition(-5, 0, 5));
            graph.AddNode("W2", new NodePosition(-5, 0, 10));
            graph.AddNode("E1", new NodePosition(5, 0, 5));
            graph.AddNode("E2", new NodePosition(5, 0, 10));
            graph.AddNode("Exit", new NodePosition(0, 0, 15));

            graph.AddEdge("S", "W1", 5f, bidirectional: true);
            graph.AddEdge("W1", "W2", 5f, bidirectional: true);
            graph.AddEdge("W2", "Exit", 5f, bidirectional: true);

            graph.AddEdge("S", "E1", 7f, bidirectional: true);
            graph.AddEdge("E1", "E2", 7f, bidirectional: true);
            graph.AddEdge("E2", "Exit", 7f, bidirectional: true);

            var planner = new AlesaRoutePlanner(graph, maxAlternatives: 3);

            // Track events fired
            int routeChangedCount = 0;
            RouteChangeReason lastReason = RouteChangeReason.ManualRequest;
            bool routeBlockedFired = false;
            bool noPathFired = false;

            planner.OnRouteChanged += (route, reason) =>
            {
                routeChangedCount++;
                lastReason = reason;
            };

            planner.OnActiveRouteBlocked += reason =>
            {
                routeBlockedFired = true;
            };

            planner.OnNoPathAvailable += () =>
            {
                noPathFired = true;
            };

            // 1. Initial plan
            planner.SetEndpoints("S", "Exit", replanImmediately: true);
            Assert(planner.HasValidRoute, "Planner must have a valid initial route.");
            Assert(Math.Abs(planner.ActiveRoute.TotalCost - 15f) < 1e-4f, "Initial best route must be West corridor (15m).");
            Assert(routeChangedCount == 1, "OnRouteChanged should have fired once.");
            Assert(lastReason == RouteChangeReason.DestinationChanged, "Reason should be DestinationChanged.");
            Assert(planner.IsActiveRouteTraversable(), "Active route must be traversable initially.");

            // 2. Unchanged graph returns cached route without recalculating
            int vBefore = planner.CachedGraphVersion;
            var cached = planner.Replan();
            Assert(planner.CachedGraphVersion == vBefore, "Version must remain identical when cached.");
            Assert(routeChangedCount == 1, "OnRouteChanged should NOT fire when returning cached route.");

            // 3. Dynamic Corridor Block -> Triggers automatic replan to East route (21m)
            graph.SetEdgeBlocked("W1", "W2", true, "Boss Ambush");
            Assert(!planner.IsActiveRouteTraversable(), "Active route must report NOT traversable after edge blocked.");

            bool replanned = planner.CheckAndReplanIfInvalid(out var newRoute);
            Assert(replanned, "CheckAndReplanIfInvalid must return true when route is blocked.");
            Assert(routeBlockedFired, "OnActiveRouteBlocked event must have fired.");
            Assert(Math.Abs(newRoute.TotalCost - 21f) < 1e-4f, $"Expected detoured cost 21.0m, got {newRoute.TotalCost:F1}m.");
            Assert(newRoute.NodeIds.Contains("E1"), "Detour must use East corridor.");
            Assert(lastReason == RouteChangeReason.CorridorBlocked, "Reason must be CorridorBlocked.");

            // 4. Reopening corridor restores optimal West route
            graph.SetEdgeBlocked("W1", "W2", false);
            bool restored = planner.CheckAndReplanIfInvalid(out var restoredRoute);
            Assert(restored, "CheckAndReplanIfInvalid must return true when graph version changes.");
            Assert(Math.Abs(restoredRoute.TotalCost - 15f) < 1e-4f, "West route (15m) must be restored.");
            Assert(lastReason == RouteChangeReason.CorridorReopened, "Reason must be CorridorReopened.");

            // 5. Player progresses along active route (normal movement)
            int routeChangedBeforeProgression = routeChangedCount;
            bool deviated1 = planner.UpdatePlayerNode("W1");
            Assert(!deviated1, "Moving to W1 along the active path should not trigger replan.");
            Assert(routeChangedCount == routeChangedBeforeProgression, "Route change event should not fire for normal progression.");
            Assert(planner.StartNodeId == "W1", "StartNodeId should advance to W1.");

            // 6. Player deviates to another corridor (Nathan enters E1 instead of continuing to W2)
            bool deviated2 = planner.UpdatePlayerNode("E1");
            Assert(deviated2, "Deviating to E1 must trigger replanning from new node.");
            Assert(lastReason == RouteChangeReason.PlayerDeviated, "Reason must be PlayerDeviated.");
            Assert(planner.StartNodeId == "E1", "StartNodeId must update to E1.");
            Assert(planner.ActiveRoute.NodeIds[0] == "E1", "Active route must now originate from E1.");

            // 7. Complete maze block -> NoPathAvailable event
            graph.SetEdgeBlocked("W2", "Exit", true);
            graph.SetEdgeBlocked("E2", "Exit", true);
            planner.CheckAndReplanIfInvalid(out var blockedAll);
            Assert(!planner.HasValidRoute, "Must report no valid route when both exits blocked.");
            Assert(noPathFired, "OnNoPathAvailable event must have fired.");
            Assert(blockedAll.Status == RouteStatus.NoPath, "Status must be NoPath.");

            Console.WriteLine("PASS");
        }
    }
}

