using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Replace PotholePopupUI.cs only. Keep this component on PotholeCanvas.
// Uses the current EnvironmentGenerator API; does not alter gameplay.
[DisallowMultipleComponent]
public sealed class PotholePopupUI : MonoBehaviour
{
    [Header("Existing Reference")]
    [SerializeField] private EnvironmentGenerator terrain;
    [Header("Popup Appearance")]
    [SerializeField, Min(0f)] private float topMargin = 14f;
    [SerializeField, Range(0.7f, 1.5f)] private float uiScale = 1f;
    [SerializeField, Range(360f, 520f)] private float panelWidth = 440f;
    [SerializeField, Range(4f, 20f)] private float fadeSpeed = 10f;
    [SerializeField] private bool respectSafeArea = true;
    [SerializeField] private bool showKeyboardHints = true;

    private readonly Color ink = new Color32(29, 36, 31, 255);
    private readonly Color cream = new Color32(245, 241, 223, 255);
    private readonly Color muted = new Color32(181, 196, 189, 255);
    private readonly Color gold = new Color32(246, 190, 65, 255);
    private readonly Color green = new Color32(153, 215, 139, 255);
    private Canvas canvas;
    private RectTransform canvasRect, panelRect, fillRect;
    private GameObject panel, choices, balanceRow, track;
    private CanvasGroup group;
    private Image badge, border, distanceBadge;
    private TMP_Text badgeLabel, heading, subtitle, distanceLabel, balance, hint, repairLabel, skipLabel;
    private Button repairButton, skipButton;
    private Sprite roundedSprite;
    private Texture2D roundedTexture;
    private Sprite panelArtwork, repairArtwork, skipArtwork;
    private Image cardBackground;
    private bool initialised, wasPending;
    private float approachDistance, previousDistance, successUntil, alpha;
    private int paidCost;

    private void Start()
    {
        if (terrain == null) terrain = FindFirstObjectByType<EnvironmentGenerator>();
        canvas = GetComponentInParent<Canvas>();
        if (terrain == null || canvas == null || canvas.GetComponent<GraphicRaycaster>() == null)
        {
            Debug.LogError("PotholePopupUI: assign Terrain and use a Canvas with GraphicRaycaster.", this);
            enabled = false;
            return;
        }
        canvasRect = canvas.GetComponent<RectTransform>();
        CreateRoundedSprite();
        LoadArtwork();
        Build();
        initialised = true;
        terrain.PotholeRepaired += Repaired;
        terrain.SetCustomPopupEnabled(true);
        panel.SetActive(false);
    }

