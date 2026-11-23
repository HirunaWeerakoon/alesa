using UnityEngine;

/// <summary>Reports substantial player/prop impacts to the hive mind with a local cooldown.</summary>
[DisallowMultipleComponent]
public class PropNoiseEmitter : MonoBehaviour
{
    [Tooltip("Relative impact speed in metres/second must exceed this value.")]
    [Min(0f)] public float minImpactSpeed = 1.5f;
    [Tooltip("Hearing radius of an impact in metres.")]
    [Min(0f)] public float noiseRadius = 8f;
    [Tooltip("Minimum time between acoustic events from this prop.")]
    [Min(0f)] public float cooldown = 0.5f;
    private float nextNoiseTime;

    private void OnCollisionEnter(Collision collision)
    {
        if (collision == null || collision.collider == null) return;
        var other = collision.collider;
        bool playerOrProp = collision.rigidbody != null ||
            other.GetComponentInParent<PlayerMotor>() != null ||
            other.GetComponentInParent<PropNoiseEmitter>() != null;
        if (playerOrProp) ReportImpact(collision.relativeVelocity.magnitude);
    }

    /// <summary>
    /// Bridges CharacterController contact, which does not reliably produce
    /// Rigidbody collision callbacks. This reports noise without adding push forces.
    /// </summary>
    /// <param name="speed">Player's horizontal contact speed in metres/second.</param>
    public void ReportPlayerImpact(float speed) { ReportImpact(speed); }

    private void ReportImpact(float speed)
    {
        if (!isActiveAndEnabled || Time.timeScale <= 0f || speed <= minImpactSpeed ||
            noiseRadius <= 0f || Time.time < nextNoiseTime) return;
        nextNoiseTime = Time.time + cooldown;
        AcousticBus.EmitSound(transform.position, noiseRadius, SoundType.PropCollision);
    }
}
