using UnityEngine;

/// <summary>
/// Scene-owned UI entry point for the cinematic death sequence. Keep the Canvas root
/// active and hide its CanvasGroup child, so Instance exists before the player dies.
/// Delegates fade, unscaled input, audio, and scene loading to the shared menu owner.
/// </summary>
[DisallowMultipleComponent]
public sealed class DeathScreenUIController : MonoBehaviour
{
    public static DeathScreenUIController Instance { get; private set; }
    [SerializeField] private DeathScreenManager menu;

    public bool IsShowing => menu != null && menu.IsOverlayShowing;

    /// <summary>Assign before activating a generated Canvas root.</summary>
    public void Configure(DeathScreenManager manager) { menu = manager; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one DeathScreenUIController may own the scene death menu.", this);
            enabled = false;
            return;
        }
        Instance = this;
    }

    /// <summary>Locks player state and time before the death video begins.</summary>
    public bool PrepareDeath()
    {
        if (menu != null) return menu.BeginDeathLockdown();
        Debug.LogError("Assign the DeathScreenManager on the death UI controller.", this);
        return false;
    }

    /// <summary>Shows the death movie and its retry/quit menu.</summary>
    public void ShowDeathScreen()
    {
        if (!isActiveAndEnabled || menu == null) return;
        // The menu activates the content root; this Canvas stays alive for singleton access.
        menu.ShowPreparedDeathScreen();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() { Instance = null; }
}
