using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Alesa.Editor
{
    /// <summary>
    /// Builds Alesa's and the boss's animator controllers and points their prefabs at them.
    /// </summary>
    /// <remarks>
    /// They used to share SharedHumanoidLocomotion, which is why the boss walked like the ally and
    /// died like a zombie. Their clip sets are now different, so they get a controller each.
    ///
    /// Built in code rather than by hand because the state machine is specific: Alesa's turn is a
    /// three-beat sequence (stop, turn, set off again) that a C# director steps through with
    /// triggers, and wiring that by hand in the Animator window is both slow and easy to get subtly
    /// wrong. Re-running replaces both controllers outright.
    /// </remarks>
    public static class CompanionBossAnimatorSetup
    {
        private const string Dir = "Assets/_Project/Animations/Humanoid/";
        private const string AlesaPath = Dir + "AlesaLocomotion.controller";
        private const string BossPath = Dir + "PossessedBossLocomotion.controller";

        private const string AlesaPrefab = "Assets/_Project/Prefabs/Characters/Alesa_Companion.prefab";
        private const string BossPrefab = "Assets/_Project/Prefabs/Characters/PossessedBoss.prefab";

        [MenuItem("ALESA/Wire Alesa and Boss Animators")]
        public static void Wire()
        {
            var log = new StringBuilder("[Animators]\n");
            var alesa = BuildAlesa(log);
            var boss = BuildBoss(log);

            AssignToPrefab(AlesaPrefab, alesa, log);
            AssignToPrefab(BossPrefab, boss, log);
            AttachDirector(log);
            WireDeathGroan(log);

            // The scene's boss is a prefab instance with its controller overridden, so the prefab
            // change alone would not reach it.
            foreach (var b in Object.FindObjectsByType<PossessedBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AssignToSceneObject(b.gameObject, boss, log);
            foreach (var a in Object.FindObjectsByType<Alesa.Runtime.AlesaFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                AssignToSceneObject(a.gameObject, alesa, log);

            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log(log.ToString());
        }

        // ---- clips ----------------------------------------------------------------------------------

        private static AnimationClip Clip(string file, StringBuilder log)
        {
            string path = Dir + file + ".fbx";
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview__")) return c;
            }
            log.AppendLine("  MISSING CLIP: " + path);
            return null;
        }

        // ---- Alesa ----------------------------------------------------------------------------------

        private static AnimatorController BuildAlesa(StringBuilder log)
        {
            AssetDatabase.DeleteAsset(AlesaPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(AlesaPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Jog", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);

            // One trigger per destination rather than a shared trigger narrowed by the Jog bool.
            // Sharing one does not work: Unity evaluates the Any State transitions in order and the
            // first that names the trigger swallows it, so the walk-side transition ate every Stop
            // and the jogging side never fired at all.
            controller.AddParameter("DoStopWalk", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoStopRun", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoTurnLeft", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoTurnRight", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoTurnJog", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoStartWalk", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("DoStartJog", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = Clip("Nervously Look Around", log);
            sm.defaultState = idle;

            var walk = sm.AddState("Walk");
            walk.motion = Clip("Female Walk", log);

            var jog = sm.AddState("Jog");
            jog.motion = Clip("Jogging", log);

            var startWalk = sm.AddState("StartWalk");
            startWalk.motion = Clip("Female Start Walking", log);

            var stopWalk = sm.AddState("StopWalk");
            stopWalk.motion = Clip("Female Stop Walking", log);

            var stopRun = sm.AddState("StopRun");
            stopRun.motion = Clip("Run To Stop", log);

            var turnLeft = sm.AddState("TurnLeft");
            turnLeft.motion = Clip("Left Turn", log);

            var turnRight = sm.AddState("TurnRight");
            turnRight.motion = Clip("Right Turn", log);

            // Jog In Circle is the turn taken at jogging pace. It is one continuous curve rather than
            // a left and a right, so both sides use it; at a jog the body reads as banking round
            // either way, which the sharp standing turns do not.
            var turnJog = sm.AddState("TurnJog");
            turnJog.motion = Clip("Jog In Circle", log);

            // Stopping: the director picks which stop by which trigger it fires.
            AnyState(sm, stopWalk, 0.12f, ("DoStopWalk", true));
            AnyState(sm, stopRun, 0.10f, ("DoStopRun", true));
            Exit(stopWalk, idle, 0.9f, 0.15f);
            Exit(stopRun, idle, 0.9f, 0.15f);

            // Turning on the spot.
            AnyState(sm, turnLeft, 0.12f, ("DoTurnLeft", true));
            AnyState(sm, turnRight, 0.12f, ("DoTurnRight", true));
            AnyState(sm, turnJog, 0.12f, ("DoTurnJog", true));

            // Setting off again.
            AnyState(sm, startWalk, 0.12f, ("DoStartWalk", true));
            AnyState(sm, jog, 0.15f, ("DoStartJog", true));
            Exit(startWalk, walk, 0.85f, 0.20f);

            // The director normally drives this with triggers, but a turn left to run out must still
            // land somewhere sensible rather than freezing on the last frame.
            Exit(turnLeft, idle, 0.95f, 0.15f);
            Exit(turnRight, idle, 0.95f, 0.15f);
            // Jog In Circle runs 5.93 s, far longer than a turn takes; the director's StartMove
            // normally cuts it short, and this only catches the case where it never arrives.
            Exit(turnJog, idle, 0.5f, 0.15f);

            // Walking and jogging swap on the Jog flag, so matching the player's pace needs no restart.
            Bool(walk, jog, "Jog", true, 0.20f);
            Bool(jog, walk, "Jog", false, 0.20f);

            // Falling back to idle when movement stops without an explicit Stop, and out of idle when
            // it resumes without an explicit StartMove.
            Bool(walk, idle, "Moving", false, 0.25f);
            Bool(jog, idle, "Moving", false, 0.25f);
            // Leaving idle without an explicit trigger. These are plain bool pairs on a state's own
            // transitions, which is evaluated per state and not subject to the Any State ordering
            // problem that the triggers above work around.
            Bool(idle, startWalk, "Moving", true, 0.20f, ("Jog", false));
            Bool(idle, jog, "Moving", true, 0.20f, ("Jog", true));

            EditorUtility.SetDirty(controller);
            log.AppendLine("  Alesa controller: 9 states (" + AlesaPath + ")");
            return controller;
        }

        // ---- boss -----------------------------------------------------------------------------------

        private static AnimatorController BuildBoss(StringBuilder log)
        {
            AssetDatabase.DeleteAsset(BossPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(BossPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("SuperAttack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Scream", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("TurnLeft", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("TurnRight", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            var idle = sm.AddState("Idle");
            idle.motion = Clip("X Bot@Standing Idle", log);
            sm.defaultState = idle;

            // She only ever travels at 1.8 m/s, which is the Female Walk clip's own pace, so the
            // locomotion is a start and a loop rather than a blend up to a run she never reaches.
            var startWalk = sm.AddState("StartWalk");
            startWalk.motion = Clip("Female Start Walking", log);

            var walk = sm.AddState("Walk");
            walk.motion = Clip("Female Walk", log);

            var stopWalk = sm.AddState("StopWalk");
            stopWalk.motion = Clip("Female Stop Walking", log);

            var turnLeft = sm.AddState("TurnLeft");
            turnLeft.motion = Clip("Left Turn", log);

            var turnRight = sm.AddState("TurnRight");
            turnRight.motion = Clip("Right Turn", log);

            var attack = sm.AddState("Attack");
            attack.motion = Clip("Brutal Assassination", log);

            var superAttack = sm.AddState("SuperAttack");
            superAttack.motion = Clip("Superhuman Choke Lift", log);

            var scream = sm.AddState("Scream");
            scream.motion = Clip("Zombie Scream", log);

            var die = sm.AddState("Die");
            die.motion = Clip("Zombie Death", log);

            Bool(idle, startWalk, "Moving", true, 0.20f);
            Exit(startWalk, walk, 0.85f, 0.20f);
            Bool(walk, stopWalk, "Moving", false, 0.15f);
            Exit(stopWalk, idle, 0.9f, 0.15f);

            AnyState(sm, turnLeft, 0.12f, ("TurnLeft", true));
            AnyState(sm, turnRight, 0.12f, ("TurnRight", true));
            Exit(turnLeft, idle, 0.95f, 0.15f);
            Exit(turnRight, idle, 0.95f, 0.15f);

            AnyState(sm, attack, 0.10f, ("Attack", true));
            AnyState(sm, superAttack, 0.10f, ("SuperAttack", true));
            AnyState(sm, scream, 0.10f, ("Scream", true));
            AnyState(sm, die, 0.10f, ("Die", true));

            Exit(attack, idle, 0.90f, 0.20f);
            // No exit from the choke lift: the run is over by the time it finishes.
            Exit(scream, idle, 0.90f, 0.20f);

            EditorUtility.SetDirty(controller);
            log.AppendLine("  Boss controller: 10 states (" + BossPath + ")");
            return controller;
        }

        // ---- transition helpers ---------------------------------------------------------------------

        private static void AnyState(AnimatorStateMachine sm, AnimatorState to, float duration,
            params (string param, bool on)[] conditions)
        {
            var t = sm.AddAnyStateTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.canTransitionToSelf = false;
            Apply(t, conditions);
        }

        private static void Exit(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = true;
            t.exitTime = exitTime;
            t.duration = duration;
        }

        private static void Bool(AnimatorState from, AnimatorState to, string param, bool on,
            float duration, params (string param, bool on)[] extra)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
            Apply(t, extra);
        }

        private static void Apply(AnimatorStateTransition t, (string param, bool on)[] conditions)
        {
            if (conditions == null) return;
            foreach (var (param, on) in conditions)
                t.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, param);
        }

        // ---- assignment -----------------------------------------------------------------------------

        private static void AssignToPrefab(string prefabPath, AnimatorController controller, StringBuilder log)
        {
            if (controller == null) return;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null) { log.AppendLine("  no Animator on " + prefabPath); return; }
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false; // AgentMotor / AlesaFollower own position
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                log.AppendLine("  " + System.IO.Path.GetFileNameWithoutExtension(prefabPath)
                    + " -> " + controller.name);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>Puts the stop-turn-start director on Alesa, prefab and scene copy alike.</summary>
        /// <remarks>
        /// Resolved by name rather than referenced as a type on purpose. An editor script that fails
        /// to compile stops Unity reloading the domain at all, so a direct reference to a brand new
        /// runtime class deadlocks: the editor assembly cannot build until the type exists, and the
        /// type cannot load until the editor assembly builds.
        /// </remarks>
        private static System.Type DirectorType =>
            System.Type.GetType("AlesaAnimationDirector, Assembly-CSharp");

        private static void AttachDirector(StringBuilder log)
        {
            var type = DirectorType;
            if (type == null) { log.AppendLine("  AlesaAnimationDirector type not compiled yet; skipped."); return; }

            var root = PrefabUtility.LoadPrefabContents(AlesaPrefab);
            try
            {
                if (root.GetComponent(type) == null)
                {
                    root.AddComponent(type);
                    PrefabUtility.SaveAsPrefabAsset(root, AlesaPrefab);
                    log.AppendLine("  AlesaAnimationDirector added to the Alesa prefab");
                }
                else log.AppendLine("  AlesaAnimationDirector already on the Alesa prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (var f in Object.FindObjectsByType<Alesa.Runtime.AlesaFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (f.GetComponent(type) != null) continue;
                Undo.AddComponent(f.gameObject, type);
                log.AppendLine("  AlesaAnimationDirector added to scene '" + f.gameObject.name + "'");
            }
        }

        /// <summary>Gives the boss Nathan's dying groan for her finishers.</summary>
        private static void WireDeathGroan(StringBuilder log)
        {
            const string groanPath = "Assets/Audio/SFX/male/731506__soundbitersfx__npcplayer-death-groans-male.wav";
            var groan = AssetDatabase.LoadAssetAtPath<AudioClip>(groanPath);
            if (groan == null) { log.AppendLine("  MISSING groan: " + groanPath); return; }

            var root = PrefabUtility.LoadPrefabContents(BossPrefab);
            try
            {
                var brain = root.GetComponent<PossessedBoss>();
                if (brain != null)
                {
                    var so = new SerializedObject(brain);
                    so.FindProperty("playerDeathGroan").objectReferenceValue = groan;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, BossPrefab);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            foreach (var b in Object.FindObjectsByType<PossessedBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(b);
                so.FindProperty("playerDeathGroan").objectReferenceValue = groan;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(b);
            }
            log.AppendLine("  death groan wired (" + groan.length.ToString("F2") + " s)");
        }

        private static void AssignToSceneObject(GameObject go, AnimatorController controller, StringBuilder log)
        {
            if (controller == null) return;
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null) return;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);
            log.AppendLine("  scene '" + go.name + "' -> " + controller.name);
        }
    }
}
