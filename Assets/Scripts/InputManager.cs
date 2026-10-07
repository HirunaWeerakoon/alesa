using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class InputManager : MonoBehaviour
{
    private PlayerInputs playerInput;
    private PlayerInputs.OnFootActions onFoot;
    private PlayerMotor motor;
   
    // Awake it called before first frame update
    void Awake()
    {
        
        playerInput = new PlayerInputs();
        onFoot = playerInput.OnFoot;
        motor = GetComponent < PlayerMotor>();

        // event hooks for single trigger and toggle actions
        onFoot.Jump.performed += ctx => motor.Jump();
        onFoot.Crouch.performed += ctx => motor.toggleCrouch();

        // event hooks for held actions (sprint)
        onFoot.Sprint.performed += ctx => motor.setSprint(true);
        onFoot.Sprint.canceled += ctx => motor.setSprint(false);

        // quick turn event
        onFoot.QuickTurn.performed += ctx => motor.quickTurn();

    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update is called once per frame

    void Update()
    {
        // tell player motor to move using the volume from our movement action
        motor.ProcessMove(onFoot.Movement.ReadValue<Vector2>());

        // rotate character with mouse delta
        Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        motor.ProcessLook(mouseDelta);

    }

    private void OnEnable()
    {
        onFoot.Enable();
    }
    private void OnDisable()
    {
        onFoot.Disable();
    }

}
