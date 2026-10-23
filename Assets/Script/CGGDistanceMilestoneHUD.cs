using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Add to a NEW, top-level empty GameObject. Canvas/CanvasScaler are added automatically.
// Passive UI: never changes the vehicle, coins, time scale or existing distance HUD.
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas), typeof(CanvasScaler))]
public sealed class CGGDistanceMilestoneHUD : MonoBehaviour
{
    public enum DisplayMode { MilestonesOnly, DistanceAndMilestones }
    [Header("Scene References")]
    [Tooltip("Drag the Vehicle_Selection panel here. Tracking waits until this panel closes.")]
    [SerializeField] private GameObject startPanel;
    [Tooltip("Drag the GameOver panel here. Ends tracking and saves your best when it opens.")]
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("Optional: the Vehicle_Car root with Driver attached. Leave empty for automatic selection.")]
    [SerializeField] private Transform playerOverride;
    [Header("Presentation")]
    [Tooltip("Milestones Only keeps your existing distance ruler. The other mode adds a distance card.")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.MilestonesOnly;
    [Tooltip("Distance card position: pixels right and down from the safe area's top-left, at 1280x720.")]
    [SerializeField] private Vector2 panelOffset = new Vector2(18f, 118f);
    [Tooltip("Height above the safe area's bottom edge, at the 1280x720 reference resolution.")]
    [SerializeField, Min(0f)] private float noticeBottomOffset = 135f;
    [SerializeField] private bool respectSafeArea = true;
    [SerializeField, Range(0.7f, 1.4f)] private float uiScale = 1f;
    [SerializeField] private bool announceNewBest = true;
    [Header("Milestones - Metres")]
    [SerializeField, Min(1)] private int firstMilestone = 250;
    [SerializeField, Min(2)] private int secondMilestone = 500;
    [SerializeField, Min(3)] private int thirdMilestone = 1000;
    [SerializeField, Min(1)] private int milestoneStepAfterThird = 500;
    [SerializeField, Min(0.5f)] private float noticeDuration = 2.2f;
    [Header("Performance And Saving")]
    [SerializeField, Range(5f, 30f)] private float uiUpdatesPerSecond = 10f;
    [SerializeField, Min(5f)] private float bestSaveInterval = 30f;

    private const string BestDistanceKey = "HCR_BEST_DISTANCE";
    private Driver[] candidates;
    private Driver driver;
    private Rigidbody2D body;
    private Transform vehicle;
    private float startX, furthest, nextSearch, nextPaint, nextSave;
    private int best, baselineBest, shown = -1, paintedBest = -1, previous, next;
    private bool tracking, finished, manualStart, recordAnnounced;
    private Canvas canvas;
    private RectTransform safeRoot, panelRect, noticeRect, fillRect;
    private CanvasGroup panelGroup, noticeGroup;
    private TMP_Text distanceText, bestText, nextText, noticeTitle, noticeText, noticeHint;
    private Image progressFill, noticeAccent;
    private float noticeAge, noticeLife;
    private bool noticeActive;
    private Texture2D cornerTexture;
    private Sprite cornerSprite;
    private int layoutWidth, layoutHeight;
    private Rect lastSafe;
    private float lastScale;
    private Vector2 lastPanelOffset;
    private float lastNoticeOffset;

    public int DistanceMetres { get; private set; }
    public int BestMetres => best;
    public bool IsTracking => tracking;
    // A large jump emits the highest reached milestone once, not every skipped threshold.
    public event Action<int> MilestoneReached;

    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        if (!canvas.isRootCanvas)
        {
            Debug.LogError("Distance HUD: put this component on its own top-level GameObject, outside other Canvases.", this);
            enabled = false;
            return;
        }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        best = Mathf.Max(0, PlayerPrefs.GetInt(BestDistanceKey, 0));
        MakeCorners();
        BuildUI();
        HideUI();
    }

    private void Start()
    {
        // Cache after VehicleSelector.Awake has selected and frozen the car.
        candidates = FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (gameOverPanel == null)
            Debug.LogWarning("Distance HUD: assign Game Over Panel so it can distinguish game over from a pause.", this);
    }

    // Optional START-button hook. Defers until Driver is enabled; button-listener order is safe.
    public void BeginRun()
    {
        if (tracking) return;
        manualStart = true;
        finished = false;
    }

    private bool GameOverVisible => gameOverPanel != null && gameOverPanel.activeInHierarchy;
    private bool MenuVisible => startPanel != null && startPanel.activeInHierarchy;

    private Driver ResolveDriver()
    {
        if (playerOverride != null)
        {
            if (driver == null || driver.transform != playerOverride) driver = playerOverride.GetComponent<Driver>();
            return driver != null && driver.isActiveAndEnabled ? driver : null;
        }
        if (driver != null && driver.isActiveAndEnabled) return driver;
        if (candidates != null)
            foreach (Driver candidate in candidates)
                if (candidate != null && candidate.isActiveAndEnabled) return candidate;
        if (Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 1f;
            candidates = FindObjectsByType<Driver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
        return null;
    }

    private void StartTracking(Driver selected)
    {
        driver = selected; vehicle = selected.transform; body = selected.GetComponent<Rigidbody2D>();
        startX = vehicle.position.x;
        furthest = 0f; DistanceMetres = 0; shown = -1; paintedBest = -1;
        baselineBest = best = Mathf.Max(best, PlayerPrefs.GetInt(BestDistanceKey, 0));
        previous = 0;
        CGGMilestoneSchedule.Evaluate(0, firstMilestone, secondMilestone, thirdMilestone,
            milestoneStepAfterThird, out _, out next);
        tracking = true; finished = false; manualStart = false; recordAnnounced = false;
        noticeActive = false; noticeGroup.alpha = 0f;
        nextPaint = 0f; nextSave = Time.unscaledTime + Mathf.Max(5f, bestSaveInterval);
        panelGroup.alpha = displayMode == DisplayMode.DistanceAndMilestones ? 1f : 0f;
        Paint();
    }

    private void Update()
    {
        if (safeRoot == null) return;
        LayoutIfNeeded();
        if (MenuVisible)
        {
            if (tracking) EndRun();
            finished = false;
            HideUI();
            return;
        }
        if (GameOverVisible)
        {
            if (tracking) { SampleDistance(); EndRun(); }
            HideUI();
            return;
        }
        if (!tracking)
        {
            if (finished && !manualStart) return;
            if (Time.timeScale <= 0f) return;
            Driver selected = ResolveDriver();
            if (selected == null) return;
            Rigidbody2D selectedBody = selected.GetComponent<Rigidbody2D>();
            if (selectedBody == null || !selectedBody.simulated) return;
            StartTracking(selected);
        }
        if (vehicle == null || !vehicle.gameObject.activeInHierarchy || driver == null || !driver.isActiveAndEnabled)
        {
            EndRun();
            return;
        }
        // Pause neither advances distance nor consumes a milestone celebration.
        if (Time.timeScale <= 0f || body == null || !body.simulated) return;
        SampleDistance();
        int reached;
        CGGMilestoneSchedule.Evaluate(DistanceMetres, firstMilestone, secondMilestone, thirdMilestone,
            milestoneStepAfterThird, out reached, out next);
        bool newRecord = announceNewBest && !recordAnnounced && baselineBest > 0 && DistanceMetres > baselineBest;
        if (newRecord) recordAnnounced = true;
        if (reached > previous)
        {
            previous = reached;
            ShowNotice(newRecord ? "MILESTONE + NEW BEST" : "DISTANCE MILESTONE", reached, "Keep climbing!", newRecord);
            MilestoneReached?.Invoke(reached);
        }
        else if (newRecord)
            ShowNotice("NEW PERSONAL BEST", DistanceMetres, "Previous best: " + baselineBest + " m", true);
        if (Time.unscaledTime >= nextPaint)
        {
            nextPaint = Time.unscaledTime + 1f / Mathf.Clamp(uiUpdatesPerSecond, 5f, 30f);
            Paint();
        }
        AnimateNotice(Time.unscaledDeltaTime);
        if (Time.unscaledTime >= nextSave)
        {
            PersistBest();
            nextSave = Time.unscaledTime + Mathf.Max(5f, bestSaveInterval);
        }
    }

    private void SampleDistance()
    {
        if (vehicle == null) return;
        float displacement = vehicle.position.x - startX;
        if (float.IsNaN(displacement) || float.IsInfinity(displacement)) return;
        furthest = Mathf.Max(furthest, displacement);
        DistanceMetres = furthest >= int.MaxValue ? int.MaxValue : Mathf.Max(0, Mathf.FloorToInt(furthest));
        best = Mathf.Max(best, DistanceMetres);
    }

    private void Paint()
    {
        panelGroup.alpha = tracking && displayMode == DisplayMode.DistanceAndMilestones ? 1f : 0f;
        if (panelGroup.alpha == 0f) return;
        if (shown != DistanceMetres)
        {
            distanceText.SetText("{0:0}<size=18> m</size>", DistanceMetres);
            shown = DistanceMetres;
        }
        if (paintedBest != best) { bestText.SetText("BEST  {0:0} m", best); paintedBest = best; }
        if (next == int.MaxValue && DistanceMetres == int.MaxValue) nextText.text = "ALL MILESTONES REACHED";
        else nextText.SetText("NEXT {0:0} m  /  {1:0} m TO GO", next, Mathf.Max(0f, (float)next - DistanceMetres));
        float fraction = Mathf.Clamp01((furthest - previous) / Mathf.Max(1f, (float)next - previous));
        fillRect.anchorMax = new Vector2(fraction, 1f);
    }

    private void ShowNotice(string title, int metres, string hint, bool record)
    {
        noticeTitle.text = title;
        noticeText.SetText("{0:0}<size=18> m</size>", metres);
        noticeHint.text = hint;
        noticeAccent.color = record ? new Color32(151, 218, 149, 255) : new Color32(246, 191, 65, 255);
        noticeAge = 0f; noticeLife = Mathf.Max(0.7f, noticeDuration); noticeActive = true;
        // At most one notice exists. A later milestone replaces a stale earlier one.
    }

    private void AnimateNotice(float dt)
    {
        if (!noticeActive) return;
        noticeAge += dt;
        float fade = Mathf.Min(Mathf.Clamp01(noticeAge / 0.2f), Mathf.Clamp01((noticeLife - noticeAge) / 0.3f));
        noticeGroup.alpha = fade;
        noticeRect.anchoredPosition = new Vector2(0f, noticeBottomOffset - (1f - fade) * 10f);
        if (noticeAge >= noticeLife) { noticeActive = false; noticeGroup.alpha = 0f; }
    }

    public void EndRun()
    {
        if (tracking) SampleDistance();
        PersistBest(); tracking = false; finished = true; manualStart = false;
        HideUI();
    }

    private void PersistBest()
    {
        // Another HUD may share this key. Never overwrite its higher record.
        int stored = Mathf.Max(0, PlayerPrefs.GetInt(BestDistanceKey, 0));
        best = Mathf.Max(best, stored);
        if (best <= stored) return;
        PlayerPrefs.SetInt(BestDistanceKey, best);
        PlayerPrefs.Save();
    }

    private void HideUI()
    {
        if (panelGroup != null) panelGroup.alpha = 0f;
        if (noticeGroup != null) noticeGroup.alpha = 0f;
        noticeActive = false;
    }

    private void OnDisable()
    {
        if (tracking) { SampleDistance(); PersistBest(); }
        HideUI(); // Re-enable resumes the same run instead of resetting its origin.
    }
    private void OnApplicationPause(bool paused)
    {
        if (paused && tracking) { SampleDistance(); PersistBest(); }
    }
    private void OnApplicationQuit() { if (tracking) { SampleDistance(); PersistBest(); } }
    private void OnDestroy()
    {
        if (safeRoot != null) Destroy(safeRoot.gameObject);
        if (cornerSprite != null) Destroy(cornerSprite);
        if (cornerTexture != null) Destroy(cornerTexture);
    }

    private void LayoutIfNeeded()
    {
        Rect safe = respectSafeArea ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);
        if (layoutWidth == Screen.width && layoutHeight == Screen.height && lastSafe == safe &&
            lastScale == uiScale && lastPanelOffset == panelOffset && lastNoticeOffset == noticeBottomOffset) return;
        layoutWidth = Screen.width; layoutHeight = Screen.height; lastSafe = safe;
        lastScale = uiScale; lastPanelOffset = panelOffset; lastNoticeOffset = noticeBottomOffset;
        safeRoot.anchorMin = new Vector2(safe.xMin / Mathf.Max(1, Screen.width), safe.yMin / Mathf.Max(1, Screen.height));
        safeRoot.anchorMax = new Vector2(safe.xMax / Mathf.Max(1, Screen.width), safe.yMax / Mathf.Max(1, Screen.height));
        safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        float available = safe.width / Mathf.Max(0.01f, canvas.scaleFactor);
        float scale = Mathf.Min(uiScale, Mathf.Max(0.2f, (available - 24f) / 320f));
        panelRect.localScale = noticeRect.localScale = Vector3.one * scale;
        panelRect.anchoredPosition = new Vector2(Mathf.Max(0f, panelOffset.x), -Mathf.Max(0f, panelOffset.y));
        noticeRect.anchoredPosition = new Vector2(0f, noticeBottomOffset);
    }

    private void BuildUI()
    {
        safeRoot = new GameObject("Distance HUD Safe Area", typeof(RectTransform)).GetComponent<RectTransform>();
        safeRoot.SetParent(transform, false);
        panelRect = Card("Distance Card", new Vector2(0f, 1f), new Vector2(286f, 112f), out panelGroup);
        Text("Label", panelRect, "DISTANCE", 13f, new Rect(16, 10, 120, 19));
        distanceText = Text("Metres", panelRect, "0 m", 32f, new Rect(16, 30, 148, 38));
        bestText = Text("Best", panelRect, "", 14f, new Rect(166, 37, 105, 24));
        bestText.color = new Color32(246, 191, 65, 255);
        Image track = Box("Progress", panelRect, new Color32(61, 79, 72, 255));
        At(track.rectTransform, new Rect(16, 77, 254, 5));
        progressFill = Box("Fill", track.transform, new Color32(246, 191, 65, 255));
        fillRect = progressFill.rectTransform;
        fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = new Vector2(0, 1);
        fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
        nextText = Text("Next", panelRect, "", 12f, new Rect(16, 87, 254, 19));
        noticeRect = Card("Milestone Toast", new Vector2(0.5f, 0f), new Vector2(320f, 96f), out noticeGroup);
        noticeAccent = Box("Accent", noticeRect, new Color32(246, 191, 65, 255));
        At(noticeAccent.rectTransform, new Rect(12, 15, 4, 64));
        noticeTitle = Text("Title", noticeRect, "", 13f, new Rect(30, 10, 274, 20));
        noticeText = Text("Achievement", noticeRect, "", 30f, new Rect(30, 30, 274, 37));
        noticeHint = Text("Hint", noticeRect, "", 12f, new Rect(30, 69, 274, 17));
    }

    private RectTransform Card(string name, Vector2 anchor, Vector2 size, out CanvasGroup group)
    {
        Image image = Box(name, safeRoot, new Color32(27, 42, 42, 244));
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.sizeDelta = size;
        group = image.gameObject.AddComponent<CanvasGroup>();
        group.interactable = false; group.blocksRaycasts = false;
        return rect;
    }
    private Image Box(string name, Transform parent, Color colour)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.sprite = cornerSprite; image.type = Image.Type.Sliced;
        image.color = colour; image.raycastTarget = false;
        return image;
    }
    private TMP_Text Text(string name, Transform parent, string caption, float size, Rect bounds)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        TMP_Text text = obj.GetComponent<TMP_Text>();
        At(text.rectTransform, bounds);
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.text = caption; text.fontSize = size; text.fontSizeMin = 11; text.fontSizeMax = size;
        text.enableAutoSizing = true; text.fontStyle = FontStyles.Bold;
        text.color = new Color32(240, 240, 222, 255);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }
    private static void At(RectTransform rect, Rect bounds)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(bounds.x, -bounds.y); rect.sizeDelta = bounds.size;
    }
    private void MakeCorners()
    {
        cornerTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        { name = "Distance HUD Corners", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[1024];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float dx = Mathf.Max(0, Mathf.Abs(x - 15.5f) - 5.5f), dy = Mathf.Max(0, Mathf.Abs(y - 15.5f) - 5.5f);
            pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Clamp01(10.5f - Mathf.Sqrt(dx * dx + dy * dy)));
        }
        cornerTexture.SetPixels(pixels); cornerTexture.Apply(false, true);
        cornerSprite = Sprite.Create(cornerTexture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f,
            100, 0, SpriteMeshType.FullRect, Vector4.one * 11f);
    }
}

// Pure integer arithmetic, testable outside Unity. No per-crossing loop or queue.
internal static class CGGMilestoneSchedule
{
    public static void Evaluate(int metres, int first, int second, int third, int step,
        out int previous, out int next)
    {
        long a = Math.Max(1L, first);
        long b = Math.Max(a + 1L, second);
        long c = Math.Max(b + 1L, third);
        long stride = Math.Max(1L, step);
        long d = Math.Max(0L, metres);
        long p, n;
        if (d < a) { p = 0; n = a; }
        else if (d < b) { p = a; n = b; }
        else if (d < c) { p = b; n = c; }
        else { p = c + ((d - c) / stride) * stride; n = p + stride; }
        previous = (int)Math.Min(int.MaxValue, p);
        next = (int)Math.Min(int.MaxValue, n);
    }
}
