using System.Collections.Generic;
using UnityEngine;
using Alesa.Core.AI;
using Alesa.Core.Graph;
using Alesa.HiveMind;
using Alesa.Runtime;        // ToCore()
using Alesa.Runtime.World;  // AIBootstrap

/// <summary>
/// Drives the <see cref="HiveMindDirector"/> inside a Unity scene: gathers what the director needs
/// each tick, and turns its decisions back into scene events.
///
/// It lives in Assembly-CSharp for the same reason the noise bridge does — assembly definitions
/// cannot reference Assembly-CSharp, but Assembly-CSharp references all of them, so this is the
/// only side that can see the AI assemblies and the gameplay scripts at once.
///
/// Safe to drop into a scene with no AIBootstrap, no minions and no player: every lookup degrades
/// to doing nothing rather than throwing, which is what lets the hive mind be finished and
/// verified before any enemy art exists.
/// </summary>
[DisallowMultipleComponent]
public class HiveMindRunner : MonoBehaviour
{
    [Header("Scene wiring")]
    [Tooltip("Player transform. Left empty, it is taken from the AI world query or the Player tag.")]
    public Transform player;

    [Tooltip("Minion pool the hive controls. Left empty, the scene's MinionSpawnDirector is used if there is one.")]
    public MinionSpawnDirector spawnDirector;

    [Tooltip("Let the hive decide how many minions may wake, and stage ambushes with them.")]
    public bool controlMinions = true;

    [Tooltip("Seconds between decisions. The director does not need frame precision.")]
    [Min(0.05f)] public float tickInterval = 0.2f;

    [Header("Tuning")]
    [Tooltip("Most minions allowed on the field at full pressure.")]
    [Min(0)] public int maxActiveMinions = 4;

    [Tooltip("Seconds for the hive to forget half of what it heard.")]
    [Min(0.5f)] public float heatHalfLife = 8f;

    [Tooltip("Enforced quiet between encounters. The strongest pacing control there is.")]
    [Min(0f)] public float relaxSeconds = 35f;

    [Header("Debug")]
    public bool logDecisions;
    public bool drawGizmos = true;

    [Header("Status (read-only)")]
    [SerializeField] private string pacingState = "not started";
    [SerializeField, Range(0f, 1f)] private float intensity;
    [SerializeField, Range(0f, 1f)] private float hottestHeat;
    [SerializeField] private int desiredMinions;
    [SerializeField] private int ambushNode = -1;

    /// <summary>The director itself, once the scene has an AIBootstrap. Null before that.</summary>
    public HiveMindDirector Director { get; private set; }

    /// <summary>
    /// Things the player should be afraid of. Minions register themselves here as they spawn, so
    /// the runner never needs to know which minion implementation the project settled on.
    /// </summary>
    private static readonly List<Transform> Threats = new List<Transform>();

    public static void RegisterThreat(Transform t)
    {
        if (t != null && !Threats.Contains(t)) Threats.Add(t);
    }

    public static void UnregisterThreat(Transform t) => Threats.Remove(t);

    /// <summary>
    /// Player health, 1 = untouched. Read from <see cref="PlayerVitals"/> when one is in the scene;
    /// settable so tests and scenes without a health system can still drive pacing.
    /// </summary>
    public float PlayerHealthFraction { get; set; } = 1f;

    private PlayerVitals _vitals;

    private AIBootstrap _bootstrap;
    private MazeGraph _graph;
    private float _nextTick;
    private float _lastGunshotTime = float.NegativeInfinity;

    /// <summary>
    /// Minions discovered automatically, used when nothing has registered itself. Scanning rather
    /// than requiring a component on each prefab keeps the hive mind out of the Agent Controller's
    /// assets, so neither side has to edit the other's work to make this run.
    /// </summary>
    private Alesa.Agents.BlindMinion.BlindMinionAgent[] _foundMinions =
        System.Array.Empty<Alesa.Agents.BlindMinion.BlindMinionAgent>();
    private float _nextMinionScan;

    private void OnEnable()
    {
        // A gunshot means a firefight, which is pressure in its own right. Listening to the bus
        // rather than the weapon keeps this decoupled from whatever the weapon script is called.
        AcousticBus.OnSoundEmitted += OnGameplaySound;
    }

    private void OnDisable()
    {
        AcousticBus.OnSoundEmitted -= OnGameplaySound;
        if (_bootstrap != null && _bootstrap.Noise != null)
            _bootstrap.Noise.NoiseEmitted -= OnAiNoise;
    }

    private void OnGameplaySound(Vector3 origin, float radius, SoundType type)
    {
        if (type == SoundType.Gunshot) _lastGunshotTime = Time.time;
    }

