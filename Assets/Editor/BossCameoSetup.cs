using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Places the scripted corridor sighting and wires the boss's maze appearance voices.
    /// </summary>
    /// <remarks>
    /// A menu item rather than hand placement for the same reason as the environment triggers: this
    /// is a loose GameObject with no prefab behind it, and the last scene rebuild lost every one of
    /// those. The coordinates come from the scene's own corridor ceiling pieces -- the east corridor
    /// runs at z 21.5, the north one at x 167.5 -- not off the floor plan, whose pixel scale is out.
    /// Re-running is safe: it updates what is there rather than adding a second copy.
    /// </remarks>
    public static class BossCameoSetup
    {
        private const string CameoName = "Trigger - Boss Corridor Cameo";
        private const string BossPrefabPath = "Assets/_Project/Prefabs/Characters/PossessedBoss.prefab";
        private const string Boss = "Assets/Audio/SFX/boss/";

        // The sighting, in order.
        private const string PaleLady = Boss + "alesiadavina-passing-of-the-pale-lady-469254.mp3";
        private const string Laughter = Boss + "alesiadavina-disturbing-laughter-1-513977.mp3";

        // One of these plays at each maze appearance.
        private static readonly string[] AppearanceVoices =
        {
            Boss + "431979__horroraudio__kid-ghost-sigh.wav",
            Boss + "alesiadavina-ominous-horror-sound-evil-woman-9-vol-001-175946.mp3",
        };

        // The junction where the east corridor (z 21.5) meets the north one (x 167.5), and the point
        // up that corridor where she fades, just short of the branch across to room G.
        private static readonly Vector3 AppearPoint = new Vector3(167.5f, 24.6f, 21.5f);
        private static readonly Vector3 VanishPoint = new Vector3(167.5f, 24.6f, 36.0f);

        // Far enough back down the corridor that she is already walking by the time the player sees
        // her, and clear of room F's doorway so turning in the doorway does not set it off.
        private static readonly Vector3 TriggerPosition = new Vector3(152.0f, 26.1f, 21.5f);
        private static readonly Vector3 TriggerSize = new Vector3(7f, 3f, 4f);

        [MenuItem("ALESA/Wire Boss Corridor Cameo")]
        public static void Wire()
        {
            var log = new System.Text.StringBuilder();
            WireCameo(log);
            WireAppearanceVoices(log);
            Debug.Log(log.ToString());
        }

        private static void WireCameo(System.Text.StringBuilder log)
        {
            var existing = Object.FindFirstObjectByType<BossCorridorCameo>(FindObjectsInactive.Include);
            GameObject go = existing != null ? existing.gameObject : new GameObject(CameoName);
            go.name = CameoName;
            go.transform.position = TriggerPosition;

            if (go.GetComponent<BoxCollider>() == null) go.AddComponent<BoxCollider>();
            var cameo = go.GetComponent<BossCorridorCameo>();
            if (cameo == null) cameo = go.AddComponent<BossCorridorCameo>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
            if (prefab == null) log.AppendLine("[Boss cameo] MISSING boss prefab: " + BossPrefabPath);

            var first = AssetDatabase.LoadAssetAtPath<AudioClip>(PaleLady);
            var second = AssetDatabase.LoadAssetAtPath<AudioClip>(Laughter);
            if (first == null) log.AppendLine("[Boss cameo] MISSING clip: " + PaleLady);
            if (second == null) log.AppendLine("[Boss cameo] MISSING clip: " + Laughter);

            var so = new SerializedObject(cameo);
            so.FindProperty("bossPrefab").objectReferenceValue = prefab;
            so.FindProperty("player").objectReferenceValue = Object.FindFirstObjectByType<PlayerGameplay>(FindObjectsInactive.Include);
            so.FindProperty("size").vector3Value = TriggerSize;
            so.FindProperty("approachDirection").vector3Value = Vector3.right; // the clay arrow: east, out of room F
            // The walk to Alesa's cage passes through (152, 22.2) — measured off the NavMesh, not guessed —
            // so a 6 m radius here cannot be stepped over, and the 8 m lead keeps her ahead down the corridor.
            so.FindProperty("activationRadius").floatValue = 6f;
            so.FindProperty("minimumLead").floatValue = 8f;
            so.FindProperty("facingTolerance").floatValue = 180f;
            so.FindProperty("appearPoint").vector3Value = AppearPoint;
            so.FindProperty("vanishPoint").vector3Value = VanishPoint;
            // 1.8 is the Walking clip's authored pace: anything less blends idle in and her feet slide.
            so.FindProperty("walkSpeed").floatValue = 1.8f;
            so.FindProperty("vanishSeconds").floatValue = 1.2f;
            so.FindProperty("firstClip").objectReferenceValue = first;
            so.FindProperty("secondClip").objectReferenceValue = second;
            so.FindProperty("volume").floatValue = 1f;
            so.FindProperty("spatialBlend").floatValue = 0.55f;
            so.FindProperty("audibleRange").floatValue = 60f;
            so.FindProperty("walkOnly").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(cameo);
            EditorSceneManager.MarkSceneDirty(go.scene);

            log.AppendLine("[Boss cameo] trigger at " + TriggerPosition.ToString("F1")
                + " size " + TriggerSize.ToString("F0")
                + "; she walks " + AppearPoint.ToString("F1") + " -> " + VanishPoint.ToString("F1")
                + " (" + Vector3.Distance(AppearPoint, VanishPoint).ToString("F1") + " m at 1.8 m/s).");
        }

        private static void WireAppearanceVoices(System.Text.StringBuilder log)
        {
            var bosses = Object.FindObjectsByType<PossessedBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);

            // The prefab too, so a boss spawned at runtime has the voices as well.
            if (prefab != null)
            {
                var root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
                try
                {
                    var onPrefab = root.GetComponent<PossessedBoss>();
                    if (onPrefab != null && ApplyVoices(onPrefab, log, "prefab"))
                        PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            foreach (var boss in bosses)
            {
                if (ApplyVoices(boss, log, "scene '" + boss.gameObject.name + "'"))
                {
                    EditorUtility.SetDirty(boss);
                    EditorSceneManager.MarkSceneDirty(boss.gameObject.scene);
                }
            }

            if (bosses.Length == 0) log.AppendLine("[Boss voices] no PossessedBoss in the scene; prefab only.");
        }

        private static bool ApplyVoices(PossessedBoss boss, System.Text.StringBuilder log, string where)
        {
            var so = new SerializedObject(boss);
            var list = so.FindProperty("appearanceVoices");
            list.arraySize = AppearanceVoices.Length;

            int wired = 0;
            for (int i = 0; i < AppearanceVoices.Length; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AppearanceVoices[i]);
                if (clip == null) { log.AppendLine("[Boss voices] MISSING clip: " + AppearanceVoices[i]); continue; }
                list.GetArrayElementAtIndex(i).objectReferenceValue = clip;
                wired++;
            }
            so.FindProperty("appearanceVoiceVolume").floatValue = 1f;
            // She appears 8-45 m away by design; the body's own voice source is 3D with a 22 m
            // falloff, so these have to be the stinger's own or it is silent exactly when it matters.
            so.FindProperty("appearanceVoiceSpatialBlend").floatValue = 0.25f;
            so.FindProperty("appearanceVoiceRange").floatValue = 90f;
            so.ApplyModifiedPropertiesWithoutUndo();

            log.AppendLine("[Boss voices] " + where + ": " + wired + " of " + AppearanceVoices.Length + " appearance clips wired.");
            return wired > 0;
        }
    }
}
