using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Pause-only presentation. PlayerGameplay retains ownership of modal movement and cursor locks.</summary>
[DisallowMultipleComponent]
public sealed class AnalogPauseMenu : MonoBehaviour
{
    [SerializeField] private PlayerGameplay player;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private RawImage frozenFrame, scanlines;
    [SerializeField] private TMP_Text[] options;
    [SerializeField] private TMP_Text healthLabel, noiseLabel;
    [SerializeField] private Image noiseBar;
    [SerializeField] private RectTransform bracket;
    [SerializeField] private string titleScene = "";
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.15f;
    [Tooltip("Connect a notes viewer. The player remains paused while the event runs.")]
    [SerializeField] private UnityEvent onCollectedNotes = new UnityEvent();
    private AudioLowPassFilter lowPass;
    private bool oldFilterEnabled, ownsFilter, visible, capturing, loading;
    private float oldCutoff, navigationAt, noiseRadius;
    private int selection, openingFrame;
    private RenderTexture capture;
    private readonly WaitForEndOfFrame captureWait = new WaitForEndOfFrame();
    private Material frameMaterial;
    private readonly string[] labels = { "RESUME [SPACE]", "COLLECTED NOTES", "QUIT [ESC]" };
    public event System.Action CollectedNotesRequested;
    public bool IsPaused => player != null && player.Mode == PlayerGameplay.ScreenMode.Paused;

    public void Configure(PlayerGameplay target, CanvasGroup canvas, RawImage frame, RawImage lines,
        TMP_Text[] menu, TMP_Text health, TMP_Text noise, Image bar, RectTransform indicator)
    {
        player = target; group = canvas; frozenFrame = frame; scanlines = lines;
        options = menu; healthLabel = health; noiseLabel = noise; noiseBar = bar; bracket = indicator;
    }

    private void OnEnable()
    {
        if (player == null) player = GetComponent<PlayerGameplay>();
        if (player == null || group == null) { enabled = false; return; }
        player.PauseMenu = this;
        player.State.Changed += StateChanged;
        AcousticBus.OnSoundEmitted += Heard;
        var listener = player.GetComponentInChildren<AudioListener>();
        if (listener != null)
        {
            lowPass = listener.GetComponent<AudioLowPassFilter>();
            if (lowPass == null) { lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>(); ownsFilter = true; lowPass.enabled = false; }
        }
        var shader = Resources.Load<Shader>("AnalogPauseFrame");
        if (shader != null) { frameMaterial = new Material(shader); frozenFrame.material = frameMaterial; }
        group.alpha = 0; group.gameObject.SetActive(false);
        UpdateHealthDisplay(100, 100); UpdateNoiseLevel(0, "SILENT");
        RefreshSelection();
    }

    public void Pause()
    {
        if (loading || player == null || player.BlocksControls) return;
        player.OpenPauseScreen();
    }

    /// <summary>Escape opens pause during gameplay and quits when already paused.</summary>
    public void HandleEscape()
    {
        if (loading) return;
        if (IsPaused) { if (Time.frameCount != openingFrame) QuitToMenu(); }
        else Pause();
    }

    public void Resume()
    {
        if (!IsPaused || loading) return;
        player.CloseScreen();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    private void StateChanged()
    {
        if (IsPaused && !visible)
        {
            visible = true; selection = 0; openingFrame = Time.frameCount;
            RefreshSelection();
            UpdateNoiseLevel(noiseRadius * 2.5f, noiseRadius >= 10 ? "HEARD" : noiseRadius >= 2 ? "SUSPICIOUS" : "SILENT");
            if (lowPass != null) { oldFilterEnabled = lowPass.enabled; oldCutoff = lowPass.cutoffFrequency; lowPass.cutoffFrequency = 900; lowPass.enabled = true; }
            StartCoroutine(CaptureFrame());
        }
        else if (!IsPaused && visible)
        {
            visible = false; RestoreAudio();
            // Death must cover the pause screen immediately, without changing death's time/cursor ownership.
            if (player.State.IsDead) { group.alpha = 0; group.gameObject.SetActive(false); }
        }
    }

    private IEnumerator CaptureFrame()
    {
        capturing = true;
        group.gameObject.SetActive(false);
        yield return captureWait;
        if (visible)
        {
            // Keep the screenshot on the GPU; avoid a synchronous CPU readback on every pause.
            if (capture == null || capture.width != Screen.width || capture.height != Screen.height)
            {
                ReleaseCapture();
                capture = new RenderTexture(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height), 0, RenderTextureFormat.ARGB32)
                { name = "Pause frozen frame", useMipMap = false, autoGenerateMips = false };
                capture.Create();
            }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(capture);
            frozenFrame.texture = capture;
            group.alpha = 0;
            group.gameObject.SetActive(true);
        }
        capturing = false;
    }

