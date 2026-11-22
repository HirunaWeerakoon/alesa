using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Presentation-only HUD. Gameplay systems supply changes through its public API.</summary>
[DisallowMultipleComponent]
public sealed class GameplayHUDController : MonoBehaviour
{
    [Serializable]
    public sealed class ViewReferences
    {
        public CanvasGroup root, crosshair, hints;
        public TMP_Text noiseHeader, actionLabel, detectionLabel, weaponLabel, healthLabel, hintLabel;
        public Image[] noiseTicks, healthSegments;
        public RectTransform dangerNotch;
        public Image vignette;
        public ECGWaveGraphic ecg;
    }

    [Header("Canvas references")]
    [SerializeField] private ViewReferences view = new ViewReferences();
    [Header("Noise (normalised hearing radius, not decibels)")]
    [SerializeField, Range(0.01f, 1f)] private float dangerThreshold = 0.75f;
    [SerializeField, Range(0f, 1f)] private float suspiciousThreshold = 0.15f;
    [SerializeField, Min(0.01f)] private float decaySpeed = 0.65f;
    [SerializeField] private Color quietTick = new Color(0.33f, 0.35f, 0.33f, 0.65f);
    [SerializeField] private Color activeTick = new Color(0.85f, 0.87f, 0.8f);
    [SerializeField] private Color dangerColor = new Color(0.9f, 0.15f, 0.12f);
    [Header("Health")]
    [SerializeField, Range(0f, 1f)] private float dangerHealth = 0.25f;
    [SerializeField, Range(0f, 1f)] private float cautionHealth = 0.6f;
    [SerializeField] private Color fineColor = new Color(0.3f, 0.8f, 0.4f);
    [SerializeField] private Color cautionColor = new Color(1f, 0.63f, 0.15f);
    [Header("Hints and crosshair")]
    [SerializeField] private bool hintsEnabled = true;
    [SerializeField] private bool autoFadeHints = true;
    [SerializeField, Min(0f)] private float hintsDuration = 10f;
    [SerializeField, Min(0.1f)] private float fadeSpeed = 4f;

    [SerializeField, Min(1f)] private float idleCrosshairSize = 36f;
    [SerializeField, Min(1f)] private float walkingCrosshairSize = 48f;
    [SerializeField, Min(1f)] private float sprintCrosshairSize = 64f;
    [SerializeField, Min(1f)] private float airborneCrosshairSize = 76f;
    [SerializeField, Min(1f)] private float crouchCrosshairSize = 26f;
    [SerializeField, Min(0.1f)] private float crosshairResponse = 12f;
    private float crosshairSize = 36f, targetCrosshairSize = 36f;

    /// <summary>Supplies locomotion state; airborne expansion takes priority over crouching.</summary>
    public void SetMovementState(bool grounded, bool sprinting, bool crouching, bool moving)
    {
        targetCrosshairSize = !grounded ? airborneCrosshairSize : crouching ? crouchCrosshairSize :
            sprinting && moving ? sprintCrosshairSize : moving ? walkingCrosshairSize : idleCrosshairSize;
    }

    /// <summary>Raised only when an API reports noise, never for visual decay.</summary>
    public event Action<float, string> NoiseReported;
    public event Action<string, int, int> WeaponAmmoChanged;
    public event Action<float, float> HealthStatusChanged;
    public event Action<bool> AimingStateChanged;

    public float NoiseLevel => noise;
    public float HealthFraction => health;
    private int displayedNoiseCount = -1;
    private Color displayedLit, displayedQuiet;
    private float noise, health = 1f, gameplayElapsed, warningPulse;
    private bool aiming, interacting, visible = true;
    private string action = "IDLE", detectionOverride;

    /// <summary>Assigns a generated Canvas before activating this controller's object.</summary>
    public void ConfigureView(ViewReferences references) { view = references ?? new ViewReferences(); displayedNoiseCount = -1; }

