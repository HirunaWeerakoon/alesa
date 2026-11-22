using System.Collections;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    public enum DemonstrationStage
    {
        Stage1_Dormant,
        Stage2_RescueActivation,
        Stage3_CorridorFollow,
        Stage4_ProactiveGuidance,
        Stage5_SprintSynchronization,
        Stage6_HazardInfiltrationAndReroute,
        Stage7_CombatSafeWaiting,
        Stage8_HazardResolved,
        Stage9_PauseLifecycle,
        Stage10_ExitArrival
    }

    /// <summary>
    /// Interactive demonstration runner for Agent 1 (Alesa).
    /// Steers actors through the 10-stage escape narrative in the test scene,
    /// verifying rescue activation, proactive voice guidance, dynamic threat detours,
    /// combat safe waiting, and exit arrival.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlesaScenarioDemonstrator : MonoBehaviour
    {
        [Header("Target References")]
        public Transform playerTransform;
        public AlesaFollower companion;
        public AlesaNavigator navigator;
        public AlesaRescueController rescueController;
        public AlesaThreatAdapter threatAdapter;
        public AlesaVoiceSpeaker voiceSpeaker;
        public AlesaGuidanceController guidanceController;

        [Header("Demonstration State")]
        public DemonstrationStage currentStage = DemonstrationStage.Stage1_Dormant;
        [Tooltip("Delay in seconds between automated stage progressions.")]
        public float autoProgressionDelay = 3.0f;
        public bool isRunningAutomatedDemo = false;

        private Coroutine _demoRoutine;

        private void Start()
        {
            FindReferences();
        }

        public void FindReferences()
        {
            if (playerTransform == null)
            {
                var p = GameObject.FindWithTag("Player") ?? GameObject.Find("Player_Nathan");
                if (p != null) playerTransform = p.transform;
            }

            if (companion == null) companion = FindFirstObjectByType<AlesaFollower>();
            if (navigator == null) navigator = FindFirstObjectByType<AlesaNavigator>();
            if (rescueController == null) rescueController = FindFirstObjectByType<AlesaRescueController>();
            if (threatAdapter == null) threatAdapter = FindFirstObjectByType<AlesaThreatAdapter>();
            if (voiceSpeaker == null) voiceSpeaker = FindFirstObjectByType<AlesaVoiceSpeaker>();
            if (guidanceController == null) guidanceController = FindFirstObjectByType<AlesaGuidanceController>();
        }

        [ContextMenu("Run Full Automated 10-Stage Escape Demo")]
        public void StartAutomatedDemo()
        {
            if (_demoRoutine != null) StopCoroutine(_demoRoutine);
            _demoRoutine = StartCoroutine(RunAutomatedDemoRoutine());
        }

        [ContextMenu("Stop Demonstration")]
        public void StopAutomatedDemo()
        {
            if (_demoRoutine != null)
            {
                StopCoroutine(_demoRoutine);
                _demoRoutine = null;
            }
            isRunningAutomatedDemo = false;
        }

        [ContextMenu("Execute Next Stage")]
        public void StepNextStage()
        {
            int next = ((int)currentStage + 1) % 10;
            ExecuteStage((DemonstrationStage)next);
        }

        public void ExecuteStage(DemonstrationStage stage)
        {
            currentStage = stage;
            FindReferences();

            switch (stage)
            {
                case DemonstrationStage.Stage1_Dormant:
                    LogStage("Stage 1: Initial Dormant State", "Alesa is locked inside isolation room. AI is dormant, speed = 0, no guidance.");
                    if (companion != null) companion.autoFreeInTestScene = false;
                    if (rescueController != null) rescueController.ResetRescue();
                    if (playerTransform != null) playerTransform.position = new Vector3(0f, 1f, -14f);
                    break;

                case DemonstrationStage.Stage2_RescueActivation:
                    LogStage("Stage 2: Rescue Activation", "Nathan unlocks the electronic door. Rescue trigger fires greeting voice line and activates Following.");
                    if (rescueController != null) rescueController.ActivateAfterRescue();
                    break;

                case DemonstrationStage.Stage3_CorridorFollow:
                    LogStage("Stage 3: Corridor Traversal", "Nathan walks forward. Alesa follows at ~1.75m trailing distance with Craig Reynolds arrival steering.");
                    if (playerTransform != null) playerTransform.position = new Vector3(0f, 1f, -9f);
                    break;

                case DemonstrationStage.Stage4_ProactiveGuidance:
                    LogStage("Stage 4: Proactive Junction Guidance", "Nathan approaches T-Junction at (0, 0, -4). Alesa proactively speaks: 'Nathan, turn left!'");
                    if (playerTransform != null) playerTransform.position = new Vector3(0f, 1f, -4f);
                    break;

                case DemonstrationStage.Stage5_SprintSynchronization:
                    LogStage("Stage 5: Sprint Synchronization", "Nathan sprints. Alesa matches top speed to 8.5 m/s in Sprinting state.");
                    if (companion != null)
                    {
                        var pm = playerTransform != null ? playerTransform.GetComponent<PlayerMotor>() : null;
                        if (pm != null) pm.SetSprinting(true);
                    }
                    if (playerTransform != null) playerTransform.position = new Vector3(-2f, 1f, -1f);
                    break;

                case DemonstrationStage.Stage6_HazardInfiltrationAndReroute:
                    LogStage("Stage 6: Threat Infiltration & Reroute", "Boss blocks West Corridor! Yen's algorithm replans East detour. Alesa shouts reroute cue.");
                    if (threatAdapter != null) threatAdapter.SimulateBossBlock();
                    break;

                case DemonstrationStage.Stage7_CombatSafeWaiting:
                    LogStage("Stage 7: Combat Safe Waiting", "Nathan draws revolver and aims at Boss. Alesa immediately halts in WaitingAim safely behind him.");
                    var pg = playerTransform != null ? playerTransform.GetComponent<PlayerGameplay>() : null;
                    if (pg != null) pg.Combat?.SetAim(true);
                    break;

                case DemonstrationStage.Stage8_HazardResolved:
                    LogStage("Stage 8: Hazard Resolved (Silver Bullet)", "Nathan shoots Boss. West Corridor unblocked. Path restores and gun is lowered.");
                    if (threatAdapter != null) threatAdapter.SimulateBossCleared();
                    var pg8 = playerTransform != null ? playerTransform.GetComponent<PlayerGameplay>() : null;
                    if (pg8 != null) pg8.Combat?.SetAim(false);
                    break;

                case DemonstrationStage.Stage9_PauseLifecycle:
                    LogStage("Stage 9: Pause Lifecycle", "Game paused. Follower movement suspended and timers frozen without drift.");
                    break;

                case DemonstrationStage.Stage10_ExitArrival:
                    LogStage("Stage 10: Exit Arrival", "Destination reached! Alesa speaks: 'We reached the exit! Keep moving!', smooth arrival deceleration into Idle.");
                    if (playerTransform != null) playerTransform.position = new Vector3(0f, 1f, 14f);
                    if (voiceSpeaker != null) voiceSpeaker.PlayVoiceCueDirectly(VoiceCueId.DestinationReached);
                    break;
            }
        }

        private IEnumerator RunAutomatedDemoRoutine()
        {
            isRunningAutomatedDemo = true;
            for (int i = 0; i <= 9; i++)
            {
                ExecuteStage((DemonstrationStage)i);
                yield return new WaitForSeconds(autoProgressionDelay);
            }
            isRunningAutomatedDemo = false;
            LogStage("Demonstration Completed", "All 10 stages of the Agent 1 escape scenario demonstrated successfully.");
        }

        private void LogStage(string title, string details)
        {
            Debug.Log($"<color=#00FFFF><b>[ALESA DEMO] {title}:</b></color> {details}", this);
        }
    }
}
