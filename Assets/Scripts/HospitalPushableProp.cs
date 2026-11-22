using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class HospitalPushableProp : MonoBehaviour
{
    // Restricts player pushing to deliberately configured loose environment props.
}
