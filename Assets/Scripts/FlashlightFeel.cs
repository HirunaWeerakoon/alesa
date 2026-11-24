using UnityEngine;
using Alesa.Agents.BlindMinion;

/// <summary>
/// Makes the flashlight feel held rather than bolted to the camera: the beam trails a little behind when you
/// turn, so it sweeps across walls instead of snapping. And when something hostile is close, the torch
/// stutters, the way a torch does in the films.
/// </summary>
[RequireComponent(typeof(Flashlight))]
public sealed class FlashlightFeel : MonoBehaviour
{
    [Header("Sway")]
    [Tooltip("How quickly the beam catches up with where you look (higher = tighter).")]
    [Min(1f)] public float followSpeed = 14f;

    [Header("Danger flicker")]
    [Tooltip("A hostile this close (m) makes the torch stutter now and then.")]
    [Min(1f)] public float dangerDistance = 7f;

    [Tooltip("Seconds between stutters while something is close, chosen at random in this range.")]
    public Vector2 stutterGap = new Vector2(2.5f, 6f);

    [Min(0.1f)] public float stutterSeconds = 0.45f;

    private Flashlight _flashlight;
    private Transform _parent;
    private Quaternion _localRotation;
    private Quaternion _smoothed;
    private float _nextStutter;
    private float _nextScan;
    private BlindMinionAgent[] _minions = new BlindMinionAgent[0];
    private PossessedBoss _boss;

    private void Awake()
    {
        _flashlight = GetComponent<Flashlight>();
        _parent = transform.parent;
        _localRotation = transform.localRotation;
        _smoothed = transform.rotation;
    }

    private void LateUpdate()
    {
        if (_parent != null)
        {
            Quaternion target = _parent.rotation * _localRotation;
            _smoothed = Quaternion.Slerp(_smoothed, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            transform.rotation = _smoothed;
        }

        if (Time.time >= _nextScan)
        {
            _nextScan = Time.time + 0.5f;
            _minions = FindObjectsByType<BlindMinionAgent>(FindObjectsSortMode.None);
            if (_boss == null) _boss = FindFirstObjectByType<PossessedBoss>();
        }

        if (Time.time < _nextStutter || !_flashlight.isOn || !HostileNearby()) return;
        _nextStutter = Time.time + Random.Range(stutterGap.x, stutterGap.y);
        _flashlight.triggerFlicker(stutterSeconds);
    }

    private bool HostileNearby()
    {
        Vector3 here = transform.position;
        float sqr = dangerDistance * dangerDistance;
        foreach (var minion in _minions)
        {
            if (minion == null || !minion.isActiveAndEnabled) continue;
            if ((minion.transform.position - here).sqrMagnitude <= sqr) return true;
        }
        return _boss != null && _boss.IsPresent && (_boss.transform.position - here).sqrMagnitude <= sqr * 2f;
    }
}
