using UnityEngine;

/// <summary>
/// Scene authoring marker for a future maze encounter director. Contains staging
/// references and editor gizmos; it does not spawn enemies or trigger encounters.
/// </summary>
[SelectionBase]
public class AmbushNode : MonoBehaviour
{
    [Header("Spawn Configuration")]
    /// <summary>Optional explicit boss spawn pose.</summary>
    [Tooltip("Exact Boss spawn position and rotation. Leave empty to use this node's transform.")]
    public Transform bossSpawnPoint;

    /// <summary>Optional animator for the encounter's door.</summary>
    [Tooltip("Optional animator for doors kicked open or smashed during the ambush.")]
    public Animator doorAnimator;

    /// <summary>Optional local audio source for encounter effects.</summary>
    [Tooltip("Optional local audio source for door kicks, metallic slams or stingers.")]
    public AudioSource ambushAudioSource;

    [Header("Corridor Staging")]
    /// <summary>Lights the future director may flicker or switch off.</summary>
    [Tooltip("Hallway/junction lights the director may flicker or cut during this encounter.")]
    public Light[] localCorridorLights = new Light[0];

    [Header("State")]
    /// <summary>Whether this node has already been used by an encounter.</summary>
    [Tooltip("True after this ambush point has fired. Available nodes are red; consumed nodes are gray.")]
    public bool isConsumed;

    /// <summary>Resolved spawn position/rotation, including a reliable null fallback.</summary>
    public Transform SpawnPoint => bossSpawnPoint != null ? bossSpawnPoint : transform;

    private void OnDrawGizmos()
    {
        Color previousColor = Gizmos.color;
        Gizmos.color = isConsumed ? Color.gray : Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, transform.forward * 1.5f);

        if (bossSpawnPoint != null) DrawSpawnPreview();
        Gizmos.color = previousColor;
    }

    private void OnDrawGizmosSelected()
    {
        // Also reveal the fallback spawn volume when authoring an unassigned node.
        if (bossSpawnPoint == null) DrawSpawnPreview();
    }

    private void DrawSpawnPreview()
    {
        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Transform spawn = SpawnPoint;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, spawn.position);
        Gizmos.matrix = Matrix4x4.TRS(spawn.position, spawn.rotation, Vector3.one);
        // Spawn transforms represent the feet; the preview is centred above them.
        Gizmos.DrawWireCube(new Vector3(0, 0.9f, 0), new Vector3(0.6f, 1.8f, 0.6f));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
