using System.Numerics;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using NUnit.Framework;

namespace Alesa.Agents.BlindMinion.Tests
{
    /// <summary>
    /// Lunge and frenzy: a hunter springs at a player just out of swing reach, and two hits in a row
    /// speed its attacks up. Both are opt-in on the brain.
    ///
    /// The minion stands at the origin and the player is moved around it; nothing here needs a graph
    /// route because a hunter that feels the player chases their live position.
    /// </summary>
    public class BlindMinionCombatVarietyTests
    {
        private sealed class FakeWorld : IWorldQuery
        {
            public float Time { get; set; }
            public Vector3 PlayerPosition { get; set; } = new Vector3(100, 0, 100);
            public Vector3 PlayerVelocity { get; set; }
            public Vector3 PlayerForward { get; set; } = Vector3.UnitZ;
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
            public float LightLevelAt(Vector3 position) => 0f;
        }

        private FakeWorld _world;
        private AIContext _ctx;

        [SetUp]
        public void Build()
        {
            var graph = new MazeGraph();
            graph.AddNode(new Vector3(0, 0, 0), NodeTag.DeadEnd, "S");
            _world = new FakeWorld();
            _ctx = new AIContext(graph, _world, new NoiseBus(), new SharedBlackboard());
        }

        private BlindMinionBrain Minion(bool lunge = true, bool frenzy = true) =>
            new BlindMinionBrain(_ctx)
            {
                CurrentPosition = Vector3.Zero,
                LungeEnabled = lunge,
                FrenzyEnabled = frenzy,
            };

        private void PlayerAt(float x) => _world.PlayerPosition = new Vector3(x, 0, 0);

        // ------------------------------------------------------------------ lunge

        [Test]
        public void Lunge_PlayerJustOutOfSwingReach_BurstsAtLungeSpeed()
        {
            var minion = Minion();
            int lunges = 0;
            minion.Lunged += () => lunges++;
            PlayerAt(2.5f);

            minion.Tick(0.1f);

            Assert.AreEqual(MinionState.Hunting, minion.State);
            Assert.IsTrue(minion.IsLunging);
            Assert.AreEqual(1, lunges);
            Assert.AreEqual(minion.LungeSpeed, minion.Intent.Speed, 1e-3f);
        }

        [Test]
        public void Lunge_PlayerBeyondLungeReach_JustRuns()
        {
            var minion = Minion();
            PlayerAt(4.5f);

            minion.Tick(0.1f);

            Assert.AreEqual(MinionState.Hunting, minion.State);
            Assert.IsFalse(minion.IsLunging);
            Assert.AreEqual(minion.HuntSpeed, minion.Intent.Speed, 1e-3f);
        }

        [Test]
        public void Lunge_Disabled_NeverLunges()
        {
            var minion = Minion(lunge: false);
            PlayerAt(2.5f);

            minion.Tick(0.1f);

            Assert.IsFalse(minion.IsLunging);
            Assert.AreEqual(minion.HuntSpeed, minion.Intent.Speed, 1e-3f);
        }

        [Test]
        public void Lunge_EndsAfterLungeSeconds_AndWaitsForCooldown()
        {
            var minion = Minion();
            int lunges = 0;
            minion.Lunged += () => lunges++;
            PlayerAt(2.5f);

            minion.Tick(0.1f);
            Assert.AreEqual(1, lunges);

            minion.Tick(minion.LungeSeconds + 0.1f);
            Assert.IsFalse(minion.IsLunging, "the burst is short");
            Assert.AreEqual(minion.HuntSpeed, minion.Intent.Speed, 1e-3f);
            Assert.AreEqual(1, lunges, "no second lunge during the cooldown");

            minion.Tick(minion.LungeCooldown + 0.1f);
            Assert.AreEqual(2, lunges, "it lunges again once the cooldown has passed");
        }

