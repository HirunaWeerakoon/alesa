using System.Collections;
using UnityEngine;
using Alesa.Runtime;

/// <summary>
/// Drives Alesa's animator: she matches Nathan's pace, and when he changes direction she pulls up,
/// turns on the spot and sets off again rather than sliding round the corner.
/// </summary>
/// <remarks>
/// The three beats are a scripted sequence, not a blend, so they are stepped through here with
/// animator triggers while <see cref="AlesaFollower.AnimationHold"/> keeps her still for the first
/// two. Driving it from the follower's own steering instead would mean the stop and the turn clips
/// play while she is still travelling, which is exactly the skating this replaces.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(AlesaFollower))]
public sealed class AlesaAnimationDirector : MonoBehaviour
{
    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly int JogParam = Animator.StringToHash("Jog");
    private static readonly int MovingParam = Animator.StringToHash("Moving");
    // One trigger per destination: a shared trigger narrowed by the Jog bool is swallowed by
    // whichever Any State transition names it first, so the jogging variants never fired.
    private static readonly int StopWalkParam = Animator.StringToHash("DoStopWalk");
    private static readonly int StopRunParam = Animator.StringToHash("DoStopRun");
    private static readonly int TurnLeftParam = Animator.StringToHash("DoTurnLeft");
    private static readonly int TurnRightParam = Animator.StringToHash("DoTurnRight");
    private static readonly int TurnJogParam = Animator.StringToHash("DoTurnJog");
    private static readonly int StartWalkParam = Animator.StringToHash("DoStartWalk");
    private static readonly int StartJogParam = Animator.StringToHash("DoStartJog");

    [Header("References (found automatically when left empty)")]
    [SerializeField] private Animator animator;
    [SerializeField] private Transform player;
    [SerializeField] private PlayerMotor playerMotor;

    [Header("When a turn is triggered")]
    [Tooltip("Degrees the player's heading must swing before she bothers stopping and turning.")]
    [SerializeField, Range(20f, 170f)] private float turnThreshold = 55f;

    [Tooltip("The player must actually be travelling this fast for a heading change to count (m/s). " +
             "Without it, a stationary player glancing around would set her turning on the spot.")]
    [SerializeField, Min(0.1f)] private float headingSpeedFloor = 1.2f;

    [Tooltip("Seconds before she will perform another stop-turn-start. Stops it firing every corner " +
             "of a weaving corridor.")]
    [SerializeField, Min(0f)] private float turnCooldown = 2.5f;

    [Header("Sequence timing (clip lengths)")]
    [Tooltip("Female Stop Walking is 1.53 s, Run To Stop 0.90 s. Held slightly shorter so the next beat overlaps.")]
    [SerializeField, Min(0.1f)] private float stopWalkSeconds = 1.1f;
    [SerializeField, Min(0.1f)] private float stopRunSeconds = 0.7f;

    [Tooltip("Left Turn and Right Turn are 1.17 s; Jog In Circle is longer and is cut short.")]
    [SerializeField, Min(0.1f)] private float turnSeconds = 0.95f;

    [Header("Pace")]
    [Tooltip("Player speed above which she jogs instead of walking. Nathan walks at 5 and sprints at 8.5.")]
    [SerializeField, Min(0.1f)] private float jogThreshold = 6.0f;

    [Tooltip("She is 'moving' above this speed, which is what takes her out of the idle look-around.")]
    [SerializeField, Min(0.01f)] private float movingThreshold = 0.25f;

    private AlesaFollower _follower;
    private Vector3 _lastHeading = Vector3.forward;
    private Vector3 _previousPlayerPosition;
    private float _nextTurnAllowedAt;
    private Coroutine _sequence;

    /// <summary>True while the stop-turn-start sequence is running.</summary>
    public bool IsTurning => _sequence != null;

    private void Awake()
    {
        _follower = GetComponent<AlesaFollower>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        ResolvePlayer();
        if (player != null)
        {
            _previousPlayerPosition = player.position;
            Vector3 f = player.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) _lastHeading = f.normalized;
        }
    }

    private void ResolvePlayer()
    {
        if (player == null)
        {
            if (_follower != null && _follower.playerTransform != null) player = _follower.playerTransform;
            else
            {
                var go = GameObject.FindWithTag("Player");
                if (go != null) player = go.transform;
            }
        }
        if (playerMotor == null && player != null) playerMotor = player.GetComponent<PlayerMotor>();
    }

    private void Update()
    {
        if (animator == null) return;
        ResolvePlayer();
        if (player == null) return;

        Vector3 travel = (player.position - _previousPlayerPosition) / Mathf.Max(0.0001f, Time.deltaTime);
        _previousPlayerPosition = player.position;
        travel.y = 0f;
        float playerSpeed = travel.magnitude;

        // Pace first: she walks when he walks and jogs when he runs, whether or not she is turning.
        bool jogging = playerSpeed >= jogThreshold
                       || (playerMotor != null && playerMotor.IsSprinting);
        animator.SetBool(JogParam, jogging);

        float ownSpeed = _follower != null ? _follower.CurrentTargetSpeed : 0f;
        animator.SetFloat(SpeedParam, ownSpeed);
        animator.SetBool(MovingParam, !IsTurning && ownSpeed > movingThreshold);

        if (IsTurning) return;

        // A heading is only meaningful while he is actually going somewhere.
        if (playerSpeed < headingSpeedFloor) return;
        Vector3 heading = travel / playerSpeed;

        float swing = Vector3.SignedAngle(_lastHeading, heading, Vector3.up);
        if (Mathf.Abs(swing) < turnThreshold) { _lastHeading = heading; return; }

        _lastHeading = heading;
        if (Time.time < _nextTurnAllowedAt) return;
        if (_follower != null && _follower.IsHoldingPosition) return;

        _nextTurnAllowedAt = Time.time + turnCooldown;
        _sequence = StartCoroutine(StopTurnStart(swing > 0f, jogging, heading));
    }

    /// <summary>Pull up, turn to the new heading, set off again.</summary>
    private IEnumerator StopTurnStart(bool turnRight, bool jogging, Vector3 newHeading)
    {
        if (_follower != null) _follower.AnimationHold = true;

        animator.SetBool(MovingParam, false);
        animator.SetTrigger(jogging ? StopRunParam : StopWalkParam);
        yield return new WaitForSeconds(jogging ? stopRunSeconds : stopWalkSeconds);

        // At a jog the turn banks round on Jog In Circle; standing, it is a sharp left or right.
        animator.SetTrigger(jogging ? TurnJogParam : (turnRight ? TurnRightParam : TurnLeftParam));

        // Rotate the body across the turn clip, so the animation and the facing agree at the end.
        Quaternion from = transform.rotation;
        Quaternion to = newHeading.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(newHeading)
            : transform.rotation;
        for (float t = 0f; t < turnSeconds; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, to, Mathf.Clamp01(t / turnSeconds));
            yield return null;
        }
        transform.rotation = to;

        animator.SetTrigger(jogging ? StartJogParam : StartWalkParam);
        animator.SetBool(MovingParam, true);
        if (_follower != null) _follower.AnimationHold = false;

        _sequence = null;
    }

    private void OnDisable()
    {
        // Never leave her frozen because the component was switched off mid-sequence.
        if (_follower != null) _follower.AnimationHold = false;
        _sequence = null;
    }
}
