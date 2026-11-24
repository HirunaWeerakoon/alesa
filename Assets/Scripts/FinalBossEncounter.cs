using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Decides when the boss may appear and checks each spot is safe. Where it appears next is chosen by the hive
// mind (see useHiveMind); the built-in heading-based choice is the fallback when the hive is unavailable.
public sealed class FinalBossEncounter : MonoBehaviour
{
    public PossessedBoss boss;
    public PlayerGameplay player;
    public Transform prototypeStart;
    public AmbushNode[] appearancePoints = new AmbushNode[0];
    public bool automaticAppearances = true, requireAlesaRescued = true;
    public float minimumAppearanceDistance = 8f;
    [Tooltip("Obsolete: replaced by Respawn After Banish. Kept so existing scenes still load.")]
    public Vector2 banishmentSeconds = new Vector2(5f,7f);
    [Header("Respawn pace")]
    [Tooltip("After a silver round drives her off, seconds before she may appear again. Short, so there is no time to relax.")]
    public Vector2 respawnAfterBanish = new Vector2(2.5f, 4f);
    [Tooltip("After she lets go of a player who got away, seconds before she may appear ahead of them.")]
    [Min(0.5f)] public float respawnAfterRelocation = 2.5f;
    [Header("Hive mind")]
    [Tooltip("Ask the hive mind where the boss appears next. When it is unavailable the built-in heading-based choice is used.")]
    public bool useHiveMind = true;
    [Tooltip("Left empty, the scene's HiveMindRunner is used, or one is created when the encounter begins.")]
    public HiveMindRunner hive;
    [Tooltip("An ambush point further than this from the hive's nearest graph node is not offered to the hive (m).")]
    [Min(0.5f)] public float hiveGraphTolerance = 4f;
    public bool logDecisions;
    public enum SelectionSource { None, Hive, Fallback }
    public SelectionSource LastSource { get; private set; }
    public bool EncounterActive { get; private set; }
    public bool Escaped { get; private set; }
    public AmbushNode LastAppearance { get; private set; }
    float nextAttempt, safeUntil;
    Vector3 banishedAt, heading = Vector3.forward, previousPosition;
    bool requireProgress;
    NavMeshPath path;
    readonly Dictionary<int, List<AmbushNode>> pointsByGraphNode = new Dictionary<int, List<AmbushNode>>();
    HiveMindRunner mappedFor;
    void Awake() { path = new NavMeshPath(); }
    void OnEnable() { if (boss != null) { boss.Banished += OnBanished; boss.RelocationRequested += OnRelocation; } }
    void OnDisable() { if (boss != null) { boss.Banished -= OnBanished; boss.RelocationRequested -= OnRelocation; } }
    public void BeginEncounter()
    {
        if (EncounterActive || Escaped || player == null || boss == null || (requireAlesaRescued && !player.AlesaFreed)) return;
        EncounterActive = true; previousPosition = player.transform.position; boss.SetPlayer(player);
        ConnectHive();
        safeUntil = Time.time + 2f;
        player.Notify("Something is hunting you. Find the exit. Silver rounds can drive it away.",5f);
    }
    [ContextMenu("Prototype/Start Encounter (Play Mode)")]
    public void StartPrototype()
    {
        if (!Application.isPlaying || player == null || player.State.IsTerminal) return;
        if (player.BlocksControls) player.CloseScreen();
        if (prototypeStart != null)
        {
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            player.transform.SetPositionAndRotation(prototypeStart.position, prototypeStart.rotation);
            if (controller != null) controller.enabled = wasEnabled;
            player.Motor.ProcessMove(Vector2.zero);
        }
        // Supplies belong to the explicit debug command, never normal maze entry.
        if (!player.Combat.HasRevolver) player.Combat.Equip(6);
        if (player.Combat.silverAmmo.loaded + player.Combat.silverAmmo.spare == 0) player.Combat.AddSilverAmmo(6);
        bool required = requireAlesaRescued; requireAlesaRescued = false; BeginEncounter(); requireAlesaRescued = required;
    }
    void Update()
    {
        if (!EncounterActive || player == null || player.BlocksControls || Time.deltaTime <= 0) return;
        UpdateHiveFlags();
        Vector3 movement = player.transform.position-previousPosition; movement.y = 0; previousPosition = player.transform.position;
        if (movement.sqrMagnitude > .0001f) heading = Vector3.Slerp(heading,movement.normalized,.25f);
        if (!automaticAppearances || Time.time < safeUntil || Time.time < nextAttempt || boss.IsPresent || boss.currentState == PossessedBoss.BossState.Appearing) return;
        if (requireProgress && Vector3.Distance(player.transform.position,banishedAt) < 3f) return;
        nextAttempt = Time.time+.75f;
        AmbushNode point = SelectAppearance();
        if (point != null && boss.AppearAt(point.SpawnPoint.position,point.SpawnPoint.rotation))
        {
            LastAppearance = point; requireProgress = false;
            // The lights near her go out as she arrives, before she is visible.
            LightFlicker.DisturbNear(point.SpawnPoint.position, 11f, 2.5f);
            if (point.ambushAudioSource != null && point.ambushAudioSource.clip != null) point.ambushAudioSource.Play();
        }
    }
    public AmbushNode SelectAppearance()
    {
        if (player == null || player.View == null) return null;
        if (path == null) path = new NavMeshPath();
        AmbushNode viaHive = useHiveMind ? SelectViaHive() : null;
        if (viaHive != null) { LastSource = SelectionSource.Hive; return viaHive; }
        AmbushNode fallback = SelectByHeading();
        LastSource = fallback != null ? SelectionSource.Fallback : SelectionSource.None;
        return fallback;
    }

