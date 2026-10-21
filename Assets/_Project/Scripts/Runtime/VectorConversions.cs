using NVec = System.Numerics.Vector3;
using UVec = UnityEngine.Vector3;

namespace Alesa.Runtime
{
    /// <summary>Core uses System.Numerics so it stays engine-free; convert at the boundary.</summary>
    public static class VectorConversions
    {
        public static NVec ToCore(this UVec v) => new NVec(v.x, v.y, v.z);
        public static UVec ToUnity(this NVec v) => new UVec(v.X, v.Y, v.Z);
    }
}
