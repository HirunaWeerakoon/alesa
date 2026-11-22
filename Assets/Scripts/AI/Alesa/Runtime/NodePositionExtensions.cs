using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Bridges pure C# NodePosition coordinates with UnityEngine.Vector3.
    /// Keeps Core completely decoupled from UnityEngine while allowing Unity scripts
    /// to seamlessly convert transforms and vectors into graph coordinates.
    /// </summary>
    public static class NodePositionExtensions
    {
        public static Vector3 ToVector3(this NodePosition position)
        {
            return new Vector3(position.X, position.Y, position.Z);
        }

        public static NodePosition ToNodePosition(this Vector3 vector)
        {
            return new NodePosition(vector.x, vector.y, vector.z);
        }
    }
}

