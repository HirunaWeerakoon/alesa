using System;
using System.Collections.Generic;
using System.IO;
using Alesa.Runtime;
using Alesa.Runtime.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Alesa.Editor
{
    /// <summary>
    /// Bakes a hospital NavMeshData asset for SampleScene (covering reception, rescue area, and
    /// corridors up to the Final Maze entrance), attaches HospitalNavMeshLoader, tags Player as
    /// "Player", and instantiates & wires Alesa_Companion.prefab at (90.5, 24.62, 1.0).
    /// </summary>
    [InitializeOnLoad]
    public static class SampleSceneAlesaSetup
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string NavFolder = "Assets/_Project/Navigation";
        private const string NavAssetPath = NavFolder + "/SampleSceneHospitalNavMesh.asset";
        private const string AlesaPrefabPath = "Assets/_Project/Prefabs/Characters/Alesa_Companion.prefab";
        private const string StatusLogPath = "Temp/SampleSceneAlesaSetupStatus.txt";

        private static readonly Vector3 BakeBoundsCenter = new Vector3(127.5f, 26.0f, 17.0f);
        private static readonly Vector3 BakeBoundsSize = new Vector3(135.0f, 14.0f, 84.0f);

        private static readonly Vector3 AlesaSpawnPosition = new Vector3(90.5f, 24.62f, 1.0f);
        private static readonly Vector3 AlesaSpawnEuler = new Vector3(0f, -90f, 0f);

        private static bool _scheduledAutoRun;

        static SampleSceneAlesaSetup()
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

            if (!NeedsSetup()) return;
            RunFullSetupInternal(interactive: false);
        }

        private static bool NeedsSetup()
        {
            return File.Exists(SampleScenePath) &&
                   File.Exists(AlesaPrefabPath) &&
                   !File.Exists(NavAssetPath);
        }

        [MenuItem("ALESA/Bake SampleScene Hospital NavMesh", false, 21)]
        public static void BakeHospitalNavMeshFromMenu()
        {
            RunBakeOnlyInternal(interactive: true);
        }

        [MenuItem("ALESA/Setup SampleScene (NavMesh + Alesa_Companion)", false, 22)]
        public static void RunFullSetupFromMenu()
        {
            RunFullSetupInternal(interactive: true);
        }

        public static void RunBatchSetup()
        {
            RunFullSetupInternal(interactive: false);
        }

        private static void RunBakeOnlyInternal(bool interactive)
        {
            try
            {
                Scene scene = EnsureSampleSceneOpen();
                NavMeshData navData = BakeHospitalNavMesh(out string diagnostics);
                AttachOrUpdateNavLoader(scene, navData);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);

                string summary = $"BAKE SUCCESS ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\nNavAsset: {NavAssetPath}\n{diagnostics}";
                WriteStatusLog(summary);
                Debug.Log("[SampleSceneAlesaSetup] " + summary);

                if (interactive)
                {
                    EditorUtility.DisplayDialog("Hospital NavMesh Baked", summary, "OK");
                }
            }
            catch (Exception ex)
            {
                WriteStatusLog($"FAILED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n{ex}\n");
                Debug.LogError($"[SampleSceneAlesaSetup] Bake failed: {ex}");
                if (Application.isBatchMode) throw;
            }
        }

        private static void RunFullSetupInternal(bool interactive)
        {
            try
            {
                Scene scene = EnsureSampleSceneOpen();

                // 1. Bake hospital NavMesh & attach loader
                NavMeshData navData = BakeHospitalNavMesh(out string diagnostics);
                HospitalNavMeshLoader loader = AttachOrUpdateNavLoader(scene, navData);

                // 2. Tag Player as "Player" and wire Alesa_Companion
                GameObject playerGo = FindPlayerGameObject(out PlayerGameplay playerGameplay);
                if (playerGo == null || playerGameplay == null)
                    throw new InvalidOperationException("Could not find Player with PlayerGameplay in SampleScene.");

                playerGo.tag = "Player";
                EditorUtility.SetDirty(playerGo);

                DialpadKeypad keypad = UnityEngine.Object.FindFirstObjectByType<DialpadKeypad>(FindObjectsInactive.Include);
                if (keypad == null)
                    throw new InvalidOperationException("Could not find DialpadKeypad in SampleScene.");

                GameObject alesaGo = PlaceAndWireAlesaCompanion(playerGo, playerGameplay, keypad);

                // 3. Save assets and scene
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                bool saved = EditorSceneManager.SaveScene(scene);
                if (!saved)
                    throw new InvalidOperationException($"Failed to save scene '{SampleScenePath}'.");

                string summary =
                    $"SUCCESS ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n" +
                    $"NavAsset: {NavAssetPath}\n" +
                    $"NavLoader: {loader.gameObject.name}\n" +
                    $"PlayerTagged: {playerGo.name} (Tag={playerGo.tag})\n" +
                    $"AlesaInstance: {alesaGo.name} at {alesaGo.transform.position}, rot={alesaGo.transform.eulerAngles}\n" +
                    $"KeypadWired: {keypad.gameObject.name}\n" +
                    diagnostics;

                WriteStatusLog(summary);
                Debug.Log("[SampleSceneAlesaSetup] Completed cleanly:\n" + summary);

                if (interactive)
                {
                    EditorUtility.DisplayDialog("SampleScene Setup Complete", summary, "OK");
                }
            }
            catch (Exception ex)
            {
                WriteStatusLog($"FAILED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n{ex}\n");
                Debug.LogError($"[SampleSceneAlesaSetup] Setup failed: {ex}");
                if (Application.isBatchMode) throw;
            }
        }

        private static Scene EnsureSampleSceneOpen()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!string.Equals(active.path, SampleScenePath, StringComparison.OrdinalIgnoreCase))
            {
                active = EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Single);
            }
            return active;
        }

        private static NavMeshData BakeHospitalNavMesh(out string diagnostics)
        {
            EnsureAssetFolder(NavFolder);

            Bounds bounds = new Bounds(BakeBoundsCenter, BakeBoundsSize);
            var markups = new List<NavMeshBuildMarkup>();
            var rawSources = new List<NavMeshBuildSource>();

            NavMeshBuilder.CollectSources(
                bounds,
                includedLayerMask: ~0,
                NavMeshCollectGeometry.PhysicsColliders,
                defaultArea: 0,
                markups,
                rawSources);

            // Collect locked barriers referenced by any DialpadKeypad so they don't permanently block the doorway.
            var excludedObjects = new HashSet<GameObject>();
            DialpadKeypad[] keypads = UnityEngine.Object.FindObjectsByType<DialpadKeypad>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < keypads.Length; i++)
            {
                if (keypads[i].lockedBarrier != null)
                {
                    excludedObjects.Add(keypads[i].lockedBarrier);
                }
            }

            var filteredSources = new List<NavMeshBuildSource>(rawSources.Count);
            int excludedCount = 0;
            for (int i = 0; i < rawSources.Count; i++)
            {
                NavMeshBuildSource src = rawSources[i];
                if (ShouldExcludeSource(src, excludedObjects))
                {
                    excludedCount++;
                    continue;
                }
                filteredSources.Add(src);
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.35f;
            settings.agentHeight = 1.75f;
            settings.agentSlope = 45f;
            settings.agentClimb = 0.4f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.1f;
            settings.overrideTileSize = true;
            settings.tileSize = 256;

            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(
                settings,
                filteredSources,
                bounds,
                Vector3.zero,
                Quaternion.identity);

            if (navData == null)
                throw new InvalidOperationException("NavMeshBuilder.BuildNavMeshData returned null.");

            navData.name = "SampleSceneHospitalNavMesh";

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(NavAssetPath);
            }

            AssetDatabase.CreateAsset(navData, NavAssetPath);
            AssetDatabase.SaveAssets();

            // Reload persisted asset and run sample/path verification
            NavMeshData persisted = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavAssetPath);
            diagnostics = VerifyBakedNavMesh(persisted, filteredSources.Count, excludedCount);
            return persisted;
        }

        private static bool ShouldExcludeSource(NavMeshBuildSource src, HashSet<GameObject> excludedObjects)
        {
            Component comp = src.component;
            if (comp == null) return false;

            if (comp is Collider col && col.isTrigger)
                return true;

            GameObject go = comp.gameObject;
            if (excludedObjects.Contains(go))
                return true;

            // Exclude dynamic characters & actors
            if (go.GetComponentInParent<PlayerMotor>() != null ||
                go.GetComponentInParent<PlayerGameplay>() != null ||
                go.GetComponentInParent<AlesaFollower>() != null ||
                go.GetComponentInParent<PossessedBoss>() != null ||
                go.GetComponentInParent<CharacterController>() != null ||
                go.GetComponentInParent<NavMeshAgent>() != null)
            {
                return true;
            }

            // Exclude barrier/door leaves so rescue cell & corridors remain traversable
            string name = go.name;
            if (name.IndexOf("Alesa access barrier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("DoorD_Left", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("DoorD_Right", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static string VerifyBakedNavMesh(NavMeshData navData, int includedSources, int excludedSources)
        {
            NavMeshDataInstance temp = NavMesh.AddNavMeshData(navData);
            try
            {
                bool alesaOnNav = NavMesh.SamplePosition(AlesaSpawnPosition, out NavMeshHit alesaHit, 2.0f, NavMesh.AllAreas);
                Vector3 playerPos = new Vector3(82.79f, 24.627f, -2.18f);
                bool playerOnNav = NavMesh.SamplePosition(playerPos, out NavMeshHit playerHit, 2.0f, NavMesh.AllAreas);

                string pathInfo = "N/A";
                if (alesaOnNav && playerOnNav)
                {
                    var path = new NavMeshPath();
                    bool ok = NavMesh.CalculatePath(alesaHit.position, playerHit.position, NavMesh.AllAreas, path);
                    pathInfo = $"ok={ok}, status={path.status}, corners={path.corners.Length}";
                }

                return
                    $"Sources: included={includedSources}, excluded={excludedSources}\n" +
                    $"AlesaSample: hit={alesaOnNav}, pos={(alesaOnNav ? alesaHit.position.ToString("F2") : "none")}\n" +
                    $"PlayerSample: hit={playerOnNav}, pos={(playerOnNav ? playerHit.position.ToString("F2") : "none")}\n" +
                    $"PathAlesaToPlayer: {pathInfo}\n";
            }
            finally
            {
                if (temp.valid) temp.Remove();
            }
        }

        private static HospitalNavMeshLoader AttachOrUpdateNavLoader(Scene scene, NavMeshData navData)
        {
            HospitalNavMeshLoader loader = UnityEngine.Object.FindFirstObjectByType<HospitalNavMeshLoader>(FindObjectsInactive.Include);
            if (loader == null)
            {
                var loaderGo = new GameObject("Hospital Navigation");
                SceneManager.MoveGameObjectToScene(loaderGo, scene);
                loader = loaderGo.AddComponent<HospitalNavMeshLoader>();
            }

            var so = new SerializedObject(loader);
            SerializedProperty prop = so.FindProperty("navigationData");
            if (prop != null)
            {
                prop.objectReferenceValue = navData;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            loader.NavigationData = navData;
            EditorUtility.SetDirty(loader);
            return loader;
        }

        private static GameObject FindPlayerGameObject(out PlayerGameplay playerGameplay)
        {
            playerGameplay = UnityEngine.Object.FindFirstObjectByType<PlayerGameplay>(FindObjectsInactive.Include);
            if (playerGameplay != null) return playerGameplay.gameObject;

            GameObject byName = GameObject.Find("Player");
            if (byName != null)
            {
                playerGameplay = byName.GetComponent<PlayerGameplay>();
                return byName;
            }

            return null;
        }

        private static GameObject PlaceAndWireAlesaCompanion(
            GameObject playerGo,
            PlayerGameplay playerGameplay,
            DialpadKeypad keypad)
        {
            AlesaFollower existingFollower = UnityEngine.Object.FindFirstObjectByType<AlesaFollower>(FindObjectsInactive.Include);
            GameObject alesaGo;

            if (existingFollower != null)
            {
                alesaGo = existingFollower.gameObject;
            }
            else
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlesaPrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Alesa prefab not found at '{AlesaPrefabPath}'.");

                alesaGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (alesaGo == null)
                    throw new InvalidOperationException($"Failed to instantiate '{AlesaPrefabPath}'.");
            }

            alesaGo.name = "Alesa_Companion";
            alesaGo.transform.SetPositionAndRotation(AlesaSpawnPosition, Quaternion.Euler(AlesaSpawnEuler));

            AlesaFollower follower = alesaGo.GetComponent<AlesaFollower>();
            if (follower != null)
            {
                follower.playerTransform = playerGo.transform;
                follower.autoFreeInTestScene = false;
                EditorUtility.SetDirty(follower);
            }

            AlesaRescueController rescue = alesaGo.GetComponent<AlesaRescueController>();
            if (rescue != null)
            {
                rescue.targetKeypad = keypad;
                rescue.playerGameplay = playerGameplay;
                EditorUtility.SetDirty(rescue);
            }

            AlesaGameplayLifecycle lifecycle = alesaGo.GetComponent<AlesaGameplayLifecycle>();
            if (lifecycle != null)
            {
                lifecycle.playerGameplay = playerGameplay;
                EditorUtility.SetDirty(lifecycle);
            }

            EditorUtility.SetDirty(alesaGo);
            return alesaGo;
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
