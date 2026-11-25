using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The one scripted sighting: the player turns east out of room F, and she crosses the junction
/// ahead of them, walks north up the corridor and fades away.
/// </summary>
/// <remarks>
/// Deliberately not run through <see cref="PossessedBoss"/>. That component is the hunting AI --
/// it intercepts, pursues and grabs. A cameo must do exactly one thing the same way every time,
/// so this spawns its own copy of the prefab, switches the AI off and walks it down a fixed line.
/// The real encounter boss is left completely alone.
/// </remarks>
[DisallowMultipleComponent]
public sealed class BossCorridorCameo : MonoBehaviour
{
    [Header("Who")]
    [Tooltip("PossessedBoss.prefab. A throwaway copy is spawned and destroyed; the scene's own boss is untouched.")]
    [SerializeField] private GameObject bossPrefab;

    [Tooltip("Left empty, the scene's player is found at startup.")]
    [SerializeField] private PlayerGameplay player;

    [Header("Where it fires")]
    [Tooltip("Metres from this object, measured flat. The player simply has to walk within this of it.")]
    [SerializeField, Min(0.5f)] private float activationRadius = 6f;

    [Tooltip("She needs room to appear ahead down the corridor rather than on top of the player, so it " +
             "holds off until they are at least this far from the spot she walks out at.")]
    [SerializeField, Min(0f)] private float minimumLead = 8f;

    [Tooltip("Shown as a box in the Scene view. Presentation only — firing is the radius above.")]
    [SerializeField] private Vector3 size = new Vector3(7f, 3f, 4f);

    [Tooltip("The direction of the clay arrow, for the gizmo and for the optional facing check.")]
    [SerializeField] private Vector3 approachDirection = Vector3.right;

    [Tooltip("Degrees the player's facing may differ from the arrow. 180 turns the check off, which is the " +
             "default: the lead distance above already guarantees she appears ahead of them.")]
    [SerializeField, Range(5f, 180f)] private float facingTolerance = 180f;

    [Header("Her walk")]
    [Tooltip("Where she appears -- the corridor junction, at floor height.")]
    [SerializeField] private Vector3 appearPoint = new Vector3(167.5f, 24.6f, 21.5f);

    [Tooltip("Where she fades out -- up the corridor, in the direction of the black arrow.")]
    [SerializeField] private Vector3 vanishPoint = new Vector3(167.5f, 24.6f, 36f);

