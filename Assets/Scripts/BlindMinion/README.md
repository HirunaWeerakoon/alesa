# Blind Minion (Agent 2)

**Owner:** Anuththara Rupasinghe
**Modules:**
- SE3062 Intelligent Systems: this agent
- SE3032 Graphics & Visualization: Agent Controller (movement and animation)

The infected patients are blind. They hunt the player only by **sound**: footsteps, doors, the revolver. The player's own weapon is what gives them away.

## Folder layout

| Folder | Assembly | What goes there |
|---|---|---|
| `Logic/` | `Alesa.BlindMinion.Logic` | The decision-making, in pure C# with no UnityEngine. Unit-testable, and kept separate from the visuals |
| `Unity/` | `Alesa.BlindMinion` | Thin MonoBehaviours that connect the logic to the scene (NavMeshAgent, gizmos) |
| `Tests/` | `Alesa.BlindMinion.Tests` | EditMode tests (*Window > General > Test Runner*) |

Everything lives in this folder and its own assemblies, so it can't clash with the rest of the team's scripts.

## Plan

- [ ] Noise events and a noise bus other scripts can report noises to
- [ ] First hearing model: straight-line distance
- [ ] Basic state machine: Dormant → Alerted → Investigating → Searching
- [ ] Unity agent that walks to what it heard (NavMeshAgent)
- [ ] Sound travels along corridors instead of through walls (graph + Dijkstra)
- [ ] Follow the sound's path; closed doors muffle sound
- [ ] Hunting for loud noises; reroute around locked doors (A*)
- [ ] Chain-reaction scream, uncertain hearing, alertness memory
- [ ] Smooth movement, turn on the spot, animation (Agent Controller)
