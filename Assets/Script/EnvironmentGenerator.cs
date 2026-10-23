using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

// File 1: terrain and repair logic. Visuals and impact feedback are separate.
// Compatible with the public calls in the existing PotholePopupUI.
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteShapeController), typeof(EdgeCollider2D))]
public class EnvironmentGenerator : MonoBehaviour
{
    [Header("Original Terrain Appearance")]
    [SerializeField] private SpriteShapeController _spriteShapeController;
    [SerializeField, Range(3, 100)] private int levelLength = 50;
    [SerializeField, Range(1f, 50f)] private float xMultiplier = 2f;
    [SerializeField, Range(1f, 50f)] private float yMultiplier = 2f;
    [SerializeField, Range(0f, 1f)] private float curveSmoothness = 0.5f;
    [SerializeField] private float noiseStep = 0.5f;
    [SerializeField] private float bottom = 10f;

    [Header("Natural Hill Slope Safety")]
    [SerializeField] private bool enableNaturalSlopeSafety = true;
    [SerializeField, Range(0.12f, 0.4f)] private float maxNaturalRisePerWorldUnit = 0.25f;

    [Header("Endless Terrain")]
    [SerializeField] private Transform player;
    [SerializeField, Min(6)] private int pointsAhead = 18;
    [SerializeField, Min(6)] private int pointsBehind = 22;
    [SerializeField, Min(1)] private int rebuildInterval = 4;

    [Header("Localized Potholes")]
    [SerializeField] private bool enablePotholes = false;
    [SerializeField, Min(8)] private int firstPotholeAfterPoints = 14;
    [SerializeField, Min(12)] private int potholeEveryPoints = 22;
    // New names deliberately avoid inheriting the old 1.6-unit dip depth.
    [SerializeField, Range(2f, 6f)] private float potholeWidth = 3.5f;
    [SerializeField, Range(0.2f, 1.2f)] private float potholeDepth = 0.6f;
    [Tooltip("Fixed world-space sampling. The same samples remain after repair.")]
    [SerializeField, Range(0.15f, 0.5f)] private float surfaceSampleSpacing = 0.25f;
    [Tooltip("Off: all holes use the medium size and price. On: small/medium/large cycle.")]
    [SerializeField] private bool varyPotholeSizes = false;
    [SerializeField, Min(1)] private int repairCost = 50;

    [Header("Approach And Repair")]
    [Tooltip("Minimum warning distance, measured from the front of the car.")]
    [SerializeField, Min(8f)] private float warningDistance = 20f;
    [SerializeField, Min(1f)] private float warningLeadSeconds = 3f;
    [Tooltip("Repair is refused inside this distance. Minimum is four world units.")]
    [SerializeField, Min(4f)] private float repairCutoffDistance = 4f;
    [SerializeField] private bool showFallbackHUD = true;
    [SerializeField] private bool enableAnimatedRepair = true;
    [SerializeField, Range(0.2f, 1.2f)] private float repairAnimationSeconds = 0.6f;

    [Header("Optional Surface Artwork - Configure After Importing New Art")]
    [Tooltip("Enable only with a profile containing grass, bare dirt and gravel variants.")]
    [SerializeField] private bool useSurfaceVariants = false;
    [SerializeField, Min(0)] private int grassSpriteIndex = 0;
    [SerializeField, Min(0)] private int dirtSpriteIndex = 1;
    [SerializeField, Min(0)] private int gravelSpriteIndex = 2;

    [Header("Adaptive Spacing")]
    [SerializeField] private bool enableAdaptiveDifficulty = false;
    [SerializeField, Min(0f)] private float adaptiveStartAfterDistance = 200f;
    [SerializeField, Min(20f)] private float distancePerDifficultyStep = 175f;
    [SerializeField, Range(1, 5)] private int spacingReductionPerStep = 2;
    [SerializeField, Min(12)] private int minimumPotholeSpacing = 14;

