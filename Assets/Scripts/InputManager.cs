using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerMotor), typeof(PlayerGameplay), typeof(PlayerMovementAudio))]
public class InputManager : MonoBehaviour
{
    [SerializeField] private Flashlight flashlight;
    private PlayerInputs playerInput;
    private InputActionMap onFoot;
    private InputAction interact, aim, shoot, reload, pause;
    private InputAction movement, jump, crouch, quickTurn, sprint, flashlightToggle, flashlightFlicker;
    private PlayerMotor motor;
    private PlayerGameplay gameplay;

    private void Awake()
    {
        playerInput = new PlayerInputs();
        onFoot = playerInput.OnFoot.Get();
        motor = GetComponent<PlayerMotor>();
        gameplay = GetComponent<PlayerGameplay>();
        if (gameplay == null) gameplay = gameObject.AddComponent<PlayerGameplay>();
        if (GetComponent<PlayerMovementAudio>() == null) gameObject.AddComponent<PlayerMovementAudio>();
        if (flashlight == null) flashlight = GetComponentInChildren<Flashlight>(true);
        // Supports play before the input importer has regenerated the wrapper.
        interact = Action("Interact", "<Keyboard>/e");
        aim = Action("Aim", "<Mouse>/rightButton");
        shoot = Action("Shoot", "<Mouse>/leftButton");
        reload = Action("Reload", "<Keyboard>/r");
        movement = onFoot.FindAction("Movement", true);
        jump = onFoot.FindAction("Jump", true);
        crouch = onFoot.FindAction("Crouch", true);
        quickTurn = onFoot.FindAction("QuickTurn", true);
        sprint = onFoot.FindAction("Sprint", true);
        flashlightToggle = onFoot.FindAction("Flashlight", true);
        flashlightFlicker = onFoot.FindAction("FlashlightFlicker", true);
        pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
    }

    private InputAction Action(string name, string binding)
    {
        return onFoot.FindAction(name) ?? onFoot.AddAction(name, InputActionType.Button, binding);
    }

    private void OnEnable() { onFoot.Enable(); pause.Enable(); }

    private void Update()
    {
        if (pause.WasPressedThisFrame()) gameplay.HandleEscape();
        if (gameplay.BlocksControls)
        {
            if (onFoot.enabled) onFoot.Disable();
            return;
        }
        if (!onFoot.enabled) onFoot.Enable();
        motor.ProcessLook(Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero);
        gameplay.Combat.SetTriggerHeld(shoot.IsPressed());
        gameplay.Combat.SetAim(aim.IsPressed());
        if (interact.WasPressedThisFrame()) gameplay.TryInteract();
        if (gameplay.BlocksControls) return;
        if (reload.WasPressedThisFrame()) gameplay.Combat.TryReload();
        if (shoot.WasPressedThisFrame()) gameplay.Combat.TryShoot();
        if (jump.WasPressedThisFrame()) motor.Jump();
        if (crouch.WasPressedThisFrame()) motor.toggleCrouch();
        if (quickTurn.WasPressedThisFrame()) motor.quickTurn();
        motor.setSprint(sprint.IsPressed() && gameplay.State.CanSprint);
        if (flashlight != null)
        {
            if (flashlightToggle.WasPressedThisFrame()) flashlight.toggleFlashlight();
            if (flashlightFlicker.WasPressedThisFrame()) flashlight.triggerFlicker();
        }
        motor.ProcessMove(movement.ReadValue<Vector2>());
    }

    private void OnDisable()
    {
        onFoot?.Disable(); pause?.Disable();
        if (motor != null) motor.ProcessMove(Vector2.zero);
        if (gameplay != null) { gameplay.Combat.SetAim(false); gameplay.Combat.SetTriggerHeld(false); }
    }

    private void OnDestroy() { pause?.Dispose(); playerInput?.Dispose(); }
}