        [Test]
        public void Lunge_PlayerBackingAwayFastAtTheEdge_IsNotWorthIt()
        {
            var minion = Minion();
            PlayerAt(3.1f);
            _world.PlayerVelocity = new Vector3(6f, 0, 0); // running away

            minion.Tick(0.1f);

            Assert.IsFalse(minion.IsLunging, "it would not catch them; it keeps running instead");
            Assert.AreEqual(minion.HuntSpeed, minion.Intent.Speed, 1e-3f);
        }

        [Test]
        public void Lunge_DoesNotDelayTheFirstSwing()
        {
            var minion = Minion();
            int swings = 0;
            minion.Attacked += () => swings++;

            PlayerAt(2.5f);
            minion.Tick(0.1f); // lunges
            PlayerAt(1.0f);
            minion.Tick(0.1f); // lands in reach

            Assert.AreEqual(MinionState.Attacking, minion.State);
            Assert.AreEqual(1, swings, "the lunge is not a swing, so the first blow is not on cooldown");
        }

        // ------------------------------------------------------------------ frenzy

        private int SwingsOver(BlindMinionBrain minion, float seconds)
        {
            int swings = 0;
            minion.Attacked += () => swings++;
            PlayerAt(1.0f); // in contact
            for (float t = 0f; t < seconds; t += 0.1f) minion.Tick(0.1f);
            return swings;
        }

        [Test]
        public void Frenzy_TwoHitsInARow_AttackMoreOften()
        {
            var calm = Minion();
            int calmSwings = SwingsOver(calm, 6f);

            var frenzied = Minion();
            frenzied.RegisterHit();
            frenzied.RegisterHit();
            Assert.IsTrue(frenzied.IsFrenzied);
            int frenziedSwings = SwingsOver(frenzied, 6f);

            Assert.Greater(frenziedSwings, calmSwings, "a frenzy shortens the time between blows");
        }

        [Test]
        public void Frenzy_AMissBreaksTheStreak()
        {
            var minion = Minion();

            minion.RegisterHit();
            minion.RegisterMiss();
            minion.RegisterHit();
            Assert.IsFalse(minion.IsFrenzied, "hit, miss, hit is not two in a row");

            minion.RegisterHit();
            Assert.IsTrue(minion.IsFrenzied, "two in a row is");
        }

        [Test]
        public void Frenzy_WearsOffAfterItsDuration()
        {
            var minion = Minion();
            minion.FrenzyDuration = 3f;
            PlayerAt(50f); // far away, so nothing else happens
            minion.RegisterHit();
            minion.RegisterHit();
            Assert.IsTrue(minion.IsFrenzied);

            minion.Tick(3.5f);
            Assert.IsFalse(minion.IsFrenzied);
        }

        [Test]
        public void Frenzy_Disabled_IgnoresHits()
        {
            var minion = Minion(frenzy: false);

            minion.RegisterHit();
            minion.RegisterHit();

            Assert.IsFalse(minion.IsFrenzied);
        }

        // ------------------------------------------------------------------ arbiter

        [Test]
        public void Arbiter_HitProbability_FallsAcrossTheLungeBand_ButStaysWorthTaking()
        {
            var arbiter = new CombatDecisionArbiter { MeleeStrikeReach = 1.8f, LungeTriggerReach = 3.2f };

            float nearEdge = arbiter.CalculateHitProbability(1.9f, 0f, AttackIntentType.LeapingLunge);
            float farEdge = arbiter.CalculateHitProbability(3.2f, 0f, AttackIntentType.LeapingLunge);

            Assert.Greater(nearEdge, farEdge, "closer is a better bet");
            Assert.GreaterOrEqual(farEdge, 0.25f, "even at the far edge a lunge at a standing player is worth it");
        }

        [Test]
        public void Arbiter_StrikeAtTheEdgeOfReach_IsStillLikely()
        {
            var arbiter = new CombatDecisionArbiter { MeleeStrikeReach = 1.8f };

            Assert.Greater(arbiter.CalculateHitProbability(1.8f, 0f, AttackIntentType.StandardMeleeStrike), 0.5f);
        }
    }
}
