using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    private CharacterController controller; // create internal variable to hold reference to physical movement controller

    [Header("Speed Settings")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8.5f;
    public float crouchSpeed = 3f;
    private float currentSpeed; // keep track of current active
    private bool isSprinting = false;

    [Header("Physics Setting")]
    public float gravity = -9.8f;
    private Vector3 playerVelocity;
    private bool isGrounded;

    [Header("Jump Setting")]
    public float jumpHeight = 1.5f;

    [Header("Crouch Setting")]
    public float standingHeight = 2f;
    public float crouchHeight = 1f;
    private bool isCrouching = false;

    [Header("Camera Setting")]
    public Camera playerCamera;
    public float mouseSensivity = 25f;
    public float maxLookAngle = 80f; // set angle to prevent player to see too far up/down
    private float xRotation = 0f;  // track camera/mouse tilt/rotation

    private Vector2 currentInput;


    // start is called before first frame update
    void Start()
    {
        controller = GetComponent<CharacterController>(); // to locate and link the character controller
        currentSpeed = walkSpeed;

    }
    
    // update is called once per frame
    void Update()
    {
        isGrounded = controller.isGrounded; // build in check. return true when bottom of the capsule touch the ground

        if(isGrounded && playerVelocity.y < 0)
        {
            playerVelocity.y = -2f; // when grounded, set downward speed to a small. ensure player remains pinned against slopes
        }

        // map 2D inputs to 3D local axes: x = left/right (A/D) , z = forward/backward (W/S)
        Vector3 moveDirection = Vector3.zero;
        moveDirection.x = currentInput.x;
        moveDirection.z = currentInput.y;

        // Apply speed strictly capped by currentSpeed
        Vector3 worldMove = transform.TransformDirection(moveDirection) * currentSpeed;

        // accelerate downward over time with gravity
        playerVelocity.y += gravity * Time.deltaTime;

        // Combined move drive both horizontal and vertical movements simultaniously
        Vector3 finalMovement = worldMove + playerVelocity;
        // apply combined movement to character controller
        controller.Move(finalMovement * Time.deltaTime);
    }

    // prevent jumping while crouching
    public void Jump()
    {
        if(isGrounded && !isCrouching)
        {
            playerVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity );
        }
    }

    public void setSprint(bool Sprinting)
    {
        isSprinting = Sprinting;
        updateSpeed();

    }

    public void updateSpeed()
    {
        if (isCrouching)
        {
            currentSpeed = crouchSpeed; // cannot override by ssprint
        }
        else if (isSprinting)
        {
            currentSpeed = sprintSpeed;
        }
        else
        {
            currentSpeed = walkSpeed;
        }
    }

    public void quickTurn()
    {
        transform.Rotate(0f,180f,0f);
    }
    public void toggleCrouch()
    {
        isCrouching = !isCrouching;

        // adjust heigh and recalculate center
        float targetHeight = isCrouching ? crouchHeight : standingHeight;
        controller.height = targetHeight;
        controller.center = new Vector3(0f, targetHeight / 2f, 0f);

        // drop sprint if crouching
        if (isCrouching)
        {
            isSprinting = false;
        }

        // apply crouching speed
        updateSpeed();
    }
    // recieve raw WASD inputs from InputManager.cs
    public void ProcessMove(Vector2 input)
    {
        // clamping dioganal movement vector
        // w+d does not exeed max speed
        currentInput = Vector2.ClampMagnitude(input,1f);
    }

    // handle mouse movement
    public void ProcessLook(Vector2 input)
    {
        /*
            multiply input from sensivity for smoothness and delta time for keep it frame rate independant
        */
        float mouseX = input.x * mouseSensivity * Time.deltaTime; // horizontal (left/right)
        float mouseY = input.y * mouseSensivity * Time.deltaTime; // vertical (up/down)

        xRotation -= mouseY; // tilt camera based on mouse rotation
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle); // lmit tilt at -/+max angle
        if(playerCamera != null)
        {
            playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f); // actually rotate camera on x axis
        }
        // yaw(left/right) rotate player body
        transform.Rotate(Vector3.up * mouseX); // Spins the whole player object left/right when move the mouse horizontally
    }

}
