using System.Collections;
using UnityEngine;
using Alesa.Agents.BlindMinion;
using Alesa.Runtime.Motion;

/// <summary>
/// Every noise a minion makes. Clips are assigned per variant, so the two zombies can sound
/// different while sharing one behaviour.
/// </summary>
/// <remarks>
/// Two AudioSources, deliberately. Vocalisations and foley overlap constantly — a bite snarl runs
/// straight into the bite itself, a body falls while the thing is still dying — and one source
/// would cut each off with the next. Footsteps are the worst offender: on a single source every
/// step would clip the moan it is walking in on.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(BlindMinionAgent))]
public sealed class MinionVoice : MonoBehaviour
{
    [Header("Sources (created at runtime when left empty)")]
    [Tooltip("Vocalisations: moans, snarls, death cries.")]
    [SerializeField] private AudioSource voice;
    [Tooltip("Physical sounds: footsteps, flesh impacts, the body hitting the floor.")]
    [SerializeField] private AudioSource foley;

    [Header("Vocal")]
    [Tooltip("Heard a gunshot, or broke into a sprint.")]
    [SerializeField] private AudioClip alert;
    [SerializeField] private AudioClip attack;
    [Tooltip("Snarl that leads into the bite.")]
    [SerializeField] private AudioClip neckBiteVoice;
    [Tooltip("Killed by body shots.")]
    [SerializeField] private AudioClip death;
    [Tooltip("Killed by headshots.")]
    [SerializeField] private AudioClip dying;
    [Tooltip("Breathing/growling while it is alive and nothing has killed it. Played on a loose interval, not looped.")]
    [SerializeField] private AudioClip idleBreath;
    [Tooltip("The minion's own reaction to taking a non-fatal hit, over the top of the impact foley.")]
    [SerializeField] private AudioClip painGrunt;
    [Tooltip("Breaking into an all-out charge after being shot at.")]
    [SerializeField] private AudioClip chargeVocal;
    [Tooltip("Cuts the charge vocal to this many seconds, fading the last moment so it does not click. 0 plays the whole clip.")]
    [SerializeField, Min(0f)] private float chargeVocalMaxSeconds;
    [Tooltip("Lost the trail and is casting about.")]
    [SerializeField] private AudioClip searchVocal;

    [Header("Foley")]
    [SerializeField] private AudioClip neckBiteImpact;
    [Tooltip("Played when a body shot lands and does not kill.")]
    [SerializeField] private AudioClip bodyShotImpact;
    [Tooltip("Played when a head shot lands and does not kill.")]
    [SerializeField] private AudioClip headShotImpact;
    [Tooltip("A swing that the player backed out of.")]
    [SerializeField] private AudioClip attackWhiff;
    [Tooltip("Gore on a headshot kill. Plays first.")]
    [SerializeField] private AudioClip headshotKill;
    [Tooltip("Optional second gore layer, started when the first finishes. Leave empty for a single hit.")]
    [SerializeField] private AudioClip headshotKillTail;
    [SerializeField] private AudioClip bodyFallFar;
    [SerializeField] private AudioClip bodyFallNear;
    [SerializeField] private AudioClip footstepLeft;
    [SerializeField] private AudioClip footstepRight;
    [Tooltip("Heavier steps used above runStepSpeed.")]
    [SerializeField] private AudioClip footstepRunLeft;
    [SerializeField] private AudioClip footstepRunRight;
    [Tooltip("Used instead of footsteps while charging on all fours.")]
    [SerializeField] private AudioClip footstepCrawl;

    [Header("Timing")]
    [Tooltip("Gap between the bite snarl and the bite landing. 0 uses the snarl's own length.")]
    [SerializeField, Min(0f)] private float biteLeadSeconds;

    [Tooltip("Surface gap (m) below which a death counts as happening on top of the player.")]
    [SerializeField, Min(0f)] private float nearDeathGap = 0.3f;

    [Header("Footsteps")]
    [Tooltip("Metres of travel per step. Scaled down automatically while sprinting.")]
    [SerializeField, Min(0.1f)] private float strideLength = 0.85f;

