using UnityEngine;

namespace Alesa.Runtime.Motion
{
    /// <summary>
    /// Provides a world-space point an agent's head should look toward.
    /// Implemented by agent adapters and polled by <see cref="HeadLookAt"/>.
    /// </summary>
    public interface ILookTargetSource
    {
        bool TryGetLookTarget(out Vector3 worldPoint);
    }
}
