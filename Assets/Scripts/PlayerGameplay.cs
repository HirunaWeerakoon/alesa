using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerGameplay : MonoBehaviour
{
    public enum ScreenMode { Playing, Paused, Note, Keypad }
    [Min(0.1f)] public float interactionDistance = 2f;
    public LayerMask interactionMask = ~0;
    [Header("Crosshair")]
    public Texture2D crosshairTexture;
    [Min(1)] public float idleCrosshairSize = 36f;
    [Min(1)] public float movingCrosshairSize = 58f;
    [Min(1)] public float aimedCrosshairSize = 22f;
    [Min(0)] public float aimedMovementExpansion = 4f;
    [Min(1)] public float crosshairResponse = 12f;
    private float crosshairSize = 36f;
    private CharacterController movementController;
    public PlayerMotor Motor { get; private set; }
    public Camera View { get; private set; }
    public RevolverCombat Combat { get; private set; }
    public ScreenMode Mode { get; private set; }
    public bool BlocksControls => Mode != ScreenMode.Playing;
    public bool AlesaFreed { get; private set; }
    public readonly List<string> NoteTitles = new List<string>();
    private readonly List<string> noteTexts = new List<string>();
    private string noteTitle, noteText, notification;
    private float notificationUntil, previousTimeScale;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible, ownsModal, previousViewEnabled;
    private DialpadKeypad keypad;
    private string code = "", keypadMessage = "";
    private Vector2 noteScroll;

    private void Awake()
    {
        Motor = GetComponent<PlayerMotor>();
        movementController = GetComponent<CharacterController>();
        crosshairSize = idleCrosshairSize;
        View = Motor.playerCamera != null ? Motor.playerCamera : GetComponentInChildren<Camera>();
        Combat = GetComponent<RevolverCombat>();
        if (Combat == null) Combat = gameObject.AddComponent<RevolverCombat>();
    }

    private void Start()
    {
        if (View == null) { Debug.LogError("PlayerGameplay needs a child camera.", this); enabled = false; return; }
        if (BlocksControls) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (BlocksControls || Combat == null) return;
        Vector3 velocity = movementController != null ? movementController.velocity : Vector3.zero;
        velocity.y = 0;
        float movement = Mathf.Clamp01(velocity.magnitude / Mathf.Max(0.01f, Motor.walkSpeed));
        float size = Mathf.Lerp(idleCrosshairSize, movingCrosshairSize, movement);
        size = Mathf.Lerp(size, aimedCrosshairSize + movement * aimedMovementExpansion, Combat.AimBlend);
        crosshairSize = Mathf.Lerp(crosshairSize, size, 1f - Mathf.Exp(-crosshairResponse * Time.deltaTime));
    }

    public bool RaycastFromView(float distance, LayerMask mask, out RaycastHit closest)
    {
        closest = default;
        if (View == null) return false;
        var hits = Physics.RaycastAll(View.ViewportPointToRay(new Vector3(0.5f, 0.5f)), distance, mask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            closest = hit;
            return true; // The first non-player obstacle blocks everything behind it.
        }
        return false;
    }

    private IInteractable Target()
    {
        if (BlocksControls || !RaycastFromView(interactionDistance, interactionMask, out RaycastHit hit)) return null;
        foreach (var component in hit.collider.GetComponentsInParent<MonoBehaviour>())
            if (component.isActiveAndEnabled && component is IInteractable target && target.CanInteract(this)) return target;
        return null;
    }

    public void TryInteract() { Target()?.Interact(this); }
    public void Notify(string message, float seconds = 3f) { notification = message; notificationUntil = Time.unscaledTime + seconds; }

    public void HandleEscape()
    {
        if (BlocksControls) CloseScreen();
        else OpenScreen(ScreenMode.Paused);
    }

    private void OpenScreen(ScreenMode mode)
    {
        if (!ownsModal)
        {
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            ownsModal = true;
        }
        Mode = mode;
        Combat.SetAim(false);
        Combat.CancelReload();
        Motor.SetControlsEnabled(false);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseScreen()
    {
        if (keypad != null)
        {
            if (keypad.stationaryCamera != null) keypad.stationaryCamera.enabled = false;
            if (View != null) View.enabled = previousViewEnabled;
            keypad = null;
        }
        Mode = ScreenMode.Playing;
        if (ownsModal)
        {
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            ownsModal = false;
        }
        Motor.SetControlsEnabled(true);
    }

    public void ReadNote(string title, string text)
    {
        NoteTitles.Add(title); noteTexts.Add(text);
        ShowNote(NoteTitles.Count - 1);
    }

    private void ShowNote(int index)
    {
        noteTitle = NoteTitles[index]; noteText = noteTexts[index]; noteScroll = Vector2.zero;
        OpenScreen(ScreenMode.Note);
    }

    public void OpenKeypad(DialpadKeypad target)
    {
        if (BlocksControls || target.stationaryCamera == null || target.stationaryCamera == View) return;
        keypad = target; code = ""; keypadMessage = "Enter the access code";
        previousViewEnabled = View.enabled;
        OpenScreen(ScreenMode.Keypad);
        View.enabled = false;
        target.stationaryCamera.enabled = true;
    }

    private void SubmitCode()
    {
        if (keypad == null) { CloseScreen(); return; }
        if (!keypad.CodeMatches(code)) { keypadMessage = "Incorrect code. Try again."; code = ""; return; }
        var solved = keypad;
        CloseScreen();
        solved.Unlock(this);
    }

    public void MarkAlesaFreed() { AlesaFreed = true; Notify("Access granted. Alesa is free.", 5f); }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && !BlocksControls && isActiveAndEnabled) OpenScreen(ScreenMode.Paused);
    }

    private void OnDisable()
    {
        if (ownsModal) CloseScreen();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnGUI()
    {
        if (Combat == null) return;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        if (!BlocksControls)
        {
            if (crosshairTexture != null)
            {
                // Draw in screen pixels so the texture stays square on any aspect ratio.
                var hudMatrix = GUI.matrix;
                GUI.matrix = Matrix4x4.identity;
                float size = crosshairSize * Screen.height / 720f;
                GUI.DrawTexture(new Rect((Screen.width - size) / 2, (Screen.height - size) / 2, size, size), crosshairTexture, ScaleMode.ScaleToFit, true);
                GUI.matrix = hudMatrix;
            }
            var target = Target();
            if (target != null) GUI.Box(new Rect(440, 420, 400, 35), "[E] " + target.Prompt);
            if (Combat.HasRevolver) GUI.Box(new Rect(1020, 650, 230, 42), Combat.IsReloading ? "Reloading..." : $"Revolver  {Combat.LoadedAmmo} / {Combat.SpareAmmo}");
            GUI.Label(new Rect(25, 670, 500, 30), "E Interact   |   RMB Aim   LMB Fire   R Reload   |   Esc Pause");
        }
        if (Time.unscaledTime < notificationUntil) GUI.Box(new Rect(300, 35, 680, 38), notification);
        if (BlocksControls)
        {
            GUI.Box(new Rect(390, 110, 500, 510), "");
            GUILayout.BeginArea(new Rect(415, 130, 450, 470));
            if (Mode == ScreenMode.Paused)
            {
                GUILayout.Label("PAUSED");
                GUILayout.Space(20);
                if (GUILayout.Button("Resume", GUILayout.Height(42))) CloseScreen();
                GUILayout.Space(20);
                GUILayout.Label("Collected notes");
                noteScroll = GUILayout.BeginScrollView(noteScroll);
                for (int i = 0; i < NoteTitles.Count; i++) if (GUILayout.Button(NoteTitles[i])) ShowNote(i);
                GUILayout.EndScrollView();
                GUILayout.Label("Esc to resume");
            }
            else if (Mode == ScreenMode.Note)
            {
                GUILayout.Label(noteTitle);
                noteScroll = GUILayout.BeginScrollView(noteScroll);
                GUILayout.Label(noteText, new GUIStyle(GUI.skin.label) { wordWrap = true });
                GUILayout.EndScrollView();
                if (GUILayout.Button("Close note", GUILayout.Height(40))) CloseScreen();
            }
            else if (Mode == ScreenMode.Keypad)
            {
                if (keypad == null) { GUILayout.Label("Keypad unavailable. Press Esc."); }
                else
                {
                    GUILayout.Label(keypadMessage);
                    GUILayout.Box(new string('*', code.Length), GUILayout.Height(40));
                    for (int row = 0; row < 3; row++)
                    {
                        GUILayout.BeginHorizontal();
                        for (int col = 1; col <= 3; col++) Digit((row * 3 + col).ToString());
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Clear", GUILayout.Height(48))) code = "";
                    Digit("0");
                    if (GUILayout.Button("Enter", GUILayout.Height(48))) SubmitCode();
                    GUILayout.EndHorizontal();
                    GUILayout.Space(20);
                    if (GUILayout.Button("Leave keypad", GUILayout.Height(35))) CloseScreen();
                }
            }
            GUILayout.EndArea();
        }
        GUI.matrix = oldMatrix;
    }

    private void Digit(string digit)
    {
        if (GUILayout.Button(digit, GUILayout.Height(48)) && code.Length < 12) code += digit;
    }
}
