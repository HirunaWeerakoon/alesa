using UnityEngine;

namespace Alesa.Runtime
{
    /// <summary>
    /// Alesa screams when the boss appears close to her, but not every time: there is only a chance she does,
    /// a long pause before she can again, and a cap for the whole playthrough. A scream that always happens
    /// stops being frightening.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AlesaBossReaction : MonoBehaviour
    {
        [Tooltip("The scream. Left empty, she does not react.")]
        public AudioClip scream;
        [Tooltip("Left empty, found on this object.")]
        public AlesaVoiceSpeaker speaker;

        [Tooltip("The boss must appear within this distance (m) of her.")]
        [Min(1f)] public float hearingDistance = 30f;
        [Range(0f, 1f)] public float chance = 0.75f;
        [Tooltip("Seconds before she can scream again.")]
        [Min(0f)] public float cooldownSeconds = 20f;
        [Tooltip("Most screams in one playthrough.")]
        [Min(0)] public int maxScreams = 3;

        private PossessedBoss _boss;
        private bool _wasPresent;
        private float _lastScream = float.NegativeInfinity;
        private int _screams;
        private float _nextLookup;

        private void Awake()
        {
            if (speaker == null) speaker = GetComponent<AlesaVoiceSpeaker>();
        }

        private void Update()
        {
            if (_boss == null)
            {
                if (Time.time < _nextLookup) return;
                _nextLookup = Time.time + 1f;
                _boss = FindFirstObjectByType<PossessedBoss>();
                if (_boss == null) return;
            }

            bool present = _boss.IsPresent || _boss.currentState == PossessedBoss.BossState.Appearing;
            bool appeared = present && !_wasPresent;
            _wasPresent = present;
            if (!appeared || speaker == null || scream == null) return;

            if (_screams >= maxScreams || Time.time - _lastScream < cooldownSeconds) return;
            if (Vector3.Distance(_boss.transform.position, transform.position) > hearingDistance) return;
            if (Random.value >= chance) return;

            if (speaker.PlayReaction(scream))
            {
                _screams++;
                _lastScream = Time.time;
            }
        }
    }
}
