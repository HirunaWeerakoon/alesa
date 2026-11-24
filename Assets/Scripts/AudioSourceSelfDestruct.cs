using UnityEngine;

/// <summary>
/// Keeps an AudioSource that has been lifted off a dying object alive long enough to finish, then
/// removes it. Without it a death cry is cut in half the moment the corpse is destroyed.
/// </summary>
[DisallowMultipleComponent]
public sealed class AudioSourceSelfDestruct : MonoBehaviour
{
    public void Begin(float seconds) => Destroy(gameObject, seconds);
}
