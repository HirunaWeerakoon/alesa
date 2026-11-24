using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A failing fluorescent tube. Steady with a faint hum, then (if <see cref="idleFlicker"/> is on) every few
/// seconds it stutters: a burst of rapid dips, now and then ending in a second or two of darkness. The
/// irregular timing is what makes it unsettling; a regular flicker reads as a decoration.
///
/// Every light with this component can also be <see cref="Disturb"/>ed on demand, which is how the game
/// cuts the lights near an ambush or just before the boss appears, whether or not that light flickers by itself.
/// </summary>
[RequireComponent(typeof(Light))]
public sealed class LightFlicker : MonoBehaviour
{
    private static readonly List<LightFlicker> All = new List<LightFlicker>();

    [Tooltip("Whether this light stutters on its own. Off, it stays steady until something disturbs it.")]
    public bool idleFlicker = true;

    [Header("Between stutters")]
    [Tooltip("Seconds of steady light between stutters, chosen at random in this range.")]
    public Vector2 steadySeconds = new Vector2(4f, 14f);

    [Header("A stutter")]
    [Tooltip("How long one burst of flickering lasts (s).")]
    public Vector2 burstSeconds = new Vector2(0.15f, 0.7f);

    [Tooltip("Chance a stutter ends with the light going out for a moment before it comes back.")]
    [Range(0f, 1f)] public float blackoutChance = 0.3f;

    [Tooltip("How long that darkness lasts (s).")]
    public Vector2 blackoutSeconds = new Vector2(0.6f, 2.2f);

    [Header("Hum")]
    [Tooltip("Fraction of brightness the light wavers by even when steady. 0 = perfectly constant.")]
    [Range(0f, 0.15f)] public float humDepth = 0.03f;

    private Light _light;
    private float _baseIntensity;
    private float _seed;
    private bool _stuttering;
    private bool _disturbed;

    /// <summary>
    /// Flickers and then darkens every such light within <paramref name="radius"/> of a point, for about
    /// <paramref name="seconds"/>. Used before an ambush and when the boss is about to appear.
    /// </summary>
    public static void DisturbNear(Vector3 point, float radius, float seconds)
    {
        float sqr = radius * radius;
        for (int i = 0; i < All.Count; i++)
        {
            var f = All[i];
            if (f == null || !f.isActiveAndEnabled || !f._light.enabled) continue;
            if ((f.transform.position - point).sqrMagnitude > sqr) continue;
            f.Disturb(seconds * Random.Range(0.8f, 1.25f));
        }
    }

    /// <summary>A burst of flicker, darkness for about <paramref name="seconds"/>, then it catches again.</summary>
    public void Disturb(float seconds)
    {
        if (!_disturbed) StartCoroutine(DisturbRoutine(seconds));
    }

    private void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light.intensity;
        _seed = Random.value * 100f;
    }

    private void OnEnable()
    {
        All.Add(this);
        StartCoroutine(Cycle());
    }

    private void OnDisable()
    {
        All.Remove(this);
        _stuttering = false;
        _disturbed = false;
        if (_light != null) _light.intensity = _baseIntensity;
    }

    private void Update()
    {
        // Hum: slow, low-amplitude wobble, only while the light is not mid-stutter.
        if (!_stuttering && !_disturbed && humDepth > 0f)
            _light.intensity = _baseIntensity * (1f + (Mathf.PerlinNoise(_seed, Time.time * 6f) - 0.5f) * 2f * humDepth);
    }

    private IEnumerator Cycle()
    {
        // Start at a random point in the cycle so the lights never stutter together.
        yield return new WaitForSeconds(Random.Range(0f, steadySeconds.y));
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(steadySeconds.x, steadySeconds.y));
            if (!idleFlicker || _disturbed) continue;

            _stuttering = true;
            yield return Burst(Random.Range(burstSeconds.x, burstSeconds.y));

            if (Random.value < blackoutChance)
            {
                _light.intensity = 0f;
                yield return new WaitForSeconds(Random.Range(blackoutSeconds.x, blackoutSeconds.y));
                // Catches again with one last weak flicker.
                _light.intensity = _baseIntensity * 0.35f;
                yield return new WaitForSeconds(0.06f);
            }

            _light.intensity = _baseIntensity;
            _stuttering = false;
        }
    }

    private IEnumerator DisturbRoutine(float seconds)
    {
        _disturbed = true;
        yield return Burst(Random.Range(0.25f, 0.6f));
        _light.intensity = 0f;
        yield return new WaitForSeconds(Mathf.Max(0.2f, seconds));
        _light.intensity = _baseIntensity * 0.4f;
        yield return new WaitForSeconds(0.08f);
        _light.intensity = _baseIntensity;
        _disturbed = false;
    }

    // Rapid, uneven dips for a short while.
    private IEnumerator Burst(float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            float step = Random.Range(0.025f, 0.09f);
            _light.intensity = _baseIntensity * (Random.value < 0.5f ? Random.Range(0f, 0.25f) : Random.Range(0.5f, 1f));
            yield return new WaitForSeconds(step);
            t += step;
        }
    }
}
