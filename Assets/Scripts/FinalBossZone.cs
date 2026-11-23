using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class FinalBossZone : MonoBehaviour
{
    public FinalBossEncounter encounter;
    public bool exit;
    void OnTriggerEnter(Collider other) { Activate(other); }
    void OnTriggerStay(Collider other) { Activate(other); }
    void Activate(Collider other)
    {
        if (encounter == null || other.GetComponentInParent<PlayerGameplay>() != encounter.player) return;
        if (exit) encounter.ReachExit(); else encounter.BeginEncounter();
    }
}
