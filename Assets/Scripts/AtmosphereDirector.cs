using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// Makes the picture react to how the player is doing. Calm, it is a muted, slightly cold, grainy image with
/// dark corners. As health falls the corners close in, colour drains and the edges smear; taking a hit
/// jolts all of that for a moment; and when the hive mind says the pressure is high, the grain thickens.
/// </summary>
/// <remarks>
/// It only changes values on a runtime copy of the volume's profile, so the profile asset is never edited
/// by playing. Each effect is optional: if the profile lacks one, that part is simply skipped.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(PostProcessVolume))]
public sealed class AtmosphereDirector : MonoBehaviour
{
    [Header("Calm")]
    [Range(0f, 1f)] public float vignetteCalm = 0.36f;
    [Range(0f, 1f)] public float grainCalm = 0.28f;
    [Range(0f, 1f)] public float aberrationCalm = 0.10f;
    [Range(-100f, 0f)] public float saturationCalm = -18f;

    [Header("Low health")]
    [Range(0f, 1f)] public float vignetteHurt = 0.62f;
    [Range(0f, 1f)] public float grainHurt = 0.5f;
    [Range(0f, 1f)] public float aberrationHurt = 0.5f;
    [Range(-100f, 0f)] public float saturationHurt = -58f;

    [Header("Taking a hit")]
    [UnityEngine.Min(0.05f)] public float hitPulseSeconds = 0.4f;
    [Range(0f, 1f)] public float hitVignetteBoost = 0.2f;
    [Range(0f, 1f)] public float hitAberrationBoost = 0.45f;

    [Header("Pressure (hive mind)")]
    [Tooltip("Extra grain at full pressure.")]
    [Range(0f, 0.5f)] public float grainPressureBoost = 0.15f;

    [Tooltip("How quickly the picture follows changes (higher = faster).")]
    [UnityEngine.Min(0.1f)] public float response = 2.5f;

    private PostProcessVolume _volume;
    private Vignette _vignette;
    private Grain _grain;
    private ChromaticAberration _aberration;
    private ColorGrading _grading;
    private PlayerVitals _vitals;
    private HiveMindRunner _hive;
    private float _pulse;       // 0..1, decays after a hit
    private float _hurt;        // smoothed 0..1
    private float _pressure;    // smoothed 0..1
    private bool _subscribed;
    private float _nextLookup;

    private void Awake()
    {
        _volume = GetComponent<PostProcessVolume>();
        var profile = _volume.profile; // a runtime copy; the asset on disk is untouched
        profile.TryGetSettings(out _vignette);
        profile.TryGetSettings(out _grain);
        profile.TryGetSettings(out _aberration);
        profile.TryGetSettings(out _grading);
    }

    private void OnDisable()
    {
        if (_vitals != null && _subscribed) _vitals.HealthChanged -= OnHealthChanged;
        _subscribed = false;
    }

    private void OnHealthChanged(float current, float max)
    {
        // A hit (health went down) jolts the picture. Healing does not.
        float fraction = max > 0f ? current / max : 0f;
        if (fraction < _lastFraction - 0.001f) _pulse = 1f;
        _lastFraction = fraction;
    }

    private float _lastFraction = 1f;

    private void Update()
    {
        if (Time.time >= _nextLookup && (_vitals == null || _hive == null))
        {
            _nextLookup = Time.time + 1f;
            if (_vitals == null) _vitals = FindFirstObjectByType<PlayerVitals>();
            if (_hive == null) _hive = FindFirstObjectByType<HiveMindRunner>();
            if (_vitals != null && !_subscribed)
            {
                _vitals.HealthChanged += OnHealthChanged;
                _lastFraction = _vitals.HealthFraction;
                _subscribed = true;
            }
        }

        float dt = Time.unscaledDeltaTime;
        float follow = 1f - Mathf.Exp(-response * dt);

        float hurtTarget = _vitals != null ? 1f - _vitals.HealthFraction : 0f;
        _hurt = Mathf.Lerp(_hurt, hurtTarget, follow);

        float pressureTarget = 0f;
        if (_hive != null && _hive.Director != null) pressureTarget = Mathf.Clamp01(_hive.Director.Intensity.Intensity);
        _pressure = Mathf.Lerp(_pressure, pressureTarget, follow);

        _pulse = Mathf.Max(0f, _pulse - dt / hitPulseSeconds);
        float pulse = _pulse * _pulse;

        if (_vignette != null) _vignette.intensity.value = Mathf.Lerp(vignetteCalm, vignetteHurt, _hurt) + hitVignetteBoost * pulse;
        if (_aberration != null) _aberration.intensity.value = Mathf.Lerp(aberrationCalm, aberrationHurt, _hurt) + hitAberrationBoost * pulse;
        if (_grain != null) _grain.intensity.value = Mathf.Clamp01(Mathf.Lerp(grainCalm, grainHurt, _hurt) + grainPressureBoost * _pressure);
        if (_grading != null) _grading.saturation.value = Mathf.Lerp(saturationCalm, saturationHurt, _hurt);
    }
}
