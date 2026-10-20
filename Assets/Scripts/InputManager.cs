using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;


public class InputManager : MonoBehaviour
{
    private PlayerInputs playerInput;
    private PlayerInputs.OnFootActions onFoot;
    private PlayerMotor motor;

    [Header("flashligh refenrece")]
    [SerializeField] private Flashlight flashlight;
    // Awake it called before first frame update
    
        void Awake()
    {
        if (playerInput != null) return;
        
        playerInput = new PlayerInputs();
        onFoot = playerInput.OnFoot;
        motor = GetComponent < PlayerMotor>();

        //auto locate flashlight if not assigned
        if(flashlight == null)
        {
            flashlight = GetComponentInChildren<Flashlight>(true);
        }


        // event hooks for single trigger and toggle actions
        onFoot.Jump.performed += ctx => { if (motor != null) motor.Jump(); };// (space)
        onFoot.Crouch.performed += ctx => { if (motor != null) motor.toggleCrouch(); }; // (left ctrl)

        // event hooks for held actions (sprint) (left shift)
        onFoot.Sprint.performed += ctx => { if (motor != null) motor.setSprint(true); };
        onFoot.Sprint.canceled += ctx => { if (motor != null) motor.setSprint(false); };

        // quick turn event (Q)
        onFoot.QuickTurn.performed += ctx => { if (motor != null) motor.quickTurn(); };

        // flashlight toggle (F)
        onFoot.Flashlight.performed += ctx =>
        {
            if (flashlight != null) flashlight.toggleFlashlight();
        };

        onFoot.FlashlightFlicker.performed += ctx =>
        {
            if (flashlight != null) flashlight.triggerFlicker();
        };

    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update is called once per frame

    void Update()
    {
        if (motor == null) return;

        // tell player motor to move using the volume from our movement action
        Vector2 movementInput = onFoot.Movement.ReadValue<Vector2>();
        motor.ProcessMove(movementInput);

        // rotate character with mouse delta
        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        motor.ProcessLook(mouseDelta);

    }

private void OnEnable()
    {
        if (playerInput == null) Awake();
        onFoot.Enable();
    }
private void OnDisable()
    {
        if (playerInput != null) onFoot.Disable();
    }

}
