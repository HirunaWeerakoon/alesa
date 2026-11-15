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

            Console.WriteLine("--------------------------------------------------");
            Console.WriteLine(" ALL 12 TESTS PASSED SUCCESSFULLY!                ");
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
    }
}

