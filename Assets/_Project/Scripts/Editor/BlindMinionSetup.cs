using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Alesa.Runtime.Motion;
using Alesa.Runtime.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using ISBlindMinionAgent = Alesa.Agents.BlindMinion.BlindMinionAgent;

namespace Alesa.Editor
{
    /// <summary>
    /// Extracts and binds embedded Zombie FBX textures onto Built-in Standard materials,
    /// builds BlindMinion_VariantA.prefab and BlindMinion_VariantB.prefab wired to
    /// SharedHumanoidLocomotion.controller + AgentMotor + HeadLookAt + IS BlindMinionAgent,
    /// and integrates AIBootstrap, AcousticBusBridge, and both minion instances into SampleScene.
    /// </summary>
    [InitializeOnLoad]
    public static class BlindMinionSetup
    {
        private static bool _scheduledAutoRun;

        static BlindMinionSetup()
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
            if (!File.Exists(Fbx01Path) || !File.Exists(Fbx02Path) ||
                !File.Exists(SampleScenePath) || !File.Exists(NavAssetPath))
                return false;

            return !File.Exists(VariantAPrefabPath) || !File.Exists(VariantBPrefabPath);
        }

        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string NavAssetPath = "Assets/_Project/Navigation/SampleSceneHospitalNavMesh.asset";
        private const string PrimaryControllerPath = "Assets/_Project/Animations/Humanoid/SharedHumanoidLocomotion.controller";
        private const string FallbackControllerPath = "Assets/_Project/Animations/SharedHumanoidLocomotion.controller";

        private const string ZombieFolder = "Assets/_Project/Models/Zombie";
        private const string Fbx01Path = ZombieFolder + "/BlindMinion_01.fbx";
        private const string Fbx02Path = ZombieFolder + "/BlindMinion_02.fbx";
        private const string Textures01Folder = ZombieFolder + "/Textures_01";
        private const string Textures02Folder = ZombieFolder + "/Textures_02";
        private const string Materials01Folder = ZombieFolder + "/Materials_01";
        private const string Materials02Folder = ZombieFolder + "/Materials_02";

        private const string PrefabFolder = "Assets/_Project/Prefabs/Characters";
        private const string VariantAPrefabPath = PrefabFolder + "/BlindMinion_VariantA.prefab";
        private const string VariantBPrefabPath = PrefabFolder + "/BlindMinion_VariantB.prefab";

        private const string StatusLogPath = "Temp/BlindMinionSetupStatus.txt";

        private static readonly string[] Materials01Names = { "Body", "Bottoms", "Hair", "Tops" };
        private static readonly string[] Materials02Names = { "Body", "Bottom", "Hair", "Shoes" };

        private static readonly Vector3[] CandidatePositionsA =
        {
            new Vector3(123.4f, 24.62f, 21.5f),
            new Vector3(115.0f, 24.62f, 12.0f),
            new Vector3(118.0f, 24.62f, 6.0f),
            new Vector3(112.0f, 24.62f, 20.0f),
            new Vector3(126.0f, 24.62f, 10.0f),
            new Vector3(108.0f, 24.62f, 2.0f),
            new Vector3(120.0f, 24.62f, 0.0f),
            new Vector3(132.0f, 24.62f, 21.5f),
        };

        private static readonly Vector3[] CandidatePositionsB =
        {
            new Vector3(123.4f, 24.62f, 33.0f),
            new Vector3(145.0f, 24.62f, 21.5f),
            new Vector3(150.0f, 24.62f, 28.0f),
            new Vector3(140.0f, 24.62f, 33.0f),
            new Vector3(135.0f, 24.62f, 28.0f),
            new Vector3(152.0f, 24.62f, 18.0f),
            new Vector3(142.0f, 24.62f, 10.0f),
            new Vector3(155.0f, 24.62f, 35.0f),
        };

        [MenuItem("ALESA/Setup Blind Minions (Textures + Prefabs + SampleScene)", false, 23)]
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

                // 1. Extract & bind embedded textures for BlindMinion_01 and BlindMinion_02
                report.AppendLine("=== 1. TEXTURE EXTRACTION & MATERIAL BINDING ===");
                ExtractAndBindTextures(Fbx01Path, Textures01Folder, Materials01Folder, Materials01Names, report);
                ExtractAndBindTextures(Fbx02Path, Textures02Folder, Materials02Folder, Materials02Names, report);

