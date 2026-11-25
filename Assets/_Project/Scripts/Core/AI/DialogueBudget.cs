using System;
using System.Collections.Generic;

namespace Alesa.Core
{
    /// <summary>
    /// Decides whether a companion line may be spoken, so a character with many lines does not talk constantly.
    ///
    /// Three kinds of line are handled differently:
    ///   • Guidance (which way to turn): rare. Spoken only some of the time, with a long gap between any two
    ///     lines, a cap per line, and a cap for the whole playthrough.
    ///   • Critical (the way is blocked, rerouting, trapped): important enough to cut through the gap, but still
    ///     capped per line, and counted toward a slightly higher overall cap.
    ///   • Always (the rescue, the exit): never refused; <see cref="Register"/> just records that they happened.
    ///
    /// Pure C#, no engine types. Cues are plain ints so the caller can use whatever enum it has.
    /// </summary>
    public sealed class DialogueBudget
    {
        private readonly Dictionary<int, int> _spokenPerCue = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _lastSpokenAt = new Dictionary<int, float>();
        private readonly Random _random;
        private float _lastAnyAt = float.NegativeInfinity;

        /// <summary>Guidance lines allowed in one playthrough.</summary>
        public int MaxGuidanceLines { get; set; } = 5;

        /// <summary>Extra lines allowed beyond that for critical ones.</summary>
        public int CriticalAllowance { get; set; } = 2;

        /// <summary>Seconds that must pass after any line before a guidance line may be spoken.</summary>
        public float MinGapSeconds { get; set; } = 22f;

        /// <summary>Shortest gap after any line before a critical one, so two never overlap.</summary>
        public float CriticalGapSeconds { get; set; } = 6f;

        /// <summary>Seconds before the same line may be spoken again.</summary>
        public float RepeatGapSeconds { get; set; } = 90f;

        /// <summary>Most times one line may be spoken in a playthrough.</summary>
        public int MaxPerCue { get; set; } = 2;

        /// <summary>Chance (0 to 1) a guidance line that passes every other check is actually spoken.</summary>
        public float GuidanceChance { get; set; } = 0.6f;

        /// <summary>Guidance and critical lines spoken so far (always-allowed lines are not counted).</summary>
        public int LinesSpoken { get; private set; }

        public DialogueBudget(int seed = 12345) => _random = new Random(seed);

        /// <summary>True when the line may be spoken now. When it returns true it has been counted.</summary>
        public bool TryAllow(int cue, bool critical, float now)
        {
            if (_spokenPerCue.TryGetValue(cue, out int times) && times >= MaxPerCue) return false;
            if (_lastSpokenAt.TryGetValue(cue, out float last) && now - last < RepeatGapSeconds) return false;

            if (critical)
            {
                if (LinesSpoken >= MaxGuidanceLines + CriticalAllowance) return false;
                if (now - _lastAnyAt < CriticalGapSeconds) return false;
            }
            else
            {
                if (LinesSpoken >= MaxGuidanceLines) return false;
                if (now - _lastAnyAt < MinGapSeconds) return false;
                if (_random.NextDouble() >= GuidanceChance) return false;
            }

            LinesSpoken++;
            Record(cue, now);
            return true;
        }

        /// <summary>Records a line that is always allowed (the rescue, the exit). It does not use up the budget.</summary>
        public void Register(int cue, float now) => Record(cue, now);

        public void Reset()
        {
            _spokenPerCue.Clear();
            _lastSpokenAt.Clear();
            _lastAnyAt = float.NegativeInfinity;
            LinesSpoken = 0;
        }

        private void Record(int cue, float now)
        {
            _spokenPerCue[cue] = _spokenPerCue.TryGetValue(cue, out int n) ? n + 1 : 1;
            _lastSpokenAt[cue] = now;
            _lastAnyAt = now;
        }
    }
}