    private void Awake()
    {
        SetLabel(view.noiseHeader, "NOISE ACTIVITY");
        SetLabel(view.hintLabel, "E Interact | RMB Aim | LMB Fire | R Reload | Esc Pause");
        UpdateWeaponAmmo("UNARMED", 0, 0);
        UpdateHealthStatus(100f, 100f);
        SetVisible(true);
        RefreshNoise();
    }

    /// <summary>Snaps up to a reported normalised level; quieter events cannot erase a loud peak.</summary>
    public void ReportNoise(float normalizedNoiseLevel, string actionName)
    {
        if (!IsFinite(normalizedNoiseLevel) || normalizedNoiseLevel <= 0f) return;
        float level = Mathf.Clamp01(normalizedNoiseLevel);
        if (level >= noise)
        {
            if (noise < dangerThreshold && level >= dangerThreshold) warningPulse = 0.35f;
            noise = level;
            action = string.IsNullOrEmpty(actionName) ? "NOISE" : actionName;
            RefreshNoise();
        }
        NoiseReported?.Invoke(level, actionName);
    }

    /// <summary>Updates ammunition text only when the weapon controller reports a change.</summary>
    public void UpdateWeaponAmmo(string weaponName, int currentClip, int totalReserve)
    {
        weaponName = string.IsNullOrEmpty(weaponName) ? "UNARMED" : weaponName;
        currentClip = Mathf.Max(0, currentClip); totalReserve = Mathf.Max(0, totalReserve);
        SetLabel(view.weaponLabel, $"{weaponName}  {currentClip} / {totalReserve}");
        WeaponAmmoChanged?.Invoke(weaponName, currentClip, totalReserve);
    }

    /// <summary>Updates health presentation; this method neither applies damage nor triggers death.</summary>
    public void UpdateHealthStatus(float currentHealth, float maxHealth)
    {
        if (!IsFinite(currentHealth) || !IsFinite(maxHealth) || maxHealth <= 0f) return;
        float current = Mathf.Clamp(currentHealth, 0f, maxHealth);
        health = current / maxHealth;
        bool danger = health <= dangerHealth;
        Color tint = danger ? dangerColor : health <= cautionHealth ? cautionColor : fineColor;
        SetLabel(view.healthLabel, danger ? "DANGER" : health <= cautionHealth ? "CAUTION" : "FINE");
        if (view.healthLabel != null) view.healthLabel.color = tint;
        if (view.healthSegments != null)
        {
            int count = Mathf.CeilToInt(health * view.healthSegments.Length);
            for (int i = 0; i < view.healthSegments.Length; i++)
                if (view.healthSegments[i] != null) view.healthSegments[i].color = i < count ? tint : quietTick;
        }
        if (view.ecg != null)
            view.ecg.SetStatus(health <= 0f ? 0f : danger ? 125f : health <= cautionHealth ? 95f : 65f, tint, danger);
        HealthStatusChanged?.Invoke(current, maxHealth);
    }

    /// <summary>Fades the crosshair away while aiming down sights.</summary>
    public void SetAimingState(bool isAiming)
    {
        if (aiming == isAiming) return;
        aiming = isAiming;
        AimingStateChanged?.Invoke(isAiming);
    }

    /// <summary>Hides the crosshair while an interaction owns the player's view.</summary>
    public void SetInteractionState(bool isInteracting) { interacting = isInteracting; }

    /// <summary>Optional actual AI alert label. Null/empty restores the noise-based estimate.</summary>
    public void SetDetectionStatus(string status) { detectionOverride = status; RefreshNoise(); }

    /// <summary>Settings hook; optionally shows the control hints for another full duration.</summary>
    public void SetControlHintsEnabled(bool enabled, bool restartTimer = false)
    {
        hintsEnabled = enabled;
        if (restartTimer) gameplayElapsed = 0f;
    }

