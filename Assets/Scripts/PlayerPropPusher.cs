using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerPropPusher : MonoBehaviour
{
    [Min(0f)] public float pushForce = 65f;
    [Min(0f)] public float maximumPropSpeed = 1.8f;
    Rigidbody lastBody;
    int lastPushFrame = -1;

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        var body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic || body.GetComponent<HospitalPushableProp>() == null) return;
        if (hit.moveDirection.y < -.35f || Mathf.Abs(hit.normal.y) > .65f) return;
        if (lastBody == body && lastPushFrame == Time.frameCount) return;
        Vector3 direction = Vector3.ProjectOnPlane(hit.moveDirection, Vector3.up);
        if (direction.sqrMagnitude < .01f || hit.moveLength < .0001f) return;
        direction.Normalize();
        float speed = Vector3.Dot(body.linearVelocity, direction);
        if (speed >= maximumPropSpeed) return;
        float impulse = Mathf.Min(pushForce * Time.deltaTime, (maximumPropSpeed - speed) * body.mass);
        body.AddForceAtPosition(direction * impulse, hit.point, ForceMode.Impulse);
        lastBody = body;
        lastPushFrame = Time.frameCount;
    }
}
