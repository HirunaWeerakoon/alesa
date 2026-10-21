using System;

public static class RevolverAmmoChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Main()
    {
        var ammo = new RevolverAmmo();
        ammo.LoadPickup(10);
        Check(ammo.loaded == 6 && ammo.spare == 4, "Pickup overflow must go to spare ammo");
        for (int i = 0; i < 6; i++) Check(ammo.TryConsume(), "Six rounds must fire");
        Check(!ammo.TryConsume() && ammo.loaded == 0, "An empty cylinder cannot fire or go negative");
        Check(ammo.Reload() == 4 && ammo.loaded == 4 && ammo.spare == 0, "Partial reload must conserve ammo");
        ammo.AddSpare(12);
        Check(ammo.Reload() == 2 && ammo.loaded == 6 && ammo.spare == 10, "Reload must top up without discarding loaded rounds");
        Check(ammo.Reload() == 0 && ammo.spare == 10, "Full reload must not spend ammo");
        ammo.AddSpare(-100);
        Check(ammo.spare == 10, "Negative pickup cannot remove ammo");
        ammo.AddSpare(int.MaxValue);
        Check(ammo.spare == int.MaxValue, "Ammo overflow cannot wrap negative");
        for (int spare = 0; spare < 20; spare++) for (int loaded = 0; loaded <= 6; loaded++)
        {
            var state = new RevolverAmmo { loaded = loaded, spare = spare };
            int total = loaded + spare;
            state.Reload();
            Check(state.loaded <= 6 && state.spare >= 0 && state.loaded + state.spare == total, "Reload conservation failed");
        }
        Console.WriteLine("Passed revolver pickup, empty-fire, partial/top-up reload, overflow, and 140 conservation cases.");
        var shell = new FiringStopSoundGate();
        Check(!shell.TryConsume(10, false), "No shell sound before any shot");
        shell.RecordShot(0, 0.55f);
        Check(!shell.TryConsume(0.4f, false), "Shell must wait for quiet");
        shell.RecordShot(0.4f, 0.55f);
        Check(!shell.TryConsume(0.6f, false), "A second shot must postpone shell sound");
        Check(!shell.TryConsume(1f, true), "Holding the trigger must delay the shell");
        Check(shell.TryConsume(1.1f, false), "Shell must play after firing stops and trigger releases");
        Check(!shell.TryConsume(2f, false), "A firing sequence must produce only one shell sound");
        shell.RecordShot(3, 0.55f);
        shell.Clear();
        Check(!shell.TryConsume(4, false), "Pause/disable must discard a pending shell");
        Console.WriteLine("Passed delayed shell sound: quiet window, repeated shots, held trigger, single playback and cancellation.");
    }
}
