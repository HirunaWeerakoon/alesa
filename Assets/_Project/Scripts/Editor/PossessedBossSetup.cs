using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Alesa.Runtime.Motion;
using Alesa.Runtime.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Alesa.Editor
{
    /// <summary>
    /// Extracts and binds embedded PossessedBoss FBX textures & Built-in Standard materials,
    /// builds PossessedBoss.prefab wired to SharedHumanoidLocomotion.controller + AgentMotor +
    /// HeadLookAt + PossessedBoss + PossessedBossMotorBridge, replaces the capsule prototype in
    /// SampleScene, and bridges the Final Maze Entry NavMesh seam with a bidirectional NavMeshSeamLink.
    /// </summary>
    [InitializeOnLoad]
    public static class PossessedBossSetup
    {
        private static bool _scheduledAutoRun;

        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string HospitalNavAssetPath = "Assets/_Project/Navigation/SampleSceneHospitalNavMesh.asset";
        private const string FinalWingNavAssetPath = "Assets/FinalBoss/FinalWingNavigation.asset";
        private const string PrototypePrefabPath = "Assets/FinalBoss/PossessedBossPrototype.prefab";
        private const string WarningClipGuid = "3f9029ffc18f5eb4ca1a916f7e4312e9";

        private const string PrimaryControllerPath = "Assets/_Project/Animations/Humanoid/SharedHumanoidLocomotion.controller";
        private const string FallbackControllerPath = "Assets/_Project/Animations/SharedHumanoidLocomotion.controller";

        private const string BossFolder = "Assets/_Project/Models/Boss";
        private const string FbxPath = BossFolder + "/PossessedBoss.fbx";
        private const string TexturesFolder = BossFolder + "/Textures";
        private const string MaterialsFolder = BossFolder + "/Materials";

        private const string PrefabFolder = "Assets/_Project/Prefabs/Characters";
        private const string BossPrefabPath = PrefabFolder + "/PossessedBoss.prefab";

        private const string StatusLogPath = "Temp/PossessedBossSetupStatus.txt";

        private static readonly string[] BossMaterialNames = { "Body", "Hair", "material" };

        // Exact Mixamo FBX Video -> Material/Slot mapping verified from PossessedBoss.fbx Connections:
        // Body:     DiffuseColor=Image_0, NormalMap=Image_2
        // Hair:     DiffuseColor=Image_3, NormalMap=Image_5
        // material: DiffuseColor=Image_6, NormalMap=Image_8
        private static readonly Dictionary<string, string> MixamoImageAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Image_0", "PossessedBoss_Body_diffuse" },
            { "Image_2", "PossessedBoss_Body_normal" },
            { "Image_3", "PossessedBoss_Hair_diffuse" },
            { "Image_5", "PossessedBoss_Hair_normal" },
            { "Image_6", "PossessedBoss_material_diffuse" },
            { "Image_8", "PossessedBoss_material_normal" },
        };

        private static readonly Vector3 DefaultBossPosition = new Vector3(182.0f, 24.66f, 84.0f);
        private static readonly Vector3 SeamLinkStartWorld = new Vector3(188.5f, 24.65f, 54.0f);
        private static readonly Vector3 SeamLinkEndWorld = new Vector3(188.5f, 24.65f, 60.5f);
        private const float SeamLinkWidth = 2.5f;

        static PossessedBossSetup()
        {
            if (Application.isBatchMode) return;
            if (_scheduledAutoRun) return;

            _scheduledAutoRun = true;
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!NeedsSetup()) return;
            RunSetupInternal(interactive: false);
        }

        private static bool NeedsSetup()
        {
            if (!File.Exists(FbxPath) || !File.Exists(SampleScenePath))
                return false;

            return !File.Exists(BossPrefabPath);
        }

        [MenuItem("ALESA/Setup Possessed Final Boss (Textures + Prefab + SampleScene)", false, 24)]
        public static void RunFullSetupFromMenu()
        {
            RunSetupInternal(interactive: true);
        }

        public static void RunBatchSetup()
        {
            RunSetupInternal(interactive: false);
        }

        private static void RunSetupInternal(bool interactive)
        {
            var report = new StringBuilder();
            try
            {
                report.AppendLine($"STARTED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})");

                // 0. Resolve preserved warning AudioClip from scene prototype / prototype prefab
                Scene scene = EnsureSampleSceneOpen();
                AudioClip preservedWarningClip = ResolvePreservedWarningClip(report);

                // 1. Extract & bind textures and Standard materials for PossessedBoss.fbx
                report.AppendLine("=== 1. TEXTURE EXTRACTION & MATERIAL REMAPPING ===");
                ExtractAndBindTexturesAndMaterials(report);

                // 2. Build PossessedBoss.prefab
                report.AppendLine("=== 2. PREFAB CREATION ===");
                RuntimeAnimatorController sharedController = LoadSharedLocomotionController();
                report.AppendLine($"AnimatorController: {AssetDatabase.GetAssetPath(sharedController)}");

                GameObject bossPrefab = BuildPossessedBossPrefab(sharedController, preservedWarningClip, report);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 3. Wire SampleScene.unity & bridge Final Maze Entry NavMesh seam
                report.AppendLine("=== 3. SAMPLESCENE WIRING & NAVMESH SEAM BRIDGE ===");
                WireSampleScene(scene, bossPrefab, preservedWarningClip, report);

                AssetDatabase.SaveAssets();

                string summary = "SUCCESS\n" + report;
                WriteStatusLog(summary);
                Debug.Log("[PossessedBossSetup] " + summary);

                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "Possessed Boss Setup Complete",
                        "Extracted textures & materials, built PossessedBoss.prefab, wired SampleScene, and bridged the Final Maze Entry NavMesh seam.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                string failure = $"FAILED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n{report}\nEXCEPTION:\n{ex}\n";
                WriteStatusLog(failure);
                Debug.LogError("[PossessedBossSetup] Setup failed:\n" + failure);
                if (Application.isBatchMode) throw;
            }
        }

        private static Scene EnsureSampleSceneOpen()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.path, SampleScenePath, StringComparison.OrdinalIgnoreCase))
            {
                scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }
            return scene;
        }

        private static AudioClip ResolvePreservedWarningClip(StringBuilder report)
        {
            AudioClip clip = null;

            GameObject encounterGo = GameObject.Find("Final Boss Encounter");
            if (encounterGo != null)
            {
                PossessedBoss existingBoss = encounterGo.GetComponentInChildren<PossessedBoss>(true);
                if (existingBoss != null && existingBoss.warningClip != null)
                {
                    clip = existingBoss.warningClip;
                    report.AppendLine($"Preserved warningClip from scene boss '{existingBoss.gameObject.name}': {AssetDatabase.GetAssetPath(clip)}");
                    return clip;
                }
            }

            GameObject protoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrototypePrefabPath);
            if (protoPrefab != null)
            {
                PossessedBoss protoBoss = protoPrefab.GetComponent<PossessedBoss>();
                if (protoBoss != null && protoBoss.warningClip != null)
                {
                    clip = protoBoss.warningClip;
                    report.AppendLine($"Preserved warningClip from prototype prefab: {AssetDatabase.GetAssetPath(clip)}");
                    return clip;
                }
            }

            string guidPath = AssetDatabase.GUIDToAssetPath(WarningClipGuid);
            if (!string.IsNullOrEmpty(guidPath))
            {
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(guidPath);
                if (clip != null)
                {
                    report.AppendLine($"Preserved warningClip by GUID: {guidPath}");
                    return clip;
                }
            }

            report.AppendLine("WARNING: Could not resolve warningClip from scene prototype or prototype prefab.");
            return null;
        }

        private static void ExtractAndBindTexturesAndMaterials(StringBuilder report)
        {
            EnsureAssetFolder(TexturesFolder);
            EnsureAssetFolder(MaterialsFolder);

            var modelImporter = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (modelImporter == null)
                throw new InvalidOperationException($"ModelImporter not found for '{FbxPath}'.");

            // Ensure Humanoid rig setup on PossessedBoss.fbx
            if (modelImporter.animationType != ModelImporterAnimationType.Human ||
                modelImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                modelImporter.animationType = ModelImporterAnimationType.Human;
                modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                modelImporter.SaveAndReimport();
                modelImporter = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
                if (modelImporter == null)
                    throw new InvalidOperationException($"ModelImporter reload failed for '{FbxPath}'.");
            }

            bool extractedViaImporter = modelImporter.ExtractTextures(TexturesFolder);
            EnsureTextureFileExtensions(TexturesFolder, report);
            RenameMixamoTextureFiles(TexturesFolder, report);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });

            // Fallback 1: extract embedded Texture2D sub-assets directly if ExtractTextures yielded none
            if (existingTexGuids.Length == 0)
            {
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is Texture2D subTex && AssetDatabase.IsSubAsset(subTex))
                    {
                        string mappedName = MapMixamoClipName(SanitizeFileName(subTex.name));
                        string destPath = $"{TexturesFolder}/{mappedName}.png";
                        try
                        {
                            string err = AssetDatabase.ExtractAsset(subTex, destPath);
                            if (!string.IsNullOrEmpty(err))
                            {
                                SaveTextureAsPngFallback(subTex, destPath);
                            }
                        }
                        catch
                        {
                            SaveTextureAsPngFallback(subTex, destPath);
                        }
                    }
                }

                RenameMixamoTextureFiles(TexturesFolder, report);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });
            }

            // Fallback 2: extract embedded PNG blocks directly from FBX binary with ".png" extensions
            if (existingTexGuids.Length == 0)
            {
                int binaryCount = ExtractEmbeddedPngsFromFbxBinary(FbxPath, TexturesFolder, report);
                if (binaryCount > 0)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });
                }
            }

            report.AppendLine($"{FbxPath} -> {TexturesFolder}: ExtractTextures={extractedViaImporter}, textureCount={existingTexGuids.Length}");

            // Mark *normal* / *bump* textures as NormalMap and reimport
            for (int i = 0; i < existingTexGuids.Length; i++)
            {
                string texPath = AssetDatabase.GUIDToAssetPath(existingTexGuids[i]);
                string lowerName = Path.GetFileNameWithoutExtension(texPath).ToLowerInvariant();
                if (lowerName.Contains("normal") || lowerName.Contains("bump"))
                {
                    var texImporter = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    if (texImporter != null && texImporter.textureType != TextureImporterType.NormalMap)
                    {
                        texImporter.textureType = TextureImporterType.NormalMap;
                        texImporter.SaveAndReimport();
                    }
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Reload textures after NormalMap reimport
            var extractedTextures = new List<(string path, string fileNameLower, Texture2D asset)>();
            existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });
            for (int i = 0; i < existingTexGuids.Length; i++)
            {
                string texPath = AssetDatabase.GUIDToAssetPath(existingTexGuids[i]);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex != null)
                {
                    string lowerName = Path.GetFileNameWithoutExtension(texPath).ToLowerInvariant();
                    extractedTextures.Add((texPath, lowerName, tex));
                    var ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
                    report.AppendLine($"  Tex: {texPath} (type={(ti != null ? ti.textureType.ToString() : "unknown")})");
                }
            }

            // Extract embedded materials (Body, Hair, material) into MaterialsFolder and remap on ModelImporter
            Shader standardShader = Shader.Find("Standard");
            if (standardShader == null)
                throw new InvalidOperationException("Built-in 'Standard' shader not found.");

            UnityEngine.Object[] fbxSubAssets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
            var embeddedMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fbxSubAssets.Length; i++)
            {
                if (fbxSubAssets[i] is Material subMat && AssetDatabase.IsSubAsset(subMat))
                {
                    embeddedMaterials[subMat.name] = subMat;
                }
            }

            var resolvedMaterials = new List<(string name, Material mat)>();
            for (int i = 0; i < BossMaterialNames.Length; i++)
            {
                string matName = BossMaterialNames[i];
                string matPath = $"{MaterialsFolder}/{matName}.mat";

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    if (embeddedMaterials.TryGetValue(matName, out Material embedded))
                    {
                        string extractErr = AssetDatabase.ExtractAsset(embedded, matPath);
                        if (!string.IsNullOrEmpty(extractErr))
                        {
                            report.AppendLine($"  ExtractAsset warning for '{matName}': {extractErr} (creating Standard material)");
                        }
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    }

                    if (mat == null)
                    {
                        mat = new Material(standardShader) { name = matName };
                        AssetDatabase.CreateAsset(mat, matPath);
                        AssetDatabase.SaveAssets();
                        mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    }
                }

                if (mat == null)
                    throw new InvalidOperationException($"Failed to extract or create material '{matPath}'.");

                if (mat.shader != standardShader)
                {
                    mat.shader = standardShader;
                }

                string stem = matName.ToLowerInvariant();
                Texture2D mainTex = FindMatchingTexture(extractedTextures, stem, "diffuse", "albedo", "basecolor", "base_color");
                Texture2D bumpMap = FindMatchingTexture(extractedTextures, stem, "normal", "bump");
                Texture2D metallicGloss = FindMatchingTexture(extractedTextures, stem, "specular", "metallic");
                Texture2D occlusionMap = FindMatchingTexture(extractedTextures, stem, "ao", "occlusion");

                if (mainTex != null)
                {
                    mat.SetTexture("_MainTex", mainTex);
                    mat.SetColor("_Color", Color.white);
                }

                if (bumpMap != null)
                {
                    mat.SetTexture("_BumpMap", bumpMap);
                    mat.SetFloat("_BumpScale", 1f);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (metallicGloss != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metallicGloss);
                    mat.EnableKeyword("_METALLICGLOSSMAP");
                }

                if (occlusionMap != null)
                {
                    mat.SetTexture("_OcclusionMap", occlusionMap);
                }

                EditorUtility.SetDirty(mat);
                resolvedMaterials.Add((matName, mat));

                report.AppendLine(
                    $"  Mat {matPath} (shader={mat.shader.name}): " +
                    $"_MainTex={(mainTex != null ? mainTex.name : "none")}, " +
                    $"_BumpMap={(bumpMap != null ? bumpMap.name : "none")}, " +
                    $"_MetallicGlossMap={(metallicGloss != null ? metallicGloss.name : "none")}, " +
                    $"_OcclusionMap={(occlusionMap != null ? occlusionMap.name : "none")}");
            }

            AssetDatabase.SaveAssets();

            // Fetch fresh ModelImporter after extraction/refresh and bind all material remaps cleanly
            modelImporter = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (modelImporter != null)
            {
                for (int i = 0; i < resolvedMaterials.Count; i++)
                {
                    modelImporter.AddRemap(
                        new AssetImporter.SourceAssetIdentifier(typeof(Material), resolvedMaterials[i].name),
                        resolvedMaterials[i].mat);
                }
                modelImporter.SaveAndReimport();
            }
        }

        private static string MapMixamoClipName(string rawName)
        {
            if (MixamoImageAliases.TryGetValue(rawName, out string aliased))
            {
                return aliased;
            }
            return rawName;
        }

        private static void RenameMixamoTextureFiles(string textureFolder, StringBuilder report)
        {
            if (!Directory.Exists(textureFolder)) return;

            string[] files = Directory.GetFiles(textureFolder);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                string ext = Path.GetExtension(file);
                if (string.IsNullOrEmpty(ext)) continue;

                string baseName = Path.GetFileNameWithoutExtension(file);
                if (!MixamoImageAliases.TryGetValue(baseName, out string mappedBase)) continue;

                string dir = Path.GetDirectoryName(file);
                string dest = Path.Combine(dir ?? textureFolder, mappedBase + ext);
                if (string.Equals(file, dest, StringComparison.OrdinalIgnoreCase)) continue;

                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);

                string oldMeta = file + ".meta";
                if (File.Exists(oldMeta)) File.Delete(oldMeta);

                report.AppendLine($"  Mapped Mixamo texture {Path.GetFileName(file)} -> {Path.GetFileName(dest)}");
            }
        }

        private static Texture2D FindMatchingTexture(
            List<(string path, string fileNameLower, Texture2D asset)> textures,
            string materialStem,
            params string[] slotKeywords)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                string name = textures[i].fileNameLower;
                if (!ContainsStemToken(name, materialStem)) continue;

                for (int k = 0; k < slotKeywords.Length; k++)
                {
                    if (name.Contains(slotKeywords[k]))
                    {
                        return textures[i].asset;
                    }
                }
            }
            return null;
        }

        private static bool ContainsStemToken(string fileNameLower, string stem)
        {
            if (fileNameLower.Contains($"_{stem}_") ||
                fileNameLower.StartsWith($"{stem}_", StringComparison.Ordinal) ||
                fileNameLower.EndsWith($"_{stem}", StringComparison.Ordinal))
            {
                return true;
            }
            return fileNameLower.Contains(stem);
        }

        private static void SaveTextureAsPngFallback(Texture2D source, string assetPath)
        {
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                readable.Apply();
                byte[] png = readable.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(readable);
                File.WriteAllBytes(assetPath, png);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void EnsureTextureFileExtensions(string textureFolder, StringBuilder report)
        {
            if (!Directory.Exists(textureFolder)) return;

            string[] files = Directory.GetFiles(textureFolder);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                string ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext)) continue;

                byte[] header = new byte[4];
                int read;
                using (var fs = File.OpenRead(file))
                {
                    read = fs.Read(header, 0, header.Length);
                }
                if (read < 4) continue;

                string detectedExt = null;
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                    detectedExt = ".png";
                else if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                    detectedExt = ".jpg";

                if (detectedExt == null) continue;

                string dest = file + detectedExt;
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);

                string oldMeta = file + ".meta";
                if (File.Exists(oldMeta)) File.Delete(oldMeta);

                report.AppendLine($"  Renamed extensionless extracted texture -> {dest}");
            }
        }

        private static int ExtractEmbeddedPngsFromFbxBinary(string fbxPath, string textureFolder, StringBuilder report)
        {
            byte[] data = File.ReadAllBytes(fbxPath);
            byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            byte[] videoMarker = { 0x00, 0x01, (byte)'V', (byte)'i', (byte)'d', (byte)'e', (byte)'o' };

            int extracted = 0;
            int searchPos = 0;

            while (searchPos <= data.Length - pngSig.Length)
            {
                int start = FindBytes(data, pngSig, searchPos, data.Length);
                if (start < 0) break;

                int cursor = start + 8;
                int pngEnd = -1;
                while (cursor + 12 <= data.Length)
                {
                    uint chunkLen = ((uint)data[cursor] << 24) |
                                    ((uint)data[cursor + 1] << 16) |
                                    ((uint)data[cursor + 2] << 8) |
                                    data[cursor + 3];
                    bool isIend = data[cursor + 4] == (byte)'I' &&
                                  data[cursor + 5] == (byte)'E' &&
                                  data[cursor + 6] == (byte)'N' &&
                                  data[cursor + 7] == (byte)'D';
                    long nextCursor = (long)cursor + 12L + chunkLen;
                    if (nextCursor > data.Length) break;

                    cursor = (int)nextCursor;
                    if (isIend)
                    {
                        pngEnd = cursor;
                        break;
                    }
                }

                if (pngEnd < 0)
                {
                    searchPos = start + 8;
                    continue;
                }

                int lookbackStart = Math.Max(0, start - 512);
                string clipName = null;
                int markerPos = FindLastBytes(data, videoMarker, lookbackStart, start);
                if (markerPos > lookbackStart)
                {
                    int nameStart = markerPos;
                    while (nameStart > lookbackStart)
                    {
                        byte b = data[nameStart - 1];
                        if (b >= 32 && b <= 126) nameStart--;
                        else break;
                    }

                    int nameLen = markerPos - nameStart;
                    if (nameLen >= 3)
                    {
                        clipName = Encoding.ASCII.GetString(data, nameStart, nameLen).Trim();
                    }
                }

                if (string.IsNullOrEmpty(clipName))
                {
                    clipName = $"{Path.GetFileNameWithoutExtension(fbxPath)}_tex_{extracted}";
                }

                string safeName = MapMixamoClipName(SanitizeFileName(clipName));
                string destPath = $"{textureFolder}/{safeName}.png";
                int byteCount = pngEnd - start;
                using (var fs = File.Create(destPath))
                {
                    fs.Write(data, start, byteCount);
                }

                extracted++;
                report.AppendLine($"  Extracted binary PNG: {destPath} ({byteCount:N0} bytes)");
                searchPos = pngEnd;
            }

            return extracted;
        }

        private static int FindBytes(byte[] haystack, byte[] needle, int start, int end)
        {
            int limit = end - needle.Length;
            for (int i = start; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static int FindLastBytes(byte[] haystack, byte[] needle, int start, int end)
        {
            int limit = end - needle.Length;
            for (int i = limit; i >= start; i--)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static string SanitizeFileName(string raw)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string cleaned = raw;
            for (int i = 0; i < invalid.Length; i++)
            {
                cleaned = cleaned.Replace(invalid[i], '_');
            }
            return cleaned;
        }

        private static RuntimeAnimatorController LoadSharedLocomotionController()
        {
            RuntimeAnimatorController controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PrimaryControllerPath);
            if (controller != null) return controller;

            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FallbackControllerPath);
            if (controller != null) return controller;

            throw new InvalidOperationException(
                $"SharedHumanoidLocomotion.controller not found at '{PrimaryControllerPath}' or '{FallbackControllerPath}'.");
        }

        private static Avatar LoadHumanoidAvatar(string fbxPath)
        {
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            Avatar fallback = null;
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is Avatar av)
                {
                    if (av.isHuman && av.isValid) return av;
                    fallback = av;
                }
            }

            if (fallback != null) return fallback;
            throw new InvalidOperationException($"No Humanoid Avatar sub-asset found in '{fbxPath}'.");
        }

        private static GameObject BuildPossessedBossPrefab(
            RuntimeAnimatorController controller,
            AudioClip warningClip,
            StringBuilder report)
        {
            EnsureAssetFolder(PrefabFolder);

            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbxAsset == null)
                throw new InvalidOperationException($"FBX asset not found at '{FbxPath}'.");

            Avatar avatar = LoadHumanoidAvatar(FbxPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate FBX '{FbxPath}'.");

            try
            {
                instance.name = "PossessedBoss";
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // Animator
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                // NavMeshAgent (starts disabled to match PossessedBoss initial Hidden state)
                NavMeshAgent navAgent = instance.GetComponent<NavMeshAgent>();
                if (navAgent == null) navAgent = instance.AddComponent<NavMeshAgent>();
                navAgent.radius = 0.32f;
                navAgent.height = 1.9f;
                navAgent.baseOffset = 0f;
                navAgent.speed = 6.8f;
                navAgent.angularSpeed = 540f;
                navAgent.acceleration = 18f;
                navAgent.stoppingDistance = 0.9f;
                navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                navAgent.enabled = false;

                // AgentMotor (Alesa.Runtime.Motion)
                AgentMotor motor = instance.GetComponent<AgentMotor>();
                if (motor == null) motor = instance.AddComponent<AgentMotor>();
                var motorSo = new SerializedObject(motor);
                SetBool(motorSo, "useNavMesh", true);
                SetBool(motorSo, "syncAttachedNavMeshAgent", true);
                SetObject(motorSo, "animator", animator);
                SetFloat(motorSo, "acceleration", 18f);
                SetFloat(motorSo, "deceleration", 20f);
                SetFloat(motorSo, "turnSpeed", 540f);
                SetFloat(motorSo, "stoppingDistance", 0.75f);
                motorSo.ApplyModifiedPropertiesWithoutUndo();

                // HeadLookAt (Alesa.Runtime.Motion)
                HeadLookAt headLook = instance.GetComponent<HeadLookAt>();
                if (headLook == null) headLook = instance.AddComponent<HeadLookAt>();
                var headSo = new SerializedObject(headLook);
                SetBool(headSo, "useAnimatorIK", true);
                headSo.ApplyModifiedPropertiesWithoutUndo();

                // AudioSource
                AudioSource audioSource = instance.GetComponent<AudioSource>();
                if (audioSource == null) audioSource = instance.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 4f;
                audioSource.maxDistance = 22f;
                audioSource.playOnAwake = false;
                audioSource.volume = 0.7f;
                audioSource.rolloffMode = AudioRolloffMode.Linear;

                // CapsuleCollider (starts disabled; toggled by PossessedBoss bodyColliders for RevolverCombat silver hits)
                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
                capsule.radius = 0.35f;
                capsule.height = 1.9f;
                capsule.center = new Vector3(0f, 0.95f, 0f);
                capsule.isTrigger = false;
                capsule.enabled = false;

                // Collect all child renderers & start them disabled to match Hidden state
                Renderer[] childRenderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < childRenderers.Length; i++)
                {
                    childRenderers[i].enabled = false;
                }

                // PossessedBoss
                PossessedBoss boss = instance.GetComponent<PossessedBoss>();
                if (boss == null) boss = instance.AddComponent<PossessedBoss>();
                boss.currentState = PossessedBoss.BossState.Hidden;
                boss.visuals = childRenderers;
                boss.bodyColliders = new Collider[] { capsule };
                boss.voice = audioSource;
                boss.warningClip = warningClip;
                boss.predictionTime = 2f;
                boss.movementSpeed = 6.8f;
                boss.appearanceWarning = 1.25f;
                boss.grabReach = 1.35f;
                boss.grabWindup = 0.85f;
                boss.grabRecovery = 1.5f;
                boss.sightMemory = 3f;
                boss.commitmentSeconds = 1.5f;

                // PossessedBossMotorBridge
                PossessedBossMotorBridge bridge = instance.GetComponent<PossessedBossMotorBridge>();
                if (bridge == null) bridge = instance.AddComponent<PossessedBossMotorBridge>();
                var bridgeSo = new SerializedObject(bridge);
                SetFloat(bridgeSo, "lookHeight", 1.55f);
                SetObject(bridgeSo, "animator", animator);
                SetBool(bridgeSo, "syncNavMeshAgent", true);
                bridgeSo.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, BossPrefabPath, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException($"Failed to save prefab at '{BossPrefabPath}'.");

                report.AppendLine(
                    $"  Prefab {BossPrefabPath}: Avatar={avatar.name} (isHuman={avatar.isHuman}, isValid={avatar.isValid}), " +
                    $"AnimatorController={controller.name}, Renderers={childRenderers.Length} (disabled), " +
                    $"NavMeshAgent.enabled={navAgent.enabled}, CapsuleCollider.enabled={capsule.enabled}, " +
                    $"warningClip={(warningClip != null ? warningClip.name : "null")}");
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void WireSampleScene(
            Scene scene,
            GameObject bossPrefab,
            AudioClip warningClip,
            StringBuilder report)
        {
            FinalBossEncounter encounter = UnityEngine.Object.FindFirstObjectByType<FinalBossEncounter>(FindObjectsInactive.Include);
            if (encounter == null)
                throw new InvalidOperationException("FinalBossEncounter not found in SampleScene.");

            GameObject playerGo = FindPlayerGameObject();
            if (playerGo == null)
                throw new InvalidOperationException("Player GameObject not found in SampleScene.");

            // Locate existing capsule prototype or prior PossessedBoss child under Final Boss Encounter
            Transform encounterRoot = encounter.transform;
            Vector3 bossPos = DefaultBossPosition;
            Quaternion bossRot = Quaternion.identity;
            Vector3 bossScale = Vector3.one;

            var toRemove = new List<GameObject>();
            Transform protoTransform = encounterRoot.Find("Possessed Boss - Capsule Prototype");
            if (protoTransform != null)
            {
                bossPos = protoTransform.position;
                bossRot = protoTransform.rotation;
                bossScale = protoTransform.localScale;
                PossessedBoss protoBoss = protoTransform.GetComponent<PossessedBoss>();
                if (protoBoss != null && protoBoss.warningClip != null && warningClip == null)
                {
                    warningClip = protoBoss.warningClip;
                }
                toRemove.Add(protoTransform.gameObject);
            }

            // Also remove any previously placed PossessedBoss instance under Final Boss Encounter for clean idempotency
            for (int i = 0; i < encounterRoot.childCount; i++)
            {
                Transform child = encounterRoot.GetChild(i);
                if (child.GetComponent<PossessedBoss>() != null && !toRemove.Contains(child.gameObject))
                {
                    if (protoTransform == null)
                    {
                        bossPos = child.position;
                        bossRot = child.rotation;
                        bossScale = child.localScale;
                    }
                    toRemove.Add(child.gameObject);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                report.AppendLine($"  Removing old boss object: '{toRemove[i].name}' at {toRemove[i].transform.position:F2}");
                UnityEngine.Object.DestroyImmediate(toRemove[i]);
            }

            // Instantiate PossessedBoss.prefab under Final Boss Encounter at exact transform
            GameObject bossGo = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab, scene);
            if (bossGo == null)
                throw new InvalidOperationException($"Failed to instantiate '{BossPrefabPath}' into SampleScene.");

            bossGo.name = "PossessedBoss";
            bossGo.transform.SetParent(encounterRoot, false);
            bossGo.transform.position = bossPos;
            bossGo.transform.rotation = bossRot;
            bossGo.transform.localScale = bossScale;

            // Collect all 15 Boss point 00..14 transforms under "Appearance and intercept points"
            Transform pointsRoot = encounterRoot.Find("Appearance and intercept points");
            if (pointsRoot == null)
                throw new InvalidOperationException("Child 'Appearance and intercept points' not found under 'Final Boss Encounter'.");

            List<Transform> interceptPoints = new List<Transform>();
            for (int i = 0; i < pointsRoot.childCount; i++)
            {
                Transform child = pointsRoot.GetChild(i);
                if (child.name.StartsWith("Boss point ", StringComparison.OrdinalIgnoreCase))
                {
                    interceptPoints.Add(child);
                }
            }

            interceptPoints.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            if (interceptPoints.Count != 15)
                throw new InvalidOperationException($"Expected 15 'Boss point XX' transforms, found {interceptPoints.Count}.");

            PossessedBoss newBoss = bossGo.GetComponent<PossessedBoss>();
            if (newBoss == null)
                throw new InvalidOperationException("Instantiated PossessedBoss prefab is missing PossessedBoss component.");

            newBoss.player = playerGo.transform;
            newBoss.interceptPoints = interceptPoints.ToArray();
            if (warningClip != null)
            {
                newBoss.warningClip = warningClip;
            }
            EditorUtility.SetDirty(newBoss);

            encounter.boss = newBoss;
            EditorUtility.SetDirty(encounter);
            EditorUtility.SetDirty(bossGo);

            report.AppendLine(
                $"  Wired PossessedBoss at {bossGo.transform.position:F2} under '{encounterRoot.name}': " +
                $"player={newBoss.player.name}, interceptPoints={newBoss.interceptPoints.Length} " +
                $"({interceptPoints[0].name}..{interceptPoints[interceptPoints.Count - 1].name}), " +
                $"warningClip={(newBoss.warningClip != null ? newBoss.warningClip.name : "null")}, " +
                $"FinalBossEncounter.boss={encounter.boss.name}");

            // Bridge the overlapping NavMesh seam at Final Maze Entry
            Transform linkTransform = encounterRoot.Find("Final Maze Entry NavMesh Link");
            GameObject linkGo;
            if (linkTransform != null)
            {
                linkGo = linkTransform.gameObject;
            }
            else
            {
                linkGo = new GameObject("Final Maze Entry NavMesh Link");
                linkGo.transform.SetParent(encounterRoot, false);
            }

            NavMeshSeamLink seamLink = linkGo.GetComponent<NavMeshSeamLink>();
            if (seamLink == null) seamLink = linkGo.AddComponent<NavMeshSeamLink>();
            seamLink.ConfigureWorldEndpoints(SeamLinkStartWorld, SeamLinkEndWorld, SeamLinkWidth, isBidirectional: true);
            EditorUtility.SetDirty(seamLink);
            EditorUtility.SetDirty(linkGo);

            report.AppendLine(
                $"  NavMeshSeamLink '{linkGo.name}': Start={seamLink.WorldStart:F2}, End={seamLink.WorldEnd:F2}, " +
                $"Width={seamLink.Width:F2}, Bidirectional={seamLink.Bidirectional}, Active={seamLink.IsActive}");

            // Verify NavMesh traversal across Hospital <-> Final Wing seam
            VerifyNavMeshSeamTraversal(seamLink, report);

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
                throw new InvalidOperationException($"Failed to save scene '{SampleScenePath}'.");
        }

        private static void VerifyNavMeshSeamTraversal(NavMeshSeamLink seamLink, StringBuilder report)
        {
            var tempInstances = new List<NavMeshDataInstance>();
            try
            {
                NavMeshData hospitalNav = AssetDatabase.LoadAssetAtPath<NavMeshData>(HospitalNavAssetPath);
                NavMeshData finalWingNav = AssetDatabase.LoadAssetAtPath<NavMeshData>(FinalWingNavAssetPath);

                HospitalNavMeshLoader hospitalLoader = UnityEngine.Object.FindFirstObjectByType<HospitalNavMeshLoader>(FindObjectsInactive.Include);
                if (hospitalNav != null && (hospitalLoader == null || !hospitalLoader.IsLoaded))
                {
                    tempInstances.Add(NavMesh.AddNavMeshData(hospitalNav));
                }

                FinalBossNavigation finalNavLoader = UnityEngine.Object.FindFirstObjectByType<FinalBossNavigation>(FindObjectsInactive.Include);
                if (finalWingNav != null && (finalNavLoader == null || !finalNavLoader.isActiveAndEnabled))
                {
                    tempInstances.Add(NavMesh.AddNavMeshData(finalWingNav));
                }

                seamLink.RefreshLink();

                var pathForward = new NavMeshPath();
                var pathBackward = new NavMeshPath();
                var pathAlesaToBoss = new NavMeshPath();

                Vector3 hospitalSide = new Vector3(188.5f, 24.65f, 50.0f);
                Vector3 finalWingSide = new Vector3(188.5f, 24.65f, 65.0f);
                Vector3 alesaPos = new Vector3(90.5f, 24.62f, 1.0f);

                bool startHitOk = NavMesh.SamplePosition(SeamLinkStartWorld, out NavMeshHit startHit, 1.5f, NavMesh.AllAreas);
                bool endHitOk = NavMesh.SamplePosition(SeamLinkEndWorld, out NavMeshHit endHit, 1.5f, NavMesh.AllAreas);
                bool hospSample = NavMesh.SamplePosition(hospitalSide, out NavMeshHit hospHit, 3f, NavMesh.AllAreas);
                bool wingSample = NavMesh.SamplePosition(finalWingSide, out NavMeshHit wingHit, 3f, NavMesh.AllAreas);
                bool alesaSample = NavMesh.SamplePosition(alesaPos, out NavMeshHit alesaHit, 3f, NavMesh.AllAreas);
                bool bossSample = NavMesh.SamplePosition(DefaultBossPosition, out NavMeshHit bossHit, 3f, NavMesh.AllAreas);

                bool fwdOk = hospSample && wingSample && NavMesh.CalculatePath(hospHit.position, wingHit.position, NavMesh.AllAreas, pathForward);
                bool bwdOk = hospSample && wingSample && NavMesh.CalculatePath(wingHit.position, hospHit.position, NavMesh.AllAreas, pathBackward);
                bool fullOk = alesaSample && bossSample && NavMesh.CalculatePath(alesaHit.position, bossHit.position, NavMesh.AllAreas, pathAlesaToBoss);

                report.AppendLine(
                    $"  Seam Verification:\n" +
                    $"    LinkStartSample={startHitOk} ({(startHitOk ? startHit.position.ToString("F2") : "none")})\n" +
                    $"    LinkEndSample={endHitOk} ({(endHitOk ? endHit.position.ToString("F2") : "none")})\n" +
                    $"    Hospital(Z=50)->FinalWing(Z=65): ok={fwdOk}, status={pathForward.status}, corners={pathForward.corners.Length}\n" +
                    $"    FinalWing(Z=65)->Hospital(Z=50): ok={bwdOk}, status={pathBackward.status}, corners={pathBackward.corners.Length}\n" +
                    $"    Alesa(90.5,1)->Boss(182,84): ok={fullOk}, status={pathAlesaToBoss.status}, corners={pathAlesaToBoss.corners.Length}");
            }
            finally
            {
                for (int i = 0; i < tempInstances.Count; i++)
                {
                    if (tempInstances[i].valid)
                    {
                        tempInstances[i].Remove();
                    }
                }
            }
        }

        private static GameObject FindPlayerGameObject()
        {
            PlayerGameplay pg = UnityEngine.Object.FindFirstObjectByType<PlayerGameplay>(FindObjectsInactive.Include);
            if (pg != null) return pg.gameObject;

            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null) return tagged;

            return GameObject.Find("Player");
        }

        private static void SetObject(SerializedObject so, string propName, UnityEngine.Object value)
        {
            SerializedProperty prop = so.FindProperty(propName);
            if (prop == null) throw new InvalidOperationException($"Serialized property '{propName}' not found on '{so.targetObject.GetType().FullName}'.");
            prop.objectReferenceValue = value;
        }

        private static void SetBool(SerializedObject so, string propName, bool value)
        {
            SerializedProperty prop = so.FindProperty(propName);
            if (prop == null) throw new InvalidOperationException($"Serialized property '{propName}' not found on '{so.targetObject.GetType().FullName}'.");
            prop.boolValue = value;
        }

        private static void SetFloat(SerializedObject so, string propName, float value)
        {
            SerializedProperty prop = so.FindProperty(propName);
            if (prop == null) throw new InvalidOperationException($"Serialized property '{propName}' not found on '{so.targetObject.GetType().FullName}'.");
            prop.floatValue = value;
        }

        private static void EnsureAssetFolder(string assetFolderPath)
        {
            string[] parts = assetFolderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void WriteStatusLog(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(StatusLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(StatusLogPath, text);
            }
            catch
            {
                // Non-fatal if Temp cannot be written.
            }
        }
    }
}
