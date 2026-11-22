using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Routes actual death and the development-only K shortcut to the video death menu.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerGameplay))]
public sealed class PlayerDeathSequence : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private DeathScreenUIController deathUI;
    [SerializeField] private bool enableDebugKey = true;
    [SerializeField] private Key debugKey = Key.K;
    public bool IsDying { get; private set; }
    private PlayerGameplay player;

    public void Configure(Camera camera, DeathScreenUIController ui) { playerCamera = camera; deathUI = ui; }
    private void Awake() { player = GetComponent<PlayerGameplay>(); }
    private void OnEnable()
    {
        if (player == null) player = GetComponent<PlayerGameplay>();
        player.State.Changed += OnStateChanged;
    }
    private void OnDisable() { if (player != null) player.State.Changed -= OnStateChanged; }
    private void OnStateChanged() { if (player.State.IsDead) TriggerDeath(); }
    private void Update()
    {
        if (player.State.IsDead && !IsDying) { TriggerDeath(); return; }
        var keyboard = Keyboard.current;
        if (enableDebugKey && (Application.isEditor || Debug.isDebugBuild) && debugKey != Key.None &&
            keyboard != null && keyboard[debugKey].wasPressedThisFrame) TriggerDeath();
    }

    /// <summary>Call from damage logic when the player dies. Repeated calls are ignored.</summary>
    [ContextMenu("Test Death Sequence (Play Mode)")]
    public void TriggerDeath()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || IsDying) return;
        if (deathUI == null) deathUI = DeathScreenUIController.Instance;
        if (deathUI == null) return;
        // Set the guard before lockdown changes the central state and invokes our callback.
        IsDying = true;
        if (!deathUI.PrepareDeath()) { IsDying = false; return; }
        foreach (var source in GetComponentsInChildren<AudioSource>(true)) source.Stop();
        deathUI.ShowDeathScreen();
    }
}