    public sealed class Pothole
    {
        public int Id { get; internal set; }
        public float CenterX { get; internal set; }
        public float Width { get; internal set; }
        public float Depth { get; internal set; }
        public int Cost { get; internal set; }
        public bool IsRepaired { get; internal set; }
        public bool IsSkipped { get; internal set; }
        public float RepairedAt { get; internal set; }
        public float LeftX => CenterX - Width * 0.5f;
        public float RightX => CenterX + Width * 0.5f;
    }

    private readonly List<Pothole> potholes = new List<Pothole>();
    private readonly List<float> naturalHeights = new List<float>();
    private Driver driver;
    private Rigidbody2D carBody;
    private Collider2D[] carColliders;
    private EdgeCollider2D groundCollider;
    private Pothole pending;
    private int firstPointIndex, lastPointIndex, nextCandidateIndex, nextId;
    private int subdivisions;
    private float startingPlayerX, noticeUntil;
    private string notice = "";
    private bool ready, customPopupEnabled, variantsReady;

    // Presentation scripts subscribe to these; no dependency on future files.
    public event Action TerrainChanged;
    public event Action<Pothole> PotholeRepaired;
    public IReadOnlyList<Pothole> Potholes => potholes;
    public Transform Player => player;
    public EdgeCollider2D GroundCollider => groundCollider;
    public SpriteShapeController ShapeController => _spriteShapeController;
    public bool IsReady => ready;
    public bool PotholesEnabled => enablePotholes;
    public float RepairVisualDuration => enableAnimatedRepair ? repairAnimationSeconds : 0f;
    public bool IsRunActive => ready && isActiveAndEnabled && Time.timeScale > 0f &&
        driver != null && driver.isActiveAndEnabled && carBody != null && carBody.simulated;
    public bool HasPendingPothole => IsRunActive && pending != null;
    public int PendingRepairCost => pending != null ? pending.Cost : 0;
    public int PendingCoinBalance => ScoreManager.instance != null ? ScoreManager.instance.GetScore() : 0;
    public float PendingDistance => pending != null ? Mathf.Max(0f, pending.LeftX - VehicleFrontX()) : 0f;
    public string CurrentNotice => Time.unscaledTime < noticeUntil ? notice : "";

    private float WorldPointSpacing => xMultiplier * transform.lossyScale.x;
    private float ForwardSpeed => carBody != null ? Mathf.Max(0f, carBody.linearVelocity.x) : 0f;
    private float RepairClearance => Mathf.Max(4f, repairCutoffDistance) + ForwardSpeed * Time.fixedDeltaTime * 2f;

    private void Start()
    {
        if (_spriteShapeController == null)
            _spriteShapeController = GetComponent<SpriteShapeController>();
        groundCollider = GetComponent<EdgeCollider2D>();
        if (player == null || _spriteShapeController == null ||
            _spriteShapeController.gameObject != gameObject || _spriteShapeController.spriteShape == null)
        {
            Debug.LogError("Ground: assign Player, this object's Sprite Shape Controller, and its profile.", this);
            enabled = false;
            return;
        }
        // Current project uses an axis-aligned, positively scaled Ground.
        // Reject unsupported transforms rather than silently placing holes incorrectly.
        if (transform.lossyScale.x <= 0f || transform.lossyScale.y <= 0f ||
            Vector3.Dot(transform.right, Vector3.right) < 0.9999f ||
            Vector3.Dot(transform.up, Vector3.up) < 0.9999f)
        {
            Debug.LogError("Ground must have positive X/Y scale and no rotation for this generator.", this);
            enabled = false;
            return;
        }
        driver = player.GetComponent<Driver>();
        carBody = player.GetComponent<Rigidbody2D>();
        carColliders = player.GetComponentsInChildren<Collider2D>(true);
        if (enablePotholes && (driver == null || carBody == null))
        {
            Debug.LogError("Assign the vehicle ROOT containing Driver and Rigidbody2D as Player.", this);
            enabled = false;
            return;
        }
        xMultiplier = Mathf.Max(0.1f, xMultiplier);
        subdivisions = Mathf.Max(2, Mathf.CeilToInt(WorldPointSpacing /
            Mathf.Clamp(surfaceSampleSpacing, 0.15f, 0.5f)));
        startingPlayerX = player.position.x;
        firstPointIndex = 0;
        int playerIndex = PlayerPointIndex();
        lastPointIndex = Mathf.Max(levelLength - 1, playerIndex + pointsAhead + rebuildInterval);
        nextCandidateIndex = Mathf.Max(3, playerIndex + Mathf.Max(8, firstPotholeAfterPoints));
        ValidateSurfaceVariants();
        if (enablePotholes) GenerateUpcomingPotholes();
        RebuildTerrain();
        ready = true;
        TerrainChanged?.Invoke();
    }

