using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource), typeof(AudioLowPassFilter))]
public sealed class HospitalEnvironmentEmitter : MonoBehaviour
{
    public AudioClip clip;
    public bool looping = true;
    [Range(0f, 1f)] public float level = .2f;
    public float activationDistance = 8f;
    public float fadeSeconds = 1.2f;
    public Vector2 interval = new Vector2(35f, 75f);
    public AudioClip warmup;
    public float warmupSeconds = 4.5f;
    [Range(0f, 1f)] public float warmupLevel = .06f;
    public Light startupLight;

    static float nextAtmosphericEvent;
    AudioSource sound;
    AudioLowPassFilter lowPass;
    Transform listener;
    bool blocked;
    float nextOcclusionCheck;
    float lightIntensity;
    bool lightEnabled;
    bool lightCaptured;

    void Awake()
    {
        sound = GetComponent<AudioSource>();
        lowPass = GetComponent<AudioLowPassFilter>();
        sound.playOnAwake = false;
        sound.volume = 0f;
        if (startupLight != null)
        {
            lightCaptured = true;
            lightIntensity = startupLight.intensity;
            lightEnabled = startupLight.enabled;
        }
    }

    void OnEnable()
    {
        if (Application.isPlaying) StartCoroutine(PlayEnvironment());
    }

    bool Nearby()
    {
        return listener != null && (sound.spatialBlend < .01f ||
            (listener.position - transform.position).sqrMagnitude < activationDistance * activationDistance);
    }

    void Update()
    {
        if (listener == null || sound == null) return;
        if (Time.time >= nextOcclusionCheck)
        {
            nextOcclusionCheck = Time.time + .35f;
            RaycastHit hit;
            blocked = sound.spatialBlend > .5f && Physics.Linecast(listener.position,
                transform.position, out hit, ~0, QueryTriggerInteraction.Ignore);
        }
        lowPass.cutoffFrequency = Mathf.Lerp(lowPass.cutoffFrequency, blocked ? 1100f : 18000f, Time.deltaTime * 5f);
    }

    IEnumerator PlayEnvironment()
    {
        while (listener == null)
        {
            var camera = Camera.main;
            if (camera != null && camera.GetComponent<AudioListener>() != null) listener = camera.transform;
            if (listener == null) yield return new WaitForSeconds(.5f);
        }
        if (clip == null) yield break;
        yield return new WaitForSeconds(Random.Range(.2f, 1f));
        if (warmup != null)
        {
            // Start only when the fixture is within earshot and not behind a wall.
            while (!Nearby() || blocked) yield return new WaitForSeconds(.25f);
            sound.clip = warmup;
            sound.loop = false;
            sound.pitch = 1f;
            sound.Play();
            float duration = Mathf.Min(warmupSeconds, warmup.length);
            float elapsed = 0f;
            while (elapsed < duration && Nearby())
            {
                elapsed += Time.deltaTime;
                float envelope = Mathf.Min(Mathf.Clamp01(elapsed / .2f), Mathf.Clamp01((duration - elapsed) / .35f));
                sound.volume = warmupLevel * envelope * (blocked ? .3f : 1f);
                if (startupLight != null)
                    startupLight.intensity = lightIntensity * (Mathf.PingPong(elapsed * 7f, 1f) > .55f ? .9f : .2f);
                yield return null;
            }
            sound.Stop();
            RestoreLight();
        }
        sound.clip = clip;
        sound.loop = looping;
        sound.volume = 0f;
        if (looping)
        {
            while (true)
            {
                bool near = Nearby();
                if (near && !sound.isPlaying)
                {
                    sound.pitch = 1f;
                    sound.time = Random.Range(0f, Mathf.Max(0f, clip.length - 1f));
                    sound.Play();
                }
                float target = near ? level * (blocked ? .3f : 1f) : 0f;
                sound.volume = Mathf.MoveTowards(sound.volume, target, Mathf.Max(.001f, level) * Time.deltaTime / Mathf.Max(.1f, fadeSeconds));
                if (!near && sound.volume < .001f && sound.isPlaying) sound.Stop();
                yield return null;
            }
        }
        yield return new WaitForSeconds(Random.Range(18f, 35f));
        while (true)
        {
            if (Nearby() && Time.time >= nextAtmosphericEvent)
            {
                nextAtmosphericEvent = Time.time + 18f;
                sound.pitch = Random.Range(.96f, 1.03f);
                sound.volume = level * (blocked ? .3f : 1f);
                sound.Play();
                while (sound.isPlaying)
                {
                    if (!Nearby()) sound.volume = Mathf.MoveTowards(sound.volume, 0f, level * Time.deltaTime);
                    if (sound.volume <= .001f) sound.Stop();
                    yield return null;
                }
            }
            yield return new WaitForSeconds(Random.Range(Mathf.Max(1f, interval.x), Mathf.Max(interval.x, interval.y)));
        }
    }

    void RestoreLight()
    {
        if (startupLight == null || !lightCaptured) return;
        startupLight.intensity = lightIntensity;
        startupLight.enabled = lightEnabled;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (sound != null) sound.Stop();
        RestoreLight();
    }
}
