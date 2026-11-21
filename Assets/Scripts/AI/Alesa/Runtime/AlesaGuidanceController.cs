using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime guidance decision coordinator for Alesa.
    /// Connects route planning, junction detection, and direction classification.
    /// Emits proactive navigation decisions for the voice playback engine.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class AlesaGuidanceController : MonoBehaviour
    {
        [Header("Target & References")]
        [Tooltip("Nathan's Transform. If unassigned, automatically finds Player in scene.")]
        public Transform playerTransform;

        [Tooltip("The route navigator managing active escape paths.")]
        public AlesaNavigator navigator;

        [Tooltip("The junction detector component.")]
        public AlesaJunctionDetector junctionDetector;

        [Header("Guidance Settings")]
        [Tooltip("Distance along the corridor in meters where proactive guidance triggers (default ~5m).")]
        [Range(2f, 10f)]
        public float lookaheadDistance = 5.0f;

        [Tooltip("Minimum time in seconds between normal navigation callouts.")]
        [Range(1f, 10f)]
        public float normalCooldown = 4.0f;

        [Tooltip("Minimum time before repeating the exact same instruction at the same junction.")]
        [Range(5f, 25f)]
        public float minRepeatInterval = 12.0f;

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private GuidanceType currentGuidanceType = GuidanceType.None;
        [SerializeField] private TurnDirection currentTurnDirection = TurnDirection.None;
        [SerializeField] private string currentTargetJunction = string.Empty;
        [SerializeField] private string lastGuidanceMessage = string.Empty;
        [SerializeField] private float lastDecisionTimestamp = 0f;

        private GuidanceDecisionController _controller;

        public GuidanceDecisionController Controller => _controller ??= new GuidanceDecisionController(
            junctionDetector != null ? junctionDetector.Detector : null,
            null,
            lookaheadDistance,
            normalCooldown);

        public GuidanceDecision LastDecision => _controller != null ? _controller.CurrentDecision : GuidanceDecision.None;
        public string LastGuidanceMessage => lastGuidanceMessage;

        public event Action<GuidanceDecision> OnGuidanceEmitted;

        private void Awake()
        {
            InitializeReferences();
            InitializeController();
        }

        private void OnEnable()
        {
            InitializeReferences();
            InitializeController();

            if (navigator != null)
            {
                navigator.OnRecommendedRouteChanged += HandleRouteChanged;
            }
        }

        private void OnDisable()
        {
            if (navigator != null)
            {
                navigator.OnRecommendedRouteChanged -= HandleRouteChanged;
            }
        }

        public void InitializeReferences()
        {
            if (navigator == null)
            {
                navigator = GetComponent<AlesaNavigator>() ?? FindFirstObjectByType<AlesaNavigator>();
            }

            if (junctionDetector == null)
            {
                junctionDetector = GetComponent<AlesaJunctionDetector>() ?? FindFirstObjectByType<AlesaJunctionDetector>();
            }

            if (playerTransform == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj == null)
                {
                    var nathan = GameObject.Find("Player_Nathan");
                    if (nathan != null) playerObj = nathan;
                }

                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                }
            }
        }

        private void InitializeController()
        {
            if (_controller == null)
            {
                var det = junctionDetector != null ? junctionDetector.Detector : new JunctionDetector(lookaheadDistance);
                _controller = new GuidanceDecisionController(det, new DirectionClassifier(), lookaheadDistance, normalCooldown);
                _controller.OnGuidanceEmitted += HandleGuidanceEmitted;
            }

            _controller.LookaheadDistance = lookaheadDistance;
            _controller.NormalCooldown = normalCooldown;
            _controller.MinRepeatIntervalSameJunction = minRepeatInterval;
        }

        private void Update()
        {
            if (playerTransform == null || navigator == null)
            {
                InitializeReferences();
                if (playerTransform == null || navigator == null) return;
            }

            if (_controller == null) InitializeController();

            _controller.LookaheadDistance = lookaheadDistance;
            _controller.NormalCooldown = normalCooldown;
            _controller.MinRepeatIntervalSameJunction = minRepeatInterval;

            var route = navigator.ActiveRoute;
            var graph = navigator.Graph;

            if (route != null && route.IsValid && graph != null)
            {
                NodePosition playerPos = playerTransform.position.ToNodePosition();
                _controller.EvaluateGuidance(graph, route, playerPos, Time.time, out _);
            }
        }

        private void HandleRouteChanged(RouteUpdateInfo info)
        {
            if (_controller == null) InitializeController();

            var graph = navigator != null ? navigator.Graph : null;
            NodePosition playerPos = playerTransform != null ? playerTransform.position.ToNodePosition() : default;

            _controller.HandleRouteChanged(graph, info.ActiveRoute, playerPos, info.ChangeReason, Time.time, out _);
        }

        private void HandleGuidanceEmitted(GuidanceDecision decision)
        {
            currentGuidanceType = decision.Type;
            currentTurnDirection = decision.Direction;
            currentTargetJunction = decision.TargetJunctionId;
            lastGuidanceMessage = decision.Message;
            lastDecisionTimestamp = decision.Timestamp;

            OnGuidanceEmitted?.Invoke(decision);
        }

        public void ResetGuidance()
        {
            _controller?.Reset();
            currentGuidanceType = GuidanceType.None;
            currentTurnDirection = TurnDirection.None;
            currentTargetJunction = string.Empty;
            lastGuidanceMessage = string.Empty;
            lastDecisionTimestamp = 0f;
        }
    }
}
