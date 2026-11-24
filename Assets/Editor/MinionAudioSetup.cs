using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Adds <see cref="MinionVoice"/> to the minion prefabs and fills in each variant's clip set.
    /// </summary>
    /// <remarks>
    /// Kept as a menu item rather than done by hand in the Inspector so the two variants' sound
    /// sets stay written down and re-runnable: twelve clips across two prefabs is a lot to redo
    /// correctly after a merge, and a silently missing clip is invisible until play-testing.
    /// Re-running it is safe — it overwrites the same fields.
    /// </remarks>
    public static class MinionAudioSetup
    {
        private const string VariantA = "Assets/_Project/Prefabs/Characters/BlindMinion_VariantA.prefab";
        private const string VariantB = "Assets/_Project/Prefabs/Characters/BlindMinion_VariantB.prefab";

        private const string Tybug = "Assets/Tybug Studios/Zombie Voice Pack - Free/";
        private const string Horror = "Assets/ZombieHorrorPackageFree/WAV/";

        /// <summary>Clips shared by both variants: the body is the same meat either way.</summary>
        private static readonly Dictionary<string, string> Shared = new Dictionary<string, string>
        {
            { "bodyFallFar",     Horror + "BodyFall/Foley_BodyFall_001.wav" },
            { "bodyFallNear",    Horror + "BodyFall/Foley_BodyFall_002.wav" },
            { "footstepLeft",    Horror + "Footsteps/Footsteps_Zombie_concret_001.wav" },
            { "footstepRight",   Horror + "Footsteps/Footsteps_Zombie_concret_002.wav" },
            { "bodyShotImpact",  Horror + "Impact/Impact_Flesh_001.wav" },
            { "headShotImpact",  Horror + "Impact/Impact_Flesh_Gory_Light_001.wav" },
            { "attackWhiff",     Horror + "Impact/Impact_Flesh_Gory_Light_001.wav" },
            { "footstepRunLeft", Horror + "Footsteps/Footsteps_Zombie_concret_002.wav" },
            { "footstepRunRight",Horror + "Footsteps/Footsteps_Zombie_concret_003.wav" },
            { "footstepCrawl",   Horror + "Foley/Foley_Crawling_A_002.wav" },
            { "searchVocal",     Tybug  + "Zombie Hiss/zombie_hiss_010.wav" },
        };

        private static readonly Dictionary<string, string> VariantAClips = new Dictionary<string, string>
        {
            // Gory_Light_002 plays first, zombie_death_004 follows when it finishes.
            { "headshotKill",     Horror + "Impact/Impact_Flesh_Gory_Light_002.wav" },
            { "headshotKillTail", Tybug  + "Zombie Death/zombie_death_004.wav" },
            { "chargeVocal",      "Assets/Audio/SFX/435651__wakerone__zombie-demon-scream.wav" },
            { "alert",         Tybug  + "Zombie Moan/zombie_moan_001.wav" },
            { "neckBiteVoice", Tybug  + "Zombie Aggressive/zombie_agressive_039.wav" },
            { "neckBiteImpact",Horror + "Bite/Zombie_Attack_Bite_002.wav" },
            { "death",         Tybug  + "Zombie Death/zombie_death_004.wav" },
            { "dying",         Tybug  + "Zombie Death/zombie_death_010.wav" },
            { "idleBreath",    Tybug  + "Zombie Growl/zombie_growl_023.wav" },
            { "painGrunt",     Tybug  + "Zombie Grunt/zombie_grunt_006.wav" },
        };

        private static readonly Dictionary<string, string> VariantBClips = new Dictionary<string, string>
        {
            { "headshotKill",  Horror + "Blood/Blood_Splash_A_001.wav" },
            { "chargeVocal",   "Assets/Audio/SFX/640217__hoxton_freeman__zombie.mp3" },
            { "alert",         Horror + "VO/Zombie01/Zombie001_Attack_A_001.wav" },
            { "neckBiteVoice", Horror + "VO/Zombie01/Zombie001_Attack_A_002.wav" },
            { "neckBiteImpact",Horror + "Bite/Zombie_Attack_Bite_001.wav" },
            { "death",         Horror + "VO/Zombie01/Zombie001_Hurt_A_001.wav" },
            { "dying",         Horror + "VO/Zombie01/Zombie001_Hurt_A_002.wav" },
            { "idleBreath",    Horror + "VO/Zombie01/Zombie001_Idle_A_003.wav" },
            { "painGrunt",     Horror + "VO/Zombie01/Zombie001_Hurt_A_003.wav" },
            // Overrides the shared attack clip (which VariantA keeps).
            { "attack",        Horror + "VO/Zombie03/Zombie003_Attack_A_003.wav" },
        };

        /// <summary>Non-clip settings that differ per variant.</summary>
        private static readonly Dictionary<string, float> VariantAFloats = new Dictionary<string, float>();

        private static readonly Dictionary<string, float> VariantBFloats = new Dictionary<string, float>
        {
            // Only the opening of the hoxton_freeman recording is wanted.
            { "chargeVocalMaxSeconds", 2f },
        };

        [MenuItem("ALESA/Wire Minion SFX")]
        public static void Wire()
        {
            // Logged, not shown in a dialog: a modal blocks Unity's main thread, which stalls any
            // automation driving the Editor.
            Debug.Log(WireAll());
        }

        public static string WireAll()
        {
            var log = new System.Text.StringBuilder();
            log.AppendLine(Apply(VariantA, VariantAClips, VariantAFloats));
            log.AppendLine(Apply(VariantB, VariantBClips, VariantBFloats));
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        private static string Apply(string prefabPath, Dictionary<string, string> variantClips, Dictionary<string, float> variantFloats)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var voice = root.GetComponent<MinionVoice>();
                if (voice == null) voice = root.AddComponent<MinionVoice>();

                var so = new SerializedObject(voice);
                var missing = new List<string>();

                foreach (var set in new[] { Shared, variantClips })
                {
                    foreach (var pair in set)
                    {
                        var prop = so.FindProperty(pair.Key);
                        if (prop == null) { missing.Add(pair.Key + " (no such field)"); continue; }

                        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(pair.Value);
                        if (clip == null) { missing.Add(pair.Key + " -> " + pair.Value); continue; }

                        prop.objectReferenceValue = clip;
                    }
                }

                foreach (var pair in variantFloats)
                {
                    var prop = so.FindProperty(pair.Key);
                    if (prop == null) { missing.Add(pair.Key + " (no such field)"); continue; }
                    prop.floatValue = pair.Value;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

                string name = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                int wired = Shared.Count + variantClips.Count + variantFloats.Count - missing.Count;
                return missing.Count == 0
                    ? name + ": " + wired + " clips wired."
                    : name + ": " + wired + " wired, FAILED -> " + string.Join(", ", missing.ToArray());
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