    /// <summary>Hides the HUD for pause, notes, keypad UI, and death without losing subscriptions.</summary>
    public void SetVisible(bool show)
    {
        visible = show;
        if (view.root != null)
        {
            view.root.alpha = show ? 1f : 0f;
            view.root.interactable = view.root.blocksRaycasts = false;
        }
        if (view.ecg != null) view.ecg.enabled = show;
    }

    private void Update()
    {
        if (!visible || Time.deltaTime <= 0f) return;
        gameplayElapsed += Time.deltaTime;
        if (noise > 0f || warningPulse > 0f)
        {
            noise = Mathf.MoveTowards(noise, 0f, decaySpeed * Time.deltaTime);
            warningPulse = Mathf.Max(0f, warningPulse - Time.deltaTime);
            if (noise <= 0f) action = "IDLE";
            RefreshNoise();
        }
        if (crosshairSize != targetCrosshairSize)
        {
            crosshairSize = Mathf.Lerp(crosshairSize, targetCrosshairSize, 1f - Mathf.Exp(-crosshairResponse * Time.deltaTime));
            if (Mathf.Abs(crosshairSize - targetCrosshairSize) < 0.05f) crosshairSize = targetCrosshairSize;
            if (view.crosshair != null) ((RectTransform)view.crosshair.transform).sizeDelta = Vector2.one * crosshairSize;
        }
        Fade(view.crosshair, aiming || interacting ? 0f : 0.65f);
        Fade(view.hints, hintsEnabled && (!autoFadeHints || gameplayElapsed < hintsDuration) ? 0.7f : 0f);
        if (view.vignette != null)
        {
            float alpha = health <= dangerHealth ? 0.12f + 0.16f * (0.5f + 0.5f * Mathf.Sin(Time.time * 7f)) : 0f;
            Color color = dangerColor; color.a = alpha;
            view.vignette.color = color;
        }
    }

    private void RefreshNoise()
    {
        SetLabel(view.actionLabel, action);
        SetLabel(view.detectionLabel, !string.IsNullOrEmpty(detectionOverride) ? detectionOverride :
            noise >= dangerThreshold ? "HEARD" : noise >= suspiciousThreshold ? "SUSPICIOUS" : "SILENT");
        if (view.dangerNotch != null)
        {
            Vector2 anchor = new Vector2(Mathf.Clamp01(dangerThreshold), 0.5f);
            view.dangerNotch.anchorMin = view.dangerNotch.anchorMax = anchor;
        }
        if (view.noiseTicks == null) return;
        int count = Mathf.CeilToInt(noise * view.noiseTicks.Length);
        Color lit = noise >= dangerThreshold || warningPulse > 0f ? dangerColor : activeTick;
        if (warningPulse > 0f) lit = Color.Lerp(lit, Color.white, warningPulse * 1.2f);
        if (count == displayedNoiseCount && lit == displayedLit && quietTick == displayedQuiet) return;
        displayedNoiseCount = count; displayedLit = lit; displayedQuiet = quietTick;
        for (int i = 0; i < view.noiseTicks.Length; i++)
        {
            var tick = view.noiseTicks[i];
            Color color = i < count ? lit : quietTick;
            if (tick != null && tick.color != color) tick.color = color;
        }
    }

    private void Fade(CanvasGroup group, float target)
    {
        if (group != null && group.alpha != target) group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.deltaTime);
    }
    private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void SetLabel(TMP_Text target, string value)
    {
        if (target != null && target.text != value) target.text = value;
    }

    [ContextMenu("Preview Health/Fine")] private void PreviewFine() { UpdateHealthStatus(100f, 100f); }
    [ContextMenu("Preview Health/Caution")] private void PreviewCaution() { UpdateHealthStatus(50f, 100f); }
    [ContextMenu("Preview Health/Danger")] private void PreviewDanger() { UpdateHealthStatus(15f, 100f); }
}
