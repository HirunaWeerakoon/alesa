using System.Numerics;
using Alesa.Core.Graph;

namespace Alesa.Core.AI
{
    /// <summary>Every agent's decision logic. Ticked by its Unity adapter; never touches GameObjects.</summary>
    public interface IAgentBrain
    {
        void Tick(float deltaTime);
    }

    /// <summary>A brain with a body. The Agent Controller's motor reads this and drives NavMesh + animation.</summary>
    public interface IMobileBrain : IAgentBrain
    {
        MovementIntent Intent { get; }
    }

    public struct MovementIntent
    {
        public bool HasTarget;
        public Vector3 Target;
        public float Speed;

        /// <summary>Optional point to face while moving (e.g. Alesa watching the player's back).</summary>
        public bool HasLookAt;
        public Vector3 LookAt;

        public static MovementIntent Idle => default;

        public static MovementIntent MoveTo(Vector3 target, float speed) =>
            new MovementIntent { HasTarget = true, Target = target, Speed = speed };
    }

    /// <summary>
    /// What the AI may ask about the world. Implemented in Unity (raycasts, lights, player
    /// controller) and faked in tests, which keeps every brain decoupled from visuals.
    /// </summary>
    public interface IWorldQuery
    {
        float Time { get; }
        Vector3 PlayerPosition { get; }
        Vector3 PlayerVelocity { get; }
        Vector3 PlayerForward { get; }
        bool HasLineOfSight(Vector3 from, Vector3 to);

        /// <summary>0 = pitch dark, 1 = fully lit.</summary>
        float LightLevelAt(Vector3 position);
    }

    /// <summary>Everything a brain is constructed with.</summary>
    public sealed class AIContext
    {
        public MazeGraph Graph { get; }
        public IWorldQuery World { get; }
        public NoiseBus Noise { get; }
        public SharedBlackboard Blackboard { get; }

        public AIContext(MazeGraph graph, IWorldQuery world, NoiseBus noise, SharedBlackboard blackboard)
        {
            Graph = graph;
            World = world;
            Noise = noise;
            Blackboard = blackboard;
        }
    }
}
