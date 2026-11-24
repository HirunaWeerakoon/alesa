using UnityEngine;

/// <summary>
/// Head collider on a minion. Exists only so a shot to the head can be told apart from a shot to
/// the body: <see cref="IDamageable.TakeDamage"/> carries a damage number and no hit position, and
/// <see cref="RevolverCombat"/> walks up from the collider it struck — so the zone has to be
/// decided by *which collider* was hit, which means a separate one for the head.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class MinionHeadHitbox : MonoBehaviour, IDamageable
{
    [SerializeField] private MinionCombatant owner;

    private void Awake()
    {
        if (owner == null) owner = GetComponentInParent<MinionCombatant>();
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f) return;
        if (owner == null) owner = GetComponentInParent<MinionCombatant>();
        if (owner != null) owner.TakeHeadshot();
    }
}
