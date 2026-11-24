using System.Collections;
using Alesa.Agents.BlindMinion;
using Alesa.Runtime.Motion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Connects a blind minion to the player combat systems without touching the AI assembly:
/// its melee hits hurt the player (after a short visible wind-up), and the player's bullets can kill it.
/// The attack swing is played by BlindMinionAgent's Attack trigger; death plays the animator's Die trigger and
/// falls back to a procedural topple when the controller has no Die state.
/// </summary>
[RequireComponent(typeof(BlindMinionAgent))]
public sealed class MinionCombatBridge : MonoBehaviour, IDamageable
{
    [Min(1f)] public float health = 100f;
    [Min(0f)] public float damagePerHit = 1f;

    [Header("Attack")]
    [Tooltip("Seconds between the lunge starting and the hit landing, so the player can see it coming.")]
    [Min(0.05f)] public float windupSeconds = 0.7f;
    [Min(0f)] public float lungeDistance = 0f;
    [Tooltip("The player must still be this close when the hit lands, otherwise it misses.")]
    [Min(0.5f)] public float maxHitDistance = 2.6f;

    [Header("Death")]
    [Min(0.1f)] public float fallSeconds = 0.9f;
    [Tooltip("Length of the death clip; the body is removed this long plus Corpse Seconds after dying.")]
    [Min(0.1f)] public float deathClipSeconds = 3f;
    [Min(0f)] public float corpseSeconds = 6f;

    private BlindMinionAgent agent;
    private PlayerHealth playerHealth;
    private bool dead, attacking;

    private void Awake() { agent = GetComponent<BlindMinionAgent>(); }
    private void OnEnable() { agent.AttackedPlayer += OnAttackedPlayer; }
    private void OnDisable() { if (agent != null) agent.AttackedPlayer -= OnAttackedPlayer; }

    private void OnAttackedPlayer(BlindMinionAgent _)
    {
        if (dead || attacking || !ResolvePlayerHealth()) return;
        StartCoroutine(Attack());
    }

    private IEnumerator Attack()
    {
        attacking = true;
        var nav = GetComponent<NavMeshAgent>();
        Vector3 toPlayer = playerHealth.transform.position - transform.position;
        toPlayer.y = 0f;
        Vector3 lunge = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized * lungeDistance : Vector3.zero;
        float elapsed = 0f, moved = 0f;
        while (elapsed < windupSeconds && !dead)
        {
            elapsed += Time.deltaTime;
            // Lunge during the second half of the wind-up.
            float target = Mathf.Clamp01((elapsed / windupSeconds - 0.4f) / 0.6f);
            if (nav != null && nav.enabled && nav.isOnNavMesh && target > moved)
            {
                nav.Move(lunge * (target - moved));
                moved = target;
            }
            yield return null;
        }
        attacking = false;
        if (dead) yield break;
        if ((playerHealth.transform.position - transform.position).sqrMagnitude <= maxHitDistance * maxHitDistance)
            playerHealth.TakeDamage(damagePerHit);
    }

    private bool ResolvePlayerHealth()
    {
        if (playerHealth != null) return true;
        var gameplay = FindFirstObjectByType<PlayerGameplay>();
        if (gameplay == null) return false;
        playerHealth = gameplay.GetComponent<PlayerHealth>();
        if (playerHealth == null) playerHealth = gameplay.gameObject.AddComponent<PlayerHealth>();
        return true;
    }

    public void TakeDamage(float damage)
    {
        if (dead) return;
        health -= Mathf.Max(0f, damage);
        if (health > 0f) return;
        dead = true;
        StopAllCoroutines();
        StartCoroutine(Die());
    }

    private IEnumerator Die()
    {
        // Stop every system that would keep moving or steering the body.
        agent.enabled = false;
        var motor = GetComponent<AgentMotor>(); if (motor != null) motor.enabled = false;
        var look = GetComponent<HeadLookAt>(); if (look != null) look.enabled = false;
        var nav = GetComponent<NavMeshAgent>(); if (nav != null) nav.enabled = false;
        var col = GetComponent<Collider>(); if (col != null) col.enabled = false;

        // Topple backwards away from where the player is, then sink into the floor.
        Vector3 away = transform.forward;
        var gameplay = FindFirstObjectByType<PlayerGameplay>();
        if (gameplay != null)
        {
            away = transform.position - gameplay.transform.position; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
        }
        away.Normalize();
        var animator = GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController != null && HasTrigger(animator, "Die"))
        {
            animator.SetTrigger("Die");
            yield return new WaitForSeconds(deathClipSeconds + corpseSeconds);
            agent.Kill();
            yield break;
        }

        Quaternion start = transform.rotation;
        Quaternion end = Quaternion.AngleAxis(90f, Vector3.Cross(Vector3.up, away)) * start;
        Vector3 startPos = transform.position;
        for (float t = 0f; t < fallSeconds; t += Time.deltaTime)
        {
            float k = t / fallSeconds; k *= k; // accelerates like a real fall
            transform.rotation = Quaternion.Slerp(start, end, k);
            transform.position = startPos + Vector3.down * 0.15f * k;
            yield return null;
        }
        transform.rotation = end;
        yield return new WaitForSeconds(corpseSeconds);
        agent.Kill();
    }

    private static bool HasTrigger(Animator animator, string name)
    {
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name) return true;
        return false;
    }
}
