using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Owns the death overlay, frozen gameplay, menu input, and scene transitions.</summary>
/// <remarks>Keep this component on an active object outside the overlay Canvas.</remarks>
[DisallowMultipleComponent]
public class DeathScreenManager : MonoBehaviour
{
    /// <summary>References supplied by the scene UI builder before this manager awakens.</summary>
    public sealed class OverlayReferences
    {
        public GameObject canvas;
        public CanvasGroup group;
        public Button retryButton, quitButton;
        public TMP_Text retryLabel, quitLabel;
    }

    /// <summary>Wires a generated view while this manager's GameObject is inactive.</summary>
    public void ConfigureOverlay(OverlayReferences view, PlayerGameplay target, Behaviour[] otherScripts)
    {
        if (gameObject.activeInHierarchy)
            throw new InvalidOperationException("Configure the death manager before activating its GameObject.");
        overlayCanvas = view.canvas;
        canvasGroup = view.group;
        retryButton = view.retryButton; quitButton = view.quitButton;
        retryLabel = view.retryLabel; quitLabel = view.quitLabel;
        player = target;
        additionalScriptsToDisable = otherScripts ?? Array.Empty<Behaviour>();
    }

    /// <summary>Uses the player death entry point for the video menu.</summary>
    public void ConfigureSequence(PlayerDeathSequence sequence)
    {
        deathSequence = sequence;
        enableDebugKey = false;
        retryText = "RETRY SIMULATION (space)";
        quitText = "ACCEPT DEATH (esc)";
        unselectedColor = new Color(1f / 3f, 1f / 3f, 1f / 3f);
        useMonospacedLabels = true;
    }

    private DeathVideoPresentation videoPresentation;
    public void ConfigureVideo(DeathVideoPresentation presentation) { videoPresentation = presentation; }

    [Header("Overlay")]
    [SerializeField] private GameObject overlayCanvas = null;
    [SerializeField] private CanvasGroup canvasGroup;
    private bool menuReady;
    [SerializeField, Min(0f)] private float fadeDuration = 0.8f;

    [Header("Actions")]
    [SerializeField] private Button retryButton = null;
    [SerializeField] private Button quitButton = null;
    [SerializeField] private TMP_Text retryLabel = null, quitLabel = null;
    [SerializeField] private string retryText = "RETRY SIMULATION (space)";
    [SerializeField] private string quitText = "ACCEPT DEATH (esc)";
    [SerializeField] private Color selectedColor = Color.white;
    [SerializeField] private Color unselectedColor = new Color(0.6f, 0.6f, 0.6f);
    [Tooltip("Enabled scene name or full scene path. Leave empty to quit the application.")]
    [SerializeField] private string mainMenuScene = "";

    [Header("Gameplay")]
    [SerializeField] private PlayerGameplay player;
    [Tooltip("Additional look, AI, HUD, or other scripts to disable during death. Do not include this manager or its parents.")]
    [SerializeField] private Behaviour[] additionalScriptsToDisable = Array.Empty<Behaviour>();
    [SerializeField] private bool pauseWorldAudio = true;


    [Header("Development testing")]
    [SerializeField] private bool enableDebugKey = true;
    [SerializeField] private Key debugKey = Key.K;

