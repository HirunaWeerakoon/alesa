using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Nathan's health. Owns the number, applies damage, drives the HUD meter, and ends the run at zero.
/// </summary>
/// <remarks>
/// Until now nothing in the game owned health: <see cref="GameplayHUDController.UpdateHealthStatus"/>
/// is presentation only and nobody called it, and <see cref="PlayerState"/> only knows alive or dead.
/// A minion reaching the player raised <c>BlindMinionAgent.AttackedPlayer</c> into empty space, so
/// being caught had no consequence at all. This component is the missing listener.
/// </remarks>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class PlayerVitals : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Tooltip("Seconds of immunity after a hit, so two minions in the same corridor cannot delete the player in one frame.")]
    [SerializeField, Min(0f)] private float invulnerabilitySeconds = 0.6f;

    [Header("References (found automatically when left empty)")]
    [SerializeField] private PlayerGameplay player;
    [SerializeField] private GameplayHUDController hud;

    [Header("Minion Wiring")]
    [Tooltip("Seconds between rescans for minions spawned after the scene loaded. 0 = scan once.")]
    [SerializeField, Min(0f)] private float minionRescanInterval = 2f;

    [Header("Debug Status (Read-Only)")]
    [SerializeField] private float currentHealth;

    private readonly HashSet<Alesa.Agents.BlindMinion.BlindMinionAgent> _wired =
        new HashSet<Alesa.Agents.BlindMinion.BlindMinionAgent>();

    private float _lastDamageTime = float.NegativeInfinity;
    private float _nextMinionScan;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthFraction => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public bool IsAlive => currentHealth > 0f;

    /// <summary>Raised after every change to <see cref="CurrentHealth"/>, including the fatal one.</summary>
    public event System.Action<float, float> HealthChanged;

    private void Awake()
    {
        currentHealth = maxHealth;
        // Not ?? — Unity's fake-null means a destroyed object is not null to the C# operator.
        if (player == null) player = GetComponent<PlayerGameplay>();
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (hud == null) hud = FindFirstObjectByType<GameplayHUDController>();
    }

    private void Start() => PushToHud();

    private void Update()
    {
        if (minionRescanInterval <= 0f && _wired.Count > 0) return;
        if (Time.time < _nextMinionScan) return;
        _nextMinionScan = Time.time + Mathf.Max(0.5f, minionRescanInterval);
        WireMinions();
    }

    /// <summary>
    /// Subscribes to every minion in the scene so no prefab or scene wiring is needed. Minions are
    /// spawned and destroyed by the hive mind, hence the periodic rescan.
    /// </summary>
    private void WireMinions()
    {
        var minions = FindObjectsByType<Alesa.Agents.BlindMinion.BlindMinionAgent>(FindObjectsSortMode.None);
        for (int i = 0; i < minions.Length; i++)
        {
            if (!_wired.Add(minions[i])) continue;
            minions[i].AttackedPlayer += OnMinionAttack;
        }

        _wired.RemoveWhere(m => m == null);
    }

    private void OnMinionAttack(Alesa.Agents.BlindMinion.BlindMinionAgent minion)
    {
        if (minion == null) return;
        TakeDamage(minion.AttackDamage);
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f || !IsAlive) return;
        if (player != null && player.State.IsTerminal) return;
        if (Time.time - _lastDamageTime < invulnerabilitySeconds) return;

        _lastDamageTime = Time.time;
        currentHealth = Mathf.Max(0f, currentHealth - damage);
        PushToHud();

        if (currentHealth <= 0f) Die();
    }

    /// <summary>Restores health without exceeding <see cref="MaxHealth"/>. Ignored once the run is over.</summary>
    public void Heal(float amount)
    {
        if (amount <= 0f || !IsAlive) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        PushToHud();
    }

    private void Die()
    {
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (player != null) player.State.SetMode(PlayerState.Mode.Dead);
    }

    private void PushToHud()
    {
        if (hud == null) hud = FindFirstObjectByType<GameplayHUDController>();
        if (hud != null) hud.UpdateHealthStatus(currentHealth, maxHealth);
        HealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void OnDestroy()
    {
        foreach (var minion in _wired)
        {
            if (minion != null) minion.AttackedPlayer -= OnMinionAttack;
        }
        _wired.Clear();
    }
}
