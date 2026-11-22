using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Creates the documented pause hierarchy once; called by the current scene's HUD setup.</summary>
[DefaultExecutionOrder(260)]
[DisallowMultipleComponent]
public sealed class AnalogPauseMenuSetup : MonoBehaviour
{
    private Texture2D staticTexture;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private PlayerGameplay player;
    private bool built;
    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (player == null || font == null) { Debug.LogError("Pause setup needs a player and TMP font.", this); return; }
        Build(player, font);
    }
    private Sprite barSprite;
    public void Build(PlayerGameplay player, TMP_FontAsset typeface)
    {
        if (built || player.PauseMenu != null) return;
        built = true;
        font = typeface;
        var root = new GameObject("PauseCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
        var content = Rect("PauseMenu_CanvasGroup", root.transform, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
        Stretch(content);
        var group = content.gameObject.AddComponent<CanvasGroup>();
        var frame = Rect("FrozenGameplayFrame", content, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
        Stretch(frame); var raw = frame.gameObject.AddComponent<RawImage>(); raw.raycastTarget = false;
        var dim = Rect("Background_Dimmer", content, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
        Stretch(dim); dim.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);
        var lines = Rect("StaticScanlines_Overlay", content, Vector2.one * 0.5f, Vector2.zero, Vector2.zero);
        Stretch(lines); lines.gameObject.AddComponent<Canvas>(); var scan = lines.gameObject.AddComponent<RawImage>(); scan.raycastTarget = false;
        staticTexture = new Texture2D(64, 128, TextureFormat.RGBA32, false);
        staticTexture.wrapMode = TextureWrapMode.Repeat; staticTexture.filterMode = FilterMode.Point;
        var pixels = new Color32[64 * 128]; var random = new System.Random(734);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 64; x++)
            pixels[y * 64 + x] = new Color32(255, 255, 255, (byte)(y % 11 == 0 ? random.Next(90, 220) : y % 2 == 0 ? 28 : 0));
        staticTexture.SetPixels32(pixels); staticTexture.Apply(false, true); scan.texture = staticTexture;
        var menu = Rect("LeftMenu_Container", content, new Vector2(0, 0.5f), new Vector2(130, 30), new Vector2(600, 320));
        var header = Text("Text_PausedHeader", menu, new Vector2(0, 110), new Vector2(600, 80), "PAUSED", 54);
        header.fontStyle = FontStyles.Bold; header.characterSpacing = 2;
        var options = new TMP_Text[3];
        string[] names = { "Text_Resume", "Text_CollectedNotes", "Text_Quit" };
        var host = new GameObject("Analog Pause Controller"); host.transform.SetParent(transform, false); host.SetActive(false);
        var controller = host.AddComponent<AnalogPauseMenu>();
        for (int i = 0; i < 3; i++)
        {
            options[i] = Text(names[i], menu, new Vector2(25, -i * 70), new Vector2(600, 55), "", 32);
            options[i].raycastTarget = true;
            var button = options[i].gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            int index = i; button.onClick.AddListener(() => controller.Activate(index));
        }
        var indicator = Text("SelectionBracket", menu, new Vector2(-15, 0), new Vector2(35, 55), ">", 32).rectTransform;
        var widget = Rect("TopRight_StatusWidget", content, Vector2.one, new Vector2(-470, -55), new Vector2(420, 190));
        var health = Text("Header_Status", widget, Vector2.zero, new Vector2(420, 32), "STATUS: FINE", 23);
        var ecg = Rect("ECG", widget, Vector2.zero, new Vector2(0, -42), new Vector2(400, 38));
        ecg.gameObject.AddComponent<ECGWaveGraphic>().raycastTarget = false;
        var noise = Text("Text_NoiseValue", widget, new Vector2(0, -92), new Vector2(420, 32), "", 18);
        var barRect = Rect("Bar_NoiseThreshold", widget, Vector2.zero, new Vector2(0, -132), new Vector2(400, 6));
        barRect.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
        var fillRect = Rect("Fill", barRect, Vector2.zero, Vector2.zero, new Vector2(400, 6));
        var bar = fillRect.gameObject.AddComponent<Image>(); bar.type = Image.Type.Filled; bar.fillMethod = Image.FillMethod.Horizontal;
        bar.color = new Color(0.4f, 0.8f, 0.6f);
        barSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * 0.5f);
        bar.sprite = barSprite;
        foreach (float radius in new[] { 2f, 10f })
        {
            var tick = Rect("Threshold " + radius, barRect, Vector2.zero, new Vector2(radius / 40 * 400, 3), new Vector2(1, 12));
            tick.gameObject.AddComponent<Image>().color = Color.white;
        }
        Text("ThresholdLabels", widget, new Vector2(0, -158), new Vector2(420, 24), "WALK 2m     SPRINT 10m", 15);
        controller.Configure(player, group, raw, scan, options, health, noise, bar, indicator);
        host.SetActive(true);
    }
    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform)); item.transform.SetParent(parent, false);
        var rect = (RectTransform)item.transform; rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private TextMeshProUGUI Text(string name, Transform parent, Vector2 position, Vector2 size, string value, float fontSize)
    {
        var label = Rect(name, parent, Vector2.zero, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = fontSize; label.text = value; label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.raycastTarget = false; return label;
    }
    private void OnDestroy() { if (staticTexture != null) Destroy(staticTexture); if (barSprite != null) Destroy(barSprite); }
}
