using System.Collections.Generic;
using UnityEngine;
using Alesa.Agents.BlindMinion;

/// <summary>
/// Turns the player's head to face a minion that has them by the neck, and holds it there until
/// the bite is over or they shake free.
/// </summary>
/// <remarks>
/// Drives <see cref="PlayerMotor"/>'s own pitch and yaw rather than the camera transform directly.
/// The motor rebuilds the camera's local rotation from a stored pitch on every look input, so a
/// script that moved the camera itself would be undone the instant control came back — the view
/// would snap to wherever the player happened to be looking before the grab. Writing the angles
/// back through <see cref="PlayerMotor.SetLook"/> means control resumes from where the camera
/// actually is.
///
/// Movement and look are suppressed through <c>SetControlsEnabled(false)</c>, not through
/// <see cref="PlayerState"/>. That distinction matters: changing the state would make
/// <c>PlayerGameplay.BlocksControls</c> true, which makes <see cref="InputManager"/> disable the
/// whole action map — including the key that is supposed to get the player out of this.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMotor))]
public sealed class NeckBiteCameraLock : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to swing the view onto the attacker.")]
    [SerializeField, Min(0.01f)] private float blendInSeconds = 0.18f;

    [Tooltip("Seconds held on the attacker before control returns by itself. The bite clip is 4.1 s; holding that long plays as a cutscene.")]
    [SerializeField, Min(0.1f)] private float holdSeconds = 1.6f;


    [Header("Escape")]
    [Tooltip("Presses of the escape key needed to break out early. 1 = a single tap.")]
    [SerializeField, Min(1)] private int escapePresses = 1;

    [Tooltip("Shortest time the lock always lasts, so a key held down from before the grab cannot skip it outright.")]
    [SerializeField, Min(0f)] private float minimumLockSeconds = 0.25f;

    [Header("Framing")]
    [Tooltip("Vertical offset from the attacker's head bone. Negative looks slightly down its face.")]
    [SerializeField] private float headOffset = -0.05f;

    [Tooltip("Whether the player's body turns to face the attacker as well as the camera.")]
    [SerializeField] private bool turnBody = true;

    [Header("Minion discovery")]
    [SerializeField, Min(0f)] private float rescanInterval = 2f;

    [Header("Debug Status (Read-Only)")]
    [SerializeField] private bool locked;
    [SerializeField] private int escapesPressed;

    private readonly HashSet<BlindMinionAgent> _wired = new HashSet<BlindMinionAgent>();
    private PlayerMotor _motor;
    private PlayerGameplay _gameplay;
    private BlindMinionAgent _biter;
    private float _lockStartedAt;
    private float _nextScan;
    private float _startPitch, _startYaw;

    /// <summary>True while the view is being held on an attacker.</summary>
    public bool IsLocked => locked;

    private void Awake()
    {
        _motor = GetComponent<PlayerMotor>();
        _gameplay = GetComponent<PlayerGameplay>();
    }

    private void OnDisable()
    {
        if (locked) Release();
        foreach (var m in _wired) if (m != null) m.NeckBitPlayer -= OnNeckBite;
        _wired.Clear();
    }

    /// <summary>Called by <see cref="InputManager"/> when the escape key is pressed.</summary>
    public void RequestEscape()
    {
        if (!locked) return;
        if (Time.time - _lockStartedAt < minimumLockSeconds) return;
        if (++escapesPressed >= escapePresses) Release();
    }

    private void Update()
    {
        WireMinions();
        if (!locked) return;

        // A fatal bite hands the camera to PlayerDeathSequence; do not fight it for the transform.
        if (_gameplay != null && _gameplay.State.IsTerminal) { Release(); return; }
        if (_biter == null) { Release(); return; }

        float elapsed = Time.time - _lockStartedAt;
        if (elapsed >= blendInSeconds + holdSeconds) { Release(); return; }

        Vector3 target = _biter.HeadPoint + Vector3.up * headOffset;
        Vector3 eye = _motor.playerCamera != null ? _motor.playerCamera.transform.position : transform.position;
        Vector3 toTarget = target - eye;
        if (toTarget.sqrMagnitude < 1e-4f) return;

        Quaternion look = Quaternion.LookRotation(toTarget);
        float wantPitch = NormalizeAngle(look.eulerAngles.x);
        float wantYaw = look.eulerAngles.y;

        // Ease on to the attacker and stay there. Blending back to where the player was looking
        // before would swing the view off a zombie that still has hold of them, which reads as the
        // camera fighting the player rather than the zombie doing it.
        float weight = elapsed < blendInSeconds
            ? Mathf.SmoothStep(0f, 1f, elapsed / blendInSeconds)
            : 1f;

        float pitch = Mathf.LerpAngle(_startPitch, wantPitch, weight);
        float yaw = turnBody ? Mathf.LerpAngle(_startYaw, wantYaw, weight) : transform.eulerAngles.y;
        _motor.SetLook(pitch, yaw);
    }

    private void OnNeckBite(BlindMinionAgent minion)
    {
        if (minion == null || _motor == null) return;
        if (locked && _biter != null) return; // already held; the first attacker keeps the camera

        _biter = minion;
        locked = true;
        escapesPressed = 0;
        _lockStartedAt = Time.time;
        _startPitch = _motor.LookPitch;
        _startYaw = transform.eulerAngles.y;
        _motor.SetControlsEnabled(false);
    }

    private void Release()
    {
        if (!locked) return;
        locked = false;
        _biter = null;
        escapesPressed = 0;

        // Leave the stored angles matching the camera's real orientation, or the next mouse
        // movement snaps the view.
        if (_motor != null)
        {
            _motor.SetLook(_motor.LookPitch, transform.eulerAngles.y);
            _motor.SetControlsEnabled(true);
        }
    }

    private void WireMinions()
    {
        if (Time.time < _nextScan) return;
        _nextScan = Time.time + Mathf.Max(0.5f, rescanInterval);

        var minions = FindObjectsByType<BlindMinionAgent>(FindObjectsSortMode.None);
        for (int i = 0; i < minions.Length; i++)
        {
            if (_wired.Add(minions[i])) minions[i].NeckBitPlayer += OnNeckBite;
        }
        _wired.RemoveWhere(m => m == null);
    }

    private static float NormalizeAngle(float degrees) => degrees > 180f ? degrees - 360f : degrees;
}
