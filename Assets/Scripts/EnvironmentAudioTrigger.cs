using UnityEngine;

/// <summary>
/// Plays a one-off environmental sound when the player walks into a volume.
/// </summary>
/// <remarks>
/// Optionally places itself at a random point along a path before the level starts, so a scare
/// does not land on the same floor tile every run. The path's ends are excluded: a trigger sitting
/// in a doorway fires as the player is still turning into the corridor, which reads as a bug
/// rather than a scare.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class EnvironmentAudioTrigger : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField] private AudioClip clip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("0 = plays at full volume wherever the player is; 1 = fully positional.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.85f;

    [SerializeField, Min(1f)] private float audibleRange = 30f;

    [Header("Firing")]
    [Tooltip("Fires once per play session. Off re-arms after the cooldown.")]
    [SerializeField] private bool once = true;

    [SerializeField, Min(0f)] private float retriggerCooldown = 45f;

    [Tooltip("Delay between entering the volume and the sound, so it does not land on the footstep that crossed the line.")]
    [SerializeField, Min(0f)] private float delaySeconds;

    [Header("Volume shape")]
    [SerializeField] private Vector3 size = new Vector3(6f, 3f, 6f);

    [Header("Random placement (optional)")]
    [Tooltip("Two or more points describing a path. Left empty, the trigger stays where it is placed.")]
    [SerializeField] private Vector3[] pathPoints;

    [Tooltip("Metres of path at each end that the trigger will never be placed in.")]
    [SerializeField, Min(0f)] private float endExclusion = 6f;

    private AudioSource _source;
    private BoxCollider _box;
    private bool _fired;
    private float _rearmAt;

    private void Awake()
    {
        _box = GetComponent<BoxCollider>();
        _box.isTrigger = true;
        _box.size = size;
        _box.center = Vector3.zero;

        PlaceOnPath();

        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.clip = clip;
        _source.volume = volume;
        _source.spatialBlend = spatialBlend;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 2f;
        _source.maxDistance = audibleRange;
    }

    /// <summary>Moves the trigger to a random point along <see cref="pathPoints"/>, ends excluded.</summary>
    private void PlaceOnPath()
    {
        if (pathPoints == null || pathPoints.Length < 2) return;

        var lengths = new float[pathPoints.Length - 1];
        float total = 0f;
        for (int i = 0; i < lengths.Length; i++)
        {
            lengths[i] = Vector3.Distance(pathPoints[i], pathPoints[i + 1]);
            total += lengths[i];
        }
        if (total <= 0f) return;

        // If the path is too short to exclude both ends, fall back to its middle rather than
        // silently placing the trigger in a doorway.
        float usable = total - endExclusion * 2f;
        float distance = usable <= 0f ? total * 0.5f : endExclusion + Random.value * usable;

        for (int i = 0; i < lengths.Length; i++)
        {
            if (distance > lengths[i]) { distance -= lengths[i]; continue; }
            float t = lengths[i] > 0f ? distance / lengths[i] : 0f;
            transform.position = Vector3.Lerp(pathPoints[i], pathPoints[i + 1], t);
            return;
        }

        transform.position = pathPoints[pathPoints.Length - 1];
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (_fired && once) return;
        if (Time.time < _rearmAt) return;

        _fired = true;
        _rearmAt = Time.time + retriggerCooldown;

        if (_source == null || clip == null) return;
        if (delaySeconds > 0f) _source.PlayDelayed(delaySeconds);
        else _source.Play();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0f, 0.35f);
        Gizmos.DrawWireCube(transform.position, size);

        if (pathPoints == null || pathPoints.Length < 2) return;
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.9f);
        for (int i = 0; i < pathPoints.Length - 1; i++) Gizmos.DrawLine(pathPoints[i], pathPoints[i + 1]);
        foreach (var p in pathPoints) Gizmos.DrawWireSphere(p, 0.3f);
    }
}
