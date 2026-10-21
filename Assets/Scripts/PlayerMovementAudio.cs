using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMotor), typeof(CharacterController))]
public class PlayerMovementAudio : MonoBehaviour
{
    [Header("Ordered sequences (repeat while moving on the ground)")]
    public AudioClip[] walkClips = new AudioClip[0];
    public AudioClip[] runClips = new AudioClip[0];
    public AudioClip[] crouchClips = new AudioClip[0];
    public AudioClip jumpClip;
    public AudioClip landClip;
    [Header("Interactions")]
    public AudioClip paperClip;
    public AudioClip weaponPickupClip;
    public AudioClip ammoPickupClip;
    [Header("Volume")]
    [Range(0f, 1f)] public float walkVolume = 0.65f;
    [Range(0f, 1f)] public float runVolume = 0.8f;
    [Range(0f, 1f)] public float crouchVolume = 0.35f;
    [Range(0f, 1f)] public float jumpVolume = 0.8f;
    [Range(0f, 1f)] public float interactionVolume = 0.8f;
    [Header("Distance between footsteps (metres)")]
    [Min(0.1f)] public float walkStepDistance = 2.25f;
    [Min(0.1f)] public float runStepDistance = 2.45f;
    [Min(0.1f)] public float crouchStepDistance = 1.8f;
    public AudioSource footstepSource;
    public AudioSource jumpSource;
    public AudioSource interactionSource;
    private PlayerMotor motor;
    private CharacterController controller;
    private PlayerGameplay gameplay;
    private int walkIndex, runIndex, crouchIndex;
    private int previousGait = -1;
    private float remainingDistance;
    private bool waitingForLanding, observedAirborne;
    private float landingQuietUntil;

    private void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        controller = GetComponent<CharacterController>();
        gameplay = GetComponent<PlayerGameplay>();
        footstepSource = Prepare(footstepSource);
        jumpSource = Prepare(jumpSource);
        interactionSource = Prepare(interactionSource);
    }

    private AudioSource Prepare(AudioSource source)
    {
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false; // Clips are individual steps, not seamless audio loops.
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void OnEnable() { motor.Jumped += PlayJump; }
    private void OnDisable()
    {
        motor.Jumped -= PlayJump;
        waitingForLanding = false; observedAirborne = false;
        StopSteps();
        if (jumpSource != null) jumpSource.Stop();
        if (interactionSource != null) interactionSource.Stop();
    }

    private void LateUpdate()
    {
        if (gameplay == null) gameplay = GetComponent<PlayerGameplay>();
        bool blocked = !motor.ControlsEnabled || Time.timeScale <= 0f || (gameplay != null && gameplay.BlocksControls);
        if (blocked) { StopSteps(); jumpSource.Stop(); return; }
        // Observe actual airborne movement before accepting ground contact. A jump
        // event can arrive after CharacterController.Move in the takeoff frame.
        if (waitingForLanding && !controller.isGrounded) observedAirborne = true;
        if (waitingForLanding && observedAirborne && controller.isGrounded)
        {
            waitingForLanding = false; observedAirborne = false;
            if (landClip != null) jumpSource.PlayOneShot(landClip, jumpVolume);
            landingQuietUntil = Time.time + 0.15f;
        }
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        float speed = velocity.magnitude;
        if (!controller.isGrounded || !motor.HasMovementInput || speed < 0.1f || Time.time < landingQuietUntil) { StopSteps(); return; }
        int gait = motor.IsCrouching ? 2 : motor.IsSprinting ? 1 : 0;
        if (gait != previousGait) { remainingDistance = 0; previousGait = gait; footstepSource.Stop(); }
        remainingDistance -= speed * Time.deltaTime;
        if (remainingDistance > 0f) return;
        if (gait == 2) { PlayNext(crouchClips, ref crouchIndex, crouchVolume); remainingDistance = Mathf.Max(0.1f, crouchStepDistance); }
        else if (gait == 1) { PlayNext(runClips, ref runIndex, runVolume); remainingDistance = Mathf.Max(0.1f, runStepDistance); }
        else { PlayNext(walkClips, ref walkIndex, walkVolume); remainingDistance = Mathf.Max(0.1f, walkStepDistance); }
    }

    private void PlayNext(AudioClip[] clips, ref int index, float volume)
    {
        if (clips == null || clips.Length == 0) return;
        var clip = clips[index % clips.Length];
        index = (index + 1) % clips.Length;
        if (clip != null) footstepSource.PlayOneShot(clip, volume);
    }

    private void StopSteps()
    {
        remainingDistance = 0; previousGait = -1;
        if (footstepSource != null) footstepSource.Stop();
    }

    public void PlayJump()
    {
        StopSteps();
        waitingForLanding = true; observedAirborne = false;
        if (jumpClip != null) jumpSource.PlayOneShot(jumpClip, jumpVolume);
    }

    public void PlayPickup(InteractablePickup.PickupKind kind)
    {
        AudioClip clip = kind == InteractablePickup.PickupKind.Note ? paperClip :
            kind == InteractablePickup.PickupKind.Ammo ? ammoPickupClip : weaponPickupClip;
        if (clip != null) interactionSource.PlayOneShot(clip, interactionVolume);
    }
}
