# Final boss capsule prototype

Branch: `feat/final-boss`. Main scene: `Assets/Scenes/SampleScene.unity`.

## Play

- Rescue Alesa using the existing keypad, then enter the final maze.
- Six silver rounds are in a small silver box just before its entrance. Use E to collect.
- V switches between ordinary and silver ammunition (0.75 seconds). R reloads the selected type.
- Each ammunition type retains its own loaded cylinder and reserve; switching cannot create rounds.
- Ordinary bullets do nothing to this boss. One silver hit banishes it immediately, including during its grab windup.
- Banishment lasts at least 5–7 seconds and requires Nathan to move 3 metres before a new appearance.
- Appearances require a complete navigation path, at least 8 metres of separation and an unseen location. Visibility and distance are checked again after the warning.
- A failed grab has a recovery window. A successful grab after its 0.85 second warning causes instant death.
- Reach `Final Maze Exit` at the north-east dead end to win. Restart reloads the scene.

## Quick test

Enter Play mode, select `Final Boss Encounter`, and use the FinalBossEncounter component's context menu: **Prototype/Start Encounter (Play Mode)**.

That debug command moves Nathan to the maze entrance, supplies a revolver/six silver reserve rounds if missing, and bypasses the rescue gate. Normal entry does none of those things. Press V then R to prepare silver rounds.

The starting player position in the saved scene is unchanged. Inspector defaults are tuning values, not a final difficulty balance.

## Agent 4 integration

`PossessedBoss` is Agent 3: route prediction, reachable interception, pursuit/search, grab and silver reaction.

`FinalBossEncounter` is a small temporary director adapter using the existing `AmbushNode` markers. Disable `automaticAppearances` when the real director takes over. Connect its appearance requests to `boss.AppearAt(feet, rotation)`, subscribe to `boss.Banished` and `boss.RelocationRequested`, and call `boss.FinishEncounter()` when escaping. Keep the banishment cooldown and visibility safety rules in the final director.

Replace the `Boss visual - replace with model` child in `PossessedBossPrototype.prefab`; retain the feet origin, root NavMeshAgent and hit collider references. Reassign `visuals` and `bodyColliders` if replacing those objects.

`FinalWingNavigation.asset` is baked from existing collision geometry only, restricted to the final wing. Rebuild it after changing corridor geometry. Existing ProBuilder geometry was not rebuilt or altered.

## Checks performed

- All 15 authored nodes have complete paths from the maze entrance.
- A real multi-corner interception route was selected ahead of a moving player.
- Visible appearances were rejected; unseen appearances were accepted.
- Switching preserved both ammunition pools.
- Actual raycast shots: ordinary consumed one round without banishing; silver consumed one round and banished the boss without disabling its root.
- Banishment scheduled a 5–7 second safe interval and selected another unseen point.
- Successful grab caused death; Escape could not dismiss death.
- Reaching the exit produced the escape state.

Warning audio currently reuses a creak at reduced pitch. Dedicated boss sound, animation, and final balance are still needed.
