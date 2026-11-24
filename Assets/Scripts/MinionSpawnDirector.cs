using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Places the level's minions and streams them in and out as the player moves.
/// </summary>
/// <remarks>
/// The pattern modern horror games use: everything is created once at load and then parked, and
/// only what is near the player is awake. Nothing is instantiated mid-level, because that is what
/// causes the hitch you feel just before a scare, and nothing distant is left running, because 40
/// shadow-casting realtime lights make an off-screen skinned mesh far from free.
///
/// Three stages:
///   1. Load — every minion is instantiated over several frames, then <c>SetActive(false)</c>.
///      Spreading it avoids one long frame at level start.
///   2. Parked — inactive. No renderers, no Animator, no Update, no NavMesh queries. The prefab's
///      meshes and textures are already in memory, so waking one costs nothing but the activation.
///   3. Awake — within <see cref="activateDistance"/> of the player. Deactivates again past
///      <see cref="deactivateDistance"/>; the gap between the two is what stops a minion flickering
///      on and off while the player paces the threshold.
///
/// Unity's frustum culling already skips drawing anything behind the player, so this is about the
/// costs culling does not remove: animation, AI, pathfinding and shadow casting.
/// </remarks>
[DisallowMultipleComponent]
public sealed class MinionSpawnDirector : MonoBehaviour
{
    /// <summary>A place minions can be put, and how many belong there.</summary>
    [System.Serializable]
    public sealed class Zone
    {
        [Tooltip("For the inspector and the spawn log only.")]
        public string label = "Zone";

        public Vector3 center;
        public Vector3 size = new Vector3(6f, 3f, 6f);

        [Tooltip("Minions placed here. Ignored when this zone is part of a shared group.")]
        [Min(0)] public int count = 1;

        [Tooltip("Zones with the same non-empty group name share one budget, split randomly between them.")]
        public string sharedGroup = "";

        [Tooltip("Leave empty to use the director's default prefabs.")]
        public GameObject prefabOverride;

        [HideInInspector] public int resolvedCount;
    }

    [Header("Prefabs (picked at random per minion unless a zone overrides)")]
    [SerializeField] private GameObject[] minionPrefabs;

    [Header("Zones")]
    [SerializeField]
    private Zone[] zones =
    {
        new Zone { label = "C storage",  center = new Vector3(121.5f, 24.6f, -4.5f), size = new Vector3(11f, 3f, 5f),  count = 1 },
        new Zone { label = "D shower",   center = new Vector3(118.5f, 24.6f, 14.5f), size = new Vector3(6f, 3f, 6f),   sharedGroup = "DE" },
        new Zone { label = "E toilet",   center = new Vector3(118.5f, 24.6f, 23.5f), size = new Vector3(6f, 3f, 6f),   sharedGroup = "DE" },
        new Zone { label = "G west",     center = new Vector3(158.5f, 24.6f, 44.5f), size = new Vector3(10f, 3f, 13f), count = 1 },
        new Zone { label = "H central",  center = new Vector3(174.5f, 24.6f, 48.5f), size = new Vector3(8f, 3f, 8f),   count = 1 },
    };

    [Header("Shared group budgets")]
    [SerializeField] private string[] sharedGroupNames = { "DE" };
    [SerializeField] private int[] sharedGroupCounts = { 3 };

    [Header("Streaming")]
    [Tooltip("Distance from the player at which a parked minion wakes up.")]
    [SerializeField, Min(1f)] private float activateDistance = 38f;

    [Tooltip("Distance at which an awake minion is parked again. Must exceed activateDistance or they flicker.")]
    [SerializeField, Min(1f)] private float deactivateDistance = 50f;

    [Tooltip("Hard ceiling on minions awake at once, whatever the distances say. The nearest win.")]
    [SerializeField, Min(1)] private int maxSimultaneouslyAwake = 4;

