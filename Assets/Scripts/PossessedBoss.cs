using System;
using UnityEngine;
using UnityEngine.AI;

// Agent 3 owns movement and combat; Agent 4 selects appearances.
[RequireComponent(typeof(NavMeshAgent))]
public class PossessedBoss : MonoBehaviour
{
    public enum BossState { Hidden, Appearing, Intercepting, Pursuing, Grabbing, Despawned, Finished }
    public BossState currentState = BossState.Hidden;
    public Transform player;
    public Transform[] interceptPoints = new Transform[0];
    public Renderer[] visuals = new Renderer[0];
    public Collider[] bodyColliders = new Collider[0];
    public AudioSource voice;
    public AudioClip warningClip;
    public float predictionTime = 2f, movementSpeed = 6.8f, appearanceWarning = 1.25f;
    public float grabReach = 1.35f, grabWindup = .85f, grabRecovery = 1.5f;
    public float sightMemory = 3f, commitmentSeconds = 1.5f;
    public event Action<PossessedBoss> Banished;
    public event Action<PossessedBoss> RelocationRequested;
    public Vector3 LastInterceptTarget { get; private set; }
    public Vector3 ObservedVelocity { get; private set; }
    public bool IsPresent => currentState == BossState.Intercepting || currentState == BossState.Pursuing || currentState == BossState.Grabbing;
    NavMeshAgent agent;
    PlayerGameplay gameplay;
    Vector3 previousPlayer, lastSeen;
    float deadline, nextPlan, lastSight, nextGrab;
    NavMeshPath route, playerRoute;
    RaycastHit[] sightHits;