    [Tooltip("Speed (m/s) below which it is standing still and takes no steps.")]
    [SerializeField, Min(0f)] private float minStepSpeed = 0.2f;

    [Tooltip("Speed (m/s) at or above which the heavier run steps are used instead of the walk pair.")]
    [SerializeField, Min(0f)] private float runStepSpeed = 3.5f;

    [Tooltip("Random pitch spread on footsteps, so a long corridor does not sound like a metronome.")]
    [SerializeField, Range(0f, 0.3f)] private float footstepPitchJitter = 0.08f;

    [Header("Idle breathing")]
    [Tooltip("Shortest and longest gap (s) between idle breaths. Randomised so two minions in one room do not breathe in unison.")]
    [SerializeField, Min(0.5f)] private float idleBreathMinGap = 3.5f;
    [SerializeField, Min(0.5f)] private float idleBreathMaxGap = 9f;

    [Tooltip("Gaps are multiplied by this while it is hunting or attacking, so it breathes harder when it is on you.")]
    [SerializeField, Range(0.1f, 1f)] private float idleBreathHuntingScale = 0.45f;

    private BlindMinionAgent _agent;
    private AgentMotor _motor;
    private Transform _player;
    private Vector3 _lastStepPosition;
    private float _strideAccumulated;
    private bool _leftFoot;
    private bool _dead;
    private float _nextBreathAt;
    private bool _subscribedToBrain;
    private MinionCombatant _combatant;
    private float _suppressAlertUntil;

    private void Awake()
    {
        _agent = GetComponent<BlindMinionAgent>();
        _motor = GetComponent<AgentMotor>();
        voice = EnsureSource(voice, "Voice", 1f);
        foley = EnsureSource(foley, "Foley", 0.9f);
        _lastStepPosition = transform.position;
    }

    private AudioSource EnsureSource(AudioSource existing, string label, float volume)
    {
        if (existing != null) return existing;
        var go = new GameObject("Audio_" + label);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;      // fully 3D: a blind hunter you cannot place is no threat
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = 25f;
        src.volume = volume;
        return src;
    }

    private void OnEnable()
    {
        if (_agent == null) return;
        _agent.Screamed += OnAlert;
        _agent.SwingStarted += OnSwingStarted;
        _agent.Lunged += OnLunged;
        _agent.Struck += OnStruck;
        _agent.Whiffed += OnWhiffed;
    }

    private void OnDisable()
    {
        if (_agent == null) return;
        _agent.Screamed -= OnAlert;
        _agent.SwingStarted -= OnSwingStarted;
        _agent.Lunged -= OnLunged;
        _agent.Struck -= OnStruck;
        _agent.Whiffed -= OnWhiffed;
        if (_agent.Brain != null) _agent.Brain.StateChanged -= OnStateChanged;
        _subscribedToBrain = false;
    }

    /// <summary>
    /// The brain is built in the agent's Awake, and component Awake/OnEnable order within one
    /// GameObject is not something to rely on, so the state subscription is made lazily.
    /// </summary>
    private void EnsureBrainSubscription()
    {
        if (_subscribedToBrain || _agent == null || _agent.Brain == null) return;
        _agent.Brain.StateChanged += OnStateChanged;
        _subscribedToBrain = true;
    }

    private void OnStateChanged(MinionState state)
    {
        if (state == MinionState.Searching) Play(voice, searchVocal);
    }

    // ------------------------------------------------------------------ footsteps