    private void Update()
    {
        if (Time.time < _nextTick) return;
        float delta = tickInterval;
        _nextTick = Time.time + tickInterval;

        if (!EnsureDirector()) return;

        Director.MaxActiveMinions = maxActiveMinions;
        Director.Heat.HalfLifeSeconds = heatHalfLife;
        Director.Pacing.RelaxSeconds = relaxSeconds;

        Director.Tick(Time.time, delta, GatherInputs(), ResolvePlayerNode());
        if (controlMinions && spawnDirector != null) spawnDirector.AwakeBudget = Director.DesiredMinionCount;
        PublishStatus();
    }

    /// <summary>
    /// Builds the director lazily. AIBootstrap creates its context in Awake, and this component
    /// may well be enabled first, so construction is deferred to the first tick that finds one.
    /// </summary>
    private bool EnsureDirector()
    {
        if (Director != null) return true;

        if (_bootstrap == null)
            _bootstrap = AIBootstrap.Instance != null ? AIBootstrap.Instance : FindFirstObjectByType<AIBootstrap>();
        if (_bootstrap == null || !_bootstrap.IsInitialized) return false;

        _graph = _bootstrap.Graph;
        if (_graph == null || _graph.Count == 0) return false;

        if (spawnDirector == null) spawnDirector = FindFirstObjectByType<MinionSpawnDirector>();
        Director = new HiveMindDirector(_graph, _bootstrap.Blackboard);

        // Hear the world exactly as the minions do, so the hive never stages an ambush based on a
        // noise its own minions could not have heard.
        Director.Heat.AcousticCost = new Alesa.Agents.BlindMinion.AcousticModel().CostFor(_graph);

        // The noise bridge puts gameplay sounds onto this bus; the director listens to the same one.
        _bootstrap.Noise.NoiseEmitted += OnAiNoise;

        Director.AmbushStaged += OnAmbushStaged;
        Director.PacingChanged += OnPacingChanged;
        return true;
    }

    private void OnAiNoise(NoiseEvent noise) => Director?.Observe(noise);

    private PacingInputs GatherInputs()
    {
        var inputs = PacingInputs.Calm;
        if (_vitals == null) _vitals = FindFirstObjectByType<PlayerVitals>();
        if (_vitals != null) PlayerHealthFraction = _vitals.HealthFraction;
        inputs.PlayerHealthFraction = PlayerHealthFraction;
        inputs.BossActive = _bootstrap.Blackboard.BossActive;

        // A gunshot keeps counting as pressure for a moment afterwards, so a single shot does not
        // register as one frame of fear and then nothing.
        inputs.PlayerFiring = Time.time - _lastGunshotTime < 1.5f;

        Transform p = ResolvePlayer();
        if (p != null)
        {
            float nearest = float.PositiveInfinity;
            for (int i = Threats.Count - 1; i >= 0; i--)
            {
                // Threats are destroyed as they die; prune rather than trusting the list.
                if (Threats[i] == null) { Threats.RemoveAt(i); continue; }
                float d = Vector3.Distance(Threats[i].position, p.position);
                if (d < nearest) nearest = d;
            }

            // Nothing registered itself, so look for minions directly. Rescanned on a timer
            // because FindObjectsByType is far too expensive to run every tick.
            if (Threats.Count == 0)
            {
                if (Time.time >= _nextMinionScan)
                {
                    _nextMinionScan = Time.time + 1f;
                    _foundMinions = FindObjectsByType<Alesa.Agents.BlindMinion.BlindMinionAgent>(
                        FindObjectsSortMode.None);
                }
                foreach (var m in _foundMinions)
                {
                    if (m == null || !m.isActiveAndEnabled) continue;
                    float d = Vector3.Distance(m.transform.position, p.position);
                    if (d < nearest) nearest = d;
                }
            }

            inputs.NearestThreatDistance = nearest;
        }

        // Heat is filled in by the director from its own map, so it is deliberately left at 0 here.
        return inputs;
    }

