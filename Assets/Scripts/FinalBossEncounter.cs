using UnityEngine;
using UnityEngine.AI;

// Temporary Agent 4 adapter. Disable automatic appearances for the real director.
public sealed class FinalBossEncounter : MonoBehaviour
{
    public PossessedBoss boss;
    public PlayerGameplay player;
    public Transform prototypeStart;
    public AmbushNode[] appearancePoints = new AmbushNode[0];
    public bool automaticAppearances = true, requireAlesaRescued = true;
    public float minimumAppearanceDistance = 8f;
    public Vector2 banishmentSeconds = new Vector2(5f,7f);
    public bool EncounterActive { get; private set; }
    public bool Escaped { get; private set; }
    public AmbushNode LastAppearance { get; private set; }
    float nextAttempt, safeUntil;
    Vector3 banishedAt, heading = Vector3.forward, previousPosition;
    bool requireProgress;
    NavMeshPath path;
    void Awake() { path = new NavMeshPath(); }
    void OnEnable() { if (boss != null) { boss.Banished += OnBanished; boss.RelocationRequested += OnRelocation; } }
    void OnDisable() { if (boss != null) { boss.Banished -= OnBanished; boss.RelocationRequested -= OnRelocation; } }
    public void BeginEncounter()
    {
        if (EncounterActive || Escaped || player == null || boss == null || (requireAlesaRescued && !player.AlesaFreed)) return;
        EncounterActive = true; previousPosition = player.transform.position; boss.SetPlayer(player);
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
        Vector3 movement = player.transform.position-previousPosition; movement.y = 0; previousPosition = player.transform.position;
        if (movement.sqrMagnitude > .0001f) heading = Vector3.Slerp(heading,movement.normalized,.25f);
        if (!automaticAppearances || Time.time < safeUntil || Time.time < nextAttempt || boss.IsPresent || boss.currentState == PossessedBoss.BossState.Appearing) return;
        if (requireProgress && Vector3.Distance(player.transform.position,banishedAt) < 3f) return;
        nextAttempt = Time.time+.75f;
        AmbushNode point = SelectAppearance();
        if (point != null && boss.AppearAt(point.SpawnPoint.position,point.SpawnPoint.rotation))
        {
            LastAppearance = point; requireProgress = false;
            if (point.ambushAudioSource != null && point.ambushAudioSource.clip != null) point.ambushAudioSource.Play();
        }
    }
    public AmbushNode SelectAppearance()
    {
        if (player == null || player.View == null) return null;
        if (path == null) path = new NavMeshPath();
        AmbushNode best = null; float score = float.NegativeInfinity;
        foreach (var point in appearancePoints)
        {
            if (point == null || point == LastAppearance || point.isConsumed) continue;
            Vector3 position=point.SpawnPoint.position, delta=position-player.transform.position; delta.y=0;
            if (delta.magnitude < minimumAppearanceDistance || Vector3.Dot(heading,delta.normalized) < .1f) continue;
            if (!PossessedBoss.CompletePath(player.transform.position,position,path)) continue;
            float length=PossessedBoss.PathLength(path);
            float safetyDistance = minimumAppearanceDistance + boss.ObservedVelocity.magnitude * boss.appearanceWarning;
            if (length < safetyDistance || length > 45f || IsVisible(position+Vector3.up) || IsVisible(position+Vector3.up*1.8f)) continue;
            float value=Vector3.Dot(heading,delta.normalized)*8f-Mathf.Abs(length-20f)*.3f;
            if (value > score) { score=value; best=point; }
        }
        return best;
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
        safeUntil=Time.time+Random.Range(Mathf.Max(5f,banishmentSeconds.x),Mathf.Max(5f,banishmentSeconds.y));
        banishedAt=player.transform.position; requireProgress=true; player.Notify("It is gone. Keep moving!",2f);
    }
    void OnRelocation(PossessedBoss ignored) { safeUntil=Time.time+3f; }
    public void ReachExit()
    {
        if (!EncounterActive || Escaped || player == null || !player.State.CanMove) return;
        Escaped=true; EncounterActive=false; boss.FinishEncounter(); player.EndRun(true);
    }
}
