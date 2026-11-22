using System;

namespace Alesa.Core
{
    public struct CoordinatorFrameInput
    {
        public NodePosition PlayerPosition;
        public NodePosition PlayerForward;
        public NodePosition CompanionPosition;
        public bool IsRescued;
        public bool ControlsEnabled;
        public bool IsAiming;
        public bool IsSprinting;
        public bool HasMovementInput;
        public float CurrentTime;
        public float DeltaTime;
    }

    public struct CoordinatorFrameOutput
    {
        public CompanionState LocomotionState;
        public NodePosition DesiredAnchorPosition;
        public float DesiredSpeed;
        public RouteResult ActiveRoute;
        public GuidanceDecision CurrentGuidance;
        public VoiceCueId EmittedVoiceCue;
        public bool IsRouteReplanned;
        public bool IsTrapped;
    }

    /// <summary>
    /// Pure C# companion behavior coordinator for Agent 1.
    /// Unifies follower movement, arrival steering, proactive junction guidance,
    /// dynamic threat replanning, and combat safe-waiting into a single deterministic pipeline.
    /// Strictly decoupled from UnityEngine for automated multi-scenario testing.
    /// </summary>
    public sealed class CompanionBehaviorCoordinator
    {
        private readonly CompanionStateMachine _stateMachine;
        private readonly FollowAnchorCalculator _anchorCalculator;
        private readonly ArrivalSteeringCalculator _steeringCalculator;
        private readonly AlesaRoutePlanner _routePlanner;
        private readonly ThreatEventCoordinator _threatCoordinator;
        private readonly GuidanceDecisionController _guidanceController;
        private readonly DirectionClassifier _classifier;

        public CompanionStateMachine StateMachine => _stateMachine;
        public FollowAnchorCalculator AnchorCalculator => _anchorCalculator;
        public ArrivalSteeringCalculator SteeringCalculator => _steeringCalculator;
        public AlesaRoutePlanner RoutePlanner => _routePlanner;
        public ThreatEventCoordinator ThreatCoordinator => _threatCoordinator;
        public GuidanceDecisionController GuidanceController => _guidanceController;