                // 2. Build BlindMinion_VariantA.prefab and BlindMinion_VariantB.prefab
                report.AppendLine("=== 2. PREFAB CREATION ===");
                RuntimeAnimatorController sharedController = LoadSharedLocomotionController();
                report.AppendLine($"AnimatorController: {AssetDatabase.GetAssetPath(sharedController)}");

                GameObject variantA = BuildBlindMinionPrefab(Fbx01Path, VariantAPrefabPath, "BlindMinion_VariantA", sharedController, report);
                GameObject variantB = BuildBlindMinionPrefab(Fbx02Path, VariantBPrefabPath, "BlindMinion_VariantB", sharedController, report);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 3. Wire SampleScene.unity with AIBootstrap, AcousticBusBridge, and both Blind Minions on NavMesh
                report.AppendLine("=== 3. SAMPLESCENE WIRING & NAVMESH PLACEMENT ===");
                WireSampleScene(variantA, variantB, report);

                AssetDatabase.SaveAssets();

                string summary = "SUCCESS\n" + report;
                WriteStatusLog(summary);
                Debug.Log("[BlindMinionSetup] " + summary);

                if (interactive)
                {
                    EditorUtility.DisplayDialog("Blind Minion Setup Complete", "Extracted textures, created both prefabs, and wired SampleScene cleanly.", "OK");
                }
            }
            catch (Exception ex)
            {
                string failure = $"FAILED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n{report}\nEXCEPTION:\n{ex}\n";
                WriteStatusLog(failure);
                Debug.LogError("[BlindMinionSetup] Setup failed:\n" + failure);
                if (Application.isBatchMode) throw;
            }
        }

        private static void ExtractAndBindTextures(
            string fbxPath,
            string textureFolder,
            string materialFolder,
            string[] materialNames,
            StringBuilder report)
        {
            EnsureAssetFolder(textureFolder);
            EnsureAssetFolder(materialFolder);

            var modelImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (modelImporter == null)
                throw new InvalidOperationException($"ModelImporter not found for '{fbxPath}'.");

            bool extractedViaImporter = modelImporter.ExtractTextures(textureFolder);
            EnsureTextureFileExtensions(textureFolder, report);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            string[] existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });

            // Fallback 1: extract embedded Texture2D sub-assets directly if ExtractTextures yielded none
            if (existingTexGuids.Length == 0)
            {
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
                for (int i = 0; i < subAssets.Length; i++)
                {
                    if (subAssets[i] is Texture2D subTex && AssetDatabase.IsSubAsset(subTex))
                    {
                        string safeName = SanitizeFileName(subTex.name);
                        string destPath = $"{textureFolder}/{safeName}.png";
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

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });
            }

            // Fallback 2: Mixamo FBXs often store embedded PNG Video clips without a ".png" extension,
            // causing Unity's ModelImporter.ExtractTextures to import them as DefaultImporter and drop them.
            // Extract valid embedded PNG blocks directly from the FBX binary with ".png" extensions.
            if (existingTexGuids.Length == 0)
            {
                int binaryCount = ExtractEmbeddedPngsFromFbxBinary(fbxPath, textureFolder, report);
                if (binaryCount > 0)
                {
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });
                }
            }

            report.AppendLine($"{fbxPath} -> {textureFolder}: ExtractTextures={extractedViaImporter}, textureCount={existingTexGuids.Length}");

            // Configure NormalMap importers first and reimport
            var extractedTextures = new List<(string path, string fileNameLower, Texture2D asset)>();
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
            existingTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });
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

            // Bind textures to each Built-in Standard material
            Shader standardShader = Shader.Find("Standard");
            for (int i = 0; i < materialNames.Length; i++)
            {
                string matName = materialNames[i];
                string matPath = $"{materialFolder}/{matName}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    throw new InvalidOperationException($"Material not found at '{matPath}'.");

                if (standardShader != null && mat.shader != standardShader)
                {
                    mat.shader = standardShader;
                }

                string stem = GetMaterialMatchStem(matName);
                Texture2D mainTex = FindMatchingTexture(extractedTextures, stem, "diffuse", "albedo", "basecolor", "base_color");
                Texture2D bumpMap = FindMatchingTexture(extractedTextures, stem, "normal", "bump");
                Texture2D metallicGloss = FindMatchingTexture(extractedTextures, stem, "specular", "metallic");
                Texture2D occlusionMap = FindMatchingTexture(extractedTextures, stem, "ao", "occlusion");

                if (mainTex != null)
                {
                    mat.SetTexture("_MainTex", mainTex);
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
                report.AppendLine(
                    $"  Mat {matPath} (shader={mat.shader.name}): " +
                    $"_MainTex={(mainTex != null ? mainTex.name : "none")}, " +
                    $"_BumpMap={(bumpMap != null ? bumpMap.name : "none")}, " +
                    $"_MetallicGlossMap={(metallicGloss != null ? metallicGloss.name : "none")}, " +
                    $"_OcclusionMap={(occlusionMap != null ? occlusionMap.name : "none")}");
            }

            // Reimport FBX once so external material remaps reflect any updated texture references cleanly
            modelImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (modelImporter != null)
            {
                modelImporter.SaveAndReimport();
            }
        }

        private static string GetMaterialMatchStem(string materialName)
        {
            string lower = materialName.ToLowerInvariant();
            if (lower == "bottoms") return "bottom";
            if (lower == "tops") return "top";
            if (lower == "shoes") return "shoe";
            return lower;
        }

        private static Texture2D FindMatchingTexture(
            List<(string path, string fileNameLower, Texture2D asset)> textures,
            string materialStem,
            params string[] slotKeywords)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                string name = textures[i].fileNameLower;
                if (!name.Contains(materialStem)) continue;

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

                // Walk PNG chunks cleanly (length + 4-byte type + data + 4-byte CRC) until IEND
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

                // Recover the FBX Video clip name immediately preceding this PNG block
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

                string safeName = SanitizeFileName(clipName);
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

        private static GameObject BuildBlindMinionPrefab(
            string fbxPath,
            string prefabPath,
            string prefabName,
            RuntimeAnimatorController controller,
            StringBuilder report)
        {
            EnsureAssetFolder(PrefabFolder);

            GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxAsset == null)
                throw new InvalidOperationException($"FBX asset not found at '{fbxPath}'.");

            Avatar avatar = LoadHumanoidAvatar(fbxPath);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate FBX '{fbxPath}'.");

            try
            {
                instance.name = prefabName;

                // Animator
                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                // NavMeshAgent
                NavMeshAgent navAgent = instance.GetComponent<NavMeshAgent>();
                if (navAgent == null) navAgent = instance.AddComponent<NavMeshAgent>();
                navAgent.radius = 0.35f;
                navAgent.height = 1.75f;
                navAgent.baseOffset = 0f;
                navAgent.speed = 5.5f;
                navAgent.angularSpeed = 300f;
                navAgent.acceleration = 8f;
                navAgent.stoppingDistance = 0.3f;
                navAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

                // AgentMotor (Alesa.Runtime.Motion)
                AgentMotor motor = instance.GetComponent<AgentMotor>();
                if (motor == null) motor = instance.AddComponent<AgentMotor>();
                var motorSo = new SerializedObject(motor);
                SetBool(motorSo, "useNavMesh", true);
                SetBool(motorSo, "syncAttachedNavMeshAgent", true);
                SetObject(motorSo, "animator", animator);
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
                audioSource.minDistance = 2f;
                audioSource.maxDistance = 25f;
                audioSource.playOnAwake = false;

                // IS BlindMinionAgent (Alesa.Agents.BlindMinion.BlindMinionAgent)
                ISBlindMinionAgent minionAgent = instance.GetComponent<ISBlindMinionAgent>();
                if (minionAgent == null) minionAgent = instance.AddComponent<ISBlindMinionAgent>();
                var agentSo = new SerializedObject(minionAgent);
                SetObject(agentSo, "voice", audioSource);
                SetFloat(agentSo, "lookHeight", 1.4f);
                SetFloat(agentSo, "investigateSpeed", 1.8f);
                SetFloat(agentSo, "huntSpeed", 5.5f);
                SetFloat(agentSo, "attackRange", 1.8f);
                agentSo.ApplyModifiedPropertiesWithoutUndo();

                // CapsuleCollider
                CapsuleCollider capsule = instance.GetComponent<CapsuleCollider>();
                if (capsule == null) capsule = instance.AddComponent<CapsuleCollider>();
                capsule.radius = 0.35f;
                capsule.height = 1.75f;
                capsule.center = new Vector3(0f, 0.875f, 0f);
                capsule.isTrigger = false;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out bool success);
                if (!success || saved == null)
                    throw new InvalidOperationException($"Failed to save prefab at '{prefabPath}'.");

                report.AppendLine(
                    $"  Prefab {prefabPath}: Avatar={avatar.name} (isHuman={avatar.isHuman}), " +
                    $"AnimatorController={controller.name}, BlindMinionAgentType={minionAgent.GetType().FullName}");
                return saved;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void WireSampleScene(GameObject variantAPrefab, GameObject variantBPrefab, StringBuilder report)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!string.Equals(scene.path, SampleScenePath, StringComparison.OrdinalIgnoreCase))
            {
                scene = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }

            // 1. Find Player
            GameObject playerGo = FindPlayerGameObject();
            if (playerGo == null)
                throw new InvalidOperationException("Player GameObject not found in SampleScene.");

            // 2. Ensure AI Systems root holds AIBootstrap + AcousticBusBridge
            GameObject aiSystemsGo = GameObject.Find("AI Systems");
            if (aiSystemsGo == null)
            {
                aiSystemsGo = new GameObject("AI Systems");
                SceneManager.MoveGameObjectToScene(aiSystemsGo, scene);
            }

            AIBootstrap bootstrap = aiSystemsGo.GetComponent<AIBootstrap>();
            if (bootstrap == null)
            {
                bootstrap = UnityEngine.Object.FindFirstObjectByType<AIBootstrap>(FindObjectsInactive.Include);
                if (bootstrap == null) bootstrap = aiSystemsGo.AddComponent<AIBootstrap>();
            }

            var bootstrapSo = new SerializedObject(bootstrap);
            SetObject(bootstrapSo, "playerTransform", playerGo.transform);
            SetBool(bootstrapSo, "buildFallbackGridIfEmpty", false);
            bootstrapSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);

            AcousticBusBridge acousticBridge = aiSystemsGo.GetComponent<AcousticBusBridge>();
            if (acousticBridge == null)
            {
                acousticBridge = UnityEngine.Object.FindFirstObjectByType<AcousticBusBridge>(FindObjectsInactive.Include);
                if (acousticBridge == null) acousticBridge = aiSystemsGo.AddComponent<AcousticBusBridge>();
            }

            var bridgeSo = new SerializedObject(acousticBridge);
            SetObject(bridgeSo, "aiBootstrap", bootstrap);
            SetFloat(bridgeSo, "loudnessScale", 1.0f);
            bridgeSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(acousticBridge);

            report.AppendLine(
                $"  AI Systems: Go={bootstrap.gameObject.name}, Player={playerGo.name} at {playerGo.transform.position:F2}, " +
                $"buildFallbackGridIfEmpty=false, AcousticBusBridge on {acousticBridge.gameObject.name} (loudnessScale=1.0)");

            // 3. Ensure SampleSceneHospitalNavMesh is active while sampling placement positions
            NavMeshData hospitalNavData = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAssetPath);
            if (hospitalNavData == null)
                throw new InvalidOperationException($"Hospital NavMesh asset not found at '{NavAssetPath}'.");

            NavMeshDataInstance tempNavInstance = default;
            HospitalNavMeshLoader loader = UnityEngine.Object.FindFirstObjectByType<HospitalNavMeshLoader>(FindObjectsInactive.Include);
            if (loader == null || !loader.IsLoaded)
            {
                tempNavInstance = NavMesh.AddNavMeshData(hospitalNavData);
            }

            try
            {
                Physics.SyncTransforms();

                Vector3 playerNavPos = playerGo.transform.position;
                bool playerSampled = NavMesh.SamplePosition(playerGo.transform.position, out NavMeshHit playerHit, 3f, NavMesh.AllAreas);
                if (playerSampled) playerNavPos = playerHit.position;

                Vector3 posA = FindValidMinionPosition(CandidatePositionsA, playerNavPos, null, out string diagA);
                Vector3 posB = FindValidMinionPosition(CandidatePositionsB, playerNavPos, posA, out string diagB);

                // Face roughly toward the central hospital corridor / player direction
                Quaternion rotA = ComputeFlatLookRotation(posA, playerNavPos);
                Quaternion rotB = ComputeFlatLookRotation(posB, posA);

                GameObject minionA = PlaceOrUpdateMinionInstance(scene, "BlindMinion_VariantA", variantAPrefab, posA, rotA, bootstrap);
                GameObject minionB = PlaceOrUpdateMinionInstance(scene, "BlindMinion_VariantB", variantBPrefab, posB, rotB, bootstrap);

                report.AppendLine($"  Placed {minionA.name} at {minionA.transform.position:F3}, rot={minionA.transform.eulerAngles:F1} | {diagA}");
                report.AppendLine($"  Placed {minionB.name} at {minionB.transform.position:F3}, rot={minionB.transform.eulerAngles:F1} | {diagB}");
            }
            finally
            {
                if (tempNavInstance.valid)
                {
                    tempNavInstance.Remove();
                }
            }

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
                throw new InvalidOperationException($"Failed to save scene '{SampleScenePath}'.");
        }

        private static Vector3 FindValidMinionPosition(
            Vector3[] candidates,
            Vector3 playerNavPos,
            Vector3? otherMinionPos,
            out string diagnostics)
        {
            var navPath = new NavMeshPath();

            // Pass 1: curated hospital corridor/room candidates
            for (int i = 0; i < candidates.Length; i++)
            {
                if (TryValidatePosition(candidates[i], playerNavPos, otherMinionPos, navPath, out Vector3 snapped, out diagnostics))
                {
                    return snapped;
                }
            }

            // Pass 2: systematic sweep across X in [105..155], Z in [-10..40]
            for (float z = 36f; z >= -8f; z -= 4f)
            {
                for (float x = 106f; x <= 154f; x += 4f)
                {
                    var probe = new Vector3(x, 24.62f, z);
                    if (TryValidatePosition(probe, playerNavPos, otherMinionPos, navPath, out Vector3 snapped, out diagnostics))
                    {
                        return snapped;
                    }
                }
            }

            throw new InvalidOperationException("Could not find a valid, unobstructed NavMesh placement position in X=[105..155], Z=[-10..40].");
        }

        private static bool TryValidatePosition(
            Vector3 probe,
            Vector3 playerNavPos,
            Vector3? otherMinionPos,
            NavMeshPath navPath,
            out Vector3 snappedPos,
            out string diagnostics)
        {
            snappedPos = default;
            diagnostics = string.Empty;

            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                return false;

            Vector3 p = hit.position;

            // Keep within requested hospital corridor/room band and well outside starting rescue cell (82..93, -5..5)
            if (p.x < 105f || p.x > 155f || p.z < -10f || p.z > 40f)
                return false;

            if (p.y < 23.5f || p.y > 26.0f)
                return false;

            if (otherMinionPos.HasValue && Vector3.Distance(p, otherMinionPos.Value) < 12f)
                return false;

            // Ensure clearance from NavMesh edges (walls / furniture)
            float edgeDist = float.PositiveInfinity;
            if (NavMesh.FindClosestEdge(p, out NavMeshHit edgeHit, NavMesh.AllAreas))
            {
                edgeDist = edgeHit.distance;
                if (edgeDist < 0.45f) return false;
            }

            // Ensure capsule volume above floor is unobstructed by walls or props
            Vector3 capBottom = p + Vector3.up * 0.55f;
            Vector3 capTop = p + Vector3.up * 1.55f;
            if (Physics.CheckCapsule(capBottom, capTop, 0.32f, ~0, QueryTriggerInteraction.Ignore))
                return false;

            // Verify reachable on NavMesh from the player's NavMesh position
            bool pathOk = NavMesh.CalculatePath(playerNavPos, p, NavMesh.AllAreas, navPath) &&
                          navPath.status == NavMeshPathStatus.PathComplete &&
                          navPath.corners.Length >= 2;
            if (!pathOk) return false;

            snappedPos = p;
            diagnostics = $"NavHit=true, EdgeClearance={edgeDist:F2}m, PathFromPlayer={navPath.status} ({navPath.corners.Length} corners)";
            return true;
        }

        private static Quaternion ComputeFlatLookRotation(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return Quaternion.identity;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        private static GameObject PlaceOrUpdateMinionInstance(
            Scene scene,
            string instanceName,
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            AIBootstrap bootstrap)
        {
            GameObject existing = GameObject.Find(instanceName);
            GameObject go;

            if (existing != null)
            {
                go = existing;
            }
            else
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (go == null)
                    throw new InvalidOperationException($"Failed to instantiate prefab '{prefab.name}' into SampleScene.");
                go.name = instanceName;
            }

            go.transform.SetPositionAndRotation(position, rotation);

            ISBlindMinionAgent agent = go.GetComponent<ISBlindMinionAgent>();
            if (agent == null)
                throw new InvalidOperationException($"Missing IS BlindMinionAgent on '{instanceName}'.");

            var agentSo = new SerializedObject(agent);
            SetObject(agentSo, "ai", bootstrap);
            agentSo.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(agent);
            EditorUtility.SetDirty(go);
            return go;
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
