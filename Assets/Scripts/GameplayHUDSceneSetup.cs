using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds and wires SampleScene's responsive gameplay HUD once at startup.</summary>
[DefaultExecutionOrder(250)]
public sealed class GameplayHUDSceneSetup : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private PlayerGameplay player;
    private Texture2D vignetteTexture;
    private Sprite vignetteSprite;

    private void Awake()
    {
        if (GetComponentInChildren<GameplayHUDController>(true) != null) return;
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) { Debug.LogError("Assign the gameplay HUD's TMP font.", this); return; }
        if (player == null) player = FindFirstObjectByType<PlayerGameplay>();
        Build();
    }

    private void Build()
    {
        var canvasObject = new GameObject("GameplayCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<Canvas>().sortingOrder = 20;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)canvasObject.transform;
        var view = new GameplayHUDController.ViewReferences { root = canvasObject.GetComponent<CanvasGroup>() };

        view.vignette = Image("Critical Health Vignette", root, Vector2.one * 0.5f, Vector2.zero, Vector2.zero, Color.clear);
        Stretch(view.vignette.rectTransform);
        view.vignette.sprite = CreateVignette();

        var noisePanel = Image("Acoustic Panel", root, Vector2.one, new Vector2(-265f, -93f),
            new Vector2(450f, 135f), new Color(0f, 0f, 0f, 0.55f)).rectTransform;
        view.noiseHeader = Text("Header", noisePanel, new Vector2(0f, 39f), new Vector2(410f, 30f), 21f);
        var ticks = Rect("Noise Ticks", noisePanel, Vector2.one * 0.5f, Vector2.zero, new Vector2(410f, 32f));
        ticks.gameObject.AddComponent<Canvas>();
        view.noiseTicks = new Image[64];
        for (int i = 0; i < view.noiseTicks.Length; i++)
        {
            float height = i % 4 == 0 ? 28f : i % 2 == 0 ? 20f : 15f;
            view.noiseTicks[i] = Image("Tick " + i, ticks, Vector2.one * 0.5f,
                new Vector2(-202f + i * 404f / 63f, 0f), new Vector2(2f, height), Color.gray);
        }
        view.dangerNotch = Image("Danger Threshold Notch", ticks, new Vector2(0.75f, 0.5f),
            Vector2.zero, new Vector2(3f, 38f), new Color(0.9f, 0.15f, 0.12f)).rectTransform;
        view.actionLabel = Text("Action", noisePanel, new Vector2(-100f, -40f), new Vector2(210f, 30f), 22f);
        view.detectionLabel = Text("Detection", noisePanel, new Vector2(110f, -40f), new Vector2(190f, 30f), 22f);
        view.detectionLabel.alignment = TextAlignmentOptions.MidlineRight;

        var bio = Image("Biometric and Weapon Panel", root, new Vector2(1f, 0f), new Vector2(-265f, 105f),
            new Vector2(450f, 150f), new Color(0f, 0f, 0f, 0.6f)).rectTransform;
        view.weaponLabel = Text("Weapon Ammo", bio, new Vector2(0f, 43f), new Vector2(410f, 42f), 28f);
        view.weaponLabel.alignment = TextAlignmentOptions.MidlineRight;
        var ecgRect = Rect("ECG Wave", bio, Vector2.one * 0.5f, new Vector2(-135f, -25f), new Vector2(130f, 65f));
        // Isolate the frequently rebuilt waveform from the static text/tick Canvas batches.
        ecgRect.gameObject.AddComponent<Canvas>();
        view.ecg = ecgRect.gameObject.AddComponent<ECGWaveGraphic>();
        view.ecg.raycastTarget = false;
        view.healthLabel = Text("Health State", bio, new Vector2(-5f, -24f), new Vector2(100f, 32f), 23f);
        view.healthSegments = new Image[10];
        var healthBar = Rect("Health Segments", bio, Vector2.one * 0.5f, new Vector2(135f, -24f), new Vector2(140f, 26f));
        for (int i = 0; i < view.healthSegments.Length; i++)
            view.healthSegments[i] = Image("Health " + i, healthBar, Vector2.one * 0.5f,
                new Vector2(-63f + i * 14f, 0f), new Vector2(10f, 24f), Color.green);

        var hints = Rect("Control Hints", root, Vector2.zero, new Vector2(575f, 45f), new Vector2(1080f, 36f));
        view.hints = hints.gameObject.AddComponent<CanvasGroup>();
        view.hintLabel = Text("Hints", hints, Vector2.zero, new Vector2(1080f, 36f), 20f);
        var crosshair = Rect("Center Crosshair", root, Vector2.one * 0.5f, Vector2.zero, new Vector2(32f, 32f));
        crosshair.gameObject.AddComponent<Canvas>();
        view.crosshair = crosshair.gameObject.AddComponent<CanvasGroup>();
        var crosshairImage = crosshair.gameObject.AddComponent<RawImage>();
        crosshairImage.texture = player != null ? player.crosshairTexture : null;
        crosshairImage.raycastTarget = false;
        crosshairImage.color = Color.white;

        var logic = new GameObject("Gameplay HUD Logic");
        logic.transform.SetParent(transform, false);
        logic.SetActive(false);
        var hud = logic.AddComponent<GameplayHUDController>();
        hud.ConfigureView(view);
        logic.AddComponent<GameplayHUDBridge>().Configure(hud, player);
        logic.SetActive(true);
    }

    private RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        var rect = (RectTransform)item.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        return rect;
    }
    private Image Image(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color tint)
    {
        var image = Rect(name, parent, anchor, position, size).gameObject.AddComponent<Image>();
        image.color = tint; image.raycastTarget = false;
        return image;
    }
    private TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name, parent, Vector2.one * 0.5f, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = fontSize;
        text.color = new Color(0.8f, 0.82f, 0.78f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private Sprite CreateVignette()
    {
        const int size = 128;
        vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        vignetteTexture.name = "HUD Critical Health Edge Gradient";
        vignetteTexture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float edge = Mathf.Max(Mathf.Abs(x / (size - 1f) * 2f - 1f), Mathf.Abs(y / (size - 1f) * 2f - 1f));
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, edge));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        vignetteTexture.SetPixels32(pixels);
        vignetteTexture.Apply(false, true);
        vignetteSprite = Sprite.Create(vignetteTexture, new Rect(0, 0, size, size),
            Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect);
        return vignetteSprite;
    }
    private void OnDestroy()
    {
        if (vignetteSprite != null) Destroy(vignetteSprite);
        if (vignetteTexture != null) Destroy(vignetteTexture);
    }
}
