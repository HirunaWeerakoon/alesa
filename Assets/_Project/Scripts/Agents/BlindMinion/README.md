# Agent 2: Blind Infected Minion – Intelligent Systems (IS) Architecture & Specification

## 1. Executive Summary & Module Scope

The Blind Minion is an acoustic-driven predator agent engineered for survival-horror gameplay. Devoid of visual faculties, its cognitive and behavioral architecture relies entirely on mathematical graph sound propagation, spatial occlusion evaluation, short-term auditory memory, and tactical combat arbitration.

### Strict Architectural Boundaries (IS vs. GV)
- **Intelligent Systems (IS - This Module):** Pure mathematical models, topological Dijkstra/A* routing, non-linear suspicion decay curves, headless state machines, combat decision arbitration, and noise event dispatching. Independent of engine rendering, rigidbodies, and animation controllers.
- **Graphics & Visualization (GV - Student 4 Duty):** 3D skeletal meshes, skinned rigs, textures, shaders, Animator controllers, Inverse Kinematics (IK), visual motion bridges, and footstep particle effects. Communicates with IS strictly via read-only movement intents and event hooks.

---

## 2. Finite State Machine (FSM) State Transition Matrix

The Blind Minion's decision brain executes a deterministic finite state machine operating at tick frequencies independent of visual frame rates.

```
       ┌───────────────────────────────┐
       │            DORMANT            │◄─────────────────────────────┐
       └──────┬─────────────────▲──────┘                              │
              │ Noise heard     │ Search timeout                      │
              ▼                 │                                     │
       ┌───────────────┐        │                                     │
       │    ALERTED    │        │                                     │
       └──┬─────────┬──┘        │                                     │
Reaction  │         │ Reaction  │                                     │
(Quiet)   │         │ (Alarming)│                                     │
          ▼         ▼           │                                     │
  ┌─────────────┐ ┌───────────┐ │                                     │
  │INVESTIGATING│ │  HUNTING  │─┼──────────────┐                      │
  └──────┬──────┘ └─────┬─────┘ │              │ Touch / In Reach     │
         │ Arrival/     │ Hunt  │              ▼                      │
         │ Timeout      │ Mem   │       ┌─────────────┐               │
         │              │ Exp   │       │  ATTACKING  │               │
         ▼              ▼       │       └──────┬──────┘               │
       ┌───────────────────────┐│              │ Target flees         │
       │       SEARCHING       ├┘              │                      │
       └───────────────────────┘               ▼                      │
                 ▲                       (Resume Hunt)                │
                 └────────────────────────────────────────────────────┘
```

### Complete State Transition Matrix

