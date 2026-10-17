using System.Collections;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class PossessedBoss : MonoBehaviour
{
    public enum BossState { Hidden, Intercepting, Despawned }
    public BossState currentState = BossState.Hidden;

    [Header("References")]
    public Transform player;
    private NavMeshAgent agent;
    private CharacterController playerController;

    [Header("Intercept Settings")]
    [Tooltip("How many seconds ahead to predict the player's position.")]
    public float predictionTime = 2.0f;

    [Header("Despawn Settings")]
    [Tooltip("Minimum distance the player must move to trigger a respawn.")]
    public float moveThresholdForRespawn = 3.0f;
    private Vector3 playerPositionAtDespawn;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        playerController = player.GetComponent<CharacterController>();

        // Start hidden until the Encounter Director (Agent 4) spawns her
        gameObject.SetActive(false);
    }

    void Update()
    {
        switch (currentState)
        {
            case BossState.Intercepting:
                InterceptPlayer();
                CheckKillCondition();
                break;

            case BossState.Despawned:
                CheckPlayerMovementForRespawn();
                break;
        }
    }

    // 1. The Intercept Logic (High Marks for Intelligent Search)
    private void InterceptPlayer()
    {
        if (player == null) return;

        // Calculate where the player is heading based on their current velocity
        Vector3 playerVelocity = playerController != null ? playerController.velocity : Vector3.zero;
        Vector3 predictedPosition = player.position + (playerVelocity * predictionTime);

        // Tell the NavMeshAgent to go to the predicted cutoff point, not the player's exact back
        agent.SetDestination(predictedPosition);
    }

    // 2. The Silver Bullet Reaction
    public void HitBySilverBullet()
    {
        if (currentState == BossState.Despawned) return;

        currentState = BossState.Despawned;
        agent.isStopped = true;

        // Record where Nathan is right now
        playerPositionAtDespawn = player.position;

        // Hide the boss visually and physically
        gameObject.SetActive(false);
        Debug.Log("Boss hit by Silver Bullet! Despawning...");
    }

    // 3. The Respawn Trigger
    private void CheckPlayerMovementForRespawn()
    {
        // Check how far Nathan has moved since the boss despawned
        float distanceMoved = Vector3.Distance(playerPositionAtDespawn, player.position);

        if (distanceMoved > moveThresholdForRespawn)
        {
            // In the final game, you will call Agent 4 here to pick a new spawn point.
            // For now, we just reactivate her.
            Respawn();
        }
    }

    private void Respawn()
    {
        currentState = BossState.Intercepting;
        gameObject.SetActive(true);
        agent.isStopped = false;
        Debug.Log("Nathan moved! Boss is hunting again.");
    }

    private void CheckKillCondition()
    {
        // Instant kill if close enough
        if (Vector3.Distance(transform.position, player.position) <= 1.5f)
        {
            Debug.Log("Nathan was caught by the Boss! Game Over.");
            // Trigger game over logic here
        }
    }
}