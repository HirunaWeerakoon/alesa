using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Level layout menu commands: where the minions live, extra ammunition, and the exit door.
/// Each can be run again; they replace what they set up rather than adding duplicates.
/// </summary>
public static class LevelLayoutSetup
{
    private struct ZoneSpec
    {
        public string label;
        public Vector3 center, size;
        public int count;
        public ZoneSpec(string label, float x, float z, float w, float d, int count)
        {
            this.label = label;
            center = new Vector3(x, 24.6f, z);
            size = new Vector3(w, 3f, d);
            this.count = count;
        }
    }

    // World coordinates read from the scene: the props in each room and the hive graph.
    private static readonly ZoneSpec[] Zones =
    {
        new ZoneSpec("Surgery",           106.5f,  -2.5f, 11f, 10f, 1),
        new ZoneSpec("D shower",          118.5f,  14.5f,  6f,  6f, 1),
        new ZoneSpec("E toilet",          118.5f,  23.5f,  6f,  6f, 1),
        new ZoneSpec("Cafeteria",         136.5f,  31.0f, 16f, 16f, 3),
        new ZoneSpec("G west (office)",   158.5f,  44.5f, 10f, 13f, 1),
        new ZoneSpec("H central (morgue)",174.5f,  48.5f,  8f,  8f, 1),
        new ZoneSpec("J tank room",       175.0f,  22.5f,  9f, 10f, 1),
        new ZoneSpec("I Alesa's cage room",187.0f, 47.0f,  8f, 16f, 2),
    };

    // Where the extra ammunition goes: the treatment storage room after the surgery.
    private static readonly Vector2[] AmmoSpots =
    {
        new Vector2(118.5f, -6.0f),
        new Vector2(125.5f, -3.0f),
    };

    [MenuItem("ALESA/Level/Apply Enemy and Ammo Layout")]
    public static void ApplyLayout()
    {
        var log = new System.Text.StringBuilder();

        // 1. Minion zones
        var director = Object.FindFirstObjectByType<MinionSpawnDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            Debug.LogError("[Level layout] No Minion Spawn Director in the scene. Run ALESA > Hive Mind > Set Up Scene first.");
            return;
        }

        var so = new SerializedObject(director);
        var zones = so.FindProperty("zones");
        zones.arraySize = Zones.Length;
        for (int i = 0; i < Zones.Length; i++)
        {
            var z = zones.GetArrayElementAtIndex(i);
            z.FindPropertyRelative("label").stringValue = Zones[i].label;
            z.FindPropertyRelative("center").vector3Value = Zones[i].center;
            z.FindPropertyRelative("size").vector3Value = Zones[i].size;
            z.FindPropertyRelative("count").intValue = Zones[i].count;
            z.FindPropertyRelative("sharedGroup").stringValue = "";
            z.FindPropertyRelative("prefabOverride").objectReferenceValue = null;
            log.AppendLine($"  {Zones[i].label}: {Zones[i].count}");
        }
        so.FindProperty("sharedGroupNames").arraySize = 0;
        so.FindProperty("sharedGroupCounts").arraySize = 0;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(director);

        int total = 0;
        foreach (var z in Zones) total += z.count;
        log.Insert(0, $"Minion zones set ({total} minions):\n");

        // 2. The two hand-placed minions are replaced by the zones above
        int removed = 0;
        foreach (var agent in Object.FindObjectsByType<Alesa.Agents.BlindMinion.BlindMinionAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (agent.transform.IsChildOf(director.transform)) continue; // the director's own pool
            log.AppendLine($"Removed hand-placed minion '{agent.name}' at {agent.transform.position:F1}.");
            Undo.DestroyObjectImmediate(agent.gameObject);
            removed++;
        }
        if (removed == 0) log.AppendLine("No hand-placed minions to remove.");

        // 3. Extra ammunition, copied from the existing ammo box
        var template = GameObject.Find("Ammo box (12 rounds)");
        if (template == null) template = GameObject.Find("Ammo box (storage)");
        if (template == null)
        {
            log.AppendLine("No 'Ammo box (12 rounds)' found to copy; no extra ammo placed.");
        }
        else
        {
            foreach (var old in Object.FindObjectsByType<InteractablePickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (old.name == "Ammo box (storage)") Undo.DestroyObjectImmediate(old.gameObject);

            int placed = 0;
            foreach (var spot in AmmoSpots)
            {
                var probe = new Vector3(spot.x, 24.6f, spot.y);
                if (!NavMesh.SamplePosition(probe, out var hit, 2f, NavMesh.AllAreas))
                {
                    log.AppendLine($"No floor found near {probe:F1}; skipped one ammo box.");
                    continue;
                }
                var copy = Object.Instantiate(template, template.transform.parent);
                Undo.RegisterCreatedObjectUndo(copy, "Place ammo");
                copy.name = "Ammo box (storage)";
                copy.SetActive(true);
                var renderer = copy.GetComponentInChildren<Renderer>();
                float halfHeight = renderer != null ? renderer.bounds.extents.y : 0.15f;
                copy.transform.position = new Vector3(hit.position.x, hit.position.y + halfHeight + 0.02f, hit.position.z);
                placed++;
            }
            log.AppendLine($"Placed {placed} extra ammo boxes in the treatment storage room.");
        }

        EditorSceneManager.MarkSceneDirty(director.gameObject.scene);
        Debug.Log("[Level layout]\n" + log + "Save the scene (Ctrl+S) to keep it.");
    }

    [MenuItem("ALESA/Level/Make Selected Object the Exit Door")]
    public static void MakeExitDoor()
    {
        var door = Selection.activeGameObject;
        if (door == null)
        {
            Debug.LogError("[Level layout] Select the exit door in the Hierarchy or Scene view first, then run this again.");
            return;
        }

        var encounter = Object.FindFirstObjectByType<FinalBossEncounter>(FindObjectsInactive.Include);
        if (encounter == null)
        {
            Debug.LogError("[Level layout] No Final Boss Encounter in the scene.");
            return;
        }

        // The interaction ray ignores trigger colliders, so the door needs a solid one.
        bool hasSolid = false;
        foreach (var c in door.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) hasSolid = true;
        if (!hasSolid)
        {
            var box = Undo.AddComponent<BoxCollider>(door);
            var renderer = door.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                box.center = door.transform.InverseTransformPoint(renderer.bounds.center);
                var s = door.transform.lossyScale;
                var size = renderer.bounds.size;
                box.size = new Vector3(size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)), size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)), size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
            }
        }

        var exit = door.GetComponent<FinalExitDoor>();
        if (exit == null) exit = Undo.AddComponent<FinalExitDoor>(door);
        Undo.RecordObject(exit, "Link exit door");
        exit.encounter = encounter;
        EditorUtility.SetDirty(exit);

        // Walking into the old exit trigger no longer ends the run; the door does.
        int disabled = 0;
        foreach (var zone in Object.FindObjectsByType<FinalBossZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!zone.exit || !zone.gameObject.activeSelf) continue;
            Undo.RecordObject(zone.gameObject, "Disable exit trigger");
            zone.gameObject.SetActive(false);
            disabled++;
        }

        EditorSceneManager.MarkSceneDirty(door.scene);
        Debug.Log($"[Level layout] '{door.name}' is now the exit door. Disabled {disabled} walk-in exit trigger(s). Save the scene (Ctrl+S) to keep it.");
    }
}
