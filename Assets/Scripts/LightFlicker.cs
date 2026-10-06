using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    private Light bulb;
    private float normalIntensity;

    [Header("Intensity Settings")]
    [Tooltip("The low intensity value during the flicker.")]
    public float dimmedIntensity = 0.2f;

    [Header("Timings")]
    [Tooltip("Pause between the double-flicker burst cycles (in seconds)")]
    public float minWaitTime = 2.0f;
    public float maxWaitTime = 3.0f;

    [Tooltip("How long the light stays dimmed during each individual dip")]
    public float flickerDuration = 0.08f;

    [Tooltip("Short pause between the first and second flicker")]
    public float gapBetweenFlickers = 0.12f;

    void Start()
    {
        bulb = GetComponent<Light>();
        normalIntensity = bulb.intensity;
        StartCoroutine(DoubleFlickerRoutine());
    }

    private IEnumerator DoubleFlickerRoutine()
    {
        while (true)
        {
            // 1. Wait for the primary 2-3 second interval
            float waitTime = Random.Range(minWaitTime, maxWaitTime);
            yield return new WaitForSeconds(waitTime);

            // 2. First flicker dip
            bulb.intensity = dimmedIntensity;
            yield return new WaitForSeconds(flickerDuration);
            bulb.intensity = normalIntensity;

            // 3. Brief gap before the second dip
            yield return new WaitForSeconds(gapBetweenFlickers);

            // 4. Second flicker dip
            bulb.intensity = dimmedIntensity;
            yield return new WaitForSeconds(flickerDuration);
            bulb.intensity = normalIntensity;
        }
    }
}