    [Tooltip("Seconds between streaming passes. There is no reason to do this every frame.")]
    [SerializeField, Min(0.05f)] private float evaluateInterval = 0.35f;

    [Tooltip("Minions instantiated per frame during the load pass.")]
    [SerializeField, Min(1)] private int spawnsPerFrame = 1;

    [Header("Placement")]
    [Tooltip("Tries to find a walkable point inside a zone before giving up on that minion.")]
    [SerializeField, Min(1)] private int placementAttempts = 40;

    [Tooltip("Smallest gap (m) between two minions placed in the same zone.")]
    [SerializeField, Min(0f)] private float minSpawnSeparation = 2f;

    [Header("Debug")]
    [SerializeField] private bool logSpawns = true;
    [SerializeField] private bool drawGizmos = true;

    private readonly List<GameObject> _minions = new List<GameObject>();
    private readonly List<float> _distances = new List<float>();
    private Transform _player;
    private float _nextEvaluate;

    public int TotalSpawned => _minions.Count;

    /// <summary>
    /// How many minions the hive mind wants awake right now. It only limits waking NEW minions: one that is
    /// already awake is never parked because the budget dropped, since that would make a hunter vanish mid-chase.
    /// Left at its default, the director behaves exactly as before.
    /// </summary>
    public int AwakeBudget { get; set; } = int.MaxValue;

    /// <summary>Minions currently awake.</summary>
    public int AwakeCount
    {
        get
        {
            int n = 0;
            foreach (var m in _minions) if (m != null && m.activeSelf) n++;
            return n;
        }
    }

    /// <summary>
    /// Puts a parked minion at a position, awake, so it is already waiting when the player arrives. Used by the
    /// hive mind's ambushes. The nearest parked minion is used, so none is dragged across the hospital.
    /// Returns false when nothing could be staged (none parked, the awake ceiling is reached, or no floor there).
    /// </summary>
    public bool StageAmbush(Vector3 worldPosition)
    {
        _minions.RemoveAll(m => m == null);
        if (AwakeCount >= maxSimultaneouslyAwake) return false;
        if (!NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas)) return false;

        GameObject best = null;
        float bestDistance = float.MaxValue;
        foreach (var m in _minions)
        {
            if (m.activeSelf) continue;
            float d = Vector3.Distance(m.transform.position, hit.position);
            if (d < bestDistance) { bestDistance = d; best = m; }
        }
        if (best == null) return false;