| Current State | Event / Stimulus Condition | Next State | Actions & Side Effects |
| :--- | :--- | :--- | :--- |
| **Dormant** | $L_{\text{perceived}} \ge L_{\text{threshold}}$ (Quiet: $L_{\text{perceived}} < 25$) | **Alerted** | Compute reaction delay $\tau = \tau_0(1 - 0.5 \cdot A)$; face perceived waypoint; queue `Investigating`. |
| **Dormant** | Gunshot OR $L_{\text{perceived}} \ge 25$ | **Alerted** | Compute accelerated reaction delay; queue `Hunting`. |
| **Dormant** | Player touches minion ($d \le \text{TouchRange}$) | **Attacking** | Immediate touch attack trigger; invoke `Attacked`; begin attack cooldown timer. |
| **Alerted** | $t_{\text{in\_state}} \ge \tau$ AND queued `Investigating` | **Investigating** | Begin walking pace locomotion ($1.8\text{ m/s}$) retracing Dijkstra acoustic predecessor route. |
| **Alerted** | $t_{\text{in\_state}} \ge \tau$ AND queued `Hunting` | **Hunting** | Begin sprinting pace locomotion ($5.5\text{ m/s}$); invoke `Screamed`; broadcast scream on `NoiseBus`. |
| **Alerted** | Secondary noise heard during reaction | **Alerted** | If new noise is alarming (gunshot/$L \ge 25$), upgrade queued state to `Hunting`. |
| **Alerted** | Player enters $\text{AttackRange}$ ($d \le 1.8\text{ m}$) | **Attacking** | Preempt reaction delay; immediately commit to melee strike. |
| **Investigating**| Arrived at final waypoint ($d \le \text{ArriveRadius}$) | **Searching** | Set movement intent to Idle; record arrival timestamp; initialize search sweep timer. |
| **Investigating**| Traversal blocked by locked door with no bypass | **Searching** | Abort path traversal; halt at threshold; enter localized listening search. |
| **Investigating**| Stuck timeout ($d_{\text{target}}$ unchanged $> 2.0\text{ s}$) | **Searching** | Fallback recovery: treat target as reached; prevent perpetual wall bumping. |
| **Investigating**| Alarming noise heard ($L \ge 25$ or gunshot) | **Hunting** | Upgrade directly to `Hunting`; recalculate route; invoke `Screamed`. |
| **Investigating**| Player enters $\text{AttackRange}$ ($d \le 1.8\text{ m}$) | **Attacking** | Immediate melee transition. |
| **Hunting** | Reached target acoustic locus | **Searching** | Transition to sweep search around LKP (Last Known Position). |
| **Hunting** | Corridor blocked by locked door | **Hunting** | Trigger `A*` reroute around obstruction through adjacent loops. If no bypass, enter `Searching`. |
| **Hunting** | Hunt memory timeout ($t - t_{\text{last\_heard}} > 20\text{ s}$) | **Searching** | Cold trail timeout; switch to localized search pattern. |
| **Hunting** | Player enters $\text{AttackRange}$ ($d \le 1.8\text{ m}$) | **Attacking** | Initiate combat decision arbitration. |
| **Attacking** | Target distance exceeds escape envelope ($d > 2.7\text{ m}$) | **Hunting** | Target broken contact; hunt exact last tactile position; no duplicate screech broadcast. |
| **Attacking** | Attack cooldown elapsed AND target in range | **Attacking** | Re-evaluate `CombatDecisionArbiter`; invoke `Attacked`. |
| **Searching** | Search duration expired ($t_{\text{in\_state}} \ge 4.0\text{ s}$) | **Dormant** | Reset alertness decay curve; enter relaxed dormant rest posture. |
| **Searching** | New noise heard ($L \ge L_{\text{threshold}}$) | **Alerted** / **Hunting** | Recalculate route toward new stimulus locus. |

---

## 3. Mathematical Specifications & Attenuation Models

### 3.1. Topological Sound Field Propagation
Sound travels through the hospital graph $G = (V, E)$ via Dijkstra shortest-path propagation where edge weights equal metric lengths plus topology penalties:
$$D(u, v) = \text{EuclideanDistance}(u, v) + \text{DoorLoss} \cdot \mathbb{I}_{\text{closed}} + \text{JunctionLoss} \cdot \mathbb{I}_{\text{junction}}$$

- **Closed Door Loss ($TL_{\text{door}}$):** $20.0\text{ dB / loudness units}$.
- **Junction Splitting Loss ($TL_{\text{junction}}$):** $1.5\text{ dB / loudness units}$.
- **Distance Attenuation:** $1.0\text{ unit per linear metre}$.

### 3.2. Direct Acoustic Occlusion & Material Transmission Loss
For direct line-of-hearing ray paths between acoustic source $\mathbf{x}_s$ and listener $\mathbf{x}_l$:
$$L_{\text{perceived}} = L_0 - \|\mathbf{x}_s - \mathbf{x}_l\| - \sum_{k=1}^N TL_{\text{barrier}, k} - TL_{\text{portal}}$$

#### Material Transmission Loss Indices
$$TL_{\text{barrier}} = TL_{\text{base}}(\text{material}) \cdot \max(0.1, \text{thickness})$$

| Material Type (`AcousticMaterialType`) | Baseline $TL_{\text{base}}$ ($\text{dB/m}$) | Typical Usage in Level |
| :--- | :--- | :--- |
| **Air** | $0\text{ dB}$ | Unobstructed hallway line of sight |
| **GlassWindow** | $8\text{ dB}$ | Observation windows, ward partitions |
| **WoodenDoor** | $14\text{ dB}$ | Patient room doors, office doors |
| **DrywallPartition** | $18\text{ dB}$ | Interior divider walls |
| **HeavyFireDoor** | $25\text{ dB}$ | Stairwell fire seals, quarantine boundaries |
| **ReinforcedConcrete** | $35\text{ dB}$ | Structural load-bearing corridor walls |

