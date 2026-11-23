using System;
using System.Numerics;

namespace Alesa.Agents.BlindMinion
{
    public enum AttackIntentType
    {
        None,
        StandardMeleeStrike,
        LeapingLunge,
        FrenzyFlurry
    }

    /// <summary>
    /// Result payload from combat decision arbitration.
    /// </summary>
    public readonly struct AttackDecision
    {
        public readonly bool ShouldAttack;
        public readonly AttackIntentType IntentType;
        public readonly float EstimatedHitProbability;
        public readonly float DesiredAttackCadence;

        public AttackDecision(bool shouldAttack, AttackIntentType intentType, float estimatedHitProbability, float desiredAttackCadence)
        {
            ShouldAttack = shouldAttack;
            IntentType = intentType;
            EstimatedHitProbability = estimatedHitProbability;
            DesiredAttackCadence = desiredAttackCadence;
        }

        public static AttackDecision Standby => new AttackDecision(false, AttackIntentType.None, 0f, 1f);
    }

    /// <summary>
    /// Pure algorithmic decision arbiter for Blind Minion combat engagements.
    /// Evaluates strike opportunity, lunge initiation, frenzy escalation, and hit probability
    /// entirely in code without coupling to animation rigs or audio players.
    /// </summary>
    public sealed class CombatDecisionArbiter
    {
        public float MeleeStrikeReach { get; set; } = 1.8f;
        public float LungeTriggerReach { get; set; } = 3.2f;
        public float BaseAttackCooldown { get; set; } = 1.5f;
        public float FrenzyCooldownMultiplier { get; set; } = 0.6f;
        public float FrenzyDuration { get; set; } = 5.0f;

        private float _lastAttackTime = float.NegativeInfinity;
        private float _frenzyActivatedAt = float.NegativeInfinity;
        private int _consecutiveHits = 0;

        public bool IsFrenzied(float currentTime) => (currentTime - _frenzyActivatedAt) < FrenzyDuration;

        /// <summary>
        /// Evaluates whether the minion should initiate a melee attack, lunge, or frenzy flurry.
        /// </summary>
        public AttackDecision EvaluateAttack(
            Vector3 minionPosition,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float currentTime)
        {
            float distance = FlatMath.Distance(minionPosition, targetPosition);
            bool frenzied = IsFrenzied(currentTime);
            float effectiveCooldown = frenzied ? BaseAttackCooldown * FrenzyCooldownMultiplier : BaseAttackCooldown;

            if (currentTime - _lastAttackTime < effectiveCooldown)
            {
                return AttackDecision.Standby;
            }

            // Target beyond maximum attack engagement envelope
            if (distance > LungeTriggerReach)
            {
                return AttackDecision.Standby;
            }

            // Relative approach velocity: positive when target is moving towards minion
            Vector3 toTarget = targetPosition - minionPosition;
            Vector3 dirToTarget = toTarget != Vector3.Zero ? Vector3.Normalize(toTarget) : Vector3.Zero;
            float closingSpeed = -Vector3.Dot(targetVelocity, dirToTarget);

            // Determine attack type and hit probability
            AttackIntentType attackType;
            if (frenzied && distance <= MeleeStrikeReach * 1.2f)
            {
                attackType = AttackIntentType.FrenzyFlurry;
            }
            else if (distance <= MeleeStrikeReach)
            {
                attackType = AttackIntentType.StandardMeleeStrike;
            }
            else
            {
                // In outer reach: evaluate lunge if target is moving away or closing fast
                attackType = AttackIntentType.LeapingLunge;
            }

            float hitProb = CalculateHitProbability(distance, closingSpeed, attackType);

            // Minimum 25% hit confidence required to commit to strike
            if (hitProb < 0.25f)
            {
                return AttackDecision.Standby;
            }

            _lastAttackTime = currentTime;
            return new AttackDecision(true, attackType, hitProb, effectiveCooldown);
        }

        /// <summary>
        /// Calculates hit-success probability scoring based on spatial distance and relative target kinematics.
        /// </summary>
        public float CalculateHitProbability(float distance, float closingSpeed, AttackIntentType type)
        {
            float reach = (type == AttackIntentType.LeapingLunge) ? LungeTriggerReach : MeleeStrikeReach;
            float proximityFactor = Math.Max(0f, 1.0f - (distance / Math.Max(0.5f, reach)));

            // Fast closing targets are easier to hit; fast retreating targets reduce hit probability
            float velocityFactor = Math.Min(1.2f, Math.Max(0.5f, 1.0f + 0.1f * closingSpeed));

            float typeBonus = (type == AttackIntentType.FrenzyFlurry) ? 1.25f : 1.0f;
            float rawProb = proximityFactor * velocityFactor * typeBonus;

            return Math.Min(1.0f, Math.Max(0.0f, rawProb));
        }

        public void RegisterHit(float currentTime)
        {
            _consecutiveHits++;
            if (_consecutiveHits >= 2)
            {
                _frenzyActivatedAt = currentTime;
            }
        }

        public void RegisterMiss()
        {
            _consecutiveHits = Math.Max(0, _consecutiveHits - 1);
        }

        public void Reset()
        {
            _lastAttackTime = float.NegativeInfinity;
            _frenzyActivatedAt = float.NegativeInfinity;
            _consecutiveHits = 0;
        }
    }
}