        best.transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        best.SetActive(true);
        var motor = best.GetComponent<Alesa.Runtime.Motion.AgentMotor>();
        if (motor != null) motor.Warp(hit.position);
        if (logSpawns) Debug.Log("[Spawn] ambush staged: " + best.name + " at " + hit.position.ToString("F1"), best);
        return true;
    }

    private void Start()
    {
        if (deactivateDistance <= activateDistance) deactivateDistance = activateDistance + 8f;
        ResolveCounts();
        StartCoroutine(SpawnAll());
    }

    /// <summary>
    /// Turns the authored counts into per-zone numbers, dividing each shared group's budget
    /// randomly between its zones. The D/E pair uses this: three minions in that section, split
    /// however the dice fall, so the player cannot learn which bathroom is occupied.
    /// </summary>
    private void ResolveCounts()
    {
        foreach (var z in zones) z.resolvedCount = string.IsNullOrEmpty(z.sharedGroup) ? z.count : 0;

        for (int g = 0; g < sharedGroupNames.Length; g++)
        {
            string name = sharedGroupNames[g];
            if (string.IsNullOrEmpty(name)) continue;
            int budget = g < sharedGroupCounts.Length ? sharedGroupCounts[g] : 0;

            var members = new List<Zone>();
            foreach (var z in zones) if (z.sharedGroup == name) members.Add(z);
            if (members.Count == 0 || budget <= 0) continue;

            for (int i = 0; i < budget; i++) members[Random.Range(0, members.Count)].resolvedCount++;
        }
    }

    private IEnumerator SpawnAll()
    {
        if (minionPrefabs == null || minionPrefabs.Length == 0)
        {
            Debug.LogWarning("MinionSpawnDirector has no prefabs assigned; no minions will be placed.", this);
            yield break;
        }

        int sinceYield = 0;
        foreach (var zone in zones)
        {
            var placed = new List<Vector3>();
            for (int i = 0; i < zone.resolvedCount; i++)
            {
                if (!TryFindSpot(zone, placed, out Vector3 spot))
                {
                    Debug.LogWarning("MinionSpawnDirector: no walkable spot in zone '" + zone.label + "' for minion " + (i + 1) + ".", this);
                    continue;
                }

                GameObject prefab = zone.prefabOverride != null
                    ? zone.prefabOverride
                    : minionPrefabs[Random.Range(0, minionPrefabs.Length)];

                var minion = Instantiate(prefab, spot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
                minion.name = prefab.name + " (" + zone.label + ")";
                minion.SetActive(false);
                _minions.Add(minion);
                placed.Add(spot);

                if (logSpawns) Debug.Log("[Spawn] " + minion.name + " at " + spot.ToString("F1"), minion);

                if (++sinceYield >= spawnsPerFrame) { sinceYield = 0; yield return null; }
            }
        }

        if (logSpawns) Debug.Log("[Spawn] " + _minions.Count + " minions parked across " + zones.Length + " zones.", this);
    }

    private bool TryFindSpot(Zone zone, List<Vector3> alreadyPlaced, out Vector3 spot)
    {
        spot = zone.center;
        for (int attempt = 0; attempt < placementAttempts; attempt++)
        {
            var candidate = new Vector3(
                zone.center.x + Random.Range(-zone.size.x, zone.size.x) * 0.5f,
                zone.center.y,
                zone.center.z + Random.Range(-zone.size.z, zone.size.z) * 0.5f);

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;

            bool tooClose = false;
            foreach (var other in alreadyPlaced)
            {
                if (Vector3.Distance(other, hit.position) < minSpawnSeparation) { tooClose = true; break; }
            }
            if (tooClose) continue;

            spot = hit.position;
            return true;
        }
        return false;
    }

    private void Update()
    {
        if (Time.time < _nextEvaluate) return;
        _nextEvaluate = Time.time + evaluateInterval;
        Evaluate();
    }

    /// <summary>Wakes the nearest minions within range and parks the rest.</summary>
    private void Evaluate()
    {
        if (_player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go == null) return;
            _player = go.transform;
        }

        _minions.RemoveAll(m => m == null); // killed minions destroy themselves
        _distances.Clear();

        Vector3 p = _player.position;
        for (int i = 0; i < _minions.Count; i++) _distances.Add(Vector3.Distance(_minions[i].transform.position, p));

        // Rank by distance so the cap always keeps the nearest, not whichever happens to be first.
        var order = new List<int>(_minions.Count);
        for (int i = 0; i < _minions.Count; i++) order.Add(i);
        order.Sort((a, b) => _distances[a].CompareTo(_distances[b]));

        int awake = 0;
        foreach (int i in order)
        {
            GameObject m = _minions[i];
            bool active = m.activeSelf;
            float d = _distances[i];

            int wakeCap = Mathf.Min(maxSimultaneouslyAwake, Mathf.Max(0, AwakeBudget));
            bool shouldWake = active
                ? awake < maxSimultaneouslyAwake && d <= deactivateDistance
                : awake < wakeCap && d <= activateDistance;
            if (shouldWake) awake++;
            if (shouldWake != active) m.SetActive(shouldWake);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || zones == null) return;
        foreach (var z in zones)
        {
            bool shared = !string.IsNullOrEmpty(z.sharedGroup);
            Gizmos.color = shared ? new Color(1f, 0.4f, 0.8f, 0.9f) : new Color(0.2f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(z.center, z.size);
        }
    }
}
