using System;

public static class PlayerStateChecks
{
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    public static void Run()
    {
        var state = new PlayerState();
        int stateNotifications = 0;
        System.Action onChanged = () => stateNotifications++;
        state.Changed += onChanged;
        state.SetAiming(false);
        state.SetWeaponObstructed(false);
        state.SetMode(PlayerState.Mode.Playing);
        Check(stateNotifications == 0, "Unchanged state should not flood HUD listeners");
        Check(state.CanMove && state.CanInteract && state.CanSprint && state.CanShoot, "Free state permits hip fire");
        state.SetAiming(true);
        Check(stateNotifications == 1, "Aim transition must notify HUD once");
        state.SetAiming(true);
        Check(stateNotifications == 1, "Held aim should not repeatedly notify HUD");
        state.Changed -= onChanged;
        Check(state.CanShoot && !state.CanSprint && state.CanInteract, "Aim should allow fire and interaction, but prevent sprint");
        state.SetWeaponObstructed(true);
        Check(!state.CanShoot && state.CurrentAction == PlayerState.Action.Aiming, "Obstruction blocks fire without losing aim intent");
        state.SetWeaponObstructed(false);
        Check(state.CanShoot, "Removing obstruction restores firing permission");
        Check(state.TryStartReload(), "Aim may transition into reload");
        Check(!state.TryStartReload(), "Reload cannot stack");
        state.SetAiming(true); state.SetAiming(false);
        Check(state.CurrentAction == PlayerState.Action.Reloading && state.CanMove && !state.CanShoot && !state.CanInteract && !state.CanSprint, "Reload cannot be overridden by held aim or interact");
        state.EndReload();
        Check(state.CurrentAction == PlayerState.Action.Free, "Reload completes to free state");
        foreach (PlayerState.Mode mode in new[] { PlayerState.Mode.Paused, PlayerState.Mode.Note, PlayerState.Mode.Keypad })
        {
            state.SetAiming(true); state.SetWeaponObstructed(true);
            state.SetMode(mode);
            Check(!state.CanMove && !state.CanShoot && !state.CanInteract && !state.CanSprint, "Modal must lock all gameplay actions");
            Check(state.CurrentAction == PlayerState.Action.Free && !state.WeaponObstructed, "Modal clears transient action state");
            state.SetAiming(true);
            Check(!state.TryStartReload() && !state.CanShoot, "Modal cannot start aim/reload");
            state.EndReload();
            Check(state.CurrentMode == mode, "Late reload completion cannot escape a modal");
            state.SetMode(PlayerState.Mode.Playing);
            Check(state.CanMove && state.CanInteract && state.CanShoot, "Resume permits hip fire without aim");
            state.TryStartReload(); state.SetMode(mode); state.EndReload();
            Check(state.CurrentMode == mode && !state.CanMove, "Reload cancellation preserves modal mode");
            state.SetMode(PlayerState.Mode.Playing);
        }
        state.SetAiming(true);
        state.SetMode(PlayerState.Mode.Dead);
        Check(state.IsDead && !state.CanMove && !state.CanShoot && !state.CanInteract && !state.CanSprint,
            "Death must lock all gameplay actions");
        state.SetAiming(true);
        Check(!state.TryStartReload(), "Dead player cannot reload");
        state.EndReload();
        foreach (PlayerState.Mode mode in new[] { PlayerState.Mode.Playing, PlayerState.Mode.Paused,
            PlayerState.Mode.Note, PlayerState.Mode.Keypad })
        {
            state.SetMode(mode);
            Check(state.IsDead && !state.CanMove && !state.CanShoot,
                "Pause/resume and late callbacks cannot revive a dead player");
        }
        Check(new PlayerState().CanMove, "A fresh player after retry must start alive");
        Console.WriteLine("Passed central state: aim/reload conflicts, obstruction, modal locking, cancellation/resume and terminal death.");
    }
}
