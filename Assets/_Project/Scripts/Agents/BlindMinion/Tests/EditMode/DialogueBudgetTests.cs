using Alesa.Core;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    /// <summary>Limits on how much a companion talks. The budget lives in Alesa.Core; these tests ride along here.</summary>
    public class DialogueBudgetTests
    {
        private static DialogueBudget Always() => new DialogueBudget { GuidanceChance = 1f };

        [Test]
        public void Guidance_RespectsTheMinimumGapBetweenLines()
        {
            var budget = Always();
            Assert.IsTrue(budget.TryAllow(1, false, 100f));
            Assert.IsFalse(budget.TryAllow(2, false, 110f), "10 s after a line is too soon");
            Assert.IsTrue(budget.TryAllow(2, false, 125f), "past the gap it may speak");
        }

        [Test]
        public void Guidance_StopsAtTheTotalCap()
        {
            var budget = Always();
            budget.MaxGuidanceLines = 3;
            budget.MaxPerCue = 99;
            budget.RepeatGapSeconds = 0f;
            int spoken = 0;
            for (int i = 0; i < 20; i++)
                if (budget.TryAllow(i % 4, false, 100f + i * 60f)) spoken++;
            Assert.AreEqual(3, spoken, "only three guidance lines in a whole playthrough");
        }

        [Test]
        public void TheSameLine_NeedsARepeatGap_AndIsCappedPerLine()
        {
            var budget = Always();
            budget.RepeatGapSeconds = 90f;
            budget.MaxPerCue = 2;
            Assert.IsTrue(budget.TryAllow(7, false, 100f));
            Assert.IsFalse(budget.TryAllow(7, false, 150f), "same line within 90 s");
            Assert.IsTrue(budget.TryAllow(7, false, 200f));
            Assert.IsFalse(budget.TryAllow(7, false, 400f), "third time exceeds the per-line cap");
        }

        [Test]
        public void Critical_CutsThroughTheGuidanceGap_ButNotTheRepeatRule()
        {
            var budget = Always();
            Assert.IsTrue(budget.TryAllow(1, false, 100f));
            Assert.IsTrue(budget.TryAllow(5, true, 108f), "a blocked-route warning may follow a line quickly");
            Assert.IsFalse(budget.TryAllow(5, true, 130f), "but not repeat itself straight away");
            Assert.IsFalse(budget.TryAllow(6, true, 110f), "and two critical lines may not overlap");
        }

        [Test]
        public void Critical_HasAHigherCapThanGuidance_ButStillACap()
        {
            var budget = Always();
            budget.MaxGuidanceLines = 1;
            budget.CriticalAllowance = 1;
            budget.MaxPerCue = 99;
            budget.RepeatGapSeconds = 0f;
            Assert.IsTrue(budget.TryAllow(1, false, 100f));
            Assert.IsTrue(budget.TryAllow(5, true, 200f));
            Assert.IsFalse(budget.TryAllow(6, true, 300f), "one over the combined cap");
            Assert.IsFalse(budget.TryAllow(2, false, 400f), "guidance is already used up");
        }

        [Test]
        public void GuidanceChance_ZeroNeverSpeaks_OneAlwaysDoes()
        {
            var never = new DialogueBudget { GuidanceChance = 0f };
            for (int i = 0; i < 20; i++) Assert.IsFalse(never.TryAllow(i, false, 100f + i * 100f));

            var always = Always();
            Assert.IsTrue(always.TryAllow(1, false, 100f));
        }

        [Test]
        public void AlwaysAllowedLines_AreRecorded_WithoutUsingTheBudget()
        {
            var budget = Always();
            budget.MaxGuidanceLines = 1;
            budget.Register(100, 50f); // the rescue
            Assert.AreEqual(0, budget.LinesSpoken);
            Assert.IsFalse(budget.TryAllow(1, false, 60f), "the rescue line still counts as just-spoken for the gap");
            Assert.IsTrue(budget.TryAllow(1, false, 80f));
        }

        [Test]
        public void Reset_StartsAFreshPlaythrough()
        {
            var budget = Always();
            budget.MaxGuidanceLines = 1;
            Assert.IsTrue(budget.TryAllow(1, false, 100f));
            Assert.IsFalse(budget.TryAllow(2, false, 500f));
            budget.Reset();
            Assert.IsTrue(budget.TryAllow(2, false, 600f));
            Assert.AreEqual(1, budget.LinesSpoken);
        }
    }
}
