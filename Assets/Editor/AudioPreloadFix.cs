using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Turns on Preload Audio Data for the project's sound effects.
    /// </summary>
    /// <remarks>
    /// Why this exists: with preload off, the clip's samples are not in memory when the game calls
    /// Play, so Unity begins an asynchronous load and returns. <c>AudioSource.isPlaying</c> is false
    /// on that frame, and a <c>PlayOneShot</c> fired at a scare or an impact is simply lost. Most of
    /// this project's sound effects are short one-shots triggered at the exact moment something
    /// happens, which is the worst case for it: they were silently dropped.
    ///
    /// Streaming clips are left alone — those are long ambient beds where the load is the point, and
    /// nothing one-shots them.
    /// </remarks>
    public static class AudioPreloadFix
    {
        private static readonly string[] Folders =
        {
            "Assets/Audio",
            "Assets/ZombieHorrorPackageFree",
            "Assets/Tybug Studios",
        };

        [MenuItem("ALESA/Fix Audio Preload Settings")]
        public static void Fix()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", Folders);
            var log = new StringBuilder();
            int changed = 0, skipped = 0, streaming = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null) continue;

                    var settings = importer.defaultSampleSettings;
                    if (settings.loadType == AudioClipLoadType.Streaming) { streaming++; continue; }
                    if (settings.preloadAudioData) { skipped++; continue; }

                    settings.preloadAudioData = true;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                    changed++;
                    if (changed <= 40) log.AppendLine("  " + path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            log.Insert(0, "[Audio preload] " + changed + " clips switched on, " + skipped
                + " already on, " + streaming + " streaming (left alone):\n");
            Debug.Log(log.ToString());
        }
    }
}
