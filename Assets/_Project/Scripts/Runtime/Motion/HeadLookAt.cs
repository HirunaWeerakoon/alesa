using UnityEngine;

namespace Alesa.Runtime.Motion
{
    /// <summary>
    /// 3D head orientation controller that aims an agent's head toward the world point provided
    /// by an <see cref="ILookTargetSource"/> (or an explicit override target).
    /// Supports both Humanoid Animator IK and direct head-bone rotation for generic rigs.
    /// </summary>
    public sealed class HeadLookAt : MonoBehaviour
    {
        [Header("Rig & Mode")]
        [Tooltip("Optional head bone used when Humanoid Animator IK is unavailable.")]
        [SerializeField] private Transform headBone;
        [SerializeField] private bool useAnimatorIK = true;

        [Header("Look Weights")]
        [SerializeField, Range(0f, 1f)] private float maxWeight = 1f;
        [SerializeField, Range(0f, 1f)] private float bodyWeight = 0.15f;
        [SerializeField, Range(0f, 1f)] private float headWeight = 0.85f;
        [SerializeField, Range(0f, 1f)] private float eyesWeight = 0.5f;
        [SerializeField, Range(0f, 1f)] private float clampWeight = 0.5f;

        [Header("Smoothing & Bone Limits")]
        [SerializeField] private float blendSpeed = 6f;
        [SerializeField, Range(0f, 120f)] private float maxYawAngle = 75f;
        [SerializeField, Range(0f, 80f)] private float maxPitchAngle = 40f;
        [SerializeField] private float boneTurnSpeed = 360f;

        private Animator _animator;
        private ILookTargetSource _source;
        private bool _hasOverrideTarget;
        private Vector3 _overrideTarget;
        private Quaternion _smoothedHeadOffset = Quaternion.identity;

        public bool HasTarget { get; private set; }
        public Vector3 CurrentTarget { get; private set; }
        public float CurrentWeight { get; private set; }
        public ILookTargetSource TargetSource => _source;

        private void Awake()
        {
            ResolveReferences();
        }

        /// <summary>Explicitly assigns the <see cref="ILookTargetSource"/> to query each frame.</summary>
        public void SetSource(ILookTargetSource source)
        {
            _source = source;
        }

        /// <summary>Temporarily overrides the look target with a fixed world-space point.</summary>
        public void SetOverrideTarget(Vector3 worldPoint)
        {
            _hasOverrideTarget = true;
            _overrideTarget = worldPoint;
        }

        /// <summary>Clears any manual override so polling returns to <see cref="TargetSource"/>.</summary>
        public void ClearOverrideTarget()
        {
            _hasOverrideTarget = false;
        }

        private void Update()
        {
            ResolveReferences();

            if (_hasOverrideTarget)
            {
                HasTarget = true;
                CurrentTarget = _overrideTarget;
            }
            else if (_source != null && _source.TryGetLookTarget(out Vector3 point))
            {
                HasTarget = true;
                CurrentTarget = point;
            }
            else
            {
                HasTarget = false;
            }

            float targetWeight = HasTarget ? maxWeight : 0f;
            CurrentWeight = Mathf.MoveTowards(CurrentWeight, targetWeight, Mathf.Max(0.1f, blendSpeed) * Time.deltaTime);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (!CanUseHumanoidIK()) return;

            if (CurrentWeight > 0.001f)
            {
                _animator.SetLookAtWeight(CurrentWeight, bodyWeight, headWeight, eyesWeight, clampWeight);
                _animator.SetLookAtPosition(CurrentTarget);
            }
            else
            {
                _animator.SetLookAtWeight(0f);
            }
        }

        private void LateUpdate()
        {
            if (CanUseHumanoidIK() || headBone == null) return;

            Quaternion targetOffset = Quaternion.identity;
            if (HasTarget)
            {
                Vector3 toTargetWorld = CurrentTarget - headBone.position;
                if (toTargetWorld.sqrMagnitude > 1e-4f)
                {
                    Vector3 localDir = transform.InverseTransformDirection(toTargetWorld.normalized);
                    float yaw = Mathf.Clamp(Mathf.Atan2(localDir.x, localDir.z) * Mathf.Rad2Deg, -maxYawAngle, maxYawAngle);
                    float flatMag = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);
                    float pitch = Mathf.Clamp(-Mathf.Atan2(localDir.y, flatMag) * Mathf.Rad2Deg, -maxPitchAngle, maxPitchAngle);
                    targetOffset = Quaternion.Euler(pitch, yaw, 0f);
                }
            }

            _smoothedHeadOffset = Quaternion.RotateTowards(
                _smoothedHeadOffset,
                targetOffset,
                Mathf.Max(1f, boneTurnSpeed) * Time.deltaTime);

            if (CurrentWeight > 0.001f)
            {
                Quaternion desiredWorld = transform.rotation * _smoothedHeadOffset;
                headBone.rotation = Quaternion.Slerp(headBone.rotation, desiredWorld, CurrentWeight);
            }
        }

        private bool CanUseHumanoidIK() =>
            useAnimatorIK && _animator != null && _animator.isHuman && _animator.enabled;

        private void ResolveReferences()
        {
            if (_source == null)
            {
                _source = GetComponent<ILookTargetSource>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            if (headBone == null && _animator != null && _animator.isHuman)
            {
                headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }
    }
}
