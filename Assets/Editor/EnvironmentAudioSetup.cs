using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Re-creates the scripted environment audio triggers in SampleScene.
    /// </summary>
    /// <remarks>
    /// A menu item rather than hand placement because these are loose GameObjects with no prefab
    /// behind them: both were lost outright the last time the scene was rebuilt, with nothing to
    /// show they had ever been there. Positions were measured off the baked NavMesh, not read off
    /// the floor plan, whose pixel scale is several metres out. Re-running is safe — it updates
    /// the existing objects rather than adding duplicates.
    /// </remarks>
    public static class EnvironmentAudioSetup
    {
        private const string ParentName = "Hospital Environment Audio";

        [MenuItem("ALESA/Wire Environment Audio Triggers")]
        public static void Wire()
        {
            var parent = GameObject.Find(ParentName);
            if (parent == null) parent = new GameObject(ParentName);

            // Room J: the player steps into the south room and the metal creaks overhead.
            // Box spans X 166..181, Z 19..32 — the room itself. The corridor running south at
            // X 166..168 is deliberately outside it, or walking past would fire the creak.
            Configure(
                parent,
                "Trigger - Room J Metal Creak",
                new Vector3(173.5f, 26.1f, 25.5f),
                new Vector3(15f, 3f, 13f),
                "Assets/Audio/SFX/815606__squarfington__quiet-metal-creaking.wav",
                delaySeconds: 0.4f,
                audibleRange: 22f);

            // The A-B corridor: fires as the player leaves room A and enters the corridor mouth.
            // The west leg measures X 86..88, Z 7..19, so this box sits across its southern end.
            Configure(
                parent,
                "Trigger - A-B Corridor Shrills",
                new Vector3(87f, 26.1f, 8f),
                new Vector3(4f, 3f, 4f),
                "Assets/Audio/Environment/240091__squashy555__haunted-shrills.wav",
                delaySeconds: 0.2f,
                audibleRange: 28f);

            EditorSceneManager.MarkSceneDirty(parent.scene);
            Debug.Log("[Environment audio] triggers wired under '" + ParentName + "'.");
        }

        private static void Configure(
            GameObject parent,
            string name,
            Vector3 position,
            Vector3 size,
            string clipPath,
            float delaySeconds,
            float audibleRange)
        {
            var existing = parent.transform.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;

            if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();
            var trigger = go.GetComponent<EnvironmentAudioTrigger>();
            if (trigger == null) trigger = go.AddComponent<EnvironmentAudioTrigger>();

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null) Debug.LogError("[Environment audio] clip not found: " + clipPath);

            var so = new SerializedObject(trigger);
            so.FindProperty("clip").objectReferenceValue = clip;
            so.FindProperty("size").vector3Value = size;
            so.FindProperty("once").boolValue = true;
            so.FindProperty("delaySeconds").floatValue = delaySeconds;
            so.FindProperty("audibleRange").floatValue = audibleRange;
            // Fire where the player crosses in, not at a random point along a path.
            so.FindProperty("pathPoints").arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[Environment audio] " + name + " at " + position.ToString("F1")
                + " size " + size.ToString("F0")
                + " clip " + (clip != null ? clip.name : "MISSING"));
        }
    }
}
