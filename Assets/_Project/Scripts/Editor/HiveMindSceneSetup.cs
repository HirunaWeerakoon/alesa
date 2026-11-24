using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click scene setup for the hive mind: the director that decides how many minions should be awake and
/// where an ambush waits, the pool of minions it controls, and the camera lock for neck bites.
/// Safe to run again: anything already in the scene is reused, nothing is duplicated.
/// </summary>
public static class HiveMindSceneSetup
{
    private const string PrefabFolder = "Assets/_Project/Prefabs/Characters/";

    [MenuItem("ALESA/Hive Mind/Set Up Scene")]
    public static void SetUp()
    {
        var log = new System.Text.StringBuilder();

        // 1. The hive mind itself
        var runner = Object.FindFirstObjectByType<HiveMindRunner>(FindObjectsInactive.Include);
        if (runner == null)
        {
            var go = new GameObject("Hive Mind");
            Undo.RegisterCreatedObjectUndo(go, "Add Hive Mind");
            runner = go.AddComponent<HiveMindRunner>();
            log.AppendLine("Added Hive Mind.");
        }
        else log.AppendLine("Hive Mind already in the scene.");

        // 2. The minion pool, with the two minion prefabs
        var director = Object.FindFirstObjectByType<MinionSpawnDirector>(FindObjectsInactive.Include);
        if (director == null)
        {
            var go = new GameObject("Minion Spawn Director");
            Undo.RegisterCreatedObjectUndo(go, "Add Minion Spawn Director");
            director = go.AddComponent<MinionSpawnDirector>();
            log.AppendLine("Added Minion Spawn Director (default zones from the code).");
        }
        else log.AppendLine("Minion Spawn Director already in the scene.");

        var prefabs = new[]
        {
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "BlindMinion_VariantA.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "BlindMinion_VariantB.prefab"),
        };
        var serialized = new SerializedObject(director);
        var list = serialized.FindProperty("minionPrefabs");
        if (list != null && list.arraySize == 0)
        {
            list.arraySize = prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            serialized.ApplyModifiedProperties();
            log.AppendLine("Assigned the two minion prefabs.");
        }

        // 3. Link the two
        Undo.RecordObject(runner, "Link Hive Mind");
        runner.spawnDirector = director;
        runner.controlMinions = true;
        EditorUtility.SetDirty(runner);

        // 4. The camera lock for neck bites
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            if (player.GetComponent<NeckBiteCameraLock>() == null)
            {
                Undo.AddComponent<NeckBiteCameraLock>(player);
                log.AppendLine("Added Neck Bite Camera Lock to the Player.");
            }
            else log.AppendLine("Player already has Neck Bite Camera Lock.");
        }
        else log.AppendLine("No object tagged Player found; Neck Bite Camera Lock not added.");

        EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
        Debug.Log("[Hive Mind setup]\n" + log + "Save the scene (Ctrl+S) to keep it.");
    }
}
