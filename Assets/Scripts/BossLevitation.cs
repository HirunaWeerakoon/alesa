using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Makes the possessed boss float instead of walk: she hovers a little above the floor, bobs slowly,
/// and her body hangs in an unnatural pose (head lolled to one side, arms drifting forward, legs
/// dangling with the toes pointed down) that never quite settles.
/// </summary>
/// <remarks>
/// No animation clip is needed. The animator is held on its idle pose, and the pose is bent on top of
/// that every frame in LateUpdate, after the animator has run. Attacks, the scream and death play
/// normally: during them she sinks back to the floor and the pose fades out, so the finishers that
/// grab the player still line up.
/// Runs after <see cref="PossessedBoss"/> so it has the last word on the walk animation.
/// </remarks>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class BossLevitation : MonoBehaviour
{
    [Header("Hover")]
    [Tooltip("Height of her feet above the floor (m).")]
    [Min(0f)] public float hoverHeight = 0.7f;
    [Tooltip("How far she rises and falls while hovering (m).")]
    [Min(0f)] public float bobAmount = 0.08f;
    [Tooltip("Seconds for one rise and fall.")]
    [Min(0.1f)] public float bobPeriod = 3.2f;
    [Tooltip("Seconds to rise into the float or sink out of it.")]
    [Min(0.01f)] public float blendSeconds = 0.6f;

    [Header("Pose (degrees)")]
    public float headTilt = 28f;
    public float headDroop = 18f;
    public float spineLean = 10f;
    [Tooltip("How far the arms reach forward from hanging straight down.")]
    public float armReach = 45f;
    [Tooltip("How far the arms spread out to the sides.")]
    public float armSpread = 12f;
    public float elbowBend = 20f;
    [Tooltip("How far the shins hang back from the knees.")]
    public float kneeBend = 22f;
    [Tooltip("How far the toes point down.")]
    public float toePoint = 45f;

    [Header("Drift")]
    [Tooltip("How much every part of the pose wanders, so she is never still (degrees).")]
    public float driftAmount = 7f;
    [Tooltip("How fast the pose wanders.")]
    public float driftSpeed = 0.35f;

    private PossessedBoss _boss;
    private NavMeshAgent _agent;
    private Animator _animator;
    private float _weight;
    private float _seed;
    private float _restOffset;

    private Transform _head, _neck, _chest, _spine, _hips;
    private Transform _lUpperArm, _rUpperArm, _lLowerArm, _rLowerArm;
    private Transform _lUpperLeg, _rUpperLeg, _lLowerLeg, _rLowerLeg, _lFoot, _rFoot;

    private void Awake()
    {
        _boss = GetComponent<PossessedBoss>();
        _agent = GetComponent<NavMeshAgent>();
        if (_agent != null) _restOffset = _agent.baseOffset;
        _animator = GetComponentInChildren<Animator>(true);
        _seed = Random.Range(0f, 100f);
        if (_animator != null && _animator.isHuman)
        {
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            _spine = _animator.GetBoneTransform(HumanBodyBones.Spine);
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _lUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _rUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _rLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _lUpperLeg = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _rUpperLeg = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _lLowerLeg = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _rLowerLeg = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _lFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _rFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
    }

    /// <summary>True while she should be floating: visible and moving about, not attacking or dying.</summary>
    private bool ShouldFloat
    {
        get
        {
            if (_boss == null) return false;
            var s = _boss.currentState;
            return s == PossessedBoss.BossState.Appearing || s == PossessedBoss.BossState.Intercepting || s == PossessedBoss.BossState.Pursuing;
        }
    }

    private void Update()
    {
        float target = ShouldFloat ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, target, Time.deltaTime / blendSeconds);

        // Float rather than step: keep the animator out of its walk cycle while hovering.
        if (_weight > 0.01f && target > 0f && _animator != null && _animator.runtimeAnimatorController != null)
            foreach (var p in _animator.parameters)
                if (p.name == "Moving" && p.type == AnimatorControllerParameterType.Bool) _animator.SetBool("Moving", false);

        if (_agent != null)
        {
            float bob = Mathf.Sin((Time.time + _seed) * Mathf.PI * 2f / bobPeriod) * bobAmount;
            _agent.baseOffset = _restOffset + (hoverHeight + bob) * Smooth(_weight);
        }
    }

    private void LateUpdate()
    {
        float w = Smooth(_weight);
        if (w <= 0.001f || _animator == null || !_animator.isHuman) return;

        Vector3 up = transform.up, fwd = transform.forward, right = transform.right;
        float t = (Time.time + _seed) * driftSpeed;

        // Torso: a slight forward hang that slowly rolls from side to side.
        Bend(_hips, fwd, Drift(t, 0f) * 0.6f * w);
        Bend(_spine, right, spineLean * 0.5f * w);
        Bend(_chest, right, spineLean * 0.5f * w);
        Bend(_chest, up, Drift(t, 1f) * w);

        // Head: lolled to one side and dropped, wandering slowly.
        Bend(_neck, fwd, (headTilt * 0.4f + Drift(t, 2f) * 0.5f) * w);
        Bend(_head, fwd, (headTilt * 0.6f + Drift(t, 3f)) * w);
        Bend(_head, right, (headDroop + Drift(t, 4f) * 0.6f) * w);

        // Arms: reaching forward a little, spread, elbows soft, never quite in step.
        Bend(_lUpperArm, right, -(armReach + Drift(t, 5f)) * w);
        Bend(_rUpperArm, right, -(armReach + Drift(t, 6f)) * w);
        Bend(_lUpperArm, fwd, (armSpread + Drift(t, 7f) * 0.5f) * w);
        Bend(_rUpperArm, fwd, -(armSpread + Drift(t, 8f) * 0.5f) * w);
        Bend(_lLowerArm, right, -elbowBend * w);
        Bend(_rLowerArm, right, -elbowBend * w);

        // Legs: dangling, shins hanging back, toes pointed down, swaying apart.
        Bend(_lUpperLeg, right, Drift(t, 9f) * 0.6f * w);
        Bend(_rUpperLeg, right, Drift(t, 10f) * 0.6f * w);
        Bend(_lLowerLeg, right, (kneeBend + Drift(t, 11f) * 0.5f) * w);
        Bend(_rLowerLeg, right, (kneeBend * 0.7f + Drift(t, 12f) * 0.5f) * w);
        Bend(_lFoot, right, toePoint * w);
        Bend(_rFoot, right, toePoint * w);
    }

    /// <summary>Rotates a bone about a world-space axis through its own pivot.</summary>
    private static void Bend(Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.01f) return;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    /// <summary>A slow, smooth wander between -driftAmount and +driftAmount, different for each part.</summary>
    private float Drift(float t, float channel) => (Mathf.PerlinNoise(t, channel * 3.7f + _seed) * 2f - 1f) * driftAmount;

    private static float Smooth(float x) => x * x * (3f - 2f * x);

    private void OnDisable()
    {
        if (_agent != null) _agent.baseOffset = _restOffset;
        _weight = 0f;
    }
}
