using UnityEngine;

/// <summary>
/// An emergency light that swells and fades on a slow cycle, like a warning beacon in a failing building.
/// The pulse is sharpened so the light spends most of its time dim and only briefly flares, which reads far
/// more urgent than a smooth sine.
/// </summary>
[RequireComponent(typeof(Light))]
public sealed class PulseLight : MonoBehaviour
{
    [Tooltip("Pulses per second.")]
    [Min(0.05f)] public float hertz = 0.7f;

    [Tooltip("Brightness at the bottom and top of the pulse, as a multiple of the light's own intensity.")]
    public Vector2 intensityRange = new Vector2(0.05f, 1f);

    [Tooltip("Higher = a sharper, shorter flare.")]
    [Range(1f, 6f)] public float sharpness = 3f;

    private Light _light;
    private float _baseIntensity;
    private float _phase;

    private void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light.intensity;
        _phase = Random.value * Mathf.PI * 2f; // so neighbouring beacons do not pulse in step
    }

    private void Update()
    {
        float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * hertz * Mathf.PI * 2f + _phase);
        wave = Mathf.Pow(wave, sharpness);
        _light.intensity = _baseIntensity * Mathf.Lerp(intensityRange.x, intensityRange.y, wave);
    }
}
