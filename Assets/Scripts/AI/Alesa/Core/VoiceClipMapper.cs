using System;

namespace Alesa.Core
{
    public enum VoiceCueId
    {
        None = 0,
        TurnLeft = 1,
        TurnRight = 2,
        ContinueStraight = 3,
        UTurn = 4,
        RouteBlocked = 5,
        RouteRerouted = 6,
        NoRouteAvailable = 7,
        DestinationReached = 8
    }

    /// <summary>
    /// Pure C# mapper and priority arbiter for companion voice navigation lines.
    /// Maps structured GuidanceDecision payloads to discrete voice cues,
    /// arbitrates audio interruption priority, and provides fallback subtitle lines.
    /// </summary>
    public static class VoiceClipMapper
    {
        /// <summary>
        /// Maps a high-level guidance decision to its corresponding voice cue ID.
        /// </summary>
        public static VoiceCueId MapDecisionToCue(in GuidanceDecision decision)
        {
            return decision.Type switch
            {
                GuidanceType.TurnInstruction => decision.Direction switch
                {
                    TurnDirection.Left => VoiceCueId.TurnLeft,
                    TurnDirection.Right => VoiceCueId.TurnRight,
                    TurnDirection.Straight => VoiceCueId.ContinueStraight,
                    TurnDirection.UTurn => VoiceCueId.UTurn,
                    _ => VoiceCueId.None
                },
                GuidanceType.RouteBlocked => VoiceCueId.RouteBlocked,
                GuidanceType.RouteRerouted => VoiceCueId.RouteRerouted,
                GuidanceType.NoRouteAvailable => VoiceCueId.NoRouteAvailable,
                GuidanceType.DestinationReached => VoiceCueId.DestinationReached,
                _ => VoiceCueId.None
            };
        }

        /// <summary>
        /// Checks whether an incoming voice cue has sufficient priority to interrupt an ongoing voice line.
        /// High-priority danger/rerouting cues immediately interrupt normal navigation instructions.
        /// </summary>
        public static bool CanInterrupt(VoiceCueId activeCue, VoiceCueId incomingCue)
        {
            if (activeCue == VoiceCueId.None) return true;

            bool incomingIsCritical = incomingCue == VoiceCueId.RouteBlocked ||
                                     incomingCue == VoiceCueId.RouteRerouted ||
                                     incomingCue == VoiceCueId.NoRouteAvailable;

            bool activeIsCritical = activeCue == VoiceCueId.RouteBlocked ||
                                   activeCue == VoiceCueId.RouteRerouted ||
                                   activeCue == VoiceCueId.NoRouteAvailable;

            // Critical warnings interrupt ordinary instructions
            if (incomingIsCritical && !activeIsCritical)
            {
                return true;
            }

            // Otherwise, let active speech finish to prevent audio overlapping
            return false;
        }

        /// <summary>
        /// Returns the standard spoken dialogue line / subtitle string for a voice cue.
        /// </summary>
        public static string GetSubtitleText(VoiceCueId cue)
        {
            return cue switch
            {
                VoiceCueId.TurnLeft => "Nathan, turn left!",
                VoiceCueId.TurnRight => "Take the right corridor!",
                VoiceCueId.ContinueStraight => "Keep going straight!",
                VoiceCueId.UTurn => "Turn around! Wrong way!",
                VoiceCueId.RouteBlocked => "That way is blocked! Take another route!",
                VoiceCueId.RouteRerouted => "Path blocked! Rerouting, follow me this way!",
                VoiceCueId.NoRouteAvailable => "No safe route available! We're trapped!",
                VoiceCueId.DestinationReached => "We reached the exit! Keep moving!",
                _ => string.Empty
            };
        }
    }
}