    [Tooltip("1.8 is the Walking clip's own pace in SharedHumanoidLocomotion. Going slower blends idle " +
             "into the walk and she shuffles with sliding feet; the player still outpaces her at 5 m/s.")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.8f;

    [SerializeField, Min(0f)] private float vanishSeconds = 1.2f;

    [Tooltip("Seconds held at the end for Female Stop Walking (1.53 s) before she fades.")]
    [SerializeField, Min(0f)] private float stopSeconds = 1.1f;

    [Tooltip("Safety net: she fades out after this long even if the path is blocked and she never arrives.")]
    [SerializeField, Min(2f)] private float maximumWalkSeconds = 20f;

    [Header("Sound, in this order")]
    [SerializeField] private AudioClip firstClip;
    [SerializeField] private AudioClip secondClip;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("0 = heard the same anywhere, 1 = fully positional. Part way, so the line still lands down a long corridor.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.55f;

    [SerializeField, Min(1f)] private float audibleRange = 60f;

    [Header("Player")]
    [Tooltip("Holds the player to a walk until she has gone and both lines have finished.")]
    [SerializeField] private bool walkOnly = true;

    [Tooltip("Shown when she appears. Leave empty for no message.")]
    [SerializeField] private string notification = "";

    /// <summary>True once it has played. It never fires a second time in a session.</summary>
    public bool HasPlayed { get; private set; }

    private bool _running;

    private void Awake()
    {
        // A collider is no longer how this fires, but an old scene may still carry one and a solid
        // box sitting across the corridor would wall the player in.
        var box = GetComponent<BoxCollider>();
        if (box != null) { box.isTrigger = true; box.size = size; box.center = Vector3.zero; }

        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (approachDirection.sqrMagnitude < 0.0001f) approachDirection = Vector3.right;
    }

    /// <summary>
    /// Polled rather than driven by a trigger volume.
    /// </summary>
    /// <remarks>
    /// This has to fire on an ordinary play-through with nothing switched on for it, and a trigger
    /// volume has too many ways to quietly not fire: a CharacterController only raises callbacks
    /// while it moves, the collider has to be reached on the right layer, and the old version also
    /// demanded a facing and a measured speed, each of which could veto it on the one pass the
    /// player makes down this corridor. A flat distance check cannot miss.
    /// </remarks>
    private void Update()
    {
        if (HasPlayed || _running) return;

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerGameplay>();
            if (player == null) return;
        }
        if (player.BlocksControls) return; // paused, or reading a note

        Vector3 here = player.transform.position; here.y = 0f;
        Vector3 centre = transform.position; centre.y = 0f;
        if ((here - centre).sqrMagnitude > activationRadius * activationRadius) return;

        // She should walk out of the dark ahead of them, not materialise in their face.
        Vector3 appear = appearPoint; appear.y = 0f;
        if ((here - appear).magnitude < minimumLead) return;

        if (facingTolerance < 180f)
        {
            Vector3 arrow = approachDirection; arrow.y = 0f;
            Vector3 facing = player.View != null ? player.View.transform.forward : player.transform.forward;
            facing.y = 0f;
            if (arrow.sqrMagnitude > 0.0001f && facing.sqrMagnitude > 0.0001f
                && Vector3.Angle(facing.normalized, arrow.normalized) > facingTolerance) return;
        }

        HasPlayed = true;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        _running = true;

        GameObject voiceHost = new GameObject("Boss Cameo Voice");
        var source = voiceHost.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = volume;
        source.spatialBlend = spatialBlend;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 4f;
        source.maxDistance = audibleRange;
        voiceHost.transform.position = appearPoint;

        GameObject boss = Spawn();
        if (boss != null && !string.IsNullOrEmpty(notification) && player != null) player.Notify(notification, 4f);
        if (walkOnly) SetSprintLock(true);

        // Both lines run on their own clock: they are a voice-over, not something keyed to her feet.
        Coroutine lines = StartCoroutine(SpeakInOrder(source));

        if (boss != null) yield return Walk(boss, voiceHost.transform);
        yield return lines;

        if (walkOnly) SetSprintLock(false);
        Destroy(voiceHost);
        _running = false;
    }

    private IEnumerator SpeakInOrder(AudioSource source)
    {
        yield return PlayAndWait(source, firstClip);
        yield return PlayAndWait(source, secondClip);
    }

    private static IEnumerator PlayAndWait(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) yield break;

        // A clip imported without Preload Audio Data is not in memory yet, and Play on it starts an
        // asynchronous load and returns with isPlaying still false. Waiting on isPlaying alone fell
        // straight through on the first frame and both lines were skipped in silence. Load first.
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
            float loadDeadline = Time.realtimeSinceStartup + 5f;
            while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < loadDeadline)
                yield return null;
        }

        source.clip = clip;
        source.Play();

        // Even loaded, isPlaying can lag the Play call by a frame, so the wait starts only once it
        // has actually begun, and is backed by the clip's own length in case it never reports true.
        float startDeadline = Time.realtimeSinceStartup + 0.5f;
        while (source != null && !source.isPlaying && Time.realtimeSinceStartup < startDeadline)
            yield return null;

        float endDeadline = Time.realtimeSinceStartup + clip.length + 0.5f;
        while (source != null && source.isPlaying && Time.realtimeSinceStartup < endDeadline)
            yield return null;
    }

    /// <summary>A copy of the boss with the hunting AI switched off, stood at the junction facing up the corridor.</summary>
    private GameObject Spawn()
    {
        if (bossPrefab == null) { Debug.LogError("[Boss cameo] No boss prefab assigned.", this); return null; }

        Vector3 heading = vanishPoint - appearPoint; heading.y = 0f;
        Quaternion rotation = heading.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(heading.normalized)
            : transform.rotation;

        GameObject boss = Instantiate(bossPrefab, appearPoint, rotation);
        boss.name = "Possessed Boss (cameo)";

        // The prefab's own Awake has already run and hidden her, so everything below is undone by hand.
        // Switched off rather than destroyed: PossessedBossMotorBridge requires PossessedBoss,
        // AgentMotor and HeadLookAt, so removing them logs "Can't remove ... because ... depends on
        // it" and leaves the component behind anyway. A disabled component gets no Update, which is
        // all that is needed, and this object is thrown away at the end of the cameo regardless.
        var brain = boss.GetComponent<PossessedBoss>();
        if (brain != null) brain.enabled = false;
        var bridge = boss.GetComponent<PossessedBossMotorBridge>();
        if (bridge != null) bridge.enabled = false;
        var motor = boss.GetComponent<Alesa.Runtime.Motion.AgentMotor>();
        if (motor != null) motor.enabled = false;
        foreach (var look in boss.GetComponentsInChildren<Alesa.Runtime.Motion.HeadLookAt>(true)) look.enabled = false;

        foreach (var r in boss.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        // She is a vision, not an obstacle: the player walks straight through where she was.
        foreach (var c in boss.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        var agent = boss.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = true;
            agent.speed = walkSpeed;
            agent.angularSpeed = 180f;
            agent.acceleration = 4f;
            agent.stoppingDistance = 0.2f;
            agent.updateRotation = true;
            agent.updatePosition = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(appearPoint, out hit, 2f, NavMesh.AllAreas)) agent.Warp(hit.position);
            else Debug.LogWarning("[Boss cameo] Appear point is not on the NavMesh; she will stand still.", this);
        }

        LightFlicker.DisturbNear(appearPoint, 12f, 2.5f);
        return boss;
    }

    private IEnumerator Walk(GameObject boss, Transform voiceHost)
    {
        var agent = boss.GetComponent<NavMeshAgent>();
        var animator = boss.GetComponentInChildren<Animator>();

        Vector3 destination = vanishPoint;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(vanishPoint, out hit, 3f, NavMesh.AllAreas)) destination = hit.position;
        if (agent != null && agent.enabled && agent.isOnNavMesh) agent.SetDestination(destination);

        float deadline = Time.time + maximumWalkSeconds;
        while (Time.time < deadline)
        {
            if (boss == null) yield break;
            if (voiceHost != null) voiceHost.position = boss.transform.position;

            float speed = agent != null && agent.enabled ? agent.velocity.magnitude : 0f;
            if (animator != null)
            {
                animator.SetFloat("Speed", speed);
                // Her own controller starts the Female Start Walking / Female Walk chain off a bool,
                // so Speed alone would leave her sliding along in the idle pose.
                SetBoolIfPresent(animator, "Moving", speed > 0.05f);
            }

            if (agent == null || !agent.enabled || !agent.isOnNavMesh) break;
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f) break;
            yield return null;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
        if (animator != null)
        {
            animator.SetFloat("Speed", 0f);
            // Female Stop Walking plays here, so she pulls up before fading rather than cutting.
            SetBoolIfPresent(animator, "Moving", false);
        }
        yield return new WaitForSeconds(stopSeconds);
        yield return FadeOut(boss);
        if (boss != null) Destroy(boss);
    }

    /// <summary>Fades her out where she stands, the same way a silver round banishes the real boss.</summary>
    private IEnumerator FadeOut(GameObject boss)
    {
        if (boss == null || vanishSeconds <= 0f) yield break;

        var renderers = boss.GetComponentsInChildren<Renderer>(true);
        var copies = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            var shared = renderers[i].sharedMaterials;
            copies[i] = new Material[shared.Length];
            for (int j = 0; j < shared.Length; j++)
            {
                if (shared[j] == null) continue;
                var copy = new Material(shared[j]);
                if (copy.shader != null && copy.shader.name == "Standard") MakeFade(copy);
                copies[i][j] = copy;
            }
            renderers[i].materials = copies[i];
        }

        Vector3 start = boss.transform.position;
        for (float t = 0f; t < vanishSeconds; t += Time.deltaTime)
        {
            if (boss == null) yield break;
            float k = Mathf.Clamp01(t / vanishSeconds);
            float alpha = 1f - k * k;
            foreach (var row in copies)
            {
                if (row == null) continue;
                foreach (var m in row)
                    if (m != null && m.HasProperty("_Color")) { var c = m.color; c.a = alpha; m.color = c; }
            }
            boss.transform.position = start + Vector3.down * (0.3f * k);
            yield return null;
        }

        foreach (var row in copies) if (row != null) foreach (var m in row) if (m != null) Destroy(m);
    }

    /// <summary>Sets a bool only when the controller actually declares it, so a swapped controller cannot throw.</summary>
    private static void SetBoolIfPresent(Animator animator, string name, bool value)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters) if (p.name == name) { animator.SetBool(name, value); return; }
    }

    private static void MakeFade(Material m)
    {
        m.SetFloat("_Mode", 2);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    private void SetSprintLock(bool locked)
    {
        if (player == null || player.Motor == null) return;
        player.Motor.sprintLocked = locked;
    }

    private void OnDisable()
    {
        // Never leave the player stuck at walking pace because the object was switched off mid-cameo.
        if (_running) SetSprintLock(false);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.9f, 0.2f, 0.4f, 0.5f);
        Gizmos.DrawWireCube(transform.position, size);
        // What actually fires it.
        Gizmos.color = new Color(0.9f, 0.2f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, activationRadius);

        Vector3 arrow = approachDirection; arrow.y = 0f;
        if (arrow.sqrMagnitude > 0.0001f)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.2f);
            Gizmos.DrawRay(transform.position, arrow.normalized * 4f);
        }

        Gizmos.color = Color.black;
        Gizmos.DrawLine(appearPoint, vanishPoint);
        Gizmos.DrawWireSphere(appearPoint, 0.4f);
        Gizmos.color = new Color(0.4f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(vanishPoint, 0.4f);
    }
}