        public CompanionBehaviorCoordinator(
            WaypointGraph graph,
            string startNodeId,
            string exitNodeId,
            float followDistance = 1.75f,
            float minPersonalSpace = 1.0f,
            float stoppingDistance = 0.4f,
            float slowingRadius = 2.5f,
            float lookaheadDistance = 5.0f,
            float guidanceCooldown = 4.0f)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));

            _stateMachine = new CompanionStateMachine(CompanionState.Dormant);
            _anchorCalculator = new FollowAnchorCalculator(followDistance, minPersonalSpace);
            _steeringCalculator = new ArrivalSteeringCalculator(stoppingDistance, slowingRadius);
            _routePlanner = new AlesaRoutePlanner(graph, new YenKShortestPaths(), maxAlternatives: 3);
            _threatCoordinator = new ThreatEventCoordinator();
            _classifier = new DirectionClassifier();

            var detector = new JunctionDetector(lookaheadDistance);
            _guidanceController = new GuidanceDecisionController(detector, _classifier, lookaheadDistance, guidanceCooldown);

            // Hook dynamic replanning into guidance decision controller
            _routePlanner.OnRouteChanged += (newRoute, reason) =>
            {
                // When route changes due to blockages, guidance triggers immediately
            };

            if (!string.IsNullOrEmpty(startNodeId) && !string.IsNullOrEmpty(exitNodeId))
            {
                _routePlanner.SetEndpoints(startNodeId, exitNodeId, replanImmediately: true);
            }
        }

        /// <summary>
        /// Executes a single simulation tick of all coordinated companion behaviors.
        /// Deterministically resolves formation anchors, steering speeds, active route health,
        /// and proactive voice instructions without race conditions.
        /// </summary>
        public CoordinatorFrameOutput Tick(in CoordinatorFrameInput input)
        {
            var output = new CoordinatorFrameOutput();

            // 1. Calculate dynamic trailing follow anchor (~1.75m behind Nathan)
            NodePosition idealAnchor = _anchorCalculator.CalculateIdealAnchor(input.PlayerPosition, input.PlayerForward);
            output.DesiredAnchorPosition = idealAnchor;

            float distToAnchor = input.CompanionPosition.DistanceTo(idealAnchor);
            float distToPlayer = input.CompanionPosition.DistanceTo(input.PlayerPosition);

            // 2. Evaluate Locomotion Finite State Machine
            var context = new CompanionContext
            {
                IsAlesaRescued = input.IsRescued,
                IsPlayerControlsEnabled = input.ControlsEnabled,
                IsPlayerAiming = input.IsAiming,
                IsPlayerSprinting = input.IsSprinting,
                HasPlayerMovementInput = input.HasMovementInput,
                DistanceToPlayer = distToPlayer,
                DistanceToFollowAnchor = distToAnchor,
                HasValidNavPath = true,
                IsStuck = false
            };

            output.LocomotionState = _stateMachine.EvaluateState(in context);

            // 3. Modulate arrival steering speed
            if (output.LocomotionState == CompanionState.WaitingAim ||
                output.LocomotionState == CompanionState.Idle ||
                output.LocomotionState == CompanionState.Dormant)
            {
                output.DesiredSpeed = 0f;
            }
            else
            {
                float topSpeed = _stateMachine.GetTargetSpeed();
                output.DesiredSpeed = _steeringCalculator.CalculateDesiredSpeed(distToAnchor, topSpeed);
            }

            // 4. Check dynamic route replanning & threat status
            bool replanned = _routePlanner.CheckAndReplanIfInvalid(out var activeRoute);
            output.IsRouteReplanned = replanned;
            output.ActiveRoute = activeRoute;
            output.IsTrapped = (activeRoute != null && activeRoute.Status == RouteStatus.NoPath);

            // 5. Evaluate proactive voice guidance
            if (input.IsRescued && activeRoute != null && activeRoute.IsValid)
            {
                if (replanned)
                {
                    _guidanceController.HandleRouteChanged(
                        _routePlanner.Graph,
                        activeRoute,
                        input.PlayerPosition,
                        RouteChangeReason.CorridorBlocked,
                        input.CurrentTime,
                        out var rerouteDecision);

                    output.CurrentGuidance = rerouteDecision;
                    output.EmittedVoiceCue = VoiceClipMapper.MapDecisionToCue(rerouteDecision);
                }
                else
                {
                    bool emitted = _guidanceController.EvaluateGuidance(
                        _routePlanner.Graph,
                        activeRoute,
                        input.PlayerPosition,
                        input.CurrentTime,
                        out var normalDecision);

                    if (emitted)
                    {
                        output.CurrentGuidance = normalDecision;
                        output.EmittedVoiceCue = VoiceClipMapper.MapDecisionToCue(normalDecision);
                    }
                    else
                    {
                        output.CurrentGuidance = GuidanceDecision.None;
                        output.EmittedVoiceCue = VoiceCueId.None;
                    }
                }
            }
            else if (output.IsTrapped)
            {
                _guidanceController.HandlePathLost(input.CurrentTime, out var trappedDecision);
                output.CurrentGuidance = trappedDecision;
                output.EmittedVoiceCue = VoiceClipMapper.MapDecisionToCue(trappedDecision);
            }
            else
            {
                output.CurrentGuidance = GuidanceDecision.None;
                output.EmittedVoiceCue = VoiceCueId.None;
            }

            return output;
        }

        public bool ReportThreat(ThreatEvent threat)
        {
            return _threatCoordinator.ReportThreat(_routePlanner.Graph, threat);
        }

        public bool ClearThreat(string threatId)
        {
            return _threatCoordinator.ClearThreat(_routePlanner.Graph, threatId);
        }
    }
}