    void Awake()
    {
        route = new NavMeshPath(); playerRoute = new NavMeshPath(); sightHits = new RaycastHit[32];
        agent = GetComponent<NavMeshAgent>(); agent.speed = movementSpeed;
        if (visuals.Length == 0) visuals = GetComponentsInChildren<Renderer>(true);
        if (bodyColliders.Length == 0) bodyColliders = GetComponentsInChildren<Collider>(true);
        Hide(BossState.Hidden);
    }
    public void SetPlayer(PlayerGameplay target)
    {
        gameplay = target; player = target != null ? target.transform : null;
        previousPlayer = player != null ? player.position : Vector3.zero;
        ObservedVelocity = Vector3.zero;
    }
    void Update()
    {
        if (player == null || gameplay == null || Time.deltaTime <= 0f || gameplay.BlocksControls) return;
        Vector3 velocity = (player.position - previousPlayer) / Time.deltaTime; previousPlayer = player.position; velocity.y = 0;
        ObservedVelocity = Vector3.Lerp(ObservedVelocity, Vector3.ClampMagnitude(velocity, 9f), 1f - Mathf.Exp(-5f * Time.deltaTime));
        if (currentState == BossState.Appearing)
        {
            if (Time.time >= deadline)
            {
                // Nathan may turn or move during the warning. Recheck at reveal time.
                var visibilityCamera = gameplay.View;
                Vector3 screen = visibilityCamera.WorldToViewportPoint(transform.position + Vector3.up);
                bool inView = screen.z > 0 && screen.x > -.1f && screen.x < 1.1f && screen.y > -.1f && screen.y < 1.1f;
                if (FlatDistance(transform.position, player.position) < 8f || (inView && HasClearSight(visibilityCamera.transform.position)))
                { Hide(BossState.Hidden); RelocationRequested?.Invoke(this); return; }
                SetVisible(true); currentState = BossState.Intercepting; lastSight = Time.time; nextPlan = 0;
                if (agent.isOnNavMesh) agent.isStopped = false;
            }
            return;
        }
        if (!IsPresent || !agent.isOnNavMesh) return;
        bool seesPlayer = HasClearSight(player.position + Vector3.up);
        if (seesPlayer) { lastSeen = player.position; lastSight = Time.time; }
        if (currentState == BossState.Grabbing)
        {
            if (Time.time < deadline) return;
            if (seesPlayer && FlatDistance(transform.position, player.position) <= grabReach)
            { currentState = BossState.Finished; gameplay.EndRun(false); }
            else { currentState = BossState.Pursuing; nextGrab = Time.time + grabRecovery; agent.isStopped = false; }
            return;
        }
        if (seesPlayer && Time.time >= nextGrab && FlatDistance(transform.position, player.position) <= grabReach)
        { currentState = BossState.Grabbing; deadline = Time.time + grabWindup; agent.isStopped = true; Cue(.8f); return; }
        if (Time.time < nextPlan) return;
        nextPlan = Time.time + Mathf.Max(.5f, commitmentSeconds);
        if (Time.time - lastSight > sightMemory)
        {
            if (FlatDistance(transform.position, lastSeen) < 2f || Time.time - lastSight > sightMemory + 4f)
            { Hide(BossState.Hidden); RelocationRequested?.Invoke(this); return; }
            currentState = BossState.Pursuing; agent.SetDestination(lastSeen); return;
        }
        Vector3 target = SelectIntercept();
        currentState = FlatDistance(target, player.position) > 2f ? BossState.Intercepting : BossState.Pursuing;
        LastInterceptTarget = target; agent.SetDestination(target);
    }
    Vector3 SelectIntercept()
    {
        if (route == null) route = new NavMeshPath();
        if (playerRoute == null) playerRoute = new NavMeshPath();
        Vector3 heading = ObservedVelocity.sqrMagnitude > .5f ? ObservedVelocity.normalized : Vector3.zero;
        float best = float.NegativeInfinity; Vector3 result = player.position;
        foreach (var point in interceptPoints)
        {
            if (point == null || heading == Vector3.zero) continue;
            Vector3 delta = point.position - player.position; delta.y = 0;
            if (delta.magnitude < 3f || delta.magnitude > 32f || Vector3.Dot(heading, delta.normalized) < .35f) continue;
            if (!CompletePath(player.position, point.position, playerRoute) || !CompletePath(transform.position, point.position, route)) continue;
            float playerLength = PathLength(playerRoute), bossLength = PathLength(route);
            if (PassesNearPlayer(route, 2f)) continue;
            float playerETA = playerLength / Mathf.Max(ObservedVelocity.magnitude, 2f);
            if (bossLength / Mathf.Max(.1f, movementSpeed) > playerETA + 1f) continue;
            float score = Vector3.Dot(heading, delta.normalized) * 5f - Mathf.Abs(playerETA - predictionTime) - bossLength * .04f;
            if (score > best) { best = score; result = point.position; }
        }
        NavMeshHit sampled;
        return NavMesh.SamplePosition(result, out sampled, 1.5f, NavMesh.AllAreas) ? sampled.position : transform.position;
    }
    bool PassesNearPlayer(NavMeshPath path, float radius)
    {
        var corners = path.corners;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 start = corners[i-1], segment = corners[i] - start;
            float t = Mathf.Clamp01(Vector3.Dot(player.position - start, segment) / Mathf.Max(.001f, segment.sqrMagnitude));
            if (FlatDistance(start + segment * t, player.position) < radius) return true;
        }
        return false;
    }
    public bool AppearAt(Vector3 feet, Quaternion rotation)
    {
        if (IsPresent || currentState == BossState.Appearing || currentState == BossState.Finished) return false;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(feet, out hit, .8f, NavMesh.AllAreas)) return false;
        agent.enabled = true;
        if (!agent.Warp(hit.position)) { agent.enabled = false; return false; }
        transform.rotation = rotation; agent.isStopped = true; agent.ResetPath();
        currentState = BossState.Appearing; deadline = Time.time + Mathf.Max(1f, appearanceWarning);
        lastSeen = player != null ? player.position : hit.position; Cue(.65f); return true;
    }
    public void HitBySilverBullet()
    {
        if (!IsPresent) return;
        Hide(BossState.Despawned); Banished?.Invoke(this);
    }
    public void FinishEncounter() { Hide(BossState.Finished); }
    void Hide(BossState state)
    {
        currentState = state;
        if (agent != null) { if (agent.enabled && agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
        SetVisible(false);
    }
    void SetVisible(bool visible)
    {
        foreach (var r in visuals) if (r != null) r.enabled = visible;
        foreach (var c in bodyColliders) if (c != null) c.enabled = visible;
    }
    void Cue(float pitch) { if (voice != null && warningClip != null) { voice.pitch = pitch; voice.PlayOneShot(warningClip); } }
    bool HasClearSight(Vector3 target)
    {
        if (sightHits == null) sightHits = new RaycastHit[32];
        Vector3 origin = transform.position + Vector3.up * 1.3f, delta = target - origin;
        int count = Physics.RaycastNonAlloc(origin, delta.normalized, sightHits, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var collider = sightHits[i].collider;
            if (collider == null) continue;
            if (!collider.transform.IsChildOf(transform) && !collider.transform.IsChildOf(player)) return false;
        }
        return true;
    }
    public static bool CompletePath(Vector3 from, Vector3 to, NavMeshPath path) => NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    public static float PathLength(NavMeshPath path)
    {
        float length = 0; var corners = path.corners;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i-1], corners[i]);
        return length;
    }
    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a,b); }
}
