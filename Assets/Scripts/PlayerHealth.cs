using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hit-based player health: the player survives <see cref="hitsToDie"/> - 1 hits and dies on the last one.
/// Death goes through PlayerGameplay.EndRun so the existing death UI/sequence stays the single authority.
/// This component only reports health to the HUD; the HUD never decides damage or death.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerHealth : MonoBehaviour, IDamageable
{
    [Min(1)] public int hitsToDie = 3;
    [Min(0f)] public float invulnerableSeconds = 1f;
    [Min(0.05f)] public float flashSeconds = 0.5f;
    public UnityEvent onHit = new UnityEvent();

    private PlayerGameplay gameplay;
    private int hitsTaken;
    private float nextHitAllowed, flashUntil;

    public int HitsTaken => hitsTaken;
    public int HitsRemaining => Mathf.Max(0, hitsToDie - hitsTaken);

    private void Awake() { gameplay = GetComponent<PlayerGameplay>(); }

    /// <summary>Any positive damage counts as one hit.</summary>
    public void TakeDamage(float damage)
    {
        if (damage <= 0f || gameplay == null || gameplay.State.IsTerminal) return;
        if (Time.time < nextHitAllowed) return;
        nextHitAllowed = Time.time + invulnerableSeconds;
        hitsTaken++;
        flashUntil = Time.unscaledTime + flashSeconds;
        PushToHud();
        onHit.Invoke();
        if (hitsTaken >= hitsToDie) { gameplay.EndRun(false); return; }
        gameplay.Notify($"You are hurt. {HitsRemaining} more hit{(HitsRemaining == 1 ? "" : "s")} and you are dead.", 2.5f);
    }

    private void PushToHud()
    {
        float max = 100f, current = max * HitsRemaining / hitsToDie;
        var hud = FindFirstObjectByType<GameplayHUDController>();
        if (hud != null) hud.UpdateHealthStatus(current, max);
        var menu = FindFirstObjectByType<AnalogPauseMenu>(FindObjectsInactive.Include);
        if (menu != null) menu.UpdateHealthDisplay(current, max);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime >= flashUntil) return;
        float alpha = 0.45f * Mathf.Clamp01((flashUntil - Time.unscaledTime) / flashSeconds);
        var previous = GUI.color;
        GUI.color = new Color(0.7f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previous;
    }
}
