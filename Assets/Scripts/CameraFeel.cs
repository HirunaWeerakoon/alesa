using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// The small movements that make a first-person view feel like a body: a walking bob, a slow breath, a kick
/// when the revolver fires, a shake when Nathan is hurt, a wider view while sprinting, and a soft blur of the
/// background while aiming.
/// </summary>
/// <remarks>
/// Sits on a pivot between the player and the camera, and only moves that pivot. PlayerMotor owns the camera's
/// own height and pitch and rewrites them constantly, so anything added to the camera directly would be undone.
/// Only position and field of view are touched, never rotation, so aiming and the interaction ray stay true.
/// </remarks>
[DisallowMultipleComponent]
public sealed class CameraFeel : MonoBehaviour
{
    [Header("Walking")]
    [Tooltip("How far the view dips with each step (m).")]
    [UnityEngine.Min(0f)] public float bobHeight = 0.035f;
    [Tooltip("Side-to-side sway (m).")]
    [UnityEngine.Min(0f)] public float bobSway = 0.02f;
    [Tooltip("Full bob cycles per metre walked.")]
    [UnityEngine.Min(0.05f)] public float cyclesPerMetre = 0.45f;
    [Range(1f, 2.5f)] public float sprintBobBoost = 1.5f;
    [Range(0f, 1f)] public float crouchBobScale = 0.5f;
    [Range(0f, 1f)] public float aimBobScale = 0.3f;

    [Header("Breathing")]
    [UnityEngine.Min(0f)] public float breathHeight = 0.004f;
    [UnityEngine.Min(0.05f)] public float breathHertz = 0.22f;

    [Header("Revolver kick")]
    [UnityEngine.Min(0f)] public float kickBack = 0.05f;
    [UnityEngine.Min(0f)] public float kickDown = 0.02f;
    [UnityEngine.Min(0f)] public float kickFov = 3f;
    [UnityEngine.Min(1f)] public float kickRecovery = 14f;

    [Header("Getting hurt")]
    [UnityEngine.Min(0f)] public float hurtShake = 0.06f;
    [UnityEngine.Min(0.05f)] public float hurtShakeSeconds = 0.4f;

    [Header("Field of view")]
    [Tooltip("Extra degrees of view while sprinting.")]
    [UnityEngine.Min(0f)] public float sprintFov = 6f;
    [UnityEngine.Min(1f)] public float fovResponse = 8f;

    [Header("Aim blur")]
    public bool aimBlur = true;
    [Tooltip("Smaller = a stronger blur of what is not in focus.")]
    [Range(1.4f, 22f)] public float aperture = 5.6f;
    [Range(10f, 300f)] public float focalLength = 60f;

    private Camera _camera;
    private CharacterController _controller;
    private PlayerMotor _motor;
    private PlayerGameplay _gameplay;
    private PlayerVitals _vitals;
    private DepthOfField _dof;
    private float _baseFov;
    private float _phase, _amplitude, _kick, _shake;
    private float _lastHealth = 1f, _focus = 8f;
    private bool _subscribed;
    private float _nextLookup;

    private void Awake()
    {
        _camera = GetComponentInChildren<Camera>();
        _controller = GetComponentInParent<CharacterController>();
        _motor = GetComponentInParent<PlayerMotor>();
        _gameplay = GetComponentInParent<PlayerGameplay>();
        if (_camera != null)
        {
            _baseFov = _camera.fieldOfView;
            var volume = _camera.GetComponent<PostProcessVolume>();
            if (volume != null) volume.profile.TryGetSettings(out _dof);
        }
    }

    private void OnEnable() => RevolverCombat.ShotFired += OnShot;

    private void OnDisable()
    {
        RevolverCombat.ShotFired -= OnShot;
        if (_vitals != null && _subscribed) _vitals.HealthChanged -= OnHealthChanged;
        _subscribed = false;
        transform.localPosition = Vector3.zero;
        if (_camera != null) _camera.fieldOfView = _baseFov;
        if (_dof != null) _dof.enabled.value = false;
    }

    private void OnShot(RevolverCombat.ShotInfo shot) => _kick = 1f;

    private void OnHealthChanged(float current, float max)
    {
        float fraction = max > 0f ? current / max : 0f;
        if (fraction < _lastHealth - 0.001f) _shake = 1f;
        _lastHealth = fraction;
    }

    private void LateUpdate()
    {
        if (_camera == null) return;
        float dt = Time.deltaTime;

        if (_vitals == null && Time.time >= _nextLookup)
        {
            _nextLookup = Time.time + 1f;
            _vitals = FindFirstObjectByType<PlayerVitals>();
            if (_vitals != null) { _vitals.HealthChanged += OnHealthChanged; _lastHealth = _vitals.HealthFraction; _subscribed = true; }
        }

        bool aiming = _gameplay != null && _gameplay.Combat != null && _gameplay.Combat.IsAiming;
        bool sprinting = _motor != null && _motor.IsSprinting;
        bool crouching = _motor != null && _motor.IsCrouching;

        // Walking bob: amplitude follows how fast the body is actually moving along the ground.
        float speed = 0f;
        if (_controller != null)
        {
            Vector3 v = _controller.velocity; v.y = 0f;
            speed = _controller.isGrounded ? v.magnitude : 0f;
        }
        float target = Mathf.Clamp01(speed / 5f) * (sprinting ? sprintBobBoost : 1f) * (crouching ? crouchBobScale : 1f) * (aiming ? aimBobScale : 1f);
        _amplitude = Mathf.Lerp(_amplitude, target, 1f - Mathf.Exp(-10f * dt));
        _phase += speed * cyclesPerMetre * Mathf.PI * 2f * dt;

        Vector3 offset = new Vector3(Mathf.Sin(_phase) * bobSway, -Mathf.Abs(Mathf.Sin(_phase)) * bobHeight, 0f) * _amplitude;
        offset.y += Mathf.Sin(Time.time * breathHertz * Mathf.PI * 2f) * breathHeight;

        // Kick from a shot, and shake from a hit; both fade quickly.
        _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-kickRecovery * dt));
        offset += new Vector3(0f, -kickDown, -kickBack) * _kick;
        _shake = Mathf.Max(0f, _shake - dt / hurtShakeSeconds);
        if (_shake > 0f) offset += Random.insideUnitSphere * (hurtShake * _shake * _shake);
        transform.localPosition = offset;

        float fov = _baseFov + (sprinting ? sprintFov : 0f) + kickFov * _kick;
        _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-fovResponse * dt));

        UpdateAimBlur(aiming, dt);
    }

    // Focus on whatever the crosshair is on and let the rest go soft; only while aiming.
    private void UpdateAimBlur(bool aiming, float dt)
    {
        if (_dof == null) return;
        bool active = aimBlur && aiming;
        _dof.enabled.value = active;
        if (!active) return;

        float distance = 12f;
        if (Physics.Raycast(_camera.transform.position, _camera.transform.forward, out RaycastHit hit, 40f, ~0, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform.root))
            distance = hit.distance;
        _focus = Mathf.Lerp(_focus, Mathf.Clamp(distance, 1.5f, 40f), 1f - Mathf.Exp(-8f * dt));
        _dof.focusDistance.value = _focus;
        _dof.aperture.value = aperture;
        _dof.focalLength.value = focalLength;
    }
}
