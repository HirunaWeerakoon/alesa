# Agent 1 (Alesa) — System Architecture

## 1. Architectural Philosophy

Agent 1 follows a **Strict Separation of Concerns** design pattern:
1. **Core Domain (`Assets/Scripts/AI/Alesa/Core`):**
   - Pure C# classes with **zero `UnityEngine` dependencies**.
   - Contains mathematical graphs, priority queues, pathfinding algorithms (A*, Yen's K-Shortest Paths), and geometric classifiers.
   - Fully testable in standalone .NET test harnesses without opening the Unity Editor.
2. **Runtime Adapters (`Assets/Scripts/AI/Alesa/Runtime`):**
   - Unity `MonoBehaviour` wrappers connecting game events, scene transforms, `NavMeshAgent`, audio sources, and UI subtitles to the core domain.
   - Converts Unity `Vector3` coordinates into core `NodePosition` values.
3. **Editor Tooling (`Assets/Scripts/AI/Alesa/Editor`):**
   - Scene gizmos, graph validation inspectors, and automated waypoint connectivity checks.

```mermaid
graph TD
    subgraph Unity Scene / Teammate Scripts
        Keypad[DialpadKeypad / onAlesaFreed]
        Player[PlayerMotor / PlayerGameplay]
        Boss[Agent 3: PossessedBoss]
        Director[Agent 4: Encounter Director]
    end

    subgraph Agent 1 Runtime (Unity Layer)
        LifeCtrl[AlesaLifecycleController]
        Brain[AlesaBrain - FSM]
        Navigator[AlesaNavigator]
        Follower[AlesaFollower - NavMeshAgent]
        Voice[AlesaVoiceGuide & Subtitles]
        Blocker[AlesaRouteBlocker API]
    end

    subgraph Agent 1 Core (Pure C# Domain - Zero UnityEngine)
        WGraph[WaypointGraph]
        Heap[MinHeap Priority Queue]
        AStar[AStarPathfinder]
        Yen[YenKShortestPaths]
        TurnGeo[DirectionClassifier]
        RouteRes[RouteResult]
    end

    Keypad -->|Activates| LifeCtrl
    LifeCtrl --> Brain
    LifeCtrl --> Navigator
    Player --> Follower
    Player --> Voice
    Boss -.->|Corridor Blocked| Blocker
    Director -.->|Ambush Hazard| Blocker
    Blocker --> Navigator

    Navigator --> WGraph
    Navigator --> AStar
    Navigator --> Yen
    AStar --> Heap
    AStar --> RouteRes
    Voice --> TurnGeo
    Brain --> Follower
```

---

## 2. Component Breakdown

### Core Layer (Pure C#)
- `NodePosition`: Lightweight 3D spatial coordinate struct with Euclidean distance calculations, decoupled from `UnityEngine.Vector3`.
- `WaypointNode`: Represents a navigational waypoint with a stable alphanumeric ID, physical position, junction flag, and metadata.
- `WaypointEdge`: Represents a directed or bidirectional corridor connecting two nodes, with base length, dynamic risk penalties, blocked status, and block reasons.
- `WaypointGraph`: Adjacency-list graph holding nodes and edges, tracking a monotonically increasing `Version` number whenever edges are blocked or modified.
- `MinHeap<TElement, TPriority>`: High-performance binary min-heap implementation for $O(\log N)$ priority queue operations, ensuring compatibility across all Unity runtime profiles.
- `AStarPathfinder`: Implements the A* graph search algorithm using Euclidean straight-line distance as an admissible heuristic $h(n)$ to find the minimum-cost route.
- `RouteResult`: Immutable result object containing the route's node sequence, total cost, graph version, and execution status (`Valid`, `NoPath`, `StartNodeNotFound`, etc.).

### Runtime Layer (Unity MonoBehaviour)
- `AlesaLifecycleController`: Manages Alesa's transition from dormant (locked in hospital room) to active companion upon receiving Nathan's keypad unlock event.
- `AlesaBrain`: Finite State Machine managing high-level behavioral states (`Dormant`, `Following`, `Holding`, `Recovering`).
- `AlesaNavigator`: Bridges scene waypoint game objects with `WaypointGraph`, triggers pathfinding, and caches the primary route and alternatives.
- `AlesaFollower`: Drives the companion's physical movement using `NavMeshAgent`, positioning her at a dynamic trailing offset (~1.7m behind Nathan).
- `AlesaVoiceGuide`: Monitors player proximity to upcoming graph junctions (~5m threshold), detects relative turn angles (Left / Right / Straight), and plays voice clips with visual subtitle fallbacks.
- `AlesaRouteBlocker`: Receives environmental obstruction events (Boss presence, debris) and updates the underlying graph edge states.

---

## 3. Algorithm Selection Justification

### Why A* Over BFS or Dijkstra?
- **Breadth-First Search (BFS):** Unweighted graph traversal; assumes every hallway has the exact same cost. In the hospital maze, corridors have varied physical lengths and supernatural hazards, which BFS cannot handle optimally.
- **Dijkstra's Algorithm:** Guarantees the shortest path on weighted graphs, but searches blindly in all directions like an expanding circle.
- **A* Search:** Combines Dijkstra's accumulated cost $g(n)$ with a directed heuristic estimate $h(n)$ toward the exit ($f(n) = g(n) + h(n)$). Because Euclidean distance is an admissible heuristic (never overestimates the true remaining distance in physical space), A* is mathematically guaranteed to find the optimal path while exploring significantly fewer nodes than Dijkstra.

