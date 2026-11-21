using Alesa.Core.AI;
using Alesa.Core.Graph;
using UnityEngine;

namespace Alesa.Runtime.World
{
    /// <summary>
    /// Scene-level composition root for shared AI services (<see cref="MazeGraph"/>,
    /// <see cref="IWorldQuery"/>, <see cref="NoiseBus"/>, and <see cref="SharedBlackboard"/>).
    /// Runs before agent adapters so <see cref="Context"/> is ready in their <c>Awake</c>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class AIBootstrap : MonoBehaviour
    {
        public static AIBootstrap Instance { get; private set; }

        [Header("World References")]
        [Tooltip("Optional explicit player transform. Falls back to GameObject tagged 'Player' if unassigned.")]
        [SerializeField] private Transform playerTransform;

        [Tooltip("Physics layers that block line-of-sight and light raycasts.")]
        [SerializeField] private LayerMask lineOfSightBlockers = Physics.DefaultRaycastLayers;

        [Header("Fallback Navigation Graph")]
        [Tooltip("Generates a simple connected grid graph when no external MazeGraph is supplied before initialization.")]
        [SerializeField] private bool buildFallbackGridIfEmpty = true;
        [SerializeField] private Vector3 fallbackGridOrigin = Vector3.zero;
        [SerializeField] private Vector2Int fallbackGridDimensions = new Vector2Int(5, 5);
        [SerializeField] private float fallbackGridSpacing = 4f;

        private AIContext _context;
        private UnityWorldQuery _worldQuery;

        /// <summary>Shared AI context. Lazily initialized so callers in Awake always receive a valid instance.</summary>
        public AIContext Context
        {
            get
            {
                EnsureInitialized();
                return _context;
            }
        }

        public MazeGraph Graph => Context.Graph;
        public IWorldQuery World => Context.World;
        public NoiseBus Noise => Context.Noise;
        public SharedBlackboard Blackboard => Context.Blackboard;
        public bool IsInitialized => _context != null;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureInitialized();
        }

        private void Update()
        {
            _worldQuery?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Explicitly initializes (or re-initializes) the shared <see cref="AIContext"/> with an
        /// optional authored <see cref="MazeGraph"/> and custom <see cref="IWorldQuery"/>.
        /// </summary>
        public void Initialize(MazeGraph graph = null, IWorldQuery worldQuery = null)
        {
            MazeGraph resolvedGraph = graph ?? (buildFallbackGridIfEmpty ? BuildFallbackGrid() : new MazeGraph());

            if (worldQuery != null)
            {
                _worldQuery = worldQuery as UnityWorldQuery;
            }
            else
            {
                _worldQuery = new UnityWorldQuery(playerTransform, lineOfSightBlockers);
                worldQuery = _worldQuery;
            }

            _context = new AIContext(resolvedGraph, worldQuery, new NoiseBus(), new SharedBlackboard());
        }

        /// <summary>Updates the tracked player transform used by the default <see cref="UnityWorldQuery"/>.</summary>
        public void SetPlayer(Transform player)
        {
            playerTransform = player;
            _worldQuery?.SetPlayer(player);
        }

        /// <summary>Refreshes cached scene lights used for <see cref="IWorldQuery.LightLevelAt"/>.</summary>
        public void RefreshSceneLights()
        {
            _worldQuery?.RefreshLights();
        }

        /// <summary>Emits a Unity world-space noise onto the shared <see cref="NoiseBus"/>.</summary>
        public void EmitNoise(Vector3 worldPosition, float loudness, NoiseType type, int sourceId = 0)
        {
            Noise.Emit(new NoiseEvent(worldPosition.ToCore(), loudness, type, Time.time, sourceId));
        }

        private void EnsureInitialized()
        {
            if (_context == null) Initialize();
        }

        private MazeGraph BuildFallbackGrid()
        {
            var graph = new MazeGraph();
            int cols = Mathf.Max(1, fallbackGridDimensions.x);
            int rows = Mathf.Max(1, fallbackGridDimensions.y);
            float spacing = Mathf.Max(0.5f, fallbackGridSpacing);

            float halfWidth = (cols - 1) * spacing * 0.5f;
            float halfDepth = (rows - 1) * spacing * 0.5f;

            var nodeIds = new int[cols, rows];
            for (int x = 0; x < cols; x++)
            {
                for (int z = 0; z < rows; z++)
                {
                    var pos = fallbackGridOrigin + new Vector3(x * spacing - halfWidth, 0f, z * spacing - halfDepth);
                    var node = graph.AddNode(pos.ToCore(), NodeTag.Junction, $"Grid_{x}_{z}");
                    nodeIds[x, z] = node.Id;
                }
            }

            for (int x = 0; x < cols; x++)
            {
                for (int z = 0; z < rows; z++)
                {
                    if (x + 1 < cols) graph.Connect(nodeIds[x, z], nodeIds[x + 1, z]);
                    if (z + 1 < rows) graph.Connect(nodeIds[x, z], nodeIds[x, z + 1]);
                }
            }

            return graph;
        }
    }
}
