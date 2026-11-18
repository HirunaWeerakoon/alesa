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
            Test16_YenAlgorithmExhaustiveReferenceComparison();
            Test17_PerformanceAndScalabilityBenchmark();
            Test18_CompanionLocomotionStateMachineTransitions();
            Test19_FollowAnchorCalculationsAndSmoothing();
            Test20_ArrivalSteeringSpeedModulationAndSmoothDeceleration();
            Test21_StateSynchronizedSpeedAndSafeWaiting();

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(" ALL 21 TESTS PASSED SUCCESSFULLY!                ");
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

        private static void Test16_YenAlgorithmExhaustiveReferenceComparison()
        {
            Console.Write("Test 16: Exhaustive DFS ground-truth comparison with Yen's algorithm... ");

            // Graph with multiple interconnecting paths:
            // S connects to A (cost 4), B (cost 6)
            // A connects to C (cost 3), D (cost 7)
            // B connects to C (cost 5), D (cost 2)
            // C connects to Exit (cost 4)
            // D connects to Exit (cost 3)
            var graph = new WaypointGraph();
            graph.AddNode("S", new NodePosition(0, 0, 0));
            graph.AddNode("A", new NodePosition(5, 0, 4));
            graph.AddNode("B", new NodePosition(5, 0, -4));
            graph.AddNode("C", new NodePosition(10, 0, 4));
            graph.AddNode("D", new NodePosition(10, 0, -4));
            graph.AddNode("Exit", new NodePosition(15, 0, 0));

            graph.AddEdge("S", "A", 4f);
            graph.AddEdge("S", "B", 6f);
            graph.AddEdge("A", "C", 3f);
            graph.AddEdge("A", "D", 7f);
            graph.AddEdge("B", "C", 5f);
            graph.AddEdge("B", "D", 2f);
            graph.AddEdge("C", "Exit", 4f);
            graph.AddEdge("D", "Exit", 3f);

            // Exhaustively find all simple paths via recursive DFS
            var allPaths = new List<(List<string> Nodes, float Cost)>();
            EnumerateAllSimplePaths(graph, "S", "Exit", new HashSet<string>(), new List<string>(), 0f, allPaths);

            // Sort ground truth paths by cost ascending
            allPaths.Sort((p1, p2) => p1.Cost.CompareTo(p2.Cost));
            Assert(allPaths.Count >= 3, $"Expected at least 3 paths in ground truth, got {allPaths.Count}");

            // Run Yen's algorithm
            var yen = new YenKShortestPaths();
            var result = yen.FindKShortestPaths(graph, "S", "Exit", k: 3);

            Assert(result.HasRoutes && result.Count == 3, "Yen must return top 3 paths.");

            for (int k = 0; k < 3; k++)
            {
                var yenRoute = result.Routes[k];
                var groundTruth = allPaths[k];

                Assert(Math.Abs(yenRoute.TotalCost - groundTruth.Cost) < 1e-4f,
                    $"Path #{k + 1} cost mismatch: Yen={yenRoute.TotalCost:F2}, GroundTruth={groundTruth.Cost:F2}");

                string yenSig = string.Join("->", yenRoute.NodeIds);
                string groundTruthSig = string.Join("->", groundTruth.Nodes);
                Assert(yenSig == groundTruthSig,
                    $"Path #{k + 1} node mismatch: Yen=[{yenSig}], GroundTruth=[{groundTruthSig}]");
            }

            Console.WriteLine("PASS");
        }

        private static void EnumerateAllSimplePaths(
            WaypointGraph graph,
            string current,
            string goal,
            HashSet<string> visited,
            List<string> path,
            float currentCost,
            List<(List<string> Nodes, float Cost)> allPaths)
        {
            visited.Add(current);
            path.Add(current);

            if (current == goal)
            {
                allPaths.Add((new List<string>(path), currentCost));
            }
            else
            {
                foreach (var edge in graph.GetOutgoingEdges(current))
                {
                    if (edge.IsTraversable && !visited.Contains(edge.ToNodeId))
                    {
                        EnumerateAllSimplePaths(graph, edge.ToNodeId, goal, visited, path, currentCost + edge.TraversalCost, allPaths);
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(current);
        }

        private static void Test17_PerformanceAndScalabilityBenchmark()
        {
            Console.Write("Test 17: Performance benchmark on 50-node hospital maze... ");

            // Build realistic 50-node maze grid (5 rows x 10 cols)
            var graph = new WaypointGraph();
            int rows = 5;
            int cols = 10;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    string id = $"N_{r}_{c}";
                    graph.AddNode(id, new NodePosition(c * 5f, 0f, r * 5f));
                }
            }

            // Connect horizontal corridors
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    graph.AddEdge($"N_{r}_{c}", $"N_{r}_{c + 1}", 5f, bidirectional: true);
                }
            }

            // Connect vertical corridors
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    graph.AddEdge($"N_{r}_{c}", $"N_{r + 1}_{c}", 5f, bidirectional: true);
                }
            }

            string startId = "N_0_0";
            string exitId = $"N_{rows - 1}_{cols - 1}";

            var yen = new YenKShortestPaths();

            // Warm-up run
            var warmup = yen.FindKShortestPaths(graph, startId, exitId, k: 3);
            Assert(warmup.HasRoutes && warmup.Count == 3, "Warmup search must succeed.");

            // Timed execution across 50 repeated queries
            const int iterations = 50;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var res = yen.FindKShortestPaths(graph, startId, exitId, k: 3);
                if (!res.HasRoutes) throw new InvalidOperationException("Benchmark run failed.");
            }
            sw.Stop();

            double avgMs = (double)sw.ElapsedMilliseconds / iterations;
            Assert(avgMs < 5.0, $"Average search time ({avgMs:F3} ms) exceeds 5.0 ms threshold.");

            Console.WriteLine($"PASS ({avgMs:F3} ms/query across {iterations} runs)");
        }

        private static void Test18_CompanionLocomotionStateMachineTransitions()
        {
            Console.Write("Test 18: Companion FSM transitions, speed matching & priority... ");

            var fsm = new CompanionStateMachine();
            Assert(fsm.CurrentState == CompanionState.Dormant, "Initial state must be Dormant.");
            Assert(fsm.GetTargetSpeed() == 0f, "Dormant speed must be 0.");

            int stateChangeCount = 0;
            CompanionState lastPrev = CompanionState.Dormant;
            CompanionState lastNext = CompanionState.Dormant;

            fsm.OnStateChanged += (prev, next) =>
            {
                stateChangeCount++;
                lastPrev = prev;
                lastNext = next;
            };

            // Context builder helper
            var ctx = new CompanionContext
            {
                IsAlesaRescued = false,
                IsPlayerControlsEnabled = true,
                IsPlayerAiming = false,
                IsPlayerSprinting = false,
                HasPlayerMovementInput = false,
                DistanceToPlayer = 2f,
                DistanceToFollowAnchor = 0.1f,
                HasValidNavPath = true,
                IsStuck = false
            };

            // 1. Unrescued remains Dormant
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Dormant, "Unrescued must remain Dormant.");

            // 2. Rescued with player stationary -> Idle
            ctx.IsAlesaRescued = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Idle, "Rescued stationary player must transition to Idle.");
            Assert(fsm.GetTargetSpeed() == 0f, "Idle speed must be 0.");
            Assert(stateChangeCount == 1 && lastPrev == CompanionState.Dormant && lastNext == CompanionState.Idle, "Event must fire Dormant -> Idle.");

            // 3. Nathan begins walking (movement input or distance > stopDistanceThreshold) -> Following
            ctx.HasPlayerMovementInput = true;
            ctx.DistanceToFollowAnchor = 1.5f;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Following, "Walking player must transition to Following.");
            Assert(fsm.GetTargetSpeed() == 5.0f, "Following speed must match walk speed (5.0 m/s).");

            // 4. Nathan sprints -> Sprinting
            ctx.IsPlayerSprinting = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Sprinting, "Sprinting player must transition to Sprinting.");
            Assert(fsm.GetTargetSpeed() == 8.5f, "Sprinting speed must match sprint speed (8.5 m/s).");

            // 5. Nathan stops sprinting and aims weapon -> WaitingAim (High priority!)
            ctx.IsPlayerSprinting = false;
            ctx.IsPlayerAiming = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.WaitingAim, "Aiming player must transition to WaitingAim.");
            Assert(fsm.GetTargetSpeed() == 0f, "WaitingAim speed must be 0 (holds safely behind Nathan).");

            // 6. Nathan stops aiming and resumes walking -> Following
            ctx.IsPlayerAiming = false;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Following, "Resuming walk after aim must return to Following.");

            // 7. Nathan stops walking, Alesa arrives at anchor -> Idle
            ctx.HasPlayerMovementInput = false;
            ctx.DistanceToFollowAnchor = 0.2f; // within stop threshold (0.35f)
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Idle, "Arriving at anchor with stationary player must transition to Idle.");

            // 8. Game paused / controls disabled -> Idle (modal safety)
            ctx.HasPlayerMovementInput = true;
            ctx.DistanceToFollowAnchor = 3f;
            ctx.IsPlayerControlsEnabled = false; // Player opened pause menu or dialpad
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Idle, "Disabled player controls must force Idle state.");

            // 9. Recovery conditions: Invalid NavMesh path
            ctx.IsPlayerControlsEnabled = true;
            ctx.HasValidNavPath = false; // corridor blocked or off navmesh
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Recovering, "Invalid NavMesh path must transition to Recovering.");
            Assert(fsm.GetTargetSpeed() == 8.5f, "Recovery uses sprint speed to catch up / navigate around.");

            // 10. Recovery condition: Agent is stuck
            ctx.HasValidNavPath = true;
            ctx.IsStuck = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Recovering, "Stuck agent must remain in Recovering.");

            // 11. Recovery condition: Extreme distance separation (>18m)
            ctx.IsStuck = false;
            ctx.DistanceToPlayer = 25f; // way too far away
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Recovering, "Extreme distance separation (>18m) must trigger Recovering.");

            // 12. Recovery resolved -> returns to Following
            ctx.DistanceToPlayer = 4f;
            ctx.DistanceToFollowAnchor = 2.5f;
            ctx.HasPlayerMovementInput = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Following, "Resolving recovery conditions returns to Following.");

            Console.WriteLine("PASS");
        }

        private static void Test19_FollowAnchorCalculationsAndSmoothing()
        {
            Console.Write("Test 19: Follow anchor trailing distance, personal space & smoothing... ");

            var calc = new FollowAnchorCalculator(preferredDistance: 1.75f, minSeparation: 1.0f);
            var playerPos = new NodePosition(10f, 0f, 10f);

            // 1. Facing North (0, 0, 1) -> Anchor should be 1.75m South (10, 0, 8.25)
            var fwdNorth = new NodePosition(0f, 0f, 1f);
            var anchorNorth = calc.CalculateIdealAnchor(playerPos, fwdNorth);
            Assert(Math.Abs(anchorNorth.X - 10f) < 1e-4f, "X should remain 10.");
            Assert(Math.Abs(anchorNorth.Z - 8.25f) < 1e-4f, $"Z should be 8.25 (10 - 1.75), got {anchorNorth.Z}");
            Assert(Math.Abs(playerPos.FlatDistanceTo(anchorNorth) - 1.75f) < 1e-4f, "Distance should be exactly 1.75m.");

            // 2. Facing East (1, 0, 0) -> Anchor should be 1.75m West (8.25, 0, 10)
            var fwdEast = new NodePosition(1f, 0f, 0f);
            var anchorEast = calc.CalculateIdealAnchor(playerPos, fwdEast);
            Assert(Math.Abs(anchorEast.X - 8.25f) < 1e-4f, "X should be 8.25.");
            Assert(Math.Abs(anchorEast.Z - 10f) < 1e-4f, "Z should be 10.");

            // 3. 180-degree turn: Facing South (0, 0, -1) -> Anchor should be North (10, 0, 11.75)
            var fwdSouth = new NodePosition(0f, 0f, -1f);
            var anchorSouth = calc.CalculateIdealAnchor(playerPos, fwdSouth);
            Assert(Math.Abs(anchorSouth.Z - 11.75f) < 1e-4f, "Z should be 11.75.");

            // 4. Personal space clamping
            var crowdedCandidate = new NodePosition(10f, 0f, 9.5f); // 0.5m away (< 1.0m min)
            var clamped = calc.ClampPersonalSpace(playerPos, crowdedCandidate);
            float clampedDist = playerPos.FlatDistanceTo(clamped);
            Assert(Math.Abs(clampedDist - 1.0f) < 1e-4f, $"Crowded anchor must be clamped to min separation 1.0m, got {clampedDist}");

            // 5. Lateral offset
            calc.LateralOffset = 0.5f; // 0.5m right flank
            var flankAnchor = calc.CalculateIdealAnchor(playerPos, fwdNorth);
            Assert(Math.Abs(flankAnchor.X - 10.5f) < 1e-4f, $"Flank anchor X should be 10.5, got {flankAnchor.X}");

            // 6. Exponential smoothing on sudden turn
            calc.LateralOffset = 0.0f;
            var currentAnchor = anchorNorth; // at (10, 0, 8.25)
            var suddenTarget = anchorSouth;  // player spun 180 deg to (10, 0, 11.75)
            var smoothed = calc.SmoothAnchor(currentAnchor, suddenTarget, deltaTime: 0.1f, smoothSpeed: 8.0f);
            Assert(smoothed.Z > currentAnchor.Z && smoothed.Z < suddenTarget.Z, "Smoothed position must interpolate smoothly toward target.");

            // 7. Zero vector fallback
            var zeroFwd = new NodePosition(0f, 0f, 0f);
            var fallback = calc.CalculateIdealAnchor(playerPos, zeroFwd);
            Assert(Math.Abs(playerPos.FlatDistanceTo(fallback) - 1.75f) < 1e-4f, "Zero forward must fallback safely without division by zero.");

            Console.WriteLine("PASS");
        }

        private static void Test20_ArrivalSteeringSpeedModulationAndSmoothDeceleration()
        {
            Console.Write("Test 20: Arrival steering speed modulation & smooth deceleration... ");

            var steering = new ArrivalSteeringCalculator(
                stoppingDistance: 0.35f,
                slowingRadius: 2.5f,
                acceleration: 8.0f,
                deceleration: 12.0f);

            float maxWalk = 5.0f;
            float maxSprint = 8.5f;

            // 1. Far away from target (> SlowingRadius) -> Full Speed
            float speedFar = steering.CalculateDesiredSpeed(distanceToTarget: 4.0f, maxSpeed: maxWalk);
            Assert(Math.Abs(speedFar - maxWalk) < 1e-4f, $"Distant target speed must be maxWalk ({maxWalk}), got {speedFar}");

            float speedSprintFar = steering.CalculateDesiredSpeed(distanceToTarget: 10.0f, maxSpeed: maxSprint);
            Assert(Math.Abs(speedSprintFar - maxSprint) < 1e-4f, $"Distant sprint speed must be maxSprint ({maxSprint}), got {speedSprintFar}");

            // 2. At or within StoppingDistance -> Zero Speed (prevents overshoot/jitter)
            float speedAtStop = steering.CalculateDesiredSpeed(distanceToTarget: 0.35f, maxSpeed: maxWalk);
            Assert(speedAtStop == 0f, $"Speed at stopping distance must be 0, got {speedAtStop}");

            float speedInsideStop = steering.CalculateDesiredSpeed(distanceToTarget: 0.15f, maxSpeed: maxWalk);
            Assert(speedInsideStop == 0f, $"Speed inside stopping distance must be 0, got {speedInsideStop}");

            // 3. Inside deceleration zone (between 0.35m and 2.5m) -> smoothly graduated speed
            float speedMid = steering.CalculateDesiredSpeed(distanceToTarget: 1.425f, maxSpeed: maxWalk);
            Assert(speedMid > 0f && speedMid < maxWalk, $"Midpoint speed ({speedMid}) must be strictly between 0 and {maxWalk}");

            // Verify monotonic deceleration: closer distance gives strictly lower speed
            float speedCloser = steering.CalculateDesiredSpeed(distanceToTarget: 0.8f, maxSpeed: maxWalk);
            float speedFurther = steering.CalculateDesiredSpeed(distanceToTarget: 2.0f, maxSpeed: maxWalk);
            Assert(speedCloser < speedMid, "Closer distance must have lower speed.");
            Assert(speedMid < speedFurther, "Further distance must have higher speed.");

            // 4. Acceleration limit (8.0 m/s^2, dt = 0.1s => max increase 0.8 m/s)
            float v0 = 0f;
            float v1 = steering.SmoothSpeed(currentSpeed: v0, targetSpeed: maxWalk, deltaTime: 0.1f);
            Assert(Math.Abs(v1 - 0.8f) < 1e-4f, $"From 0 to {maxWalk} with 8.0 accel and dt=0.1s must reach 0.8, got {v1}");

            // 5. Deceleration limit (12.0 m/s^2, dt = 0.1s => max decrease 1.2 m/s)
            float vTop = 5.0f;
            float vBraked = steering.SmoothSpeed(currentSpeed: vTop, targetSpeed: 0f, deltaTime: 0.1f);
            Assert(Math.Abs(vBraked - 3.8f) < 1e-4f, $"From 5.0 to 0.0 with 12.0 decel and dt=0.1s must reach 3.8, got {vBraked}");

            // 6. Horizontal steering velocity vector
            var currentPos = new NodePosition(0f, 0f, 0f);
            var targetPos = new NodePosition(0f, 0f, 5f); // 5m North
            var (vx, vz) = steering.CalculateSteeringVelocity(currentPos, targetPos, currentSpeed: 5.0f, maxSpeed: maxWalk, deltaTime: 0.1f);
            Assert(Math.Abs(vx) < 1e-4f, $"VX should be 0, got {vx}");
            Assert(Math.Abs(vz - 5.0f) < 1e-4f, $"VZ should be 5.0 towards North, got {vz}");

            // 7. Steering velocity inside stopping distance
            var (vxStop, vzStop) = steering.CalculateSteeringVelocity(currentPos, new NodePosition(0f, 0f, 0.2f), currentSpeed: 1f, maxSpeed: maxWalk, deltaTime: 0.1f);
            Assert(vxStop == 0f && vzStop == 0f, "Steering velocity inside stopping distance must be exactly (0, 0)");

            Console.WriteLine("PASS");
        }

        private static void Test21_StateSynchronizedSpeedAndSafeWaiting()
        {
            Console.Write("Test 21: State-synchronized speed & safe waiting behaviors... ");

            var fsm = new CompanionStateMachine(CompanionState.Following);
            fsm.WalkSpeed = 5.0f;
            fsm.SprintSpeed = 8.5f;

            var ctx = new CompanionContext
            {
                IsAlesaRescued = true,
                IsPlayerControlsEnabled = true,
                IsPlayerAiming = false,
                IsPlayerSprinting = false,
                HasPlayerMovementInput = true,
                DistanceToPlayer = 3.0f,
                DistanceToFollowAnchor = 2.0f,
                HasValidNavPath = true,
                IsStuck = false
            };

            // 1. Walking input -> Following at WalkSpeed (5.0 m/s)
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Following, "Walking input should be in Following state.");
            Assert(fsm.GetTargetSpeed() == 5.0f, $"Walking speed must be 5.0, got {fsm.GetTargetSpeed()}");

            // 2. Sprint input -> Sprinting at SprintSpeed (8.5 m/s)
            ctx.IsPlayerSprinting = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Sprinting, "Sprinting input should be in Sprinting state.");
            Assert(fsm.GetTargetSpeed() == 8.5f, $"Sprint speed must be 8.5, got {fsm.GetTargetSpeed()}");

            // 3. Aiming input while sprinting -> Immediate preemption to WaitingAim (0.0 m/s)
            ctx.IsPlayerAiming = true;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.WaitingAim, "Aiming must immediately preempt sprinting.");
            Assert(fsm.GetTargetSpeed() == 0.0f, $"Aiming target speed must immediately be 0.0, got {fsm.GetTargetSpeed()}");

            // 4. Safe formation during aim: anchor remains strictly behind Nathan
            var calc = new FollowAnchorCalculator(preferredDistance: 1.75f, minSeparation: 1.0f);
            var playerPos = new NodePosition(50f, 0f, 50f);
            var aimFwd = new NodePosition(0f, 0f, 1f); // Aiming North
            var anchor = calc.CalculateIdealAnchor(playerPos, aimFwd);

            // Verify Alesa's anchor is South of Nathan (behind him, never crossing into firing line)
            Assert(anchor.Z < playerPos.Z, "Anchor during aiming must remain strictly behind the player's firing cone.");
            Assert(Math.Abs(playerPos.FlatDistanceTo(anchor) - 1.75f) < 1e-4f, "Anchor distance behind Nathan must be maintained.");

            // 5. Aim released -> restores Sprinting if player still sprinting
            ctx.IsPlayerAiming = false;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Sprinting, "Releasing aim while sprinting resumes Sprinting state.");
            Assert(fsm.GetTargetSpeed() == 8.5f, "Speed restores to sprint speed.");

            // 6. Sprint stopped, player stationary -> transition to Idle
            ctx.IsPlayerSprinting = false;
            ctx.HasPlayerMovementInput = false;
            ctx.DistanceToFollowAnchor = 0.2f;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Idle, "Arriving at anchor with stationary player transitions to Idle.");
            Assert(fsm.GetTargetSpeed() == 0.0f, "Idle speed must be 0.0.");

            // 7. Modal or controls disabled (e.g. reading note or solving keypad) -> Idle
            ctx.HasPlayerMovementInput = true;
            ctx.IsPlayerControlsEnabled = false;
            fsm.EvaluateState(ctx);
            Assert(fsm.CurrentState == CompanionState.Idle, "Modal or disabled controls must force Idle state.");

            Console.WriteLine("PASS");
        }
    }
}