    private void Update()
    {
        if (!ready || player == null || Time.timeScale <= 0f) return;
        StreamTerrain();
        if (!enablePotholes || !IsRunActive) return;
        UpdateApproach();
        if (pending == null) return;
        if (Input.GetKeyDown(KeyCode.R)) ChooseRepair();
        else if (Input.GetKeyDown(KeyCode.S)) ChooseSkip();
    }

    private void StreamTerrain()
    {
        int playerIndex = PlayerPointIndex();
        int oldFirst = firstPointIndex, oldLast = lastPointIndex;
        if (playerIndex + pointsAhead >= lastPointIndex - rebuildInterval)
            lastPointIndex = Mathf.Max(lastPointIndex, playerIndex + pointsAhead + rebuildInterval);
        int desiredFirst = Mathf.Max(0, playerIndex - pointsBehind);
        if (desiredFirst >= firstPointIndex + rebuildInterval)
            firstPointIndex = desiredFirst;
        if (oldFirst == firstPointIndex && oldLast == lastPointIndex) return;

        if (enablePotholes)
        {
            GenerateUpcomingPotholes();
            float firstX = PointWorldX(firstPointIndex);
            for (int i = potholes.Count - 1; i >= 0; i--)
            {
                if (potholes[i].RightX >= firstX - 2f) continue;
                if (pending == potholes[i]) pending = null;
                potholes.RemoveAt(i);
            }
        }
        RebuildTerrain();
        TerrainChanged?.Invoke();
    }

    private int PlayerPointIndex()
    {
        float localX = transform.InverseTransformPoint(player.position).x;
        return Mathf.Max(0, Mathf.FloorToInt((localX - transform.position.x) / xMultiplier));
    }

    private float RawHeight(int index) => Mathf.PerlinNoise(0f, index * noiseStep) * yMultiplier;

    private float NaturalHeight(int index)
    {
        if (!enablePotholes || !enableNaturalSlopeSafety) return RawHeight(index);
        if (naturalHeights.Count == 0) naturalHeights.Add(RawHeight(0));
        float maxRise = WorldPointSpacing * maxNaturalRisePerWorldUnit / transform.lossyScale.y;
        while (naturalHeights.Count <= index)
        {
            int next = naturalHeights.Count;
            naturalHeights.Add(Mathf.MoveTowards(naturalHeights[next - 1], RawHeight(next), maxRise));
        }
        return naturalHeights[index];
    }

    // Preserve the existing scene's terrain origin convention.
    private Vector3 OriginalLocalPoint(int index) => transform.position +
        new Vector3(xMultiplier * index, NaturalHeight(index), 0f);

    private float PointWorldX(int index) => transform.TransformPoint(
        new Vector3(transform.position.x + xMultiplier * index, 0f, 0f)).x;

