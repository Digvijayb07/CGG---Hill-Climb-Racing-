
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EndlessDistanceHUD : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform car;
    [SerializeField] private TMP_Text distanceText;
    [SerializeField] private RectTransform rulerViewport;

    [Header("Ruler Appearance")]
    [SerializeField] private int tickCount = 14;
    [SerializeField] private float tickSpacing = 38f;
    [SerializeField] private float pixelsPerUnit = 5f;

    private RectTransform[] ticks;
    private float startingX;
    private float maxDistance;
    private bool raceStarted = false;
    private int displayedMetres = -1;
    private float displayedScroll = float.NaN;

    private void Awake()
    {
        if (rulerViewport == null)
        {
            Debug.LogError("Assign RulerViewport!");
            return;
        }

        tickCount = Mathf.Max(2, tickCount);
        ticks = new RectTransform[tickCount];

        for (int i = 0; i < tickCount; i++)
        {
            GameObject tick = new GameObject(
                "RulerTick_" + i,
                typeof(RectTransform),
                typeof(Image)
            );

            tick.transform.SetParent(rulerViewport, false);

            RectTransform rect =
                tick.GetComponent<RectTransform>();

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);

            rect.sizeDelta = new Vector2(30f, 4f);

            Image image = tick.GetComponent<Image>();
            image.color = new Color(0.35f, 0.45f, 0.5f);
            image.raycastTarget = false;

            ticks[i] = rect;
        }

        UpdateDisplay();
    }

    public void BeginRun()
    {
        if (car == null)
        {
            Debug.LogError("Assign Vehicle_Car!");
            return;
        }

        startingX = car.position.x;
        maxDistance = 0f;
        raceStarted = true;
        UpdateDisplay();
    }

    private void Update()
    {
        if (!raceStarted || car == null || Time.timeScale <= 0f)
            return;

        float travelled = car.position.x - startingX;

        // Keep the highest forward distance reached.
        maxDistance = Mathf.Max(maxDistance, travelled);

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        int metres = Mathf.FloorToInt(maxDistance);
        if (distanceText != null && displayedMetres != metres)
        {
            // Preserve the plain "123m" format read by GameOverStatsUI.
            distanceText.SetText("{0:0}m", metres);
            displayedMetres = metres;
        }

        if (ticks == null)
            return;

        float totalWidth = Mathf.Max(1f, tickSpacing) * ticks.Length;
        float scroll = maxDistance * pixelsPerUnit;
        if (scroll == displayedScroll) return;
        displayedScroll = scroll;

        for (int i = 0; i < ticks.Length; i++)
        {
            float x = Mathf.Repeat(
                i * tickSpacing - scroll + totalWidth / 2f,
                totalWidth
            ) - totalWidth / 2f;

            Vector2 position = new Vector2(x, 0f);
            if (ticks[i].anchoredPosition != position) ticks[i].anchoredPosition = position;
        }
    }
}