    private Transform ResolvePlayer()
    {
        if (player != null) return player;
        var tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) player = tagged.transform;
        return player;
    }

    private int ResolvePlayerNode()
    {
        Transform p = ResolvePlayer();
        if (p == null || _graph == null) return -1;
        return _graph.NearestNode(p.position.ToCore());
    }

    private void OnAmbushStaged(AmbushCandidate candidate)
    {
        // Put a minion where the director wants one waiting, unless the player would watch it appear.
        if (controlMinions && spawnDirector != null)
        {
            Vector3 spot = _graph[candidate.Node].Position.ToUnity();
            bool staged = !PlayerCouldSee(spot) && spawnDirector.StageAmbush(spot);
            // The lights near the ambush stutter and die, so the dark is part of the warning.
            if (staged) LightFlicker.DisturbNear(spot, 9f, Random.Range(1.5f, 3f));
            if (logDecisions) Debug.Log($"<color=#ff9f7f>[Hive mind]</color> ambush minion {(staged ? "staged" : "not staged")} at node {candidate.Node}", this);
        }

        if (!logDecisions) return;
        Debug.Log($"<color=#ff9f7f>[Hive mind]</color> staging at node {candidate.Node} " +
                  $"({candidate.Distance:0.#} m away, on route: {candidate.OnRoute}, score {candidate.Score:0.00})", this);
    }

    private void OnPacingChanged(PacingState state)
    {
        if (logDecisions) Debug.Log($"<color=#ff9f7f>[Hive mind]</color> pacing -> {state}", this);
    }

    // True when a point is inside the player's view and not hidden behind anything.
    private bool PlayerCouldSee(Vector3 point)
    {
        var camera = Camera.main;
        if (camera == null) return false;
        Vector3 chest = point + Vector3.up;
        Vector3 viewport = camera.WorldToViewportPoint(chest);
        if (viewport.z <= 0f || viewport.x < -0.1f || viewport.x > 1.1f || viewport.y < -0.1f || viewport.y > 1.1f) return false;
        Vector3 origin = camera.transform.position;
        Vector3 toPoint = chest - origin;
        if (!Physics.Raycast(origin, toPoint.normalized, out RaycastHit hit, toPoint.magnitude, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.distance >= toPoint.magnitude - 0.3f;
    }

    private void PublishStatus()
    {
        pacingState = Director.Pacing.State.ToString();
        intensity = Director.Intensity.Intensity;
        hottestHeat = Director.Heat.HottestHeat;
        desiredMinions = Director.DesiredMinionCount;
        ambushNode = Director.CurrentAmbush.IsValid ? Director.CurrentAmbush.Node : -1;
    }

    // ------------------------------------------------------------------ queries for other systems

    /// <summary>True once the director exists, which needs an initialised AIBootstrap with a graph.</summary>
    public bool IsReady => EnsureDirector();

    /// <summary>The scene's AI blackboard, or null before the AIBootstrap is up.</summary>
    public SharedBlackboard Blackboard => _bootstrap != null && _bootstrap.IsInitialized ? _bootstrap.Blackboard : null;

    /// <summary>Graph node nearest a world position, or -1 when none is within <paramref name="maxDistance"/> metres.</summary>
    public int NodeNear(Vector3 world, float maxDistance)
    {
        if (!EnsureDirector()) return -1;
        int node = _graph.NearestNode(world.ToCore());
        if (node < 0) return -1;
        return Vector3.Distance(_graph[node].Position.ToUnity(), world) <= maxDistance ? node : -1;
    }

    /// <summary>
    /// Scores every graph node the hive could stage an encounter at right now, best first. It weighs what the
    /// hive has heard (heat), how far the node is from the player, and how recently it was used. Nodes for which
    /// <paramref name="isUsable"/> is false are left out, which is how the caller applies what only the scene
    /// knows, such as whether the player can see the spot.
    /// </summary>
    public List<AmbushCandidate> RankAmbushNodes(System.Func<int, bool> isUsable)
    {
        var ranked = new List<AmbushCandidate>();
        if (!EnsureDirector()) return ranked;
        int playerNode = ResolvePlayerNode();
        if (playerNode < 0) return ranked;

        var scorer = Director.Ambush;
        var previous = scorer.IsUnusable;
        scorer.IsUnusable = node => isUsable != null && !isUsable(node);
        try { ranked = scorer.Score(_graph, Director.Heat, _bootstrap.Blackboard.RecommendedRoute, playerNode, Time.time); }
        finally { scorer.IsUnusable = previous; }

        ranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        return ranked;
    }

    /// <summary>Records that something was staged at a node, so it is not picked again straight away.</summary>
    public void NoteAmbushUsed(int node)
    {
        if (Director != null) Director.Ambush.NoteUsed(node, Time.time);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || Director == null || _graph == null) return;

        // Heat, as a red glow over the graph. This is the hive's belief, not the truth.
        for (int n = 0; n < _graph.Count; n++)
        {
            float h = Director.Heat.HeatAt(n);
            if (h <= 0.01f) continue;
            Gizmos.color = new Color(1f, 0f, 0f, Mathf.Clamp01(h));
            Gizmos.DrawSphere(_graph[n].Position.ToUnity(), 0.3f + h);
        }

        if (Director.CurrentAmbush.IsValid)
        {
            Gizmos.color = Color.yellow;
            Vector3 at = _graph[Director.CurrentAmbush.Node].Position.ToUnity();
            Gizmos.DrawWireCube(at + Vector3.up, Vector3.one * 1.5f);
        }
    }
}