    private void Update()
    {
        // The motor is disabled on death but its solver keeps its last speed, so without this a
        // corpse goes on taking footsteps.
        if (_dead) return;

        EnsureBrainSubscription();
        TickIdleBreath();

        Vector3 travelled = transform.position - _lastStepPosition;
        travelled.y = 0f;
        _lastStepPosition = transform.position;

        float speed = _motor != null ? _motor.CurrentSpeed : travelled.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
        if (speed < minStepSpeed) { _strideAccumulated = 0f; return; }

        _strideAccumulated += travelled.magnitude;

        // A sprint covers ground faster per step, not just more often, so the stride shortens
        // with speed — otherwise a running zombie sounds like it is gliding between footfalls.
        float stride = strideLength * Mathf.Clamp(1f - (speed - 1.8f) * 0.06f, 0.55f, 1f);
        if (_strideAccumulated < stride) return;

        _strideAccumulated = 0f;
        PlayFootstep(_leftFoot);
        _leftFoot = !_leftFoot;
    }

    /// <summary>
    /// Breathing on a loose, randomised interval rather than a looping source. A loop would sit
    /// under everything else at a constant level and make two minions in one room phase against
    /// each other; irregular breaths are what let the player place a thing they cannot see.
    /// </summary>
    private void TickIdleBreath()
    {
        if (idleBreath == null) return;
        if (Time.time < _nextBreathAt)
        {
            return;
        }

        bool firstCall = _nextBreathAt <= 0f;
        ScheduleNextBreath();
        if (firstCall) return; // stagger the first one instead of every minion breathing on spawn

        Play(voice, idleBreath);
    }

    private void ScheduleNextBreath()
    {
        float scale = IsWorkedUp ? idleBreathHuntingScale : 1f;
        float min = Mathf.Min(idleBreathMinGap, idleBreathMaxGap) * scale;
        float max = Mathf.Max(idleBreathMinGap, idleBreathMaxGap) * scale;
        _nextBreathAt = Time.time + Random.Range(min, max);
    }

    private bool IsWorkedUp
    {
        get
        {
            if (_agent == null || _agent.Brain == null) return false;
            var state = _agent.Brain.State;
            return state == MinionState.Hunting || state == MinionState.Attacking;
        }
    }

