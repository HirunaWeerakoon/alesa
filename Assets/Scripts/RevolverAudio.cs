using UnityEngine;

[DisallowMultipleComponent]
public class RevolverAudio : MonoBehaviour
{
    public AudioClip emptyClickClip;
    public AudioClip reloadClip;
    public AudioClip gunshotClip;
    public AudioClip shellFallClip;
    [Tooltip("One shell sound after this much quiet time since the final actual shot, with the trigger released.")]
    [Min(0.01f)] public float firingStopDelay = 0.55f;
    [Range(0f, 1f)] public float gunshotVolume = 1f;
    [Range(0f, 1f)] public float mechanismVolume = 0.7f;
    public AudioSource shotSource;
    public AudioSource reloadSource;
    public AudioSource mechanismSource;
    private RevolverCombat combat;
    private PlayerGameplay gameplay;
    private readonly FiringStopSoundGate shellGate = new FiringStopSoundGate();

    private void Awake()
    {
        combat = GetComponent<RevolverCombat>();
        gameplay = GetComponent<PlayerGameplay>();
        shotSource = Prepare(shotSource);
        shotSource.priority = 32;
        reloadSource = Prepare(reloadSource);
        mechanismSource = Prepare(mechanismSource);
    }

    private AudioSource Prepare(AudioSource source)
    {
        if (source == null) source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        return source;
    }

    private void OnEnable()
    {
        if (combat == null) return;
        combat.onShot.AddListener(Shot);
        combat.onEmptyClick.AddListener(EmptyClick);
        combat.onReloadStarted.AddListener(ReloadStarted);
        combat.onReloadCanceled.AddListener(StopReload);
        combat.onReloaded.AddListener(StopReload);
    }

    private void OnDisable()
    {
        if (combat != null)
        {
            combat.onShot.RemoveListener(Shot);
            combat.onEmptyClick.RemoveListener(EmptyClick);
            combat.onReloadStarted.RemoveListener(ReloadStarted);
            combat.onReloadCanceled.RemoveListener(StopReload);
            combat.onReloaded.RemoveListener(StopReload);
        }
        StopAll();
    }

    private void LateUpdate()
    {
        if (gameplay == null) gameplay = GetComponent<PlayerGameplay>();
        if (combat == null || !combat.isActiveAndEnabled || Time.timeScale <= 0 || (gameplay != null && gameplay.BlocksControls)) { StopAll(); return; }
        if (combat == null || !shellGate.TryConsume(Time.time, combat.IsTriggerHeld)) return;
        if (shellFallClip != null) mechanismSource.PlayOneShot(shellFallClip, mechanismVolume);
    }

    private void Shot()
    {
        // A new shot restarts the quiet window; it does not queue another clink.
        if (gunshotClip != null) shotSource.PlayOneShot(gunshotClip, gunshotVolume);
        shellGate.RecordShot(Time.time, Mathf.Max(firingStopDelay, combat.shotInterval + 0.05f));
    }

    private void EmptyClick()
    {
        if (emptyClickClip != null) mechanismSource.PlayOneShot(emptyClickClip, mechanismVolume);
    }

    private void ReloadStarted()
    {
        if (reloadClip != null) reloadSource.PlayOneShot(reloadClip, mechanismVolume);
    }

    private void StopReload() { if (reloadSource != null) reloadSource.Stop(); }
    private void StopAll()
    {
        shellGate.Clear();
        if (shotSource != null) shotSource.Stop();
        if (reloadSource != null) reloadSource.Stop();
        if (mechanismSource != null) mechanismSource.Stop();
    }
}
