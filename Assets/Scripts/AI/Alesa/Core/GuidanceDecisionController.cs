using System;

namespace Alesa.Core
{
    public enum GuidanceType
    {
        None = 0,
        TurnInstruction = 1,
        RouteBlocked = 2,
        RouteRerouted = 3,
        DestinationReached = 4,
        NoRouteAvailable = 5
    }

    /// <summary>
    /// Immutable record of an intelligent guidance decision produced by the GuidanceDecisionController.
    /// </summary>
    public readonly struct GuidanceDecision : IEquatable<GuidanceDecision>
    {
        public GuidanceType Type { get; }
        public TurnDirection Direction { get; }
        public string TargetJunctionId { get; }
        public float DistanceToJunction { get; }
        public float SignedAngle { get; }
        public bool IsCritical { get; }
        public string Message { get; }
        public float Timestamp { get; }

        public bool HasInstruction => Type != GuidanceType.None;

        public GuidanceDecision(
            GuidanceType type,
            TurnDirection direction,
            string targetJunctionId,
            float distanceToJunction,
            float signedAngle,
            bool isCritical,
            string message,
            float timestamp)
        {
            Type = type;
            Direction = direction;
            TargetJunctionId = targetJunctionId ?? string.Empty;
            DistanceToJunction = distanceToJunction;
            SignedAngle = signedAngle;
            IsCritical = isCritical;
            Message = message ?? string.Empty;
            Timestamp = timestamp;
        }

        public static GuidanceDecision None => new GuidanceDecision(GuidanceType.None, TurnDirection.None, null, 0f, 0f, false, null, 0f);

        public static GuidanceDecision Turn(TurnDirection direction, string junctionId, float distance, float angle, float time)
        {
            string msg = direction switch
            {
                TurnDirection.Left => "Nathan, turn left!",
                TurnDirection.Right => "Nathan, turn right!",
                TurnDirection.Straight => "Keep going straight!",
                TurnDirection.UTurn => "Turn around!",
                _ => "Proceed through the junction."
            };

            return new GuidanceDecision(GuidanceType.TurnInstruction, direction, junctionId, distance, angle, false, msg, time);
        }

        public static GuidanceDecision Blocked(string blockedNodeId, float time)
        {
            return new GuidanceDecision(GuidanceType.RouteBlocked, TurnDirection.None, blockedNodeId, 0f, 0f, true, "That way is blocked! Recalculating route...", time);
        }

        public static GuidanceDecision Rerouted(TurnDirection newDirection, string junctionId, float distance, float angle, float time)
        {
            string dirWord = newDirection == TurnDirection.Left ? "left" : (newDirection == TurnDirection.Right ? "right" : "ahead");
            return new GuidanceDecision(GuidanceType.RouteRerouted, newDirection, junctionId, distance, angle, true, $"Rerouting! Take the corridor to your {dirWord}!", time);
        }

        public static GuidanceDecision NoRoute(float time)
        {
            return new GuidanceDecision(GuidanceType.NoRouteAvailable, TurnDirection.None, null, 0f, 0f, true, "No safe route available! We're trapped!", time);
        }

        public static GuidanceDecision Arrived(string exitId, float time)
        {
            return new GuidanceDecision(GuidanceType.DestinationReached, TurnDirection.None, exitId, 0f, 0f, false, "We reached the exit! Keep moving!", time);
        }

        public bool Equals(GuidanceDecision other)
        {
            return Type == other.Type &&
                   Direction == other.Direction &&
                   string.Equals(TargetJunctionId, other.TargetJunctionId, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => obj is GuidanceDecision other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Type, Direction, StringComparer.OrdinalIgnoreCase.GetHashCode(TargetJunctionId ?? string.Empty));
        public override string ToString() => $"Decision[{Type} -> {Direction} @ '{TargetJunctionId}' (Dist={DistanceToJunction:F1}m, Critical={IsCritical})]";
    }

    /// <summary>
    /// Pure C# proactive navigation decision controller.
    /// Evaluates active route geometry, junction proximity (~5m), direction classification,
    /// and dynamic corridor blockages. Enforces cooldowns and state hysteresis to prevent
    /// repeated or spammy announcements while ensuring critical reroutes take immediate priority.
    /// </summary>
    public sealed class GuidanceDecisionController
    {
        private readonly JunctionDetector _detector;
        private readonly DirectionClassifier _classifier;

        public float LookaheadDistance { get; set; } = 5.0f;
        public float NormalCooldown { get; set; } = 4.0f;
        public float MinRepeatIntervalSameJunction { get; set; } = 12.0f;

        public float LastDecisionTime { get; private set; } = -999f;
        public string LastAnnouncedJunctionId { get; private set; } = string.Empty;
        public TurnDirection LastAnnouncedDirection { get; private set; } = TurnDirection.None;
        public GuidanceDecision CurrentDecision { get; private set; } = GuidanceDecision.None;

        public event Action<GuidanceDecision> OnGuidanceEmitted;

        public GuidanceDecisionController(
            JunctionDetector detector = null,
            DirectionClassifier classifier = null,
            float lookaheadDistance = 5.0f,
            float normalCooldown = 4.0f)
        {
            _detector = detector ?? new JunctionDetector(lookaheadDistance);
            _classifier = classifier ?? new DirectionClassifier();
            LookaheadDistance = Math.Max(1.0f, lookaheadDistance);
            NormalCooldown = Math.Max(0.5f, normalCooldown);
        }

        /// <summary>
        /// Evaluates active navigation state each tick and returns a new GuidanceDecision if triggered.
        /// </summary>
        public bool EvaluateGuidance(
            WaypointGraph graph,
            RouteResult activeRoute,
            NodePosition playerPosition,
            float currentTime,
            out GuidanceDecision decision)
        {
            decision = GuidanceDecision.None;

            if (graph == null || activeRoute == null || !activeRoute.IsValid || activeRoute.NodeCount < 2)
            {
                return false;
            }

            _detector.DefaultLookaheadDistance = LookaheadDistance;

            // 1. Check if Nathan has arrived at the destination exit node
            string exitNodeId = activeRoute.NodeIds[activeRoute.NodeCount - 1];
            if (graph.TryGetNode(exitNodeId, out var exitNode))
            {
                if (playerPosition.DistanceTo(exitNode.Position) < 2.5f)
                {
                    if (LastAnnouncedJunctionId != exitNodeId)
                    {
                        decision = GuidanceDecision.Arrived(exitNodeId, currentTime);
                        EmitDecision(decision, currentTime, exitNodeId, TurnDirection.None);
                        return true;
                    }
                }
            }

            // 2. Scan forward for the next upcoming junction on the active route
            bool approaching = _detector.IsApproachingJunction(
                graph,
                activeRoute,
                playerPosition,
                LookaheadDistance,
                out JunctionInfo nextJunction);

            if (!approaching || !nextJunction.IsValid)
            {
                // Reset junction tracking if player passed the previously announced junction
                if (!string.IsNullOrEmpty(LastAnnouncedJunctionId) && nextJunction.IsValid &&
                    !string.Equals(LastAnnouncedJunctionId, nextJunction.NodeId, StringComparison.OrdinalIgnoreCase))
                {
                    LastAnnouncedJunctionId = string.Empty;
                    LastAnnouncedDirection = TurnDirection.None;
                }
                return false;
            }

            // 3. Classify turn direction at the upcoming junction
            TurnDirection turnDir = _classifier.ClassifyRouteTurn(graph, activeRoute, nextJunction.RouteIndex, out float signedAngle);
            if (turnDir == TurnDirection.None)
            {
                return false;
            }

            // 4. Duplicate suppression filter:
            // If Nathan is still approaching the same junction with the same turn direction,
            // prevent repeating unless a long repeat interval has elapsed
            bool isSameJunction = string.Equals(LastAnnouncedJunctionId, nextJunction.NodeId, StringComparison.OrdinalIgnoreCase);
            bool isSameDirection = LastAnnouncedDirection == turnDir;

            if (isSameJunction && isSameDirection)
            {
                if (currentTime - LastDecisionTime < MinRepeatIntervalSameJunction)
                {
                    return false; // Suppress duplicate instruction
                }
            }

            // 5. Normal cooldown check
            if (currentTime - LastDecisionTime < NormalCooldown)
            {
                return false;
            }

            // 6. Generate proactive turn instruction decision
            decision = GuidanceDecision.Turn(turnDir, nextJunction.NodeId, nextJunction.DistanceAlongRoute, signedAngle, currentTime);
            EmitDecision(decision, currentTime, nextJunction.NodeId, turnDir);
            return true;
        }

        /// <summary>
        /// Informs the controller that the active route has changed (e.g. from dynamic corridor blockage).
        /// High-priority corridor-blocked events bypass standard cooldowns and issue immediate guidance.
        /// </summary>
        public bool HandleRouteChanged(
            WaypointGraph graph,
            RouteResult newRoute,
            NodePosition playerPosition,
            RouteChangeReason reason,
            float currentTime,
            out GuidanceDecision decision)
        {
            decision = GuidanceDecision.None;

            // Invalidate stale announced junction since route geometry changed
            LastAnnouncedJunctionId = string.Empty;
            LastAnnouncedDirection = TurnDirection.None;

            if (reason == RouteChangeReason.CorridorBlocked)
            {
                // Check if new route exists
                if (newRoute != null && newRoute.IsValid)
                {
                    // Find new turn direction at upcoming junction
                    if (_detector.TryFindNextJunctionOnRoute(graph, newRoute, 0, out int jIdx, out string jId, out _))
                    {
                        TurnDirection newDir = _classifier.ClassifyRouteTurn(graph, newRoute, jIdx, out float angle);
                        float dist = _detector.CalculateDistanceAlongRoute(graph, newRoute, playerPosition, jIdx);
                        decision = GuidanceDecision.Rerouted(newDir, jId, dist, angle, currentTime);
                    }
                    else
                    {
                        decision = GuidanceDecision.Blocked(string.Empty, currentTime);
                    }
                }
                else
                {
                    decision = GuidanceDecision.NoRoute(currentTime);
                }

                EmitDecision(decision, currentTime, decision.TargetJunctionId, decision.Direction);
                return true;
            }

            return false;
        }

        private void EmitDecision(GuidanceDecision decision, float time, string junctionId, TurnDirection direction)
        {
            CurrentDecision = decision;
            LastDecisionTime = time;
            LastAnnouncedJunctionId = junctionId;
            LastAnnouncedDirection = direction;
            OnGuidanceEmitted?.Invoke(decision);
        }

        /// <summary>
        /// Resets all internal cooldown and state trackers.
        /// </summary>
        public void Reset()
        {
            LastDecisionTime = -999f;
            LastAnnouncedJunctionId = string.Empty;
            LastAnnouncedDirection = TurnDirection.None;
            CurrentDecision = GuidanceDecision.None;
        }
    }
}
