// Single authority for action permissions; presentation and physics remain in their components.
public sealed class PlayerState
{
    public enum Mode { Playing, Paused, Note, Keypad }
    public enum Action { Free, Aiming, Reloading }
    public Mode CurrentMode { get; private set; }
    public Action CurrentAction { get; private set; }
    public bool WeaponObstructed { get; private set; }
    public bool CanMove => CurrentMode == Mode.Playing;
    public bool CanInteract => CanMove && CurrentAction != Action.Reloading;
    public bool CanSprint => CanMove && CurrentAction == Action.Free;
    public bool CanShoot => CanMove && CurrentAction == Action.Aiming && !WeaponObstructed;

    public void SetMode(Mode mode)
    {
        if (CurrentMode == mode) return;
        CurrentMode = mode;
        CurrentAction = Action.Free;
        WeaponObstructed = false;
    }

    public void SetAiming(bool requested)
    {
        if (!CanMove || CurrentAction == Action.Reloading) return;
        CurrentAction = requested ? Action.Aiming : Action.Free;
    }

    public bool TryStartReload()
    {
        if (!CanMove || CurrentAction == Action.Reloading) return false;
        CurrentAction = Action.Reloading;
        return true;
    }

    public void EndReload() { if (CurrentAction == Action.Reloading) CurrentAction = Action.Free; }
    public void SetWeaponObstructed(bool blocked) { WeaponObstructed = blocked; }
}
