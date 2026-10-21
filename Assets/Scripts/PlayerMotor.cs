using UnityEngine;

[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    [Header("Speed")]
    public float walkSpeed = 5f, sprintSpeed = 8.5f, crouchSpeed = 3f, aimSpeed = 2f;
    [Header("Grounding")]
    public float gravity = -9.8f;
    public float jumpHeight = 1.5f;
    [Min(0.01f)] public float groundProbeDistance = 0.12f;
    [Min(0.1f)] public float groundStickSpeed = 2f;
    public LayerMask collisionMask = ~0;
    [Header("Crouch and view")]
    public float standingHeight = 1.95f, crouchHeight = 1f;
    public float standingCameraY = 1.82f, crouchCameraY = 0.8f;
    public float crouchTransitionSpeed = 10f;
    public Camera playerCamera;
    public float mouseSensivity = 25f, maxLookAngle = 80f;
    public bool IsCrouching => crouching;
    public bool IsGrounded { get; private set; }
    public bool IsSprinting => sprinting && !crouching && !aiming && (gameplay == null || gameplay.State.CanSprint);
    public bool ControlsEnabled => controlsEnabled && (gameplay == null || gameplay.State.CanMove);
    public bool HasMovementInput => input.sqrMagnitude > 0.01f;
    public event System.Action Jumped;

    private CharacterController controller;
    private PlayerGameplay gameplay;
    private Vector2 input;
    private bool crouching, sprinting, aiming, controlsEnabled = true, jumpRequested;
    private float verticalSpeed, xRotation, originalStepOffset;
    private Vector3 groundNormal = Vector3.up;
    private Transform capsuleVisual;
    private Vector3 visualScale;
    private readonly Collider[] overlaps = new Collider[32];
    private readonly RaycastHit[] groundHits = new RaycastHit[32];

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        gameplay = GetComponent<PlayerGameplay>();
        originalStepOffset = controller.stepOffset;
        capsuleVisual = transform.Find("Capsule");
        if (capsuleVisual != null) visualScale = capsuleVisual.localScale;
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
    }

    public void SetControlsEnabled(bool value)
    {
        controlsEnabled = value;
        if (!value) { input = Vector2.zero; sprinting = false; jumpRequested = false; }
    }
    public void SetAiming(bool value) { aiming = value; }
    public void setSprint(bool value) { sprinting = value; }
    public void updateSpeed() { } // Retained for existing Inspector event bindings.
    public void ProcessMove(Vector2 value) { input = ControlsEnabled ? Vector2.ClampMagnitude(value, 1f) : Vector2.zero; }
    public void Jump() { if (ControlsEnabled && !crouching) jumpRequested = true; }

    private Vector3 Feet => transform.TransformPoint(controller.center - Vector3.up * controller.height * 0.5f);
    private float HorizontalScale => Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z));

    public bool CanStand() { return CanFitHeight(standingHeight); }

    private bool CanFitHeight(float height)
    {
        float radius = Mathf.Max(0.02f, controller.radius * HorizontalScale - 0.015f);
        float worldHeight = Mathf.Max(radius * 2f, height * Mathf.Abs(transform.lossyScale.y));
        Vector3 bottom = Feet + Vector3.up * (radius + 0.02f);
        Vector3 top = Feet + Vector3.up * (worldHeight - radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, collisionMask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++) if (!overlaps[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    public void toggleCrouch()
    {
        if (!ControlsEnabled) return;
        if (crouching && !CanStand()) { gameplay?.Notify("Not enough room to stand."); return; }
        crouching = !crouching;
        if (crouching) { sprinting = false; jumpRequested = false; }
    }

    private bool ProbeGround()
    {
        if (verticalSpeed > 0.01f) return false;
        float radius = controller.radius * HorizontalScale;
        float inset = Mathf.Min(controller.skinWidth * HorizontalScale, radius * 0.25f);
        Vector3 origin = Feet + Vector3.up * (radius + 0.04f);
        int count = Physics.SphereCastNonAlloc(origin, Mathf.Max(0.01f, radius - inset), Vector3.down,
            groundHits, groundProbeDistance + inset + 0.04f, collisionMask, QueryTriggerInteraction.Ignore);
        if (count == groundHits.Length) return false;
        float nearest = float.PositiveInfinity;
        float minimumY = Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
        groundNormal = Vector3.up;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < minimumY || hit.distance >= nearest) continue;
            nearest = hit.distance; groundNormal = hit.normal;
        }
        return nearest < float.PositiveInfinity;
    }

    private void Update()
    {
        if (!ControlsEnabled || !controller.enabled || Time.deltaTime <= 0f) { jumpRequested = false; return; }
        IsGrounded = ProbeGround();
        if (IsGrounded) verticalSpeed = -groundStickSpeed;

        float target = crouching ? crouchHeight : standingHeight;
        float height = Mathf.Lerp(controller.height, target, 1f - Mathf.Exp(-crouchTransitionSpeed * Time.deltaTime));
        if (height > controller.height && !CanFitHeight(height))
        {
            crouching = true; sprinting = false; jumpRequested = false;
            height = controller.height;
        }
        Vector3 feet = Feet;
        controller.height = Mathf.Max(controller.radius * 2f, height);
        controller.center = transform.InverseTransformPoint(feet) + Vector3.up * (controller.height * 0.5f);
        controller.stepOffset = IsGrounded ? Mathf.Max(0f, Mathf.Min(originalStepOffset, controller.height - controller.radius * 2f)) : 0f;
        UpdateViewAndBody();

        if (jumpRequested && IsGrounded && !crouching && controller.height >= standingHeight - 0.05f)
        {
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            IsGrounded = false; Jumped?.Invoke();
        }
        jumpRequested = false;
        float speed = crouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
        if (aiming) speed = Mathf.Min(speed, aimSpeed);
        Vector3 direction = transform.TransformDirection(new Vector3(input.x, 0, input.y));
        if (IsGrounded) direction = Vector3.ProjectOnPlane(direction, groundNormal).normalized * input.magnitude;
        verticalSpeed += gravity * Time.deltaTime;
        var flags = controller.Move((direction * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
        IsGrounded = verticalSpeed <= 0f && ProbeGround();
        if (IsGrounded) verticalSpeed = -groundStickSpeed;
    }

    private void UpdateViewAndBody()
    {
        float fraction = Mathf.InverseLerp(crouchHeight, standingHeight, controller.height);
        if (playerCamera != null)
        {
            var position = playerCamera.transform.localPosition;
            position.y = Mathf.Min(controller.height - 0.1f, Mathf.Lerp(crouchCameraY, standingCameraY, fraction));
            playerCamera.transform.localPosition = position;
        }
        if (capsuleVisual != null)
        {
            var position = capsuleVisual.localPosition; position.y = controller.center.y;
            capsuleVisual.localPosition = position;
            capsuleVisual.localScale = new Vector3(visualScale.x, visualScale.y * controller.height / standingHeight, visualScale.z);
        }
    }

    public void quickTurn() { if (ControlsEnabled) transform.Rotate(0, 180, 0); }
    public void ProcessLook(Vector2 delta)
    {
        if (!ControlsEnabled) return;
        xRotation = Mathf.Clamp(xRotation - delta.y * mouseSensivity * 0.01f, -maxLookAngle, maxLookAngle);
        if (playerCamera != null) playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0, 0);
        transform.Rotate(0, delta.x * mouseSensivity * 0.01f, 0);
    }
}
