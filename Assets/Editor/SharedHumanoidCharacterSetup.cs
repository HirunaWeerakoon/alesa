using System;
using System.IO;
using Alesa.Runtime;
using Alesa.Runtime.Motion;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace Alesa.Editor
{
    /// <summary>
    /// Configures the 3 Mixamo Humanoid locomotion FBXs, builds SharedHumanoidLocomotion.controller,
    /// and creates the Alesa_Companion Prefab Variant derived from Aria_Blake_Unity.prefab.
    /// </summary>
    [InitializeOnLoad]
    public static class SharedHumanoidCharacterSetup
    {
        private const string HumanoidAnimFolder = "Assets/_Project/Animations/Humanoid";
        private const string IdleFbxPath = HumanoidAnimFolder + "/X Bot@Standing Idle.fbx";
        private const string WalkFbxPath = HumanoidAnimFolder + "/X Bot@Walking.fbx";
        private const string RunFbxPath = HumanoidAnimFolder + "/X Bot@Running.fbx";
        private const string ControllerPath = HumanoidAnimFolder + "/SharedHumanoidLocomotion.controller";

        private const string BaseAriaPrefabPath = "Assets/Aria Blake/Prefabs/Aria_Blake_Unity.prefab";
        private const string PrefabVariantFolder = "Assets/_Project/Prefabs/Characters";
        private const string PrefabVariantPath = PrefabVariantFolder + "/Alesa_Companion.prefab";

        private const string StatusLogPath = "Temp/SharedHumanoidSetupStatus.txt";

        private static bool _scheduledAutoRun;

        static SharedHumanoidCharacterSetup()
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
            RunSetupInternal(interactive: false);
        }

        [MenuItem("ALESA/Setup Shared Humanoid & Alesa Companion", false, 20)]
        public static void RunSetupFromMenu()
        {
            RunSetupInternal(interactive: true);
        }

        public static void RunBatchSetup()
        {
            RunSetupInternal(interactive: false);
        }

        private static bool NeedsSetup()
        {
            if (!File.Exists(IdleFbxPath) || !File.Exists(WalkFbxPath) || !File.Exists(RunFbxPath))
                return false;

            if (!File.Exists(ControllerPath) || !File.Exists(PrefabVariantPath))
                return true;

            return !IsConfiguredHumanoid(IdleFbxPath) ||
                   !IsConfiguredHumanoid(WalkFbxPath) ||
                   !IsConfiguredHumanoid(RunFbxPath);
        }

        private static bool IsConfiguredHumanoid(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return false;
            if (importer.animationType != ModelImporterAnimationType.Human) return false;
            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) return false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (!clips[i].loopTime || !clips[i].lockRootRotation || !clips[i].lockRootHeightY)
                    return false;
            }
            return true;
        }

        private static void RunSetupInternal(bool interactive)
        {
            try
            {
                // 1. Configure Mixamo FBX import settings (Humanoid + Loop Time + Bake Into Pose Y/Rot)
                ConfigureHumanoidFbx(IdleFbxPath, "Standing Idle");
                ConfigureHumanoidFbx(WalkFbxPath, "Walking");
                ConfigureHumanoidFbx(RunFbxPath, "Running");

                AnimationClip idleClip = LoadPrimaryClip(IdleFbxPath);
                AnimationClip walkClip = LoadPrimaryClip(WalkFbxPath);
                AnimationClip runClip = LoadPrimaryClip(RunFbxPath);

                // 2. Build SharedHumanoidLocomotion.controller
                AnimatorController controller = BuildSharedAnimatorController(idleClip, walkClip, runClip);

                // 3. Create Alesa_Companion Prefab Variant from Aria_Blake_Unity.prefab
                CreateAlesaCompanionPrefabVariant(controller);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                string summary =
                    $"SUCCESS ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n" +
                    $"IdleClip: {idleClip.name}\n" +
                    $"WalkClip: {walkClip.name}\n" +
                    $"RunClip: {runClip.name}\n" +
                    $"Controller: {ControllerPath}\n" +
                    $"PrefabVariant: {PrefabVariantPath}\n";

                WriteStatusLog(summary);
                Debug.Log("[SharedHumanoidCharacterSetup] Completed cleanly:\n" + summary);

                if (interactive)
                {
                    EditorUtility.DisplayDialog(
                        "Shared Humanoid Setup Complete",
                        "Configured Mixamo Humanoid clips, built SharedHumanoidLocomotion.controller, and created Alesa_Companion.prefab.",
                        "OK");
                }
            }
            catch (Exception ex)
            {
                WriteStatusLog($"FAILED ({DateTime.Now:yyyy-MM-dd HH:mm:ss})\n{ex}\n");
                Debug.LogError($"[SharedHumanoidCharacterSetup] Setup failed: {ex}");
                if (Application.isBatchMode) throw;
            }
        }

        private static void ConfigureHumanoidFbx(string assetPath, string desiredClipName)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"ModelImporter not found for '{assetPath}'.");

            bool changedRig = importer.animationType != ModelImporterAnimationType.Human ||
                              importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;

            if (changedRig)
            {
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null)
                    throw new InvalidOperationException($"ModelImporter reload failed for '{assetPath}'.");
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = importer.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0)
                throw new InvalidOperationException($"No embedded animation clips found in '{assetPath}'.");

            for (int i = 0; i < clips.Length; i++)
            {
                ModelImporterClipAnimation clip = clips[i];
                if (clips.Length == 1 && !string.IsNullOrEmpty(desiredClipName))
                {
                    clip.name = desiredClipName;
                }
                clip.loopTime = true;
                clip.loopPose = true;
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clips[i] = clip;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip LoadPrimaryClip(string fbxPath)
        {
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < subAssets.Length; i++)
            {
                if (subAssets[i] is AnimationClip clip &&
                    !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            throw new InvalidOperationException($"No AnimationClip sub-asset found in '{fbxPath}'.");
        }

        private static AnimatorController BuildSharedAnimatorController(
            AnimationClip idleClip,
            AnimationClip walkClip,
            AnimationClip runClip)
        {
            EnsureAssetFolder(HumanoidAnimFolder);

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("AngularVelocity", AnimatorControllerParameterType.Float);
            controller.AddParameter("TurningInPlace", AnimatorControllerParameterType.Bool);

            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            AnimatorState locomotionState = controller.CreateBlendTreeInController("Locomotion", out BlendTree blendTree, 0);
            blendTree.name = "Locomotion";
            blendTree.blendType = BlendTreeType.Simple1D;
            blendTree.blendParameter = "Speed";
            blendTree.useAutomaticThresholds = false;
            blendTree.AddChild(idleClip, 0.0f);
            blendTree.AddChild(walkClip, 1.8f);
            blendTree.AddChild(runClip, 5.0f);

            controller.layers[0].stateMachine.defaultState = locomotionState;

            EditorUtility.SetDirty(blendTree);
            EditorUtility.SetDirty(locomotionState);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            return controller;
        }

        private static void CreateAlesaCompanionPrefabVariant(AnimatorController controller)
        {
            EnsureAssetFolder(PrefabVariantFolder);

            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseAriaPrefabPath);
            if (basePrefab == null)
                throw new InvalidOperationException($"Base prefab not found at '{BaseAriaPrefabPath}'.");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate base prefab '{BaseAriaPrefabPath}'.");

            try
            {
                instance.name = "Alesa_Companion";

                Animator animator = instance.GetComponent<Animator>();
                if (animator == null) animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                NavMeshAgent navAgent = instance.GetComponent<NavMeshAgent>();
                if (navAgent == null) navAgent = instance.AddComponent<NavMeshAgent>();
                navAgent.baseOffset = 0f;
                navAgent.radius = 0.35f;
                navAgent.height = 1.75f;

                AlesaFollower follower = instance.GetComponent<AlesaFollower>();
                if (follower == null) follower = instance.AddComponent<AlesaFollower>();
                follower.agent = navAgent;
                follower.autoFreeInTestScene = false;
                follower.playerTransform = null;

                AlesaRescueController rescue = instance.GetComponent<AlesaRescueController>();
                if (rescue == null) rescue = instance.AddComponent<AlesaRescueController>();
                rescue.follower = follower;
                rescue.targetKeypad = null;
                rescue.playerGameplay = null;

                AlesaGameplayLifecycle lifecycle = instance.GetComponent<AlesaGameplayLifecycle>();
                if (lifecycle == null) lifecycle = instance.AddComponent<AlesaGameplayLifecycle>();
                lifecycle.follower = follower;
                lifecycle.playerGameplay = null;

                AgentMotor motor = instance.GetComponent<AgentMotor>();
                if (motor == null) motor = instance.AddComponent<AgentMotor>();
                SetObjectField(motor, "animator", animator);

                if (instance.GetComponent<HeadLookAt>() == null)
                {
                    instance.AddComponent<HeadLookAt>();
                }

                AlesaMotorBridge bridge = instance.GetComponent<AlesaMotorBridge>();
                if (bridge == null) bridge = instance.AddComponent<AlesaMotorBridge>();
                SetObjectField(bridge, "lifecycle", lifecycle);

                if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabVariantPath) != null)
                {
                    AssetDatabase.DeleteAsset(PrefabVariantPath);
                }

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, PrefabVariantPath, out bool success);
                if (!success || savedPrefab == null)
                    throw new InvalidOperationException($"Failed to save Prefab Variant at '{PrefabVariantPath}'.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void SetObjectField(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop != null)
            {
                prop.objectReferenceValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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
