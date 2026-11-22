using UnityEngine;
using UnityEngine.AI;

namespace Alesa.Runtime.World
{
    /// <summary>
    /// Registers a baked <see cref="NavMeshData"/> asset with Unity's runtime NavMesh system
    /// without requiring the external <c>com.unity.ai.navigation</c> package.
    /// Mirrors the <c>FinalBossNavigation</c> pattern so multiple wing/hospital NavMesh assets
    /// can coexist cleanly in <c>SampleScene</c>.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HospitalNavMeshLoader : MonoBehaviour
    {
        [Tooltip("Baked NavMeshData asset covering the hospital floors, corridors, and rescue area.")]
        [SerializeField] private NavMeshData navigationData;

        private NavMeshDataInstance _instance;

        public NavMeshData NavigationData
        {
            get => navigationData;
            set
            {
                if (navigationData == value && _instance.valid) return;
                UnregisterNavMesh();
                navigationData = value;
                if (isActiveAndEnabled)
                {
                    RegisterNavMesh();
                }
            }
        }

        public bool IsLoaded => _instance.valid;

        private void Awake()
        {
            RegisterNavMesh();
        }

        private void OnEnable()
        {
            RegisterNavMesh();
        }

        private void OnDisable()
        {
            UnregisterNavMesh();
        }

        private void OnDestroy()
        {
            UnregisterNavMesh();
        }

        private void RegisterNavMesh()
        {
            if (navigationData != null && !_instance.valid)
            {
                _instance = NavMesh.AddNavMeshData(navigationData);
            }
        }

        private void UnregisterNavMesh()
        {
            if (_instance.valid)
            {
                _instance.Remove();
                _instance = default;
            }
        }
    }
}
