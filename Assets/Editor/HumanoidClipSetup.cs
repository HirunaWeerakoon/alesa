using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Sets the new Mixamo clips to Humanoid and fixes their loop flags.
    /// </summary>
    /// <remarks>
    /// Mixamo exports arrive as Generic with Loop Time off. Generic will not retarget onto the Aria
    /// Blake or boss avatars at all, and a cyclic clip without Loop Time hitches once per cycle —
    /// every zombie clip in this project needed the same fix.
    /// </remarks>
    public static class HumanoidClipSetup
    {
        private const string Dir = "Assets/_Project/Animations/Humanoid/";

        /// <summary>Clip file name, and whether it is a cycle that must loop.</summary>
        private static readonly (string file, bool loop)[] Clips =
        {
            ("Female Start Walking", false),
            ("Female Walk",          true),
            ("Female Stop Walking",  false),
            ("Run To Stop",          false),
            ("Left Turn",            false),
            ("Right Turn",           false),
            ("Jogging",              true),
            ("Jog In Circle",        true),
            ("Nervously Look Around", true),
            ("Brutal Assassination", false),
            ("Superhuman Choke Lift", false),
        };

        [MenuItem("ALESA/Wire Humanoid Clips")]
        public static void Wire()
        {
            var log = new StringBuilder("[Humanoid clips]\n");

            foreach (var (file, loop) in Clips)
            {
                string path = Dir + file + ".fbx";
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) { log.AppendLine("  MISSING " + path); continue; }

                bool dirty = false;
                if (importer.animationType != ModelImporterAnimationType.Human)
                {
                    importer.animationType = ModelImporterAnimationType.Human;
                    dirty = true;
                }

                // Root motion stays off everywhere in this project: AgentMotor and AlesaFollower own
                // position. A clip with travel baked into the root would read as skating.
                var clips = importer.defaultClipAnimations;
                if (clips == null || clips.Length == 0) clips = importer.clipAnimations;
                if (clips != null && clips.Length > 0)
                {
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i].loopTime != loop) { clips[i].loopTime = loop; dirty = true; }
                        if (loop && !clips[i].loopPose) { clips[i].loopPose = true; dirty = true; }
                    }
                    importer.clipAnimations = clips;
                }

                if (dirty) importer.SaveAndReimport();

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string clipName = "?";
                float length = 0f;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var c = o as AnimationClip;
                    if (c == null || c.name.StartsWith("__preview__")) continue;
                    clipName = c.name; length = c.length; break;
                }

                log.AppendLine("  " + file.PadRight(22)
                    + (importer.animationType == ModelImporterAnimationType.Human ? "Humanoid" : "NOT HUMANOID")
                    + "  loop=" + loop
                    + "  clip='" + clipName + "' " + length.ToString("F2") + " s"
                    + (asset != null ? "" : "  (asset failed to load)"));
            }

            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }
    }
}