    // Evaluate the original horizontal-tangent Bezier curve at a known X.
    // Invert Bezier X so changing curveSmoothness does not move the sample grid.
    private Vector3 NaturalLocalPoint(float coordinate)
    {
        coordinate = Mathf.Max(0f, coordinate);
        int index = Mathf.FloorToInt(coordinate);
        float fraction = coordinate - index;
        float lo = 0f, hi = 1f, c = Mathf.Clamp01(curveSmoothness);
        for (int i = 0; i < 16; i++)
        {
            float t = (lo + hi) * 0.5f, u = 1f - t;
            float bx = 3f * u * u * t * c + 3f * u * t * t * (1f - c) + t * t * t;
            if (bx < fraction) lo = t; else hi = t;
        }
        float parameter = fraction <= 0f ? 0f : (lo + hi) * 0.5f;
        float blend = parameter * parameter * (3f - 2f * parameter);
        float y = Mathf.Lerp(NaturalHeight(index), NaturalHeight(index + 1), blend);
        return transform.position + new Vector3(coordinate * xMultiplier, y, 0f);
    }

    private Vector3 RoadLocalPoint(float coordinate, bool includeDamage)
    {
        Vector3 point = NaturalLocalPoint(coordinate);
        if (!includeDamage) return point;
        float worldX = transform.TransformPoint(point).x;
        Pothole hole = HoleAt(worldX);
        if (hole == null || hole.IsRepaired) return point;
        float u = (worldX - hole.CenterX) / (hole.Width * 0.5f);
        float bowl = Mathf.Max(0f, 1f - u * u);
        // Both depth and slope go to zero at the hole boundaries.
        point.y -= hole.Depth * bowl * bowl / transform.lossyScale.y;
        return point;
    }

    // Future visuals query the same piecewise-linear road used by the spline.
    public Vector3 GetRoadPoint(float worldX, bool includeDamage = true)
    {
        if (subdivisions <= 0) return new Vector3(worldX, transform.position.y, transform.position.z);
        float localX = transform.InverseTransformPoint(new Vector3(worldX, transform.position.y, transform.position.z)).x;
        float coordinate = Mathf.Max(0f, (localX - transform.position.x) / xMultiplier);
        float sample = coordinate * subdivisions;
        int a = Mathf.FloorToInt(sample);
        Vector3 p0 = RoadLocalPoint(a / (float)subdivisions, includeDamage);
        Vector3 p1 = RoadLocalPoint((a + 1) / (float)subdivisions, includeDamage);
        return transform.TransformPoint(Vector3.Lerp(p0, p1, sample - a));
    }

    private Pothole HoleAt(float worldX)
    {
        foreach (Pothole hole in potholes)
            if (worldX >= hole.LeftX && worldX <= hole.RightX) return hole;
        return null;
    }

    private void GenerateUpcomingPotholes()
    {
        while (nextCandidateIndex + 6 <= lastPointIndex)
        {
            int center = FindGentleCenter(nextCandidateIndex);
            int size = varyPotholeSizes ? nextId % 3 : 1;
            float widthScale = size == 0 ? 0.85f : size == 2 ? 1.2f : 1f;
            float depthScale = size == 0 ? 0.7f : size == 2 ? 1.2f : 1f;
            float costScale = size == 0 ? 0.5f : size == 2 ? 2f : 1f;
            // Keep the floor comfortably above the terrain's bottom closure.
            float floorClearance = (NaturalHeight(center) + Mathf.Max(1f, bottom)) *
                transform.lossyScale.y - 0.3f;
            potholes.Add(new Pothole
            {
                Id = nextId++, CenterX = PointWorldX(center),
                Width = Mathf.Clamp(potholeWidth, 2f, 6f) * widthScale,
                Depth = Mathf.Min(Mathf.Clamp(potholeDepth, 0.2f, 1.2f) * depthScale,
                    Mathf.Max(0.05f, floorClearance)),
                Cost = Mathf.Max(1, Mathf.RoundToInt(repairCost * costScale))
            });
            int spacing = SpacingAt(center);
            // Enforce a physical gap even on unusually small terrain scales.
            int minimumPoints = Mathf.CeilToInt((potholeWidth * 1.2f + 8f) / WorldPointSpacing);
            nextCandidateIndex = center + Mathf.Max(spacing, minimumPoints);
        }
    }

