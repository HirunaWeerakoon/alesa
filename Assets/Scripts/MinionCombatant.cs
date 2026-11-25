using UnityEngine;
using Alesa.Agents.BlindMinion;

/// <summary>
/// A minion's body: how much shooting it takes to put down, which death it plays, and what it does
/// when a bullet goes past it rather than into it.
/// </summary>
/// <remarks>
/// Lives on the minion root, so <see cref="RevolverCombat"/>'s
/// <c>GetComponentsInParent&lt;MonoBehaviour&gt;</c> walk finds it for any body shot.
/// Headshots arrive through <see cref="MinionHeadHitbox"/> on a separate collider instead, because
/// <see cref="IDamageable.TakeDamage"/> carries no hit position and cannot tell the two apart.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(BlindMinionAgent))]
public sealed class MinionCombatant : MonoBehaviour, IDamageable
{
    [Header("Toughness (shot counts, not damage numbers)")]
    [SerializeField, Min(1)] private int torsoShotsToKill = 3;
    [SerializeField, Min(1)] private int headShotsToKill = 2;

    [Header("Reaction to being shot at")]
    [Tooltip("How close a passing bullet has to come (m) for the minion to charge the shooter.")]
    [SerializeField, Min(0f)] private float nearMissRadius = 2.5f;

    [Tooltip("The minion only charges a shot it was turned towards. Half-angle in degrees from its own forward to the player.")]
    [SerializeField, Range(0f, 180f)] private float facingAngle = 70f;

    [Tooltip("Seconds of all-out charge before it drops back to its normal hunt.")]
    [SerializeField, Min(0.5f)] private float chargeSeconds = 6f;

    [Tooltip("Chance the charge is on all fours (Running_Crawl) rather than upright (Run).")]
    [SerializeField, Range(0f, 1f)] private float crawlChance = 0.5f;

    [Header("Corpse")]
    [Tooltip("Seconds before the body is removed. 0 leaves it in the level.")]
    [SerializeField, Min(0f)] private float corpseSeconds = 12f;

    [Header("Debug Status (Read-Only)")]
    [SerializeField] private int torsoHits;
    [SerializeField] private int headHits;
    [SerializeField] private bool dead;
    [SerializeField] private bool charging;

    private BlindMinionAgent _agent;
    private MinionVoice _voice;
    private Transform _player;
    private float _chargeEndsAt;
    private bool _onAllFours;
    private bool _runStarted;

    public bool IsDead => dead;
    public bool IsCharging => charging;

    /// <summary>True while charging on all fours, so the audio can swap to the crawling foley.</summary>
    public bool IsCrawlCharge => charging && _onAllFours;

    private void Awake()
    {
        _agent = GetComponent<BlindMinionAgent>();
        _voice = GetComponent<MinionVoice>();
    }

    private void OnEnable() => RevolverCombat.ShotFired += OnShotFired;

    private void OnDisable() => RevolverCombat.ShotFired -= OnShotFired;

    private void Update()
    {
        if (!charging || dead) return;
        if (_agent == null || _agent.Brain == null) return;

        // The run animation is held back until the scream has finished, or the Charging bool would
        // cut straight from the scream into a sprint and the howl would never be seen. The brain
        // roots the body for the same stretch, so it screams where it stands and only then moves.
        if (!_runStarted && !_agent.Brain.IsScreaming)
        {
            _runStarted = true;
            _agent.SetCharging(true, _onAllFours);
            // The clock starts here, not at the scream, so the hold never eats into the chase.
            _chargeEndsAt = Time.time + chargeSeconds;
        }

        if (_runStarted && Time.time >= _chargeEndsAt)
        {
            charging = false;
            _runStarted = false;
            _agent.SetCharging(false, false);
            return;
        }

        // Refresh the target every frame so the charge tracks a player who keeps moving, rather
        // than sprinting to where they stood when the shot was fired.
        _agent.Brain.HuntPlayer();
    }

    // ------------------------------------------------------------------ damage

    /// <summary>Body shot, from <see cref="RevolverCombat"/>. The damage value is ignored: the spec is shot counts.</summary>
    public void TakeDamage(float damage)
    {
        if (dead || damage <= 0f) return;
        torsoHits++;
        if (torsoHits >= torsoShotsToKill) { Die(false); return; }
        if (_voice != null) _voice.PlayBodyShot();
        BeginCharge();
    }

    /// <summary>Head shot, from <see cref="MinionHeadHitbox"/>.</summary>
    public void TakeHeadshot()
    {
        if (dead) return;
        headHits++;
        if (headHits >= headShotsToKill) { Die(true); return; }
        if (_voice != null) _voice.PlayHeadShot();
        BeginCharge();
    }

    private void Die(bool headshot)
    {
        dead = true;
        charging = false;

        // Before PlayDeath: it disables components, and the voice detaches its sources here so the
        // death cry and the body fall outlive the corpse timer.
        if (_voice != null) _voice.PlayDeath(headshot);
        if (_agent != null) _agent.PlayDeath(headshot);

        // Let shots pass through the corpse instead of stopping on it.
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;

        enabled = false;
        if (corpseSeconds > 0f) Destroy(gameObject, corpseSeconds);
    }

    // ------------------------------------------------------------------ being shot at

    /// <summary>
    /// A bullet that misses is still a message. If it cracked past close by and the minion was
    /// already turned towards the player, it stops guessing and charges.
    /// </summary>
    private void OnShotFired(RevolverCombat.ShotInfo shot)
    {
        if (dead || charging || _agent == null || _agent.Brain == null) return;

        // A shot that hit this minion is a hit, not a near miss.
        if (shot.Struck != null && shot.Struck.transform.IsChildOf(transform)) return;

        if (!ResolvePlayer()) return;

        Vector3 toMinion = transform.position - shot.Origin;
        float along = Vector3.Dot(toMinion, shot.Direction);
        if (along <= 0f) return; // fired away from it

        float perpendicular = Vector3.Distance(toMinion, shot.Direction * along);
        if (perpendicular > nearMissRadius) return;

        Vector3 toPlayer = _player.position - transform.position;
        toPlayer.y = 0f;
        Vector3 facing = transform.forward;
        facing.y = 0f;
        if (toPlayer.sqrMagnitude < 1e-4f || facing.sqrMagnitude < 1e-4f) return;
        if (Vector3.Angle(facing, toPlayer) > facingAngle) return;

        BeginCharge();
    }

    /// <summary>
    /// Screams where it stands, then runs the player down. Raised by a bullet that landed and by
    /// one that cracked past, because from the minion's side those are the same message.
    /// </summary>
    /// <remarks>
    /// Note what is <em>not</em> set here: <see cref="BlindMinionAgent.SetCharging"/>. The Charging
    /// bool starts the run animation, and starting it now would cut the scream off at the first
    /// frame. <see cref="Update"/> raises it once the brain reports the scream is over.
    /// </remarks>
    private void BeginCharge()
    {
        if (dead || charging || _agent == null || _agent.Brain == null) return;

        charging = true;
        _runStarted = false;
        _onAllFours = Random.value < crawlChance;
        _chargeEndsAt = float.MaxValue; // set properly once the scream ends and the run starts

        // Before HuntPlayer: entering Hunting screams, and PlayCharge silences that scream so the
        // two vocals do not land together.
        if (_voice != null) _voice.PlayCharge();
        _agent.Brain.HuntPlayer();
    }

    private bool ResolvePlayer()
    {
        if (_player != null) return true;
        var go = GameObject.FindWithTag("Player");
        if (go != null) _player = go.transform;
        return _player != null;
    }
}
