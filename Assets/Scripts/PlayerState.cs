// Single authority for action permissions; presentation and physics remain in their components.
public sealed class PlayerState
{
    public enum Mode { Playing, Paused, Note, Keypad, Dead }
    public enum Action { Free, Aiming, Reloading }
    public Mode CurrentMode { get; private set; }
    public Action CurrentAction { get; private set; }
    public bool WeaponObstructed { get; private set; }
    public bool IsDead => CurrentMode == Mode.Dead;
    /// <summary>Raised only when mode, action, or obstruction actually changes.</summary>
    public event System.Action Changed;
    public bool CanMove => CurrentMode == Mode.Playing;
    public bool CanInteract => CanMove && CurrentAction != Action.Reloading;
    public bool CanSprint => CanMove && CurrentAction == Action.Free;
    public bool CanShoot => CanMove && CurrentAction != Action.Reloading && !WeaponObstructed;

    public void SetMode(Mode mode)
    {
        // Death is terminal for this player instance. Retry creates a fresh scene/player.
        if (IsDead || CurrentMode == mode) return;
        CurrentMode = mode;
        CurrentAction = Action.Free;
        WeaponObstructed = false;
        Changed?.Invoke();
    }

    public void SetAiming(bool requested)
    {
        if (!CanMove || CurrentAction == Action.Reloading) return;
        Action next = requested ? Action.Aiming : Action.Free;
        if (CurrentAction == next) return;
        CurrentAction = next;
        Changed?.Invoke();
    }

    public bool TryStartReload()
    {
        if (!CanMove || CurrentAction == Action.Reloading) return false;
        CurrentAction = Action.Reloading;
        Changed?.Invoke();
        return true;
    }

    public void EndReload()
    {
        if (CurrentAction != Action.Reloading) return;
        CurrentAction = Action.Free;
        Changed?.Invoke();
    }
    public void SetWeaponObstructed(bool blocked)
    {
        if (WeaponObstructed == blocked) return;
        WeaponObstructed = blocked;
        Changed?.Invoke();
    }
}