#### Doorway & Portal Aperture Diffraction Loss
When sound traverses an open, ajar, or sealed portal:
$$TL_{\text{portal}}(f_{\text{open}}, w) = \begin{cases} 
0.0\text{ dB} & \text{if } f_{\text{open}} \ge 0.95 \quad \text{(fully open)} \\
20.0\text{ dB} & \text{if } f_{\text{open}} \le 0.05 \quad \text{(sealed boundary)} \\
-10 \log_{10}\left(\frac{f_{\text{open}} \cdot w}{w}\right) + 3.0\text{ dB} & \text{otherwise (ajar slit diffraction)}
\end{cases}$$

### 3.3. Dynamic Threat Scoring & Acoustic Priority
Acoustic events are scored and prioritized in a bounded binary-search heap (`NoisePriorityQueue`):
$$\text{ThreatScore} = \frac{L_{\text{perceived}} \cdot W(\text{type})}{1.0 + 0.05 \cdot \|\mathbf{x}_{\text{noise}} - \mathbf{x}_{\text{minion}}\|}$$

| Noise Type (`NoiseType`) | Category Multiplier $W(\text{type})$ | Inherent Threat Justification |
| :--- | :--- | :--- |
| **Gunshot** | $3.0$ | Violent acoustic spike, immediate lethal prey identification |
| **Scream** | $2.5$ | Kin distress / pack alert broadcast |
| **DoorSlam** | $1.8$ | Heavy mechanical impact |
| **ThrownObject** | $1.6$ | Environmental distraction lure |
| **Trolley** | $1.4$ | Continuous mechanical roll |
| **Sprint** | $1.3$ | Rapidly moving bipedal footsteps |
| **Footstep** | $1.0$ | Subtle ambient locomotion |

### 3.4. Sensory Memory & Excitation Accumulation
Short-term auditory impressions persist in a rolling retention buffer (`SensoryMemoryBuffer`). Cumulative excitation decays exponentially over half-life $t_{\text{half}} = 6.0\text{ s}$:
$$E(t) = \sum_{i \in \text{Memory}} L_i \cdot W(\text{type}_i) \cdot \exp\left(-\ln(2) \frac{t - t_i}{t_{\text{half}}}\right)$$

Acoustic velocity vector is estimated across sequential impressions $\Delta \mathbf{x} = \mathbf{x}_{\text{new}} - \mathbf{x}_{\text{old}}$:
$$\hat{\mathbf{v}}_{\text{acoustic}} = \frac{\Delta \mathbf{x}}{\|\Delta \mathbf{x}\|} \quad \text{for } \|\Delta \mathbf{x}\| \ge 0.2\text{ m}, \Delta t > 0.05\text{ s}$$

### 3.5. Suspicion Decay Curves & Adaptive Reaction Time
Alertness $A(t) \in [0, 1]$ dissipates across silence duration $T = 30.0\text{ s}$. Under the default **Sigmoidal Tension** profile, alertness plateaus during initial inspection before rolling off smoothly:
$$A(t) = A_0 \cdot \frac{1}{1.0 + \left(\frac{t / T}{1.0001 - t / T}\right)^{2.5}}$$

Adaptive secondary reaction delay accelerates with residual alertness:
$$\tau_{\text{reaction}} = \tau_0 \cdot (1.0 - \min(0.9, \alpha_{\text{speedup}} \cdot A(t))), \quad \alpha_{\text{speedup}} = 0.5$$

### 3.6. Combat Decision & Hit Probability Arbitration
The `CombatDecisionArbiter` evaluates strikes based on spatial distance and relative target closing velocity:
$$v_{\text{closing}} = -\hat{\mathbf{d}}_{\text{to\_target}} \cdot \mathbf{v}_{\text{target}}$$
$$P_{\text{hit}} = \left(1.0 - \frac{d}{d_{\text{reach}}}\right) \cdot \text{clamp}(1.0 + 0.1 \cdot v_{\text{closing}}, 0.5, 1.2) \cdot W_{\text{intent}}$$

- Standard Melee Strike reach: $1.8\text{ m}$
- Leaping Lunge reach: $3.2\text{ m}$
- Frenzy Flurry trigger: $\ge 2$ consecutive hits, cooldown reduced from $1.5\text{ s}$ to $0.9\text{ s}$ ($0.6\times$).

