using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Puts <see cref="PlayerVoice"/> on the player and fills in Nathan's hurt and breathing clips.
    /// </summary>
    public static class PlayerAudioSetup
    {
        private const string Male = "Assets/Audio/SFX/male/";

        private const string FirstHurt = Male + "457965__fabrizio84__male-hurt_001.wav";
        private const string SecondHurt = Male + "457965__fabrizio84__male-hurt_002.wav";
        private const string FatalHurt = Male + "731506__soundbitersfx__npcplayer-death-groans-male.wav";
        private const string Breathing = Male + "554907__nox_sound__male_breath_fast_loop_stereo.wav";

        [MenuItem("ALESA/Wire Player Voice")]
        public static void Wire()
        {
            var vitals = Object.FindFirstObjectByType<PlayerVitals>(FindObjectsInactive.Include);
            if (vitals == null)
            {
                Debug.LogError("[Player voice] No PlayerVitals in the scene; nothing to attach to.");
                return;
            }

            var voice = vitals.GetComponent<PlayerVoice>();
            if (voice == null) voice = vitals.gameObject.AddComponent<PlayerVoice>();

            var log = new System.Text.StringBuilder();
            var so = new SerializedObject(voice);
            Assign(so, "firstHurt", FirstHurt, log);
            Assign(so, "secondHurt", SecondHurt, log);
            Assign(so, "fatalHurt", FatalHurt, log);
            Assign(so, "lowHealthBreathing", Breathing, log);
            so.FindProperty("hurtVolume").floatValue = 1f;
            so.FindProperty("breathingVolume").floatValue = 0.8f;
            so.FindProperty("lowHealthFraction").floatValue = 0.34f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The breathing clip is a loop, and an AudioSource only loops cleanly if the asset is
            // not set to decompress-on-load with a trailing gap; leaving the import settings alone
            // is fine, but the loop flag on the source is set in PlayerVoice, not here.
            EditorUtility.SetDirty(voice);
            EditorSceneManager.MarkSceneDirty(voice.gameObject.scene);

            log.Insert(0, "[Player voice] on '" + vitals.gameObject.name + "':\n");
            Debug.Log(log.ToString());
        }

        private static void Assign(SerializedObject so, string field, string path, System.Text.StringBuilder log)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { log.AppendLine("  " + field + ": NO SUCH FIELD"); return; }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) { log.AppendLine("  " + field + ": MISSING " + path); return; }

            prop.objectReferenceValue = clip;
            log.AppendLine("  " + field + " = " + clip.name + " (" + clip.length.ToString("F2") + " s)");
        }
    }
}
