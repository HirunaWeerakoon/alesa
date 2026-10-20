using UnityEngine;
using UnityEngine.Events;

public class DamageableTarget : MonoBehaviour, IDamageable
{
    [Min(1)] public float health = 100;
    public UnityEvent onDefeated = new UnityEvent();
    private bool defeated;

    public void TakeDamage(float damage)
    {
        if (defeated) return;
        health -= Mathf.Max(0, damage);
        if (health > 0) return;
        defeated = true;
        onDefeated.Invoke();
        gameObject.SetActive(false);
    }
}
