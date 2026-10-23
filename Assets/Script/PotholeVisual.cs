using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// File 2. Attach ONCE to Ground, alongside the replacement EnvironmentGenerator.
// Creates visual meshes only: no colliders, forces, fuel changes or terrain edits.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnvironmentGenerator))]
public sealed class PotholeVisual : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnvironmentGenerator terrain;
    [Tooltip("Optional sprite material supporting vertex colours. Empty uses Ground's existing material.")]
    [SerializeField] private Material visualMaterial;

    [Header("Ground Detail - World Units")]
    [SerializeField, Range(12, 48)] private int samplesPerHole = 28;
    [SerializeField, Range(0.05f, 0.4f)] private float exposedSoilThickness = 0.18f;
    [SerializeField, Range(0.01f, 0.08f)] private float surfaceInset = 0.025f;
    [SerializeField, Range(3, 18)] private int looseStoneCount = 9;
    [SerializeField, Range(12, 80)] private int gravelSpeckCount = 44;

    [Header("Roadside Warning")]
    [SerializeField] private bool showWarningSign = true;
    [SerializeField, Range(0.4f, 1.5f)] private float signHeight = 0.95f;
    [SerializeField, Range(0.3f, 2f)] private float signDistanceBeforeHole = 0.8f;

    [Header("Colours")]
    [SerializeField] private Color exposedSoil = new Color(0.34f, 0.23f, 0.14f);
    [SerializeField] private Color cavityShadow = new Color(0.18f, 0.13f, 0.10f);
    [SerializeField] private Color brokenRim = new Color(0.58f, 0.42f, 0.25f);
    [SerializeField] private Color stoneColour = new Color(0.66f, 0.55f, 0.40f);
    [SerializeField] private Color gravelFill = new Color(0.54f, 0.46f, 0.34f);
    [SerializeField] private Color gravelSurface = new Color(0.75f, 0.65f, 0.47f);
    [SerializeField] private Color dustColour = new Color(0.72f, 0.61f, 0.45f, 0.6f);

    [Header("Rendering")]
    [Tooltip("Added to Ground's sorting order. Ground is 0 in the supplied scene.")]
    [SerializeField] private int sortingOrderOffset = 1;
    [Tooltip("Small positive Z offset puts detail behind vehicles sharing its sorting order.")]
    [SerializeField, Range(0f, 0.2f)] private float worldZOffset = 0.03f;

    [Header("Optional Audio")]
    [SerializeField] private AudioClip repairSound;
    [SerializeField] private AudioClip impactSound;
    [SerializeField, Range(0f, 1f)] private float soundVolume = 0.45f;

    private sealed class HoleView
    {
        public GameObject Object;
        public Mesh Mesh;
        public bool Repaired;
    }

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius, Age, Life, Angle, Spin;
        public Color Colour;
        public bool Stone;
    }

    private readonly Dictionary<int, HoleView> views = new Dictionary<int, HoleView>();
    private readonly HashSet<int> visibleIds = new HashSet<int>();
    private readonly List<int> removedIds = new List<int>();
    private readonly List<Particle> particles = new List<Particle>();
    private readonly MeshData geometry = new MeshData();
    private readonly MeshData effects = new MeshData();
    private Transform runtimeRoot;
    private Material runtimeMaterial;
    private Mesh effectMesh;
    private MeshRenderer effectRenderer;
    private AudioSource audioSource;
    private EnvironmentGenerator subscribedTerrain;
    private int sortingLayer, sortingOrder;
    private float nextImpactTime;
    private bool initialised;
    private const int MaximumParticles = 160;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        if (terrain == null) terrain = GetComponent<EnvironmentGenerator>();
        if (terrain == null) return;
        subscribedTerrain = terrain;
        subscribedTerrain.TerrainChanged += Synchronise;
        subscribedTerrain.PotholeRepaired += OnRepair;
        if (runtimeRoot != null) runtimeRoot.gameObject.SetActive(true);
        if (terrain.IsReady) Synchronise();
    }

    private void Start() => Synchronise();

    private bool EnsureResources()
    {
        if (initialised) return true;
        if (terrain == null || !terrain.IsReady || !terrain.PotholesEnabled) return false;
        var groundRenderer = terrain.ShapeController.spriteShapeRenderer;
        Material source = visualMaterial != null ? visualMaterial : groundRenderer.sharedMaterial;
        if (source == null || source.shader == null || !source.HasProperty("_MainTex"))
        {
            Debug.LogError("PotholeVisual: assign a sprite material with a _MainTex property. " +
                "The project's existing Sprite-Lit-Default material is supported.", this);
            enabled = false;
            return false;
        }

        // Clone a referenced material so Android builds retain its shader.
        // Never edit Ground's shared material or an asset on disk.
        runtimeMaterial = new Material(source) { name = "Pothole Visual Material (Runtime)" };
        runtimeMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
        runtimeMaterial.SetTextureScale("_MainTex", Vector2.one);
        runtimeMaterial.SetTextureOffset("_MainTex", Vector2.zero);
        if (runtimeMaterial.HasProperty("_Color")) runtimeMaterial.SetColor("_Color", Color.white);
        if (runtimeMaterial.HasProperty("_RendererColor")) runtimeMaterial.SetColor("_RendererColor", Color.white);
        if (runtimeMaterial.HasProperty("_MaskTex")) runtimeMaterial.SetTexture("_MaskTex", Texture2D.whiteTexture);
        if (runtimeMaterial.HasProperty("_EnableExternalAlpha")) runtimeMaterial.SetFloat("_EnableExternalAlpha", 0f);
        runtimeMaterial.DisableKeyword("ETC1_EXTERNAL_ALPHA");
        runtimeMaterial.DisableKeyword("SKINNED_SPRITE");

        sortingLayer = groundRenderer.sortingLayerID;
        sortingOrder = groundRenderer.sortingOrder + sortingOrderOffset;
        runtimeRoot = new GameObject("Pothole Visuals (Runtime)").transform;
        runtimeRoot.SetParent(transform, false);
        effectMesh = new Mesh { name = "Pothole Dust (Runtime)" };
        effectMesh.MarkDynamic();
        effectRenderer = CreateRenderer("Repair And Impact Dust", effectMesh);
        effectRenderer.enabled = false;
        audioSource = runtimeRoot.gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        initialised = true;
        return true;
    }

    private MeshRenderer CreateRenderer(string objectName, Mesh mesh)
    {
        GameObject obj = new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer));
        obj.transform.SetParent(runtimeRoot, false);
        obj.GetComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = runtimeMaterial;
        renderer.sortingLayerID = sortingLayer;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        // MeshRenderer does not populate SpriteRenderer's per-draw values.
        MaterialPropertyBlock properties = new MaterialPropertyBlock();
        properties.SetColor("unity_SpriteColor", Color.white);
        properties.SetVector("unity_SpriteProps", new Vector4(1f, 1f, 1f, 0f));
        properties.SetColor("_RendererColor", Color.white);
        properties.SetVector("_Flip", Vector4.one);
        renderer.SetPropertyBlock(properties);
        return renderer;
    }

    private void Synchronise()
    {
        if (!isActiveAndEnabled || !EnsureResources()) return;
        visibleIds.Clear();
        foreach (EnvironmentGenerator.Pothole hole in terrain.Potholes)
        {
            visibleIds.Add(hole.Id);
            if (!views.TryGetValue(hole.Id, out HoleView view))
            {
                view = new HoleView { Mesh = new Mesh { name = "Pothole " + hole.Id + " Detail" } };
                view.Object = CreateRenderer("Pothole " + hole.Id, view.Mesh).gameObject;
                views.Add(hole.Id, view);
                BuildHole(hole, view);
            }
            else if (view.Repaired != hole.IsRepaired)
                BuildHole(hole, view);
        }
        removedIds.Clear();
        foreach (int id in views.Keys)
            if (!visibleIds.Contains(id)) removedIds.Add(id);
        foreach (int id in removedIds)
        {
            HoleView view = views[id];
            Destroy(view.Mesh);
            Destroy(view.Object);
            views.Remove(id);
        }
    }

    private Vector2 Surface(float x, bool includeDamage = true)
    {
        Vector3 point = terrain.GetRoadPoint(x, includeDamage);
        return new Vector2(point.x, point.y);
    }

    private static float Bowl(EnvironmentGenerator.Pothole hole, float x)
    {
        float u = (x - hole.CenterX) / (hole.Width * 0.5f);
        float q = Mathf.Max(0f, 1f - u * u);
        return q * q;
    }

    private void BuildHole(EnvironmentGenerator.Pothole hole, HoleView view)
    {
        geometry.Clear();
        int count = Mathf.Clamp(samplesPerHole, 12, 48);
        for (int i = 0; i < count; i++)
        {
            float x0 = Mathf.Lerp(hole.LeftX, hole.RightX, i / (float)count);
            float x1 = Mathf.Lerp(hole.LeftX, hole.RightX, (i + 1) / (float)count);
            Vector2 a = Surface(x0), b = Surface(x1);
            a.y -= surfaceInset;
            b.y -= surfaceInset;
            float q0 = Bowl(hole, x0), q1 = Bowl(hole, x1);
            if (hole.IsRepaired)
            {
                // A visible section of compacted gravel occupies the old cavity.
                Vector2 c = b - Vector2.up * (hole.Depth + exposedSoilThickness) * q1;
                Vector2 d = a - Vector2.up * (hole.Depth + exposedSoilThickness) * q0;
                geometry.Quad(a, b, c, d, gravelFill);
                geometry.Quad(a, b, b - Vector2.up * 0.055f * q1,
                    a - Vector2.up * 0.055f * q0, gravelSurface);
            }
            else
            {
                // Everything follows the physical depression; no oval overlays.
                Vector2 c = b - Vector2.up * exposedSoilThickness * Mathf.Sqrt(q1);
                Vector2 d = a - Vector2.up * exposedSoilThickness * Mathf.Sqrt(q0);
                geometry.Quad(a, b, c, d, cavityShadow, exposedSoil);
                geometry.Quad(a, b, b - Vector2.up * 0.035f * q1,
                    a - Vector2.up * 0.035f * q0, brokenRim);
            }
        }

        // Local deterministic randomness never changes pickup/gameplay randomness.
        System.Random random = new System.Random(unchecked(hole.Id * 1103515245 + 12345));
        int stones = hole.IsRepaired ? gravelSpeckCount : looseStoneCount;
        for (int i = 0; i < stones; i++)
        {
            float x = Mathf.Lerp(hole.LeftX, hole.RightX, Between(random, 0.14f, 0.86f));
            Vector2 surface = Surface(x);
            float thickness = hole.IsRepaired ? (hole.Depth + exposedSoilThickness) * Bowl(hole, x) : exposedSoilThickness;
            float radius = hole.IsRepaired ? Between(random, 0.012f, 0.028f) : Between(random, 0.025f, 0.045f);
            radius = Mathf.Min(radius, thickness * 0.2f);
            if (radius < 0.004f) continue;
            // Embed detail below the surface, so decorative stones cannot imply
            // a large invisible obstacle sitting on the road.
            Vector2 position = surface - Vector2.up * (surfaceInset +
                Between(random, radius * 1.2f, Mathf.Max(radius * 1.2f, thickness - radius)));
            Color tint = Color.Lerp(hole.IsRepaired ? gravelSurface : stoneColour,
                exposedSoil, Between(random, 0f, 0.55f));
            geometry.Stone(position, radius, Between(random, -1f, 1f), tint);
        }
        if (!hole.IsRepaired)
        {
            AddCrack(hole.LeftX + hole.Width * 0.08f, 1f);
            AddCrack(hole.RightX - hole.Width * 0.08f, -1f);
            if (showWarningSign) AddWarningSign(hole);
        }
        Upload(view.Mesh, geometry);
        view.Repaired = hole.IsRepaired;
    }

    private void AddCrack(float x, float direction)
    {
        Vector2 a = Surface(x) - Vector2.up * (surfaceInset + 0.025f);
        Vector2 b = a + new Vector2(direction * 0.045f, -0.085f);
        Vector2 c = b + new Vector2(-direction * 0.025f, -0.075f);
        geometry.Line(a, b, 0.014f, cavityShadow);
        geometry.Line(b, c, 0.009f, cavityShadow);
    }

    private void AddWarningSign(EnvironmentGenerator.Pothole hole)
    {
        Vector2 ground = Surface(hole.LeftX - signDistanceBeforeHole);
        Vector2 top = ground + Vector2.up * signHeight;
        geometry.Line(ground - Vector2.up * 0.08f, top, 0.055f, new Color(0.40f, 0.31f, 0.22f));
        Vector2 baseCentre = top - Vector2.up * 0.08f;
        Color dark = new Color(0.19f, 0.20f, 0.16f);
        geometry.Triangle(baseCentre + new Vector2(-0.30f, 0f),
            baseCentre + new Vector2(0f, 0.53f), baseCentre + new Vector2(0.30f, 0f), dark);
        geometry.Triangle(baseCentre + new Vector2(-0.235f, 0.035f),
            baseCentre + new Vector2(0f, 0.44f), baseCentre + new Vector2(0.235f, 0.035f),
            new Color(0.94f, 0.73f, 0.23f));
        geometry.Line(baseCentre + new Vector2(0f, 0.17f), baseCentre + new Vector2(0f, 0.32f), 0.04f, dark);
        geometry.Stone(baseCentre + new Vector2(0f, 0.11f), 0.025f, 0f, dark);
    }

    private void OnRepair(EnvironmentGenerator.Pothole hole)
    {
        if (!isActiveAndEnabled || !EnsureResources()) return;
        Synchronise();
        if (repairSound != null) audioSource.PlayOneShot(repairSound, soundVolume);
        float duration = terrain.RepairVisualDuration;
        if (duration <= 0f) return;
        System.Random random = new System.Random(unchecked(hole.Id * 8191 + 73));
        for (int i = 0; i < 20; i++)
        {
            float x = Mathf.Lerp(hole.LeftX, hole.RightX, Between(random, 0.05f, 0.95f));
            Vector2 p = Surface(x) + Vector2.up * 0.035f;
            AddParticle(p, new Vector2(Between(random, -0.45f, 0.45f), Between(random, 0.25f, 0.85f)),
                Between(random, 0.075f, 0.14f), duration * Between(random, 0.7f, 1f), dustColour, false, random);
        }
        for (int i = 0; i < 9; i++)
        {
            float x = Mathf.Lerp(hole.LeftX, hole.RightX, Between(random, 0.12f, 0.88f));
            Vector2 p = Surface(x) + Vector2.up * Between(random, 0.22f, 0.55f);
            AddParticle(p, new Vector2(Between(random, -0.12f, 0.12f), -0.35f),
                Between(random, 0.02f, 0.04f), duration, gravelSurface, true, random);
        }
    }

    // Used by the next PotholeHazard file only after a genuine ground contact.
    public void PlayImpact(Vector3 worldPoint, float strength)
    {
        if (!isActiveAndEnabled || terrain == null || !terrain.IsRunActive ||
            Time.time < nextImpactTime || !EnsureResources()) return;
        strength = Mathf.Clamp01(strength);
        if (strength < 0.05f) return;
        nextImpactTime = Time.time + 0.15f;
        System.Random random = new System.Random(unchecked(Mathf.RoundToInt(worldPoint.x * 997f) + 31));
        int count = Mathf.RoundToInt(Mathf.Lerp(3f, 8f, strength));
        for (int i = 0; i < count; i++)
            AddParticle(worldPoint, new Vector2(Between(random, -0.6f, 0.6f), Between(random, 0.3f, 0.8f)),
                Between(random, 0.045f, 0.09f), 0.45f, dustColour, false, random);
        if (impactSound != null) audioSource.PlayOneShot(impactSound, soundVolume * strength);
    }

    private void AddParticle(Vector2 position, Vector2 velocity, float radius,
        float life, Color colour, bool stone, System.Random random)
    {
        if (particles.Count >= MaximumParticles) return;
        particles.Add(new Particle
        {
            Position = position, Velocity = velocity, Radius = radius,
            Life = Mathf.Max(0.1f, life), Colour = colour, Stone = stone,
            Angle = Between(random, 0f, Mathf.PI), Spin = Between(random, -4f, 4f)
        });
    }

    private void Update()
    {
        if (!initialised || particles.Count == 0 || Time.deltaTime <= 0f) return;
        effects.Clear();
        float dt = Time.deltaTime;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { particles.RemoveAt(i); continue; }
            if (p.Stone) p.Velocity.y -= 3.2f * dt;
            p.Position += p.Velocity * dt;
            p.Angle += p.Spin * dt;
            if (p.Stone && p.Position.y <= Surface(p.Position.x).y + p.Radius)
            {
                particles.RemoveAt(i);
                continue;
            }
            float t = p.Age / p.Life;
            Color colour = p.Colour;
            colour.a *= p.Stone ? 1f - t * t : Mathf.Sin(Mathf.PI * t);
            if (p.Stone) effects.Stone(p.Position, p.Radius, p.Angle, colour);
            else effects.Puff(p.Position, p.Radius * (1f + 1.8f * t), colour);
            particles[i] = p;
        }
        Upload(effectMesh, effects);
        effectRenderer.enabled = particles.Count > 0;
    }

    private static float Between(System.Random random, float min, float max) =>
        Mathf.Lerp(min, max, (float)random.NextDouble());

    private void Upload(Mesh mesh, MeshData data)
    {
        // All design dimensions are world units, even though Ground is scaled.
        data.LocalVertices.Clear();
        float z = terrain.GetRoadPoint(terrain.Player.position.x).z + worldZOffset;
        foreach (Vector2 point in data.Vertices)
            data.LocalVertices.Add(runtimeRoot.InverseTransformPoint(new Vector3(point.x, point.y, z)));
        mesh.Clear();
        mesh.SetVertices(data.LocalVertices);
        mesh.SetColors(data.Colours);
        mesh.SetUVs(0, data.UVs);
        mesh.SetNormals(data.Normals);
        mesh.SetTriangles(data.Triangles, 0);
        mesh.RecalculateBounds();
    }

    private void OnDisable()
    {
        if (subscribedTerrain != null)
        {
            subscribedTerrain.TerrainChanged -= Synchronise;
            subscribedTerrain.PotholeRepaired -= OnRepair;
            subscribedTerrain = null;
        }
        particles.Clear();
        if (effectRenderer != null) effectRenderer.enabled = false;
        if (runtimeRoot != null) runtimeRoot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        foreach (HoleView view in views.Values)
            if (view.Mesh != null) Destroy(view.Mesh);
        views.Clear();
        if (effectMesh != null) Destroy(effectMesh);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (runtimeRoot != null) Destroy(runtimeRoot.gameObject);
    }

    // Small mesh builder. Each hole is one renderer; particles share another.
    private sealed class MeshData
    {
        public readonly List<Vector2> Vertices = new List<Vector2>();
        public readonly List<Vector3> LocalVertices = new List<Vector3>();
        public readonly List<Color> Colours = new List<Color>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();

        public void Clear()
        {
            Vertices.Clear(); LocalVertices.Clear(); Colours.Clear();
            UVs.Clear(); Normals.Clear(); Triangles.Clear();
        }

        private int Vertex(Vector2 p, Color c)
        {
            int index = Vertices.Count;
            Vertices.Add(p); Colours.Add(c); UVs.Add(new Vector2(0.5f, 0.5f));
            Normals.Add(Vector3.back);
            return index;
        }

        public void Triangle(Vector2 a, Vector2 b, Vector2 c, Color colour)
        {
            int start = Vertex(a, colour); Vertex(b, colour); Vertex(c, colour);
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color colour) =>
            Quad(a, b, c, d, colour, colour);

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color top, Color bottom)
        {
            int start = Vertex(a, top); Vertex(b, top); Vertex(c, bottom); Vertex(d, bottom);
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
            Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 3);
        }

        public void Line(Vector2 a, Vector2 b, float width, Color colour)
        {
            Vector2 direction = b - a;
            if (direction.sqrMagnitude < 0.000001f) return;
            Vector2 side = new Vector2(-direction.y, direction.x).normalized * width * 0.5f;
            Quad(a + side, b + side, b - side, a - side, colour);
        }

        public void Stone(Vector2 centre, float radius, float angle, Color colour)
        {
            Vector2 u = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            Vector2 v = new Vector2(-u.y, u.x) * 0.65f;
            Quad(centre - u + v * 0.5f, centre + u * 0.25f + v,
                centre + u - v * 0.4f, centre - u * 0.3f - v, colour);
        }

        public void Puff(Vector2 centre, float radius, Color colour)
        {
            int start = Vertex(centre, colour);
            Color outer = colour; outer.a = 0f;
            const int sides = 10;
            for (int i = 0; i < sides; i++)
            {
                float angle = -i * Mathf.PI * 2f / sides;
                Vertex(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.8f) * radius, outer);
            }
            for (int i = 0; i < sides; i++)
            {
                Triangles.Add(start); Triangles.Add(start + 1 + i);
                Triangles.Add(start + 1 + (i + 1) % sides);
            }
        }
    }
}