    private void Update()
    {
        var pad = Gamepad.current;
        if (pad != null && pad.startButton.wasPressedThisFrame)
        {
            if (IsPaused) Resume(); else Pause();
        }
        if (group == null) return;
        group.alpha = Mathf.MoveTowards(group.alpha, visible && !capturing ? 1 : 0, Time.unscaledDeltaTime / fadeDuration);
        group.interactable = visible && !loading;
        group.blocksRaycasts = visible;
        if (!visible && group.alpha <= 0 && group.gameObject.activeSelf)
        {
            group.gameObject.SetActive(false);
            // Retain one GPU buffer for the next pause; resize only when resolution changes.
        }
        if (!visible) { noiseRadius = Mathf.MoveTowards(noiseRadius, 0, Time.deltaTime * 20); return; }
        if (scanlines != null)
        {
            scanlines.uvRect = new Rect(0, Time.unscaledTime * 0.07f, 1, 1);
            scanlines.color = new Color(1, 1, 1, 0.04f + 0.025f * Mathf.Sin(Time.unscaledTime * 23));
        }
        if (bracket != null) bracket.anchoredPosition = Vector2.Lerp(bracket.anchoredPosition,
            new Vector2(-15, -selection * 70), 1 - Mathf.Exp(-18 * Time.unscaledDeltaTime));
        if (loading || Time.frameCount == openingFrame) return;
        var key = Keyboard.current;
        if (key != null && key.spaceKey.wasPressedThisFrame) { Resume(); return; }
        if (pad != null && pad.buttonSouth.wasPressedThisFrame) { Resume(); return; }
        int step = 0;
        if (key != null)
        {
            if (key.wKey.wasPressedThisFrame || key.upArrowKey.wasPressedThisFrame) step = -1;
            if (key.sKey.wasPressedThisFrame || key.downArrowKey.wasPressedThisFrame) step = 1;
        }
        if (pad != null && Time.unscaledTime >= navigationAt && Mathf.Abs(pad.dpad.y.ReadValue()) > 0.5f)
        { step = pad.dpad.y.ReadValue() > 0 ? -1 : 1; navigationAt = Time.unscaledTime + 0.2f; }
        if (step != 0) { selection = (selection + step + 3) % 3; RefreshSelection(); }
        if (key != null && key.enterKey.wasPressedThisFrame) Activate(selection);
    }

    public void Activate(int index)
    {
        if (!IsPaused || loading) return;
        if (index == 0) Resume();
        else if (index == 1) { onCollectedNotes.Invoke(); CollectedNotesRequested?.Invoke(); }
        else if (index == 2) QuitToMenu();
    }

    private void RefreshSelection()
    {
        if (options == null) return;
        for (int i = 0; i < options.Length && i < labels.Length; i++)
        {
            if (options[i] == null) continue;
            options[i].text = labels[i];
            options[i].color = i == selection ? Color.white : new Color(0.4f, 0.4f, 0.4f);
        }
    }

    public void UpdateHealthDisplay(float currentHealth, float maxHealth)
    {
        if (healthLabel == null || float.IsNaN(currentHealth) || float.IsInfinity(currentHealth) || maxHealth <= 0 || float.IsNaN(maxHealth) || float.IsInfinity(maxHealth)) return;
        float fraction = Mathf.Clamp01(currentHealth / maxHealth);
        healthLabel.text = fraction > 0.6f ? "STATUS: FINE" : fraction > 0.25f ? "STATUS: CAUTION" : "STATUS: DANGER";
        healthLabel.color = fraction > 0.6f ? new Color(0.3f, 0.9f, 0.6f) : fraction > 0.25f ? new Color(1, 0.65f, 0.2f) : new Color(1, 0.2f, 0.2f);
    }

    /// <summary>Supplied dB are presentation values; hearing radius is not a physical dB measurement.</summary>
    public void UpdateNoiseLevel(float currentDecibels, string alertTag)
    {
        if (float.IsNaN(currentDecibels) || float.IsInfinity(currentDecibels)) return;
        float value = Mathf.Max(0, currentDecibels);
        if (noiseLabel != null) noiseLabel.text = $"NOISE LEVEL: {value:0} dB [{alertTag}]";
        if (noiseBar != null) noiseBar.fillAmount = Mathf.Clamp01(value / 100);
    }
    private void Heard(Vector3 position, float radius, SoundType type)
    {
        if (player == null || player.BlocksControls) return;
        noiseRadius = Mathf.Max(noiseRadius, radius);
    }

    public void QuitToMenu()
    {
        if (!IsPaused || loading) return;
        if (!string.IsNullOrWhiteSpace(titleScene))
        {
            if (!Application.CanStreamedLevelBeLoaded(titleScene)) { Debug.LogError("Add the title scene to Build Profiles before quitting to it.", this); return; }
            StartCoroutine(LoadTitle()); return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    private IEnumerator LoadTitle()
    {
        loading = true;
        var operation = SceneManager.LoadSceneAsync(titleScene);
        if (operation == null) { loading = false; yield break; }
        ResumeForTransition();
        while (!operation.isDone) yield return null;
    }
    private void ResumeForTransition() { player.CloseScreen(); Time.timeScale = 1; RestoreAudio(); }
    private void RestoreAudio() { if (lowPass != null) { lowPass.cutoffFrequency = oldCutoff; lowPass.enabled = oldFilterEnabled; } }
    private void ReleaseCapture()
    {
        if (frozenFrame != null) frozenFrame.texture = null;
        if (capture == null) return;
        capture.Release(); Destroy(capture); capture = null;
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (player != null)
        {
            player.State.Changed -= StateChanged;
            if (IsPaused) player.CloseScreen();
            if (player.PauseMenu == this) player.PauseMenu = null;
        }
        AcousticBus.OnSoundEmitted -= Heard;
        if (visible) RestoreAudio();
        if (group != null) group.gameObject.SetActive(false);
        ReleaseCapture();
        if (frameMaterial != null) Destroy(frameMaterial);
        if (ownsFilter && lowPass != null) Destroy(lowPass);
    }
}