    private int FindGentleCenter(int start)
    {
        int best = start;
        float score = float.PositiveInfinity;
        for (int i = start; i <= start + 3; i++)
        {
            float candidate = Mathf.Abs(NaturalHeight(i + 1) - NaturalHeight(i - 1)) +
                Mathf.Abs(NaturalHeight(i) - (NaturalHeight(i - 1) + NaturalHeight(i + 1)) * 0.5f);
            if (candidate < score) { score = candidate; best = i; }
        }
        return best;
    }

    private int SpacingAt(int center)
    {
        int normal = Mathf.Max(12, potholeEveryPoints);
        float distance = PointWorldX(center) - startingPlayerX;
        if (!enableAdaptiveDifficulty || distance < adaptiveStartAfterDistance) return normal;
        int stage = 1 + Mathf.FloorToInt((distance - adaptiveStartAfterDistance) /
            Mathf.Max(20f, distancePerDifficultyStep));
        return Mathf.Max(Mathf.Clamp(minimumPotholeSpacing, 12, normal),
            normal - stage * Mathf.Max(1, spacingReductionPerStep));
    }

    private void ValidateSurfaceVariants()
    {
        variantsReady = useSurfaceVariants;
        if (!variantsReady) return;
        foreach (AngleRange range in _spriteShapeController.spriteShape.angleRanges)
        {
            if (range.sprites == null || !ValidSprite(range, grassSpriteIndex) ||
                !ValidSprite(range, dirtSpriteIndex) || !ValidSprite(range, gravelSpriteIndex))
            {
                variantsReady = false;
                Debug.LogWarning("Surface variants disabled: add grass, dirt and gravel to each profile angle range.", this);
                break;
            }
        }
    }

    private static bool ValidSprite(AngleRange range, int index) =>
        index >= 0 && index < range.sprites.Count && range.sprites[index] != null;

    private int EdgeSpriteAt(float worldX)
    {
        if (!variantsReady) return 0;
        Pothole hole = HoleAt(worldX);
        return hole == null ? grassSpriteIndex : hole.IsRepaired ? gravelSpriteIndex : dirtSpriteIndex;
    }

    private void RebuildTerrain()
    {
        Spline spline = _spriteShapeController.spline;
        spline.Clear();
        spline.isOpenEnded = false;
        int count;
        if (enablePotholes)
        {
            // All rebuilds use exactly the same global sample grid.
            // Repair changes heights only inside a hole, never tangent modes.
            int firstSample = firstPointIndex * subdivisions;
            int lastSample = lastPointIndex * subdivisions;
            count = lastSample - firstSample + 1;
            for (int sample = firstSample; sample <= lastSample; sample++)
            {
                int k = sample - firstSample;
                float coordinate = sample / (float)subdivisions;
                Vector3 point = RoadLocalPoint(coordinate, true);
                spline.InsertPointAt(k, point);
                spline.SetTangentMode(k, ShapeTangentMode.Linear);
                spline.SetCorner(k, false);
                float nextX = point.x + xMultiplier / subdivisions;
                float midpointX = transform.TransformPoint(new Vector3((point.x + nextX) * 0.5f, point.y, point.z)).x;
                spline.SetSpriteIndex(k, EdgeSpriteAt(midpointX));
            }
        }
        else
        {
            // Preserve the original generator when potholes are disabled.
            count = lastPointIndex - firstPointIndex + 1;
            for (int i = firstPointIndex; i <= lastPointIndex; i++)
            {
                int k = i - firstPointIndex;
                spline.InsertPointAt(k, OriginalLocalPoint(i));
                if (k <= 0 || k >= count - 1) continue;
                spline.SetTangentMode(k, ShapeTangentMode.Continuous);
                spline.SetLeftTangent(k, Vector3.left * xMultiplier * curveSmoothness);
                spline.SetRightTangent(k, Vector3.right * xMultiplier * curveSmoothness);
            }
        }
        float bottomY = transform.position.y - Mathf.Max(1f, bottom);
        spline.InsertPointAt(count, new Vector3(OriginalLocalPoint(lastPointIndex).x, bottomY, 0f));
        spline.InsertPointAt(count + 1, new Vector3(OriginalLocalPoint(firstPointIndex).x, bottomY, 0f));
        _spriteShapeController.autoUpdateCollider = true;
        _spriteShapeController.RefreshSpriteShape();
        // Refresh alone does not generate fresh collider data immediately.
        _spriteShapeController.BakeMesh().Complete();
        _spriteShapeController.BakeCollider();
    }

