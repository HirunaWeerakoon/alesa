using System.Collections;
using UnityEngine;

/// <summary>
/// Nathan's own voice: what he makes when he is hit, and the breathing that gives away how close
/// to finished he is.
/// </summary>
/// <remarks>
/// Driven entirely off <see cref="PlayerVitals.HealthChanged"/> rather than from the minion's side,
/// so it covers every source of damage — a swing, a bite, anything added later — without each one
/// having to remember to make a noise.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerVitals))]
public sealed class PlayerVoice : MonoBehaviour
{
    [Header("Hurt, in order")]
    [Tooltip("The first time he is hurt.")]
    [SerializeField] private AudioClip firstHurt;

    [Tooltip("The second time, and every non-fatal hit after it, alternating with the first.")]
    [SerializeField] private AudioClip secondHurt;

    [Tooltip("The hit that takes him to zero, played before the death screen appears.")]
    [SerializeField] private AudioClip fatalHurt;

    [SerializeField, Range(0f, 1f)] private float hurtVolume = 1f;

    [Header("Running out")]
    [Tooltip("Fast breathing, looped, once health drops below the threshold. Fades in and out.")]
    [SerializeField] private AudioClip lowHealthBreathing;

    [Tooltip("Fraction of full health below which the breathing starts.")]
    [SerializeField, Range(0.05f, 0.9f)] private float lowHealthFraction = 0.34f;

    [SerializeField, Range(0f, 1f)] private float breathingVolume = 0.8f;

    [Tooltip("Seconds to fade the breathing in when he drops low, and out again when he recovers.")]
    [SerializeField, Min(0.05f)] private float breathingFadeSeconds = 0.9f;

    private PlayerVitals _vitals;
    private AudioSource _hurtSource;
    private AudioSource _breathSource;
    private int _hurtCount;
    private bool _breathing;
    private Coroutine _fade;

    // Seeded from the real value, not from a sentinel: PlayerVitals raises HealthChanged once at
    // Start just to populate the HUD, and against a sentinel that first push reads as a wound.
    private float _lastHealth;

    private void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _lastHealth = _vitals.CurrentHealth;

        // 2D: this is Nathan's own throat and his own lungs, not something in the room with him.
        _hurtSource = CreateSource("Audio_Hurt", hurtVolume, false);
        _breathSource = CreateSource("Audio_Breathing", 0f, true);
    }

    private AudioSource CreateSource(string label, float volume, bool loop)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        src.volume = volume;
        src.loop = loop;
        return src;
    }

    private void OnEnable()
    {
        if (_vitals != null) _vitals.HealthChanged += OnHealthChanged;
    }

    private void OnDisable()
    {
        if (_vitals != null) _vitals.HealthChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(float current, float max)
    {
        float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (current < _lastHealth) OnHurt(current);
        _lastHealth = current;

        UpdateBreathing(current > 0f && fraction <= lowHealthFraction);
    }

    private void OnHurt(float remaining)
    {
        AudioClip clip;
        if (remaining <= 0f) clip = fatalHurt;
        else if (_hurtCount == 0) clip = firstHurt;
        else if (_hurtCount == 1) clip = secondHurt;
        // Past the second, the two alternate rather than the later one repeating forever.
        else clip = (_hurtCount % 2 == 0) ? firstHurt : secondHurt;

        _hurtCount++;

        if (clip == null || _hurtSource == null) return;
        _hurtSource.volume = hurtVolume;
        _hurtSource.PlayOneShot(clip);
    }

    private void UpdateBreathing(bool shouldBreathe)
    {
        if (shouldBreathe == _breathing) return;
        _breathing = shouldBreathe;

        if (_breathSource == null || lowHealthBreathing == null) return;

        if (shouldBreathe)
        {
            _breathSource.clip = lowHealthBreathing;
            if (!_breathSource.isPlaying) _breathSource.Play();
        }

        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(FadeBreathing(shouldBreathe ? breathingVolume : 0f));
    }

    private IEnumerator FadeBreathing(float target)
    {
        float start = _breathSource.volume;
        for (float t = 0f; t < breathingFadeSeconds; t += Time.unscaledDeltaTime)
        {
            _breathSource.volume = Mathf.Lerp(start, target, t / breathingFadeSeconds);
            yield return null;
        }
        _breathSource.volume = target;
        // Stopping only once silent keeps a brief dip-and-recover from clicking.
        if (target <= 0f) _breathSource.Stop();
        _fade = null;
    }
}