---

## 4. Shared Blackboard & Inter-Minion Communication Schema

Inter-agent communication operates through the decouple-first `NoiseBus` and `SharedBlackboard`.

### 4.1. `NoiseEvent` Data Contract
```csharp
public readonly struct NoiseEvent
{
    public readonly Vector3 Position;     // World coordinates of acoustic origin
    public readonly float Loudness;       // Source amplitude (metres reach in open air)
    public readonly NoiseType Type;       // Category classification
    public readonly float Time;           // Simulation timestamp
    public readonly int SourceId;         // Originating entity ID (-1 for world/player)
}
```

### 4.2. Screech Alert Relay & Echo Loop Suppression
When a minion starts hunting, it invokes `TryBroadcastScreech`. Infinite reverberation cascades are suppressed via:
1. **Self-Id Filtering:** Each minion checks `noise.SourceId == Id` and ignores self-emitted sounds.
2. **Relay Cooldown:** Minion alert broadcast enforced minimum cooldown $\Delta t_{\text{cooldown}} = 4.0\text{ s}$.
3. **Hunter Preemption:** Active hunters and currently alerted minions ignore remote screeches to prevent losing direct player tracking leads.

### 4.3. Blackboard Keys & Shared State Schema
| Blackboard Key | Data Type | Usage & Lifetime |
| :--- | :--- | :--- |
| `blind_minion.alert_relay` | `ScreechAlertRecord` | Stamped minion alert record with position and loudness. |
| `blind_minion.target_lkp` | `Vector3` | Last Known Position centroid shared across nearby pack members. |
| `blind_minion.lkp_uncertainty` | `float` | Expanding radius of uncertainty around LKP ($1.5\text{ m/s}$ inflation). |
| `blind_minion.group_agitation` | `float` | Normalized pack agitation level $[0, 1]$ computed by `MinionAlertRelay`. |

---

## 5. Interface Contract for Graphics & Visualization (GV - Student 4)

Student 4's visual locomotion, rigging, and animation controllers consume the following pure read-only properties from `BlindMinionAgent` and `BlindMinionBrain`:

### 5.1. Locomotion Intent (`MovementIntent`)
```csharp
public readonly struct MovementIntent
{
    public readonly bool HasDestination; // True when navigation is active
    public readonly Vector3 Destination; // Target waypoint position
    public readonly float DesiredSpeed;  // 0.0 (Idle), 1.8 m/s (Investigate), 5.5 m/s (Hunt)
    public readonly bool HasLookAt;      // True when head orientation is constrained
    public readonly Vector3 LookAt;      // Spatial orientation focal point
}
```

### 5.2. Waypoint Path Array Consumption
- **`IReadOnlyList<int> Route`:** Ordered node indices representing the current acoustic path.
- **`int RouteIndex`:** Current progression index along the route array.
- **`Vector3 FinalTarget`:** Epiventer target position of the acoustic stimulus.

### 5.3. Animation State & Audio Event Hooks
Visual and audio systems should bind to these pure C# event delegates without modifying IS brain logic:
- `event Action<MinionState> StateChanged`: Drives animator parameters (`IsDormant`, `IsAlerted`, `IsInvestigating`, `IsHunting`, `IsSearching`, `IsAttacking`).
- `event Action Attacked`: Triggers attack swing animation clip, claw trail particle effects, and impact hurtbox activation.
- `event Action Screamed`: Triggers vocal screech audio source and camera shake impulse.

---

## 6. Verification & Test Execution

The Blind Minion IS module includes a comprehensive suite of headless EditMode unit and integration tests requiring zero Unity scene loading or physics simulations:

```powershell
# In Unity Test Runner:
# Window > General > Test Runner > EditMode > Run All
```

1. **`BlindMinionBrainTests.cs`:** Core Dijkstra routing, door rerouting, hearing thresholds, and memory decay.
2. **`BlindMinionPerceptionTests.cs`:** Decibel loss through multi-material layers, portal diffraction, priority queue threat ranking, and alert relay cooldowns.
3. **`BlindMinionIntegrationTests.cs`:** End-to-end headless FSM cycles, LKP search grid reachability fallbacks, and closing velocity combat arbitration.
