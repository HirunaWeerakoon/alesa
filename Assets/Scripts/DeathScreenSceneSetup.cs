using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>Wires the death video and its two action buttons into the current player.</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class DeathScreenSceneSetup : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private PlayerGameplay player;
    [SerializeField] private VideoClip deathVideo = null;

    private void Awake()
    {
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (font == null || player == null) { Debug.LogError("Death screen requires a player and TMP font.", this); return; }
        if (EventSystem.current == null)
        {
            var events = new GameObject("Death UI EventSystem", typeof(EventSystem));
            events.transform.SetParent(transform, false);
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var canvasObject = new GameObject("DeathOverlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.SetActive(false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var content = Stretch("Death Video View", canvasObject.transform);
        var black = content.gameObject.AddComponent<Image>();
        black.color = Color.black;
        var screen = Stretch("Animation", content).gameObject.AddComponent<RawImage>();
        screen.raycastTarget = false;
        screen.color = Color.clear;
        var fit = screen.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = deathVideo != null && deathVideo.height > 0 ? (float)deathVideo.width / deathVideo.height : 16f / 9f;
        var actions = Rect("Death Actions", content, Vector2.zero, new Vector2(450, 110), new Vector2(810, 140));
        var view = new DeathScreenManager.OverlayReferences { canvas = content.gameObject, group = actions.gameObject.AddComponent<CanvasGroup>() };
        view.retryButton = ActionButton("Retry", actions, new Vector2(0, 30), out view.retryLabel);
        view.quitButton = ActionButton("Quit", actions, new Vector2(0, -30), out view.quitLabel);
        var system = new GameObject("DeathScreenSystem");
        system.transform.SetParent(transform, false);
        system.SetActive(false);
        var presentation = system.AddComponent<DeathVideoPresentation>();
        presentation.Configure(deathVideo, screen);
        var manager = system.AddComponent<DeathScreenManager>();
        manager.ConfigureOverlay(view, player, System.Array.Empty<Behaviour>());
        manager.ConfigureVideo(presentation);
        var ui = canvasObject.AddComponent<DeathScreenUIController>();
        ui.Configure(manager);
        var sequence = player.GetComponent<PlayerDeathSequence>();
        if (sequence == null) sequence = player.gameObject.AddComponent<PlayerDeathSequence>();
        sequence.Configure(player.View, ui);
        manager.ConfigureSequence(sequence);
        system.SetActive(true);
        canvasObject.SetActive(true);
    }

    private RectTransform Stretch(string name, Transform parent)
    {
        var rect = Rect(name, parent, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        var rect = (RectTransform)item.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private UnityEngine.UI.Image Image(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color)
    {
        var rect = Rect(name, parent, anchor, position, size);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private RectTransform Panel(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        return Image(name, parent, anchor, position, size, new Color(0f, 0f, 0f, 0.65f)).rectTransform;
    }

    private TextMeshProUGUI Text(string name, Transform parent, Vector2 position, Vector2 size, string text, float fontSize)
    {
        var rect = Rect(name, parent, Vector2.one * 0.5f, position, size);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.color = new Color(0.92f, 0.92f, 0.9f);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private Button ActionButton(string name, Transform parent, Vector2 position, out TMP_Text label)
    {
        var background = Image(name, parent, Vector2.one * 0.5f, position, new Vector2(810f, 60f), Color.white);
        background.raycastTarget = true;
        var button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.15f);
        colors.pressedColor = new Color(0.85f, 0.18f, 0.12f, 0.3f);
        button.colors = colors;
        label = Text("Label", background.transform, Vector2.zero, new Vector2(780f, 55f), name, 24f);
        return button;
    }
}
