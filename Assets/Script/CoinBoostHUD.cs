using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Illustrated, non-interactive boost HUD. Attach to your existing UI object.
/// Creates a separate overlay Canvas automatically; does not change gameplay.
/// READY: BoostBadge PNG. ACTIVE/COOLDOWN/LOW COINS: live text & progress.
/// </summary>
[DisallowMultipleComponent]
public class CoinBoostHUD : MonoBehaviour
{
    [Header("Optional: auto-finds the active car if empty")]
    [SerializeField] private CoinBoost boost;

    [Header("Ready artwork (auto-loads Resources/CGGUI/BoostBadge if empty)")]
    [SerializeField] private Sprite readyArtwork;

    [Header("Placement on 1280 x 720 UI")]
    [SerializeField] private Vector2 topRightOffset = new Vector2(-24f, -126f);
    [SerializeField, Range(3f, 15f)] private float fadeSpeed = 9f;

    private enum HudMode { Hidden, Ready, Active, Cooldown, NeedCoins, Notice }
    private HudMode mode = HudMode.Hidden;
    private CoinBoost legacyOwner;
    private bool legacyWasVisible;
    private float nextTargetCheck;
    private float nextStatusRefresh;

    private Canvas canvas;
    private CanvasGroup rootGroup;
    private CanvasGroup artworkGroup;
    private CanvasGroup statusGroup;
    private Image artworkImage;
    private TextMeshProUGUI heading;
    private TextMeshProUGUI subheading;
    private Image barBack;
    private Image barFill;
    private Color activeAccent = new Color(1f, 0.72f, 0.11f, 1f);
    private Color cooldownAccent = new Color(0.15f, 0.84f, 1f, 1f);

    private void Start()
    {
        if (readyArtwork == null)
            readyArtwork = Resources.Load<Sprite>("CGGUI/BoostBadge");
        BuildUI();
        ResolveBoost();
    }

    private void OnEnable()
    {
        if (canvas != null) canvas.enabled = true;
        if (boost != null && legacyOwner == null) TakeOverLegacyHud();
    }

    private void OnDisable()
    {
        if (canvas != null) canvas.enabled = false;
        RestoreLegacyHud();
    }

    private void RestoreLegacyHud()
    {
        if (legacyOwner != null)
            legacyOwner.SetLegacyHUDVisible(legacyWasVisible);
        legacyOwner = null;
    }

    private void TakeOverLegacyHud()
    {
        if (boost == null) return;
        if (legacyOwner == boost) return;
        RestoreLegacyHud();
        legacyOwner = boost;
        legacyWasVisible = boost.LegacyHUDVisible;
        boost.SetLegacyHUDVisible(false);
    }

    private void ResolveBoost()
    {
        // Explicit reference wins while its vehicle is active. If a different
        // vehicle is selected, locate that one's CoinBoost automatically.
        if (boost != null && boost.gameObject.activeInHierarchy)
        {
            TakeOverLegacyHud();
            return;
        }

        if (Time.unscaledTime < nextTargetCheck) return;
        nextTargetCheck = Time.unscaledTime + 0.4f;
        CoinBoost[] candidates = FindObjectsByType<CoinBoost>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (CoinBoost candidate in candidates)
        {
            if (candidate != null && candidate.gameObject.activeInHierarchy)
            {
                boost = candidate;
                TakeOverLegacyHud();
                return;
            }
        }
        RestoreLegacyHud();
        boost = null;
    }