    private float VehicleFrontX()
    {
        if (player == null) return 0f;
        float x = player.position.x;
        if (carColliders == null) return x;
        foreach (Collider2D col in carColliders)
            if (col != null && col.enabled && !col.isTrigger && col.gameObject.activeInHierarchy)
                x = Mathf.Max(x, col.bounds.max.x);
        return x;
    }

    private void UpdateApproach()
    {
        float front = VehicleFrontX();
        if (pending != null && pending.LeftX - front <= RepairClearance)
        {
            pending.IsSkipped = true;
            pending = null;
        }
        if (pending != null) return;
        float warning = Mathf.Max(warningDistance, ForwardSpeed * warningLeadSeconds, RepairClearance + 3f);
        foreach (Pothole hole in potholes)
        {
            if (hole.IsRepaired || hole.IsSkipped) continue;
            float distance = hole.LeftX - front;
            if (distance <= RepairClearance) { hole.IsSkipped = true; continue; }
            if (distance <= warning) pending = hole;
            break;
        }
    }

    public void ChooseRepair()
    {
        if (!HasPendingPothole) return;
        Pothole hole = pending;
        if (hole.LeftX - VehicleFrontX() <= RepairClearance)
        {
            ChooseSkip();
            ShowNotice("Too close to repair safely.");
            return;
        }
        if (ScoreManager.instance == null || !ScoreManager.instance.TrySpendCoins(hole.Cost))
        {
            ShowNotice("Not enough coins. Slow down or keep driving.");
            return;
        }
        hole.IsRepaired = true;
        hole.RepairedAt = Time.time;
        pending = null;
        RebuildTerrain();
        ShowNotice("Road repaired! -" + hole.Cost + " coins");
        TerrainChanged?.Invoke();
        PotholeRepaired?.Invoke(hole);
    }

    public void ChooseSkip()
    {
        if (!HasPendingPothole) return;
        pending.IsSkipped = true;
        pending = null;
        // The dip remains. No automatic speed or fuel deductions.
    }

    public void SetCustomPopupEnabled(bool value) => customPopupEnabled = value;

    private void ShowNotice(string message)
    {
        notice = message;
        noticeUntil = Time.unscaledTime + 1.8f;
    }

    // Temporary input UI until the replacement PotholePopupUI is installed.
    private void OnGUI()
    {
        if (!showFallbackHUD || customPopupEnabled || !IsRunActive || !enablePotholes) return;
        if (pending == null && string.IsNullOrEmpty(CurrentNotice)) return;
        float width = Mathf.Min(380f, Screen.width - 16f);
        GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, 10f, width, 128f), GUI.skin.box);
        if (pending != null)
        {
            GUILayout.Label("POTHOLE AHEAD - " + Mathf.CeilToInt(PendingDistance) + " m");
            GUILayout.Label("Coins: " + PendingCoinBalance + "    Repair: " + PendingRepairCost);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("[R] REPAIR", GUILayout.Height(30f))) ChooseRepair();
            if (GUILayout.Button("[S] SKIP", GUILayout.Height(30f))) ChooseSkip();
            GUILayout.EndHorizontal();
        }
        if (!string.IsNullOrEmpty(CurrentNotice)) GUILayout.Label(CurrentNotice);
        GUILayout.EndArea();
    }
}