    private void Build()
    {
        Image outer = Box("Pothole Decision", canvas.transform, new Color32(105, 92, 58, 255));
        border = outer;
        panel = outer.gameObject;
        panelRect = outer.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 1f);
        group = panel.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        Shadow shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.28f);
        shadow.effectDistance = new Vector2(0f, -4f);
        Image inner = Box("Card", panel.transform, new Color32(28, 41, 41, 250));
        cardBackground = inner;
        Stretch(inner.rectTransform, 1.5f);
        if (panelArtwork != null)
        {
            UseArtwork(outer, panelArtwork);
            inner.enabled = false;
            shadow.enabled = false;
        }

        badge = Box("Warning Badge", panel.transform, gold);
        badgeLabel = Label("Symbol", badge.transform, 25f, ink, TextAlignmentOptions.Center);
        Stretch(badgeLabel.rectTransform, 1f);
        badgeLabel.text = "!";
        heading = Label("Heading", panel.transform, 21f, cream);
        heading.fontStyle = FontStyles.Bold;
        subtitle = Label("Subtitle", panel.transform, 13f, muted);
        distanceBadge = Box("Distance Badge", panel.transform, new Color32(49, 64, 60, 255));
        distanceLabel = Label("Distance", distanceBadge.transform, 16f, cream, TextAlignmentOptions.Center);
        Stretch(distanceLabel.rectTransform, 3f);

        balanceRow = new GameObject("Balance Row", typeof(RectTransform));
        balanceRow.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)balanceRow.transform, 0f);
        balance = Label("Coin Balance", balanceRow.transform, 15f, cream);
        hint = Label("Decision Hint", balanceRow.transform, 13f, muted, TextAlignmentOptions.MidlineRight);
        Image trackImage = Box("Approach Track", panel.transform, new Color32(66, 78, 69, 255));
        track = trackImage.gameObject;
        Image fill = Box("Distance Remaining", track.transform, gold);
        fillRect = fill.rectTransform;
        Stretch(fillRect, 0f);

        choices = new GameObject("Choices", typeof(RectTransform));
        choices.transform.SetParent(panel.transform, false);
        Stretch((RectTransform)choices.transform, 0f);
        repairButton = MakeButton("Repair", choices.transform, gold, ink, out repairLabel);
        skipButton = MakeButton("Skip", choices.transform, new Color32(57, 73, 68, 255), cream, out skipLabel);
        if (repairArtwork != null) UseArtwork((Image)repairButton.targetGraphic, repairArtwork, 4f);
        if (skipArtwork != null) UseArtwork((Image)skipButton.targetGraphic, skipArtwork, 4f);
        repairButton.onClick.AddListener(terrain.ChooseRepair);
        skipButton.onClick.AddListener(terrain.ChooseSkip);
    }

    private void LoadArtwork()
    {
        Texture2D atlas = Resources.Load<Texture2D>("CGGUI/RepairPopupAtlas");
        if (atlas == null) return; // Keep the existing card usable if the art is unavailable.
        panelArtwork = AtlasSprite(atlas, 34f, 146f, 1187f, 522f, new Vector4(90f, 75f, 90f, 80f));
        repairArtwork = AtlasSprite(atlas, 38f, 852f, 710f, 224f, new Vector4(85f, 65f, 85f, 65f));
        skipArtwork = AtlasSprite(atlas, 758f, 852f, 460f, 222f, new Vector4(85f, 65f, 85f, 65f));
    }

    private static Sprite AtlasSprite(Texture2D texture, float x, float top, float width, float height, Vector4 borderPixels)
    {
        // Pixel regions of the original 1254-square PNG; preserve alpha and gutters.
        float scale = texture.width / 1254f;
        Rect rect = new Rect(x * scale, texture.height - (top + height) * scale, width * scale, height * scale);
        return Sprite.Create(texture, rect, Vector2.one * 0.5f, 100f * scale, 0, SpriteMeshType.FullRect, borderPixels * scale);
    }

    private static void UseArtwork(Image image, Sprite artwork, float density = 3f)
    {
        image.sprite = artwork;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = density;
        image.color = Color.white;
    }

    private Image Box(string name, Transform parent, Color colour)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = roundedSprite;
        image.type = Image.Type.Sliced;
        image.color = colour;
        image.raycastTarget = false;
        return image;
    }

    private TMP_Text Label(string name, Transform parent, float size, Color colour,
        TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size; text.fontSizeMin = 11f; text.fontSizeMax = size;
        text.enableAutoSizing = true; text.color = colour; text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private Button MakeButton(string name, Transform parent, Color colour, Color textColour, out TMP_Text label)
    {
        Image image = Box(name, parent, colour);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colours = button.colors;
        colours.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
        colours.pressedColor = new Color(0.78f, 0.78f, 0.78f);
        colours.selectedColor = Color.white;
        colours.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.75f);
        colours.fadeDuration = 0.1f;
        button.colors = colours;
        label = Label("Label", image.transform, 18f, textColour, TextAlignmentOptions.Center);
        label.fontStyle = FontStyles.Bold;
        Stretch(label.rectTransform, 7f);
        return button;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset;
    }

    private static void At(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void Layout(bool pending)
    {
        Rect safe = respectSafeArea ? Screen.safeArea : new Rect(0f, 0f, Screen.width, Screen.height);
        float pixelScale = Mathf.Max(0.1f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f)) * uiScale;
        pixelScale = Mathf.Min(pixelScale, Mathf.Max(0.1f, (safe.width - 24f) / panelWidth));
        float buttonHeight = Mathf.Max(46f, 44f / pixelScale);
        float width = panelWidth;
        float artInset = panelArtwork != null ? 12f : 0f;
        panelRect.sizeDelta = new Vector2(width, pending ? 116f + buttonHeight + artInset * 2f : 82f);
        panelRect.localScale = Vector3.one * (pixelScale / Mathf.Max(0.01f, canvas.scaleFactor));
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 screenPoint = new Vector2(safe.center.x, safe.yMax - topMargin * pixelScale);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, camera, out Vector2 position))
            panelRect.anchoredPosition = position - new Vector2(canvasRect.rect.center.x, canvasRect.rect.yMax)
                + Vector2.up * (1f - alpha) * 8f;
        float offset = pending ? artInset : 0f;
        At(badge.rectTransform, 18f, 15f + offset, 36f, 36f);
        At(heading.rectTransform, 63f, 13f + offset, width - 195f, 26f);
        At(subtitle.rectTransform, 63f, 39f + offset, width - 85f, pending ? 20f : 30f);
        At(distanceBadge.rectTransform, width - 124f, 16f + offset, 102f, 29f);
        At(balance.rectTransform, 22f, 67f + offset, 150f, 21f);
        At(hint.rectTransform, 175f, 67f + offset, width - 197f, 21f);
        At((RectTransform)track.transform, 22f, 96f + offset, width - 44f, 4f);
        float repairWidth = (width - 46f) * 0.63f;
        At((RectTransform)repairButton.transform, 18f, 108f + offset, repairWidth, buttonHeight);
        At((RectTransform)skipButton.transform, 28f + repairWidth, 108f + offset, width - 46f - repairWidth, buttonHeight);
    }

    private void Update()
    {
        if (!initialised || terrain == null) return;
        bool pending = terrain.HasPendingPothole;
        bool success = Time.unscaledTime < successUntil;
        bool show = terrain.IsRunActive && (pending || success || !string.IsNullOrEmpty(terrain.CurrentNotice));
        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        panel.SetActive(alpha > 0f);
        group.alpha = alpha;
        group.interactable = group.blocksRaycasts = show && pending && alpha > 0.85f;
        if (alpha <= 0f) { wasPending = false; return; }

        choices.SetActive(pending); balanceRow.SetActive(pending); track.SetActive(pending);
        distanceBadge.gameObject.SetActive(pending);
        Layout(pending);
        if (panelArtwork != null)
        {
            // Use the compact original toast for success/status messages.
            border.sprite = pending ? panelArtwork : roundedSprite;
            border.pixelsPerUnitMultiplier = pending ? 3f : 1f;
            cardBackground.enabled = !pending;
        }
        if (pending)
        {
            float distance = terrain.PendingDistance;
            if (!wasPending || distance > previousDistance + 5f) approachDistance = Mathf.Max(1f, distance);
            approachDistance = Mathf.Max(approachDistance, distance);
            previousDistance = distance;
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(distance / approachDistance), 1f);
            int cost = terrain.PendingRepairCost, coins = terrain.PendingCoinBalance;
            bool affordable = coins >= cost;
            heading.text = "ROAD DAMAGE";
            subtitle.text = "Repair the road or take the bump";
            distanceLabel.text = Mathf.CeilToInt(distance) + " m ahead";
            balance.text = "COINS  " + coins;
            hint.text = affordable ? "Choose before the edge" : "Need " + (cost - coins) + " more coins";
            hint.color = affordable ? muted : gold;
            repairLabel.text = "REPAIR  " + cost + (showKeyboardHints ? "  [R]" : "");
            skipLabel.text = "SKIP" + (showKeyboardHints ? "  [S]" : "");
            repairButton.interactable = affordable;
            badge.color = gold; badgeLabel.text = "!";
            border.color = panelArtwork != null ? Color.white : new Color32(105, 92, 58, 255);
        }
        else
        {
            heading.text = success ? "ROAD REPAIRED" : "ROAD UPDATE";
            subtitle.text = success ? "-" + paidCost + " coins  |  Smooth road ahead" : terrain.CurrentNotice;
            badge.color = success ? green : gold;
            badgeLabel.text = success ? "+" : "!";
            border.color = success ? new Color32(78, 112, 72, 255) : new Color32(105, 92, 58, 255);
        }
        wasPending = pending;
    }

    private void Repaired(EnvironmentGenerator.Pothole hole)
    {
        paidCost = hole.Cost;
        successUntil = Time.unscaledTime + 1.8f;
    }

    private void CreateRoundedSprite()
    {
        const int size = 32;
        const float radius = 11f;
        roundedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        { name = "Pothole UI Corners (Runtime)", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - 15.5f) - (15.5f - radius));
            float dy = Mathf.Max(0f, Mathf.Abs(y - 15.5f) - (15.5f - radius));
            pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy)));
        }
        roundedTexture.SetPixels(pixels); roundedTexture.Apply(false, true);
        roundedSprite = Sprite.Create(roundedTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(12f, 12f, 12f, 12f));
    }

    private void OnEnable()
    {
        if (initialised && terrain != null) terrain.SetCustomPopupEnabled(true);
    }
    private void OnDisable()
    {
        alpha = 0f; wasPending = false; successUntil = 0f;
        if (panel != null) panel.SetActive(false);
        if (terrain != null) terrain.SetCustomPopupEnabled(false);
    }
    private void OnDestroy()
    {
        if (terrain != null) terrain.PotholeRepaired -= Repaired;
        if (panel != null) Destroy(panel);
        if (roundedSprite != null) Destroy(roundedSprite);
        if (roundedTexture != null) Destroy(roundedTexture);
        if (panelArtwork != null) Destroy(panelArtwork);
        if (repairArtwork != null) Destroy(repairArtwork);
        if (skipArtwork != null) Destroy(skipArtwork);
    }
}
