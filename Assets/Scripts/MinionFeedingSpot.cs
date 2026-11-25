using UnityEngine;
using Alesa.Agents.BlindMinion;
using Alesa.Runtime.Motion;

/// <summary>
/// Holds one minion crouched over a corpse, biting, until the player gets close enough to disturb it.
/// </summary>
/// <remarks>
/// The minion it uses is not placed by hand: <see cref="MinionSpawnDirector"/> streams minions in by
/// distance and drops them at a random walkable point inside their zone, so there is nothing in the
/// scene to attach this to. Instead it waits for one to appear nearby, claims it, and parks it here.
///
/// Holding it is done by switching the brain and the motor off rather than by forcing a state. The
/// brain has no "busy" state to borrow and would re-decide every tick; with its Update not running it
/// simply holds the last intent, and the Animator is a separate component so the bite still plays.
/// Everything is restored on release, so the minion carries on as an ordinary one from then on.
/// </remarks>
[DisallowMultipleComponent]
public sealed class MinionFeedingSpot : MonoBehaviour
{
    [Header("What it is feeding on")]
    [Tooltip("The corpse. Used to face the minion at it; leave empty to use this object's own rotation.")]
    [SerializeField] private Transform body;

    [Header("Claiming")]
    [Tooltip("How far from this spot a newly spawned minion may be and still be claimed for the meal.")]
    [SerializeField, Min(1f)] private float claimRadius = 12f;

    [Tooltip("Offset from this object at which the minion kneels, in metres.")]
    [SerializeField] private Vector3 feedOffset = Vector3.zero;

    [Header("Feeding")]
    [Tooltip("Seconds between bites. The Neck_Bite clip runs 4.13 s, so a little over that loops it cleanly.")]
    [SerializeField, Min(0.5f)] private float biteInterval = 4.3f;

    [Tooltip("Player this close and it stops eating and behaves normally from then on.")]
    [SerializeField, Min(1f)] private float disturbRadius = 8f;

    [Tooltip("It also looks up if it can see the player further off than that, down the room.")]
    [SerializeField, Min(0f)] private float disturbSightRadius = 16f;

    private BlindMinionAgent _claimed;
    private AgentMotor _claimedMotor;
    private Transform _player;
    private float _nextBite;
    private bool _released;
    private float _nextScan;

    /// <summary>True while a minion is actually crouched here eating.</summary>
    public bool IsFeeding => _claimed != null && !_released;

    private Vector3 FeedPosition => transform.position + transform.TransformVector(feedOffset);

    private void Update()
    {
        if (_released) return;

        if (_claimed == null)
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + 0.5f; // the director streams minions in; no need to look every frame
            TryClaim();
            return;
        }

        // The corpse's diner may have been shot and destroyed.
        if (_claimed == null || !_claimed.gameObject.activeInHierarchy) { Release(); return; }

        if (IsDisturbed()) { Release(); return; }

        // Re-park every frame: nothing else is driving the transform now, but a ragdoll, a shove or
        // another minion's separation steering could still have nudged it off the corpse.
        _claimed.transform.SetPositionAndRotation(FeedPosition, FacingRotation());

        if (Time.time >= _nextBite)
        {
            _nextBite = Time.time + biteInterval;
            _claimed.PlayFeedingBite();
        }
    }

    private Quaternion FacingRotation()
    {
        if (body == null) return transform.rotation;
        Vector3 toBody = body.position - FeedPosition;
        toBody.y = 0f;
        return toBody.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toBody.normalized) : transform.rotation;
    }

    private void TryClaim()
    {
        var minions = FindObjectsByType<BlindMinionAgent>(FindObjectsSortMode.None);
        BlindMinionAgent best = null;
        float bestDistance = claimRadius;

        for (int i = 0; i < minions.Length; i++)
        {
            var m = minions[i];
            if (m == null || !m.isActiveAndEnabled) continue;

            // Never take one that is already busy with the player: being eaten alive elsewhere beats dressing.
            var combatant = m.GetComponent<MinionCombatant>();
            if (combatant != null && (combatant.IsDead || combatant.IsCharging)) continue;

            float distance = Vector3.Distance(m.transform.position, FeedPosition);
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = m;
        }

        if (best == null) return;

        // Do not steal one that has already noticed the player on its way over.
        if (IsDisturbed()) { _released = true; return; }

        _claimed = best;
        _claimedMotor = best.GetComponent<AgentMotor>();

        var navAgent = best.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) navAgent.ResetPath();

        best.StopLocomotionAnimation();
        best.enabled = false;                                   // stops the brain re-deciding
        if (_claimedMotor != null) _claimedMotor.enabled = false; // stops it being driven anywhere

        best.transform.SetPositionAndRotation(FeedPosition, FacingRotation());
        _nextBite = 0f; // bite on the very next frame, so it is never caught standing idle
    }

    private bool IsDisturbed()
    {
        if (!ResolvePlayer()) return false;

        float distance = Vector3.Distance(_player.position, FeedPosition);
        if (distance <= disturbRadius) return true;
        if (distance > disturbSightRadius) return false;

        // Further out it only looks up if there is actually a clear view, so a player in the next
        // room does not pull it off the corpse through a wall.
        Vector3 from = FeedPosition + Vector3.up * 1.2f;
        Vector3 to = _player.position + Vector3.up * 1.2f;
        Vector3 line = to - from;
        float length = line.magnitude;
        if (length <= 0.75f) return true;
        Vector3 stop = from + line * ((length - 0.75f) / length); // stop outside the player's own capsule
        return !Physics.Linecast(from, stop, ~0, QueryTriggerInteraction.Ignore);
    }

    private bool ResolvePlayer()
    {
        if (_player != null) return true;
        var go = GameObject.FindWithTag("Player");
        if (go != null) _player = go.transform;
        return _player != null;
    }

    /// <summary>Hands the minion back to its own brain. From here it is an ordinary minion.</summary>
    private void Release()
    {
        _released = true;
        if (_claimed != null)
        {
            _claimed.enabled = true;
            if (_claimedMotor != null) _claimedMotor.enabled = true;

            // Its brain still holds the position it was spawned at, which is no longer where it is.
            var navAgent = _claimed.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh) navAgent.Warp(_claimed.transform.position);
        }
        _claimed = null;
        _claimedMotor = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.9f);
        Gizmos.DrawWireSphere(FeedPosition, 0.4f);
        Gizmos.color = new Color(0.8f, 0.4f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(FeedPosition, claimRadius);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(FeedPosition, disturbRadius);
        if (body != null) Gizmos.DrawLine(FeedPosition, body.position);
    }
}