    /// <summary>True while the overlay owns gameplay.</summary>
    public bool IsVisible { get; private set; }
    public bool IsOverlayShowing => overlayShown;
    private bool overlayShown;
    private PlayerDeathSequence deathSequence;
    private string selectedRetry, unselectedRetry, selectedQuit, unselectedQuit;
    private bool useMonospacedLabels;
    private readonly List<Behaviour> disabledScripts = new List<Behaviour>();
    private readonly List<bool> previousEnabled = new List<bool>();
    private bool transitioning, previousListenerPause, previousCursorVisible, ownsGlobals;
    private float previousTimeScale, fadeElapsed;
    private CursorLockMode previousCursorLock;
    private int selection, openingFrame;
    private float nextNavigationTime;
    private EventSystem menuEventSystem;
    private bool previousNavigationEvents;
    private AsyncOperation pendingLoad;

    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (overlayCanvas != null && transform.IsChildOf(overlayCanvas.transform))
        {
            Debug.LogError("DeathScreenManager must be outside the overlay Canvas hierarchy.", this);
            enabled = false;
            return;
        }
        if (canvasGroup == null && overlayCanvas != null)
            canvasGroup = overlayCanvas.GetComponent<CanvasGroup>();
        // Cache menu strings once; navigation in Update creates no new strings.
        selectedRetry = Monospace("> " + retryText); unselectedRetry = Monospace("  " + retryText);
        selectedQuit = Monospace("> " + quitText); unselectedQuit = Monospace("  " + quitText);
        UpdateSelection();
        if (overlayCanvas != null) overlayCanvas.SetActive(false);
        if (retryButton != null) retryButton.onClick.AddListener(RetrySimulation);
        if (quitButton != null) quitButton.onClick.AddListener(AcceptDeath);
        // This manager owns keyboard/controller navigation; buttons retain mouse support.
        SetManualNavigation(retryButton);
        SetManualNavigation(quitButton);
    }

    private static void SetText(TMP_Text target, string value) { if (target != null) target.text = value; }
    private string Monospace(string text) { return useMonospacedLabels ? "<mspace=0.6em>" + text + "</mspace>" : text; }
    private static void SetManualNavigation(Button button)
    {
        if (button != null) button.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>Shows the death overlay once, cancels modal gameplay, and freezes controls.</summary>
    public void TriggerDeathScreen()
    {
        if (deathSequence != null) { deathSequence.TriggerDeath(); return; }
        ShowPreparedDeathScreen();
    }

    /// <summary>Freezes gameplay and marks death before cinematic animation, without showing UI.</summary>
    public bool BeginDeathLockdown()
    {
        if (!isActiveAndEnabled || transitioning) return false;
        if (IsVisible) return true;
        if (overlayCanvas == null)
        {
            Debug.LogError("Assign the death overlay Canvas before triggering death.", this);
            return false;
        }
        // Close notes/keypads/pause first so their cleanup cannot later undo this overlay.
        if (player != null && player.BlocksControls) player.CloseScreen();
        previousTimeScale = Time.timeScale;
        previousListenerPause = AudioListener.pause;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        ownsGlobals = true;
        if (player != null)
        {
            if (player.Combat != null)
            {
                player.Combat.CancelReload();
                player.Combat.SetAim(false);
                player.Combat.SetTriggerHeld(false);
            }
            player.State.SetMode(PlayerState.Mode.Dead);
            if (player.Motor != null) player.Motor.SetControlsEnabled(false);
            DisableScript(player.GetComponent<InputManager>());
            DisableScript(player.Motor);
            DisableScript(player.Combat);
            DisableScript(player.GetComponent<PlayerMovementAudio>());
            DisableScript(player);
        }
        if (additionalScriptsToDisable != null)
            foreach (var script in additionalScriptsToDisable) DisableScript(script);
        menuEventSystem = EventSystem.current;
        if (menuEventSystem != null)
        {
            previousNavigationEvents = menuEventSystem.sendNavigationEvents;
            // Avoid duplicate Submit from the UI input module; pointer clicks still work.
            menuEventSystem.sendNavigationEvents = false;
            menuEventSystem.SetSelectedGameObject(null);
        }
        IsVisible = true;
        Time.timeScale = 0f;
        if (pauseWorldAudio) AudioListener.pause = true;
        return true;
    }

    /// <summary>Reveals the prepared overlay with the death video.</summary>
    public void ShowPreparedDeathScreen()
    {
        if (overlayShown || !BeginDeathLockdown()) return;
        overlayShown = true;
        openingFrame = Time.frameCount;
        fadeElapsed = 0f;
        menuReady = false;
        selection = 0;
        UpdateSelection();
        overlayCanvas.SetActive(true);
        if (videoPresentation != null) videoPresentation.PlayDeath();
        if (canvasGroup != null)
        {
            canvasGroup.alpha = fadeDuration > 0f ? 0f : 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
        Time.timeScale = 0f;
        if (pauseWorldAudio) AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void DisableScript(Behaviour script)
    {
        if (script == null || script == this || disabledScripts.Contains(script)) return;
        disabledScripts.Add(script);
        previousEnabled.Add(script.enabled);
        script.enabled = false;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (!IsVisible)
        {
            if (enableDebugKey && (Application.isEditor || Debug.isDebugBuild) &&
                debugKey != Key.None && keyboard != null && keyboard[debugKey].wasPressedThisFrame)
                TriggerDeathScreen();
            return;
        }
        if (!overlayShown) return;
        fadeElapsed += Time.unscaledDeltaTime;
        float textAlpha = fadeDuration <= 0f ? 1f : Mathf.Clamp01(fadeElapsed / fadeDuration);
        menuReady = textAlpha >= 1f;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = textAlpha;
            canvasGroup.interactable = menuReady && !transitioning;
            canvasGroup.blocksRaycasts = menuReady;
        }
        if (!menuReady) return;
        if (transitioning || Time.frameCount == openingFrame) return;
        var pad = Gamepad.current;
        bool up = keyboard != null && (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame);
        bool down = keyboard != null && (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame);
        if (pad != null && Time.unscaledTime >= nextNavigationTime)
        {
            float axis = pad.leftStick.y.ReadValue() + pad.dpad.y.ReadValue();
            if (Mathf.Abs(axis) > 0.5f)
            {
                up |= axis > 0f; down |= axis < 0f;
                nextNavigationTime = Time.unscaledTime + 0.2f;
            }
        }
        if (up || down) { selection = up ? 0 : 1; UpdateSelection(); }
        if ((keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
            (pad != null && pad.buttonEast.wasPressedThisFrame)) { AcceptDeath(); return; }
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) { RetrySimulation(); return; }
        bool submit = (keyboard != null && keyboard.enterKey.wasPressedThisFrame) ||
            (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame));
        if (submit) { if (selection == 0) RetrySimulation(); else AcceptDeath(); }
    }

    private void UpdateSelection()
    {
        SetText(retryLabel, selection == 0 ? selectedRetry : unselectedRetry);
        SetText(quitLabel, selection == 1 ? selectedQuit : unselectedQuit);
        if (retryLabel != null) retryLabel.color = selection == 0 ? selectedColor : unselectedColor;
        if (quitLabel != null) quitLabel.color = selection == 1 ? selectedColor : unselectedColor;
    }

    /// <summary>Reloads the active scene asynchronously. Also callable from a UI button.</summary>
    public void RetrySimulation()
    {
        if (!overlayShown || !menuReady || transitioning) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.buildIndex < 0 || !Application.CanStreamedLevelBeLoaded(scene.buildIndex))
        {
            Debug.LogError("Add the current scene to the active Build Profile Scene List before retrying.", this);
            return;
        }
        StartCoroutine(LoadScene(scene.path));
    }

    /// <summary>Loads the configured menu, or quits when no menu scene is specified.</summary>
    public void AcceptDeath()
    {
        if (!overlayShown || !menuReady || transitioning) return;
        if (!string.IsNullOrWhiteSpace(mainMenuScene))
        {
            if (!Application.CanStreamedLevelBeLoaded(mainMenuScene))
            {
                Debug.LogError("Main menu scene is missing from the active Build Profile Scene List.", this);
                return;
            }
            StartCoroutine(LoadScene(mainMenuScene));
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator LoadScene(string scene)
    {
        transitioning = true;
        if (canvasGroup != null) canvasGroup.interactable = false;
        AsyncOperation operation;
        try { operation = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single); }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            transitioning = false;
            if (canvasGroup != null) canvasGroup.interactable = true;
            yield break;
        }
        if (operation == null)
        {
            transitioning = false;
            if (canvasGroup != null) canvasGroup.interactable = true;
            yield break;
        }
        pendingLoad = operation;
        operation.allowSceneActivation = false;
        while (operation.progress < 0.9f) yield return null;
        // Restore global settings before the next scene's Awake/Start executes.
        Time.timeScale = 1f;
        AudioListener.pause = previousListenerPause;
        ownsGlobals = false;
        RestoreNavigation();
        operation.allowSceneActivation = true;
    }

    private void RestoreNavigation()
    {
        if (menuEventSystem != null) menuEventSystem.sendNavigationEvents = previousNavigationEvents;
        menuEventSystem = null;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        if (ownsGlobals)
        {
            Time.timeScale = transitioning ? 1f : previousTimeScale;
            AudioListener.pause = previousListenerPause;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            ownsGlobals = false;
        }
        RestoreNavigation();
        // A disabled manager must never leave an asynchronous load stalled at 90%.
        if (pendingLoad != null && !pendingLoad.isDone) pendingLoad.allowSceneActivation = true;
        if (!transitioning)
        {
            for (int i = disabledScripts.Count - 1; i >= 0; i--)
                if (disabledScripts[i] != null) disabledScripts[i].enabled = previousEnabled[i];
            // Removing the overlay does not revive the dead player. Only retry starts fresh.
        }
        disabledScripts.Clear(); previousEnabled.Clear();
        if (overlayCanvas != null && IsVisible) overlayCanvas.SetActive(false);
        IsVisible = false;
        overlayShown = false;
    }

    private void OnDestroy()
    {
        if (retryButton != null) retryButton.onClick.RemoveListener(RetrySimulation);
        if (quitButton != null) quitButton.onClick.RemoveListener(AcceptDeath);
    }
}
