using UnityEngine;
using Alesa.Core.AI;        // NoiseBus, NoiseEvent, NoiseType
using Alesa.Runtime;        // ToCore() extension: UnityEngine.Vector3 -> System.Numerics.Vector3
using Alesa.Runtime.World;  // AIBootstrap, the scene's shared AI services

/// <summary>
/// Hive mind step 1. The game has two separate sound channels that never met:
/// gameplay shouts on <see cref="AcousticBus"/> (footsteps, sprints, gunshots, kicked props)
/// while every AI brain listens on <see cref="NoiseBus"/>. Without this component the
/// infected are deaf to the player. This forwards one bus to the other, unchanged.
///
/// It lives in Assembly-CSharp on purpose: assembly definitions such as Alesa.Core cannot
/// reference Assembly-CSharp, but Assembly-CSharp references all of them, so the only place
/// that can see both buses is this side.
/// </summary>
[DisallowMultipleComponent]
public class AcousticNoiseBridge : MonoBehaviour
{
    /// <summary>
    /// Source id stamped on every forwarded noise. The AI uses 0 for "the player or the world",
    /// which is what lets a minion ignore its own screech instead of chasing itself.
    /// </summary>
    public const int PlayerSourceId = 0;

    [Header("Debug")]
    [Tooltip("Log every forwarded noise to the Console. Handy while play-testing; turn off for builds.")]
    public bool logForwardedNoise;

    [Header("Status (read-only)")]
    [SerializeField] private int forwardedCount;
    [SerializeField] private string lastNoise = "nothing yet";

    /// <summary>
    /// Bus to forward into. Normally left empty, so the bridge uses the scene's AIBootstrap.
    /// Tests set this to a throwaway bus so they can run without a bootstrap in the scene.
    /// It is a plain C# class, so it is deliberately a property rather than a serialised field.
    /// </summary>
    public NoiseBus TargetBusOverride { get; set; }

    /// <summary>How many noises have been forwarded since this component was enabled.</summary>
    public int ForwardedCount => forwardedCount;
    /// <summary>Loudness of the most recently forwarded noise, in metres of open-air reach.</summary>
    public float LastLoudness { get; private set; }
    /// <summary>Category of the most recently forwarded noise.</summary>
    public NoiseType LastType { get; private set; }

    private AIBootstrap cachedBootstrap;

    // Subscribe only while enabled, so disabling the bridge genuinely deafens the hive.
    private void OnEnable()
    {
        forwardedCount = 0;
        lastNoise = "nothing yet";
        AcousticBus.OnSoundEmitted += Forward;
    }

    private void OnDisable()
    {
        AcousticBus.OnSoundEmitted -= Forward;
    }

    /// <summary>
    /// Translates one gameplay sound into one AI noise.
    /// Both systems already measure loudness the same way — "metres this carries in open air" —
    /// so the emitter's radius passes straight through as loudness with no rescaling.
    /// </summary>
    private void Forward(Vector3 origin, float radius, SoundType type)
    {
        // A zero or broken radius means "nothing audible happened". Emitters use radius 0 to stay
        // silent (crouch-walking, a dry trigger click), so dropping these keeps the hive quiet.
        if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius)) return;

        NoiseBus bus = ResolveBus();
        // No AI in this scene yet. Staying silent rather than throwing keeps the bridge safe to
        // leave in SampleScene before any minions or AIBootstrap exist.
        if (bus == null) return;

        NoiseType mapped = MapType(type);

        // NoiseEvent is a readonly struct and Emit walks a delegate list, so a footstep costs no
        // managed allocation. That matters because footsteps fire several times a second forever.
        bus.Emit(new NoiseEvent(origin.ToCore(), radius, mapped, Time.time, PlayerSourceId));

        forwardedCount++;
        LastLoudness = radius;
        LastType = mapped;
        lastNoise = $"{mapped} {radius:0.#} m at {origin}";
        if (logForwardedNoise) Debug.Log($"<color=#7fd4ff>[Noise bridge]</color> {lastNoise}", this);
    }

    /// <summary>
    /// Maps a gameplay sound category onto the AI's vocabulary.
    /// The two enums were written by different people and do not line up one to one:
    /// AcousticBus has four physical categories, NoiseBus has seven, including Scream, which only
    /// minions produce and so never appears here.
    /// </summary>
    public static NoiseType MapType(SoundType type)
    {
        switch (type)
        {
            case SoundType.Footstep: return NoiseType.Footstep;
            case SoundType.Sprint: return NoiseType.Sprint;
            case SoundType.Gunshot: return NoiseType.Gunshot;
            // Carts, trays and swinging doors all arrive as PropCollision. Trolley is the closest
            // AI category; if doors ever need to read differently, AcousticBus needs its own
            // SoundType for them first, because the radius alone cannot tell them apart.
            case SoundType.PropCollision: return NoiseType.Trolley;
            default: return NoiseType.Footstep;
        }
    }

    /// <summary>
    /// Finds the bus to emit into. The override wins so tests stay independent of the scene.
    /// Otherwise the scene's AIBootstrap owns it; that reference is cached but re-fetched whenever
    /// it dies, which happens on every domain reload and scene load.
    /// </summary>
    private NoiseBus ResolveBus()
    {
        if (TargetBusOverride != null) return TargetBusOverride;

        // Unity's fake-null means a destroyed bootstrap compares equal to null, so this correctly
        // refetches after a scene reload rather than handing back a dead reference.
        if (cachedBootstrap == null)
            cachedBootstrap = AIBootstrap.Instance != null
                ? AIBootstrap.Instance
                : FindFirstObjectByType<AIBootstrap>();

        return cachedBootstrap != null ? cachedBootstrap.Noise : null;
    }
}
