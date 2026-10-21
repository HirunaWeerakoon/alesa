using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMotor), typeof(PlayerGameplay), typeof(PlayerMovementAudio))]
public class InputManager : MonoBehaviour
{
    [SerializeField] private Flashlight flashlight;
    private PlayerInputs playerInput;
    private InputActionMap onFoot;
    private InputAction interact, aim, shoot, reload, pause;
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
        gameplay.Combat.SetTriggerHeld(shoot.IsPressed());
        gameplay.Combat.SetAim(aim.IsPressed());
        if (interact.WasPressedThisFrame()) gameplay.TryInteract();
        if (gameplay.BlocksControls) return;
        if (reload.WasPressedThisFrame()) gameplay.Combat.TryReload();
        if (shoot.WasPressedThisFrame()) gameplay.Combat.TryShoot();
        if (onFoot.FindAction("Jump").WasPressedThisFrame()) motor.Jump();
        if (onFoot.FindAction("Crouch").WasPressedThisFrame()) motor.toggleCrouch();
        if (onFoot.FindAction("QuickTurn").WasPressedThisFrame()) motor.quickTurn();
        motor.setSprint(onFoot.FindAction("Sprint").IsPressed());
        if (flashlight != null)
        {
            if (onFoot.FindAction("Flashlight").WasPressedThisFrame()) flashlight.toggleFlashlight();
            if (onFoot.FindAction("FlashlightFlicker").WasPressedThisFrame()) flashlight.triggerFlicker();
        }
        motor.ProcessMove(onFoot.FindAction("Movement").ReadValue<Vector2>());
        motor.ProcessLook(Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero);
    }

    private void OnDisable()
    {
        onFoot?.Disable(); pause?.Disable();
        if (motor != null) motor.ProcessMove(Vector2.zero);
        if (gameplay != null) { gameplay.Combat.SetAim(false); gameplay.Combat.SetTriggerHeld(false); }
    }

    private void OnDestroy() { pause?.Dispose(); playerInput?.Dispose(); }
}
