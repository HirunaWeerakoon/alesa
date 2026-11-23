using UnityEngine;

/// <summary>Connects Alesa player/weapon events and the acoustic bus to a presentation-only HUD.</summary>
[DefaultExecutionOrder(300)]
public sealed class GameplayHUDBridge : MonoBehaviour
{
    [SerializeField] private GameplayHUDController hud;
    [SerializeField] private PlayerGameplay player;
    [Tooltip("Acoustic radius mapped to a full meter. Does not change AI hearing.")]
    [SerializeField, Min(1f)] private float fullScaleRadius = 40f;
    private RevolverCombat weapon;
    private bool previousCanvasHUD, subscribed;

    /// <summary>Called by the scene setup while this object is inactive.</summary>
    public void Configure(GameplayHUDController controller, PlayerGameplay target) { hud = controller; player = target; }

    private void OnEnable()
    {
        if (hud == null) hud = GetComponent<GameplayHUDController>();
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (hud == null || player == null || player.Combat == null)
        {
            Debug.LogWarning("Gameplay HUD bridge requires an initialized player and HUD.", this);
            return;
        }
        weapon = player.Combat;
        previousCanvasHUD = player.UseCanvasHUD;
        player.UseCanvasHUD = true;
        player.State.Changed += RefreshState;
        weapon.WeaponStatusChanged += RefreshWeapon;
        AcousticBus.OnSoundEmitted += Hear;
        subscribed = true;
        RefreshWeapon(); RefreshState();
    }

    private void OnDisable()
    {
        if (!subscribed) return;
        if (player != null)
        {
            player.State.Changed -= RefreshState;
            player.UseCanvasHUD = previousCanvasHUD;
        }
        if (weapon != null) weapon.WeaponStatusChanged -= RefreshWeapon;
        AcousticBus.OnSoundEmitted -= Hear;
        if (hud != null) hud.SetVisible(false);
        subscribed = false;
    }

    private void LateUpdate()
    {
        if (!subscribed || player == null || hud == null || player.BlocksControls || player.Motor == null) return;
        var motor = player.Motor;
        hud.SetMovementState(motor.IsGrounded, motor.IsSprinting, motor.IsCrouching, motor.HasMovementInput);
    }

    private void RefreshState()
    {
        if (hud == null || player == null) return;
        hud.SetVisible(!player.BlocksControls);
        hud.SetAimingState(player.State.CurrentAction == PlayerState.Action.Aiming);
        hud.SetInteractionState(player.BlocksControls);
    }

    private void RefreshWeapon()
    {
        if (hud != null && weapon != null)
            hud.UpdateWeaponAmmo(weapon.HasRevolver ? "Revolver" : "UNARMED", weapon.LoadedAmmo, weapon.SpareAmmo);
    }

    private void Hear(Vector3 origin, float radius, SoundType type)
    {
        if (hud == null || player == null || player.BlocksControls) return;
        string action;
        switch (type)
        {
            case SoundType.Footstep: action = "WALKING"; break;
            case SoundType.Sprint: action = "SPRINTING"; break;
            case SoundType.Gunshot: action = "GUNSHOT"; break;
            case SoundType.PropCollision: action = "PROP IMPACT"; break;
            default: action = "NOISE"; break;
        }
        hud.ReportNoise(radius / Mathf.Max(1f, fullScaleRadius), action);
    }
}