    // ---- hive mind -------------------------------------------------------------------------------------------

    void ConnectHive()
    {
        if (!useHiveMind) return;
        if (hive == null) hive = FindFirstObjectByType<HiveMindRunner>();
        if (hive == null) hive = new GameObject("Hive Mind (runtime)").AddComponent<HiveMindRunner>();
    }

    // Tells the director what the player is going through, so its pacing and intensity react to the chase.
    void UpdateHiveFlags()
    {
        if (hive == null) return;
        var blackboard = hive.Blackboard;
        if (blackboard == null) return;
        blackboard.EscapePhaseActive = EncounterActive;
        blackboard.BossActive = boss != null && (boss.IsPresent || boss.currentState == PossessedBoss.BossState.Appearing);
    }

    // The ambush points are fixed in the level, so each is matched to its nearest hive graph node once.
    void MapPointsToGraph()
    {
        pointsByGraphNode.Clear();
        mappedFor = hive;
        foreach (var point in appearancePoints)
        {
            if (point == null) continue;
            int node = hive.NodeNear(point.SpawnPoint.position, hiveGraphTolerance);
            if (node < 0) continue;
            List<AmbushNode> list;
            if (!pointsByGraphNode.TryGetValue(node, out list)) pointsByGraphNode[node] = list = new List<AmbushNode>();
            list.Add(point);
        }
    }

    // The hive ranks places by what it has heard and how far they are; this only offers it places that are
    // safe to use right now (unseen, reachable, ahead of the player), and returns the one it picks.
    AmbushNode SelectViaHive()
    {
        if (hive == null || !hive.IsReady) return null;
        if (mappedFor != hive) MapPointsToGraph();
        if (pointsByGraphNode.Count == 0) return null;

        var usable = new Dictionary<int, AmbushNode>();
        foreach (var pair in pointsByGraphNode)
        {
            AmbushNode best = null; float bestValue = float.NegativeInfinity;
            foreach (var point in pair.Value)
            {
                float value;
                if (TryEvaluate(point, out value) && value > bestValue) { bestValue = value; best = point; }
            }
            if (best != null) usable[pair.Key] = best;
        }
        if (usable.Count == 0) return null;

        var ranked = hive.RankAmbushNodes(usable.ContainsKey);
        if (ranked.Count == 0) return null;

        var chosen = ranked[0];
        hive.NoteAmbushUsed(chosen.Node);
        if (logDecisions)
            Debug.Log($"[Boss] hive chose graph node {chosen.Node} ({usable[chosen.Node].name}), score {chosen.Score:0.00}, " +
                      $"{chosen.Distance:0.#} m away, {usable.Count} safe candidates", this);
        return usable[chosen.Node];
    }

    // ---- built-in choice -------------------------------------------------------------------------------------

    AmbushNode SelectByHeading()
    {
        AmbushNode best = null; float score = float.NegativeInfinity;
        foreach (var point in appearancePoints)
        {
            float value;
            if (TryEvaluate(point, out value) && value > score) { score = value; best = point; }
        }
        return best;
    }

    // Whether the boss may appear at a point right now, and how good a spot it is by heading and distance.
    bool TryEvaluate(AmbushNode point, out float value)
    {
        value = 0f;
        if (point == null || point == LastAppearance || point.isConsumed) return false;
        Vector3 position = point.SpawnPoint.position, delta = position - player.transform.position; delta.y = 0;
        if (delta.magnitude < minimumAppearanceDistance || Vector3.Dot(heading, delta.normalized) < .1f) return false;
        if (!PossessedBoss.CompletePath(player.transform.position, position, path)) return false;
        float length = PossessedBoss.PathLength(path);
        float safetyDistance = minimumAppearanceDistance + boss.ObservedVelocity.magnitude * boss.appearanceWarning;
        if (length < safetyDistance || length > 45f || IsVisible(position + Vector3.up) || IsVisible(position + Vector3.up * 1.8f)) return false;
        value = Vector3.Dot(heading, delta.normalized) * 8f - Mathf.Abs(length - 20f) * .3f;
        return true;
    }

    bool IsVisible(Vector3 point)
    {
        var camera=player.View; Vector3 viewport=camera.WorldToViewportPoint(point);
        if (viewport.z<=0 || viewport.x<-.1f || viewport.x>1.1f || viewport.y<-.1f || viewport.y>1.1f) return false;
        RaycastHit hit; Vector3 delta=point-camera.transform.position;
        if (!player.RaycastFromPoint(camera.transform.position,delta.normalized,delta.magnitude,~0,out hit)) return true;
        return hit.distance>=delta.magnitude-.15f;
    }
    void OnBanished(PossessedBoss ignored)
    {
        safeUntil=Time.time+Random.Range(Mathf.Max(1f,respawnAfterBanish.x),Mathf.Max(1f,respawnAfterBanish.y));
        banishedAt=player.transform.position; requireProgress=true; player.Notify("It is gone. Keep moving!",2f);
    }
    void OnRelocation(PossessedBoss ignored) { safeUntil=Time.time+respawnAfterRelocation; }
    public void ReachExit()
    {
        if (!EncounterActive || Escaped || player == null || !player.State.CanMove) return;
        Escaped=true; EncounterActive=false; boss.FinishEncounter(); player.EndRun(true);
        UpdateHiveFlags();
    }
}