    private void PlayFootstep(bool left)
    {
        if (_combatant == null) _combatant = GetComponent<MinionCombatant>();
        bool crawling = _combatant != null && _combatant.IsCrawlCharge;
        float speed = _motor != null ? _motor.CurrentSpeed : 0f;

        AudioClip clip;
        if (crawling && footstepCrawl != null) clip = footstepCrawl;
        else if (speed >= runStepSpeed && footstepRunLeft != null)
            clip = left ? footstepRunLeft : footstepRunRight;
        else clip = left ? footstepLeft : footstepRight;

        if (clip == null || foley == null) return;
        foley.pitch = 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter);
        foley.PlayOneShot(clip);
        foley.pitch = 1f;
    }

    // ------------------------------------------------------------------ events

    /// <summary>
    /// Hunt scream. Suppressed just after a charge: starting a charge also makes the brain enter
    /// Hunting, which screams, so without this the charge vocal and the alert moan land on the
    /// same frame and fight each other.
    /// </summary>
    private void OnAlert()
    {
        if (Time.time < _suppressAlertUntil) return;
        Play(voice, alert);
    }

    /// <summary>A hunter springing at the player: the attack snarl. The swing that follows has its own.</summary>
    private void OnLunged() => Play(voice, attack);

    /// <summary>The swing has begun. Whether it lands is not known yet.</summary>
    private void OnSwingStarted(bool neckBite) => Play(voice, neckBite ? neckBiteVoice : attack);

    /// <summary>The swing connected. The bite's own impact belongs here, not at the snarl.</summary>
    private void OnStruck(bool neckBite)
    {
        if (!neckBite || neckBiteImpact == null) return;
        if (biteLeadSeconds > 0f) StartCoroutine(PlayAfter(biteLeadSeconds, neckBiteImpact));
        else Play(foley, neckBiteImpact);
    }

    /// <summary>The player backed out of reach before the blow landed.</summary>
    private void OnWhiffed() => Play(foley, attackWhiff);

    /// <summary>
    /// Breaking into a charge. The clip can be cut short: one of the supplied charge sounds is a
    /// long recording whose useful part is only the opening, so this plays on the source directly
    /// rather than as a one-shot, which is the only way to stop it again.
    /// </summary>
    public void PlayCharge()
    {
        _suppressAlertUntil = Time.time + 0.5f;
        if (chargeVocal == null || voice == null) return;
        if (chargeVocalMaxSeconds <= 0f || chargeVocalMaxSeconds >= chargeVocal.length)
        {
            Play(voice, chargeVocal);
            return;
        }

        voice.clip = chargeVocal;
        voice.time = 0f;
        voice.Play();
        StartCoroutine(FadeOutCharge(chargeVocalMaxSeconds));
    }

    private IEnumerator FadeOutCharge(float seconds)
    {
        const float fade = 0.2f;
        yield return new WaitForSeconds(Mathf.Max(0f, seconds - fade));
        float startVolume = voice.volume;
        for (float t = 0f; t < fade; t += Time.deltaTime)
        {
            if (voice == null) yield break;
            voice.volume = Mathf.Lerp(startVolume, 0f, t / fade);
            yield return null;
        }
        if (voice == null) yield break;
        voice.Stop();
        voice.clip = null;
        voice.volume = startVolume;
    }

    private IEnumerator PlayAfter(float seconds, AudioClip clip)
    {
        yield return new WaitForSeconds(seconds);
        Play(foley, clip);
    }

    /// <summary>A body shot that did not kill: the impact, and the minion's reaction to it.</summary>
    public void PlayBodyShot()
    {
        Play(foley, bodyShotImpact);
        Play(voice, painGrunt);
    }

    /// <summary>A head shot that did not kill.</summary>
    public void PlayHeadShot()
    {
        Play(foley, headShotImpact);
        Play(voice, painGrunt);
    }

    /// <summary>
    /// The kill. Called before the colliders are turned off, so the sources are still alive to
    /// start their clips — they finish playing on a detached object if the corpse is removed.
    /// </summary>
    public void PlayDeath(bool headshot)
    {
        if (_dead) return;
        _dead = true;
        Play(voice, headshot ? dying : death);
        if (headshot)
        {
            Play(foley, headshotKill);
            // A second gore layer, started when the first finishes rather than on top of it.
            if (headshotKillTail != null)
            {
                float after = headshotKill != null ? headshotKill.length : 0f;
                StartCoroutine(PlayAfter(after, headshotKillTail));
            }
        }

        bool onTopOfPlayer = DistanceGapToPlayer() <= nearDeathGap;
        AudioClip fall = onTopOfPlayer ? bodyFallNear : bodyFallFar;
        if (fall == null) return;

        // The body hits the floor after the cry starts, not with it.
        AudioClip cry = headshot ? dying : death;
        float delay = cry != null ? Mathf.Min(cry.length * 0.5f, 0.6f) : 0.3f;
        Detach();
        StartCoroutine(PlayAfter(delay, fall));
    }

    /// <summary>Surface-to-surface gap to the player, matching how the contact ranges are measured.</summary>
    private float DistanceGapToPlayer()
    {
        if (_player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go == null) return float.MaxValue;
            _player = go.transform;
        }

        float bodyRadius = 0.35f;
        var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (nav != null)
        {
            var s = transform.lossyScale;
            bodyRadius = nav.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        }

        float playerRadius = 0.325f;
        var cc = _player.GetComponent<CharacterController>();
        if (cc != null) playerRadius = cc.radius;

        Vector3 a = transform.position, b = _player.position;
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b) - (bodyRadius + playerRadius);
    }

    /// <summary>
    /// Lifts the audio sources off the minion so the death cry and the body fall survive the
    /// corpse being destroyed. Without this a short corpse timer cuts the death sound in half.
    /// </summary>
    private void Detach()
    {
        foreach (var src in new[] { voice, foley })
        {
            if (src == null) continue;
            src.transform.SetParent(null, true);
            var keeper = src.gameObject.AddComponent<AudioSourceSelfDestruct>();
            keeper.Begin(6f);
        }
    }

    private static void Play(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;
        source.PlayOneShot(clip);
    }
}
