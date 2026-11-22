using UnityEngine;

/// <summary>HUD for hive mind sound events. Shows gameplay hearing radius, not audio decibels.</summary>
[DisallowMultipleComponent]
public class AcousticSoundMeter : MonoBehaviour
{
    [Tooltip("Hearing radius that fills the meter. Events above this still show their actual radius.")]
    [Min(1f)] public float fullScaleRadius = 40f;
    [Tooltip("Seconds to hold a sound before the visual meter fades.")]
    [Min(0f)] public float holdDuration = 0.35f;
    [Tooltip("Seconds for a full meter to fade back to silence.")]
    [Min(0.1f)] public float fadeDuration = 1f;

    private PlayerGameplay gameplay;
    private float displayedRadius, peakRadius, holdRemaining;
    private SoundType displayedType;
    private bool labelDirty;
    private string soundLabel = "Quiet";

    private void Awake() { gameplay = GetComponent<PlayerGameplay>(); }

    private void OnEnable()
    {
        displayedRadius = peakRadius = holdRemaining = 0f;
        soundLabel = "Quiet";
        labelDirty = false;
        AcousticBus.OnSoundEmitted += HandleSound;
    }

    private void OnDisable() { AcousticBus.OnSoundEmitted -= HandleSound; }

    private void HandleSound(Vector3 origin, float radius, SoundType type)
    {
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) return;
        // Preserve the stronger event when a footstep immediately follows a gunshot.
        if (radius < displayedRadius) return;
        displayedRadius = peakRadius = radius;
        displayedType = type;
        holdRemaining = holdDuration;
        labelDirty = true;
    }

    private void Update()
    {
        if (gameplay != null && gameplay.BlocksControls) return;
        if (holdRemaining > 0f) holdRemaining -= Time.deltaTime;
        else displayedRadius = Mathf.MoveTowards(displayedRadius, 0f,
            fullScaleRadius / Mathf.Max(0.1f, fadeDuration) * Time.deltaTime);

        if (labelDirty)
        {
            string category;
            switch (displayedType)
            {
                case SoundType.Footstep: category = "Walking"; break;
                case SoundType.Sprint: category = "Sprinting"; break;
                case SoundType.Gunshot: category = "Gunshot"; break;
                case SoundType.PropCollision: category = "Prop impact"; break;
                default: category = "Sound"; break;
            }
            soundLabel = $"{category} - {peakRadius:0.#} m hearing radius";
            labelDirty = false;
        }
        if (displayedRadius <= 0f) soundLabel = "Quiet";
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint ||
            (gameplay != null && gameplay.BlocksControls)) return;

        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
            new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f));
        GUI.color = Color.white;
        GUI.Box(new Rect(942f, 18f, 320f, 94f), GUIContent.none);
        GUI.Label(new Rect(956f, 25f, 292f, 24f), "NOISE ACTIVITY");
        GUI.Label(new Rect(956f, 77f, 292f, 24f), soundLabel);

        float level = Mathf.Clamp01(displayedRadius / Mathf.Max(1f, fullScaleRadius));
        GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        GUI.DrawTexture(new Rect(956f, 55f, 292f, 14f), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(0.35f, 0.85f, 0.55f), new Color(1f, 0.25f, 0.15f), level);
        GUI.DrawTexture(new Rect(956f, 55f, 292f * level, 14f), Texture2D.whiteTexture);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }
}