    private void Update()
    {
        if (rootGroup == null) return;
        ResolveBoost();
        bool shouldShow = boost != null && boost.CanDisplayHUD;
        rootGroup.alpha = Mathf.MoveTowards(rootGroup.alpha,
            shouldShow ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        if (!shouldShow) { mode = HudMode.Hidden; nextStatusRefresh = 0f; return; }

        // Fade every frame, but format text/progress only ten times per second.
        bool showArt = mode == HudMode.Ready && readyArtwork != null && boost.BoostCost == 25;
        artworkGroup.alpha = Mathf.MoveTowards(artworkGroup.alpha,
            showArt ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);
        statusGroup.alpha = 1f - artworkGroup.alpha;
        if (Time.unscaledTime < nextStatusRefresh) return;
        nextStatusRefresh = Time.unscaledTime + 0.1f;

        float amount = 0f;
        bool barVisible = false;
        Color accent = cooldownAccent;
        string title = "";
        string detail = "";
        HudMode nextMode;

        if (boost.IsBoosting)
        {
            nextMode = HudMode.Active;
            title = "BOOST ACTIVE";
            detail = boost.BoostTimeRemaining.ToString("0.0") + "s REMAINING";
            amount = boost.BoostDuration <= 0f ? 0f :
                Mathf.Clamp01(boost.BoostTimeRemaining / boost.BoostDuration);
            barVisible = true;
            accent = activeAccent;
        }
        else if (boost.CooldownTimeRemaining > 0f)
        {
            nextMode = HudMode.Cooldown;
            title = "BOOST READY IN " + boost.CooldownTimeRemaining.ToString("0.0") + "s";
            detail = "RECHARGING";
            amount = boost.CooldownDuration <= 0f ? 1f :
                Mathf.Clamp01(1f - boost.CooldownTimeRemaining / boost.CooldownDuration);
            barVisible = true;
        }
        else if (!string.IsNullOrEmpty(boost.CurrentNotice))
        {
            nextMode = HudMode.Notice;
            title = boost.CurrentNotice;
            detail = "[B] TO BOOST WHEN READY";
        }
        else
        {
            int balance = ScoreManager.instance == null ? 0 : ScoreManager.instance.GetScore();
            if (balance < boost.BoostCost)
            {
                nextMode = HudMode.NeedCoins;
                title = "NEED " + (boost.BoostCost - balance) + " MORE COINS";
                detail = "BOOST COST: " + boost.BoostCost;
            }
            else
            {
                nextMode = HudMode.Ready;
                // The PNG has a fixed -25 label; use live text if the cost was customized.
                title = "[B] BOOST -" + boost.BoostCost + " COINS";
                detail = "READY TO USE";
            }
        }

        mode = nextMode;
        heading.text = title;
        subheading.text = detail;
        heading.color = mode == HudMode.Active ? activeAccent :
            mode == HudMode.Cooldown ? cooldownAccent : Color.white;
        barFill.color = accent;
        barFill.fillAmount = amount;
        barBack.enabled = barVisible;
        barFill.enabled = barVisible;
    }

    private void OnDestroy()
    {
        RestoreLegacyHud();
        if (canvas != null) Destroy(canvas.gameObject);
    }

    private void BuildUI()
    {
        // Scene-owned root. No DontDestroyOnLoad or creation in OnDestroy:
        // Unity removes it normally on scene reload / Play Mode exit.
        GameObject canvasObject = new GameObject("CGG_BoostHUD_Runtime",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        rootGroup = canvasObject.GetComponent<CanvasGroup>();
        rootGroup.alpha = 0f;
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;

        RectTransform holder = MakeRect("BoostHolder", canvasObject.transform);
        holder.anchorMin = holder.anchorMax = holder.pivot = Vector2.one;
        holder.anchoredPosition = topRightOffset;
        holder.sizeDelta = new Vector2(360f, 120f);

        RectTransform ready = MakeRect("ReadyArtwork", holder);
        Stretch(ready);
        artworkImage = ready.gameObject.AddComponent<Image>();
        artworkImage.sprite = readyArtwork;
        artworkImage.preserveAspect = true;
        artworkImage.raycastTarget = false;
        artworkGroup = ready.gameObject.AddComponent<CanvasGroup>();
        artworkGroup.alpha = 0f;

        RectTransform status = MakeRect("DynamicStatus", holder);
        Stretch(status);
        statusGroup = status.gameObject.AddComponent<CanvasGroup>();
        statusGroup.alpha = 1f;
        Image bg = status.gameObject.AddComponent<Image>();
        bg.color = new Color(0.025f, 0.13f, 0.23f, 0.96f);
        bg.raycastTarget = false;
        Outline outline = status.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0.02f, 0.06f, 0.9f);
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = true;

        RectTransform stripe = MakeRect("TopAccent", status);
        stripe.anchorMin = new Vector2(0f, 1f);
        stripe.anchorMax = new Vector2(1f, 1f);
        stripe.pivot = new Vector2(0.5f, 1f);
        stripe.anchoredPosition = Vector2.zero;
        stripe.sizeDelta = new Vector2(0f, 7f);
        Image stripeImage = stripe.gameObject.AddComponent<Image>();
        stripeImage.color = cooldownAccent;
        stripeImage.raycastTarget = false;

        heading = MakeText("LiveHeading", status, new Vector2(0f, -36f),
            new Vector2(340f, 40f), 28f, FontStyles.Bold);
        subheading = MakeText("LiveSubheading", status, new Vector2(0f, -76f),
            new Vector2(340f, 29f), 19f, FontStyles.Bold);
        subheading.color = new Color(0.85f, 0.94f, 1f, 1f);

        RectTransform track = MakeRect("ProgressTrack", status);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(1f, 0f);
        track.pivot = new Vector2(0.5f, 0f);
        track.anchoredPosition = new Vector2(0f, 10f);
        track.sizeDelta = new Vector2(-28f, 9f);
        barBack = track.gameObject.AddComponent<Image>();
        barBack.color = new Color(0.14f, 0.30f, 0.39f, 1f);
        barBack.raycastTarget = false;
        RectTransform fill = MakeRect("ProgressFill", track);
        Stretch(fill);
        barFill = fill.gameObject.AddComponent<Image>();
        barFill.type = Image.Type.Filled;
        barFill.fillMethod = Image.FillMethod.Horizontal;
        barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        barFill.fillAmount = 0f;
        barFill.raycastTarget = false;
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI MakeText(string name, Transform parent,
        Vector2 position, Vector2 size, float fontSize, FontStyles style)
    {
        RectTransform rt = MakeRect(name, parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        TextMeshProUGUI label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 17f;
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;
        label.text = "";
        return label;
    }
}
