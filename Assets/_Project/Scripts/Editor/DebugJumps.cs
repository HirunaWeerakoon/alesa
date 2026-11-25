using UnityEditor;
using UnityEngine;

/// <summary>
/// Play-mode shortcuts for testing the late game without walking the whole hospital or fighting every minion.
/// They do nothing outside Play mode, and nothing here is part of the game itself.
/// </summary>
public static class DebugJumps
{
    private static bool InPlayMode(string what)
    {
        if (Application.isPlaying) return true;
        Debug.LogWarning("[Debug jump] " + what + " only works in Play mode.");
        return false;
    }

    private static void Teleport(PlayerGameplay player, Vector3 position, float yaw)
    {
        var controller = player.GetComponent<CharacterController>();
        bool was = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        if (controller != null) controller.enabled = was;
    }

    [MenuItem("ALESA/Debug/Park All Enemies (Play Mode)")]
    public static void ParkEnemies()
    {
        if (!InPlayMode("Park enemies")) return;
        int n = 0;
        var director = Object.FindFirstObjectByType<MinionSpawnDirector>();
        if (director != null) director.gameObject.SetActive(false);
        foreach (var agent in Object.FindObjectsByType<Alesa.Agents.BlindMinion.BlindMinionAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            agent.gameObject.SetActive(false);
            n++;
        }
        Debug.Log("[Debug jump] Parked " + n + " minions and switched off the spawn director.");
    }

    [MenuItem("ALESA/Debug/Teleport to Alesa's Cage (Play Mode)")]
    public static void ToCage()
    {
        if (!InPlayMode("Teleport to the cage")) return;
        var player = Object.FindFirstObjectByType<PlayerGameplay>();
        if (player == null) return;
        // Inside the cage room, west of the cage, facing it.
        Teleport(player, new Vector3(188.2f, 24.7f, 47f), 90f);
        Debug.Log("[Debug jump] You are in Alesa's cage room. Press E on the cage to free her.");
    }

    [MenuItem("ALESA/Debug/Free Alesa and Start the Final Wing (Play Mode)")]
    public static void StartFinalWing()
    {
        if (!InPlayMode("Start the final wing")) return;
        var player = Object.FindFirstObjectByType<PlayerGameplay>();
        var encounter = Object.FindFirstObjectByType<FinalBossEncounter>();
        if (player == null || encounter == null) { Debug.LogWarning("[Debug jump] Player or Final Boss Encounter not found."); return; }
        if (!player.AlesaFreed) player.MarkAlesaFreed();
        encounter.StartPrototype(); // moves the player to the maze start, hands out the revolver and silver rounds, begins the chase
        Debug.Log("[Debug jump] Alesa freed and the final wing started.");
    }
}
