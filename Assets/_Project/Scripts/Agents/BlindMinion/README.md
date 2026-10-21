# Agent 2: Blind Infected Minion

The minion hunts by sound. It can't see, so it reacts to:

- footsteps (louder when sprinting, almost silent when crouching)
- gunshots
- doors opening and slamming
- bottles thrown to lure it away

## How it works (viva notes)

| Part | File | Idea |
|---|---|---|
| Hearing | `Logic/AcousticModel.cs` | A noise spreads through the maze graph using **Dijkstra**. It loses 1 loudness unit per metre, `ClosedDoorLoss` (20) at a closed door and `JunctionLoss` (1.5) at each junction, where the sound splits. Walls block sound because sound only travels along corridors. |
| Direction | `BlindMinionBrain.BuildRoute` | The minion **retraces the sound's own path** (the Dijkstra predecessor chain) node by node. It follows the corridor round corners, never straight through walls. |
| Blocked door | `BlindMinionBrain.TryRerouteAround` | Sound passes through a locked door, but the minion can't. An *investigating* minion waits at the door. A *hunting* one looks for a way round with **A\***. |
| Decisions | `BlindMinionBrain.Tick` | A state machine: `Dormant → Alerted (0.6 s reaction) → Investigating / Hunting → Searching → Dormant`. Touching the player switches it to `Attacking`. |
| Chain reaction | `BlindMinionBrain.Scream` | When a minion starts hunting, it **screams** (loudness 30). The scream is a normal noise, so minions close to it hunt and scream in turn, and minions further away only investigate. The ripple fades out by itself. A minion ignores its own scream (`NoiseEvent.SourceId`), and hunters ignore other minions' screams so they stay on the player. |
| Uncertain hearing | `BlindMinionBrain.GuessSourcePosition` | Below perceived loudness 20, the minion only knows roughly where the noise came from. Its guess can be off by `6 × (1 − perceived / 20)` metres, so a gunshot gives your exact spot but a distant footstep only says "somewhere over there". Each minion has its own random seed, so different minions search different spots. |
| Stuck recovery | `BlindMinionBrain.Stuck` | If the minion hasn't got 0.25 m closer to its target in 2 s, it treats the target as reached. Otherwise a guess that lands inside a wall would freeze it forever. |
| Memory | `BlindMinionBrain.Alertness` | Hearing anything sets alertness to 1, which fades to 0 over 30 s. While alert, the minion hears quieter sounds (threshold `× (1 − 0.6 × alertness)`) and reacts faster (`× (1 − 0.5 × alertness)`). Sneaking past a minion you just disturbed is much harder. |
| Tuning | `Unity/BlindMinionAgent.cs` | Every threshold, speed, loss and memory value can be changed in the Inspector. |

Loudness units are "metres of reach in open air", so a footstep of 8 is heard up to 8 m down an empty corridor. Defaults:

| Noise | Loudness |
|---|---|
| Crouch step | 2 |
| Walk step | 8 |
| Sprint step | 16 |
| Bottle | 20 |
| Door slam | 22 |
| Gunshot | 60 |

The minion hears anything at 5 or more and hunts at 25 or more, or on any gunshot.

## Level 1: unit tests (no scene)

Go to *Window > General > Test Runner > EditMode* and click **Run All**. The `BlindMinionBrainTests` include:

- A gunshot two corridors away wakes the minion and it hunts. The perceived loudness is exactly 28.5.
- A quiet footstep far away is not heard.
- A closed door muffles a noise that an open door lets through.
- The minion goes to the corner first, not diagonally through the wall.
- It searches, then goes back to sleep.
- At a locked door with no way round, it waits there.
- Touching a sleeping minion gets you attacked.
- A hunting minion's scream wakes a neighbour that missed the original noise. Each minion screams once per hunt and ignores its own scream.
- A loud noise is pinpointed exactly, and a faint one only gives a rough guess. Two minions guess different spots, and a minion whose guess is unreachable gives up instead of freezing.
- A recently disturbed minion hears a step that a calm one misses, reacts faster, and calms down again after 30 s.

PlayMode tests: *Test Runner > PlayMode > Run All* replays the sandbox scenarios automatically, including being caught three times and respawning.

## Level 2: greybox sandbox (play it)

1. Menu **Alesa > Build Blind Minion Sandbox**. This creates `Scenes/Sandbox_BlindMinion.unity` with walls, a door, the maze graph and 3 sleeping minions, and bakes the NavMesh.
2. Press **Play**, click in the Game view, and turn on **Gizmos** in the Game view toolbar.
3. Things to try:
   - **Crouch (C)** past the minion in the west corridor, then **sprint** past it. The second time it hears you.
   - **Shoot (G)** near the closed cafeteria door. The minions in the cafeteria are alerted, but their route is blocked by the door. Hunting ones look for another way round, through the east loop.
   - **Open the door (E)**, then **throw a bottle (Q)** into a corner. The minions go to the bottle, not to you.
   - Walk into a sleeping minion and you get attacked. **Three hits and you respawn** at the start, with 2 s of grace.
   - **Fire (G)** and watch the HUD: every minion that starts hunting **screams**, which can wake minions the shot itself didn't reach. Lower the gunshot loudness on the player to see the ripple more clearly.
   - Sprint past the west minion, let it go back to sleep, then **walk** (don't sprint) past it within 30 s. A calm minion hears a walking step only within about 3 m, but a freshly alert one hears it up to about 6 m away. The HUD shows its current threshold.

Debug drawing (Gizmos on):

- **Blue circles:** the sound field. Each circle's size shows how loud the last noise was at that node.
- **Yellow line:** the route the minion is retracing.
- **Orange circle:** how unsure the minion is about where the noise came from.
- **Coloured ball above its head:** its state. Grey = dormant, orange = alerted, yellow = investigating, red = hunting, purple = searching, magenta = attacking.

To change the layout, edit the `Map` and `Links` arrays in `Sandbox/Editor/MinionSandboxBuilder.cs` and run the menu again.

The `Sandbox/` folder is test tooling only. The real player, doors and hospital level come from the Systems Engineer and the World Builder.
