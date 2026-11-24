using UnityEngine;
using UnityEngine.AI;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class FinalBossNavigation : MonoBehaviour
{
    public NavMeshData navigationData;
    NavMeshDataInstance instance;
    void OnEnable() { if (navigationData != null && !instance.valid) instance = NavMesh.AddNavMeshData(navigationData); }
    void OnDisable() { if (instance.valid) instance.Remove(); }
}
