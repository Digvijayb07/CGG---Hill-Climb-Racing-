using UnityEngine;

// CGG Algorithm: Parallax background, based on the project presentation (slide 11).
// Attach to an EMPTY scene GameObject, not to Ground or to the Main Camera.
// Four art textures load from Assets/Resources/CGGParallax automatically.
// Your existing Sprite Shape terrain is already the 1.0x foreground layer.
[DefaultExecutionOrder(10000)]
public sealed class CGGParallaxBackground : MonoBehaviour
{
    [Header("Camera (auto-finds Main Camera if empty)")]
    [SerializeField] private Camera targetCamera;

    [Header("Visual placement (fraction of camera height)")]
    [SerializeField, Range(-0.25f, 0.25f)] private float mountainVerticalOffset = 0.04f;
    [SerializeField, Range(-0.25f, 0.40f)] private float forestVerticalOffset = 0.13f;
    [SerializeField, Range(-0.25f, 0.50f)] private float farmVerticalOffset = 0.24f;

    [Header("Background props")]
    [Tooltip("OFF: the farm is not drawn, avoiding houses that appear to float over our separately moving Sprite Shape terrain. Turn ON only if you want a distinct DISTANT farm hill (not ground-attached props).")]
    [SerializeField] private bool showDistantFarmLayer = false;

    [Header("Rendering")]
    [Tooltip("Use the same Sorting Layer as Ground, but a negative order.")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Bigger than 1 ensures the sky covers the full camera viewport.")]
    [SerializeField, Range(1.02f, 1.30f)] private float coverage = 1.10f;

    private const int TilesPerLayer = 5;
    private const float PixelsPerUnit = 100f;

    private sealed class ParallaxLayer
    {
        public readonly float factor;
        public readonly float yOffset;
        public readonly SpriteRenderer[] renderers;
        public readonly Sprite sprite;

        public ParallaxLayer(float factor, float yOffset, Sprite sprite)
        {
            this.factor = factor;
            this.yOffset = yOffset;
            this.sprite = sprite;
            renderers = new SpriteRenderer[TilesPerLayer];
        }
    }

    private ParallaxLayer[] layers;
    private float startCameraX;
    private bool ready;

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || !targetCamera.orthographic)
        {
            Debug.LogError("CGG Parallax: assign your orthographic Main Camera.", this);
            enabled = false;
            return;
        }

        Texture2D sky = Resources.Load<Texture2D>("CGGParallax/Sky");
        Texture2D mountains = Resources.Load<Texture2D>("CGGParallax/Mountains");
        Texture2D forest = Resources.Load<Texture2D>("CGGParallax/Forest");
        Texture2D farm = Resources.Load<Texture2D>("CGGParallax/FarmWindmill");

        if (sky == null || mountains == null || forest == null || (showDistantFarmLayer && farm == null))
        {
            Debug.LogError("CGG Parallax: missing one or more PNGs under Assets/Resources/CGGParallax. Copy the COMPLETE Assets folder from the ZIP into your project.", this);
            enabled = false;
            return;
        }

        startCameraX = targetCamera.transform.position.x;
        // A distant farm cannot physically attach to the independently generated
        // foreground terrain: parallax moves 0.75x, while the terrain moves 1x.
        // Keep that optional. Sky/mountains/forest still work independently.
        layers = showDistantFarmLayer
            ? new[]
            {
                CreateLayer("Sky_0x",            sky,       0.00f, 0.00f,                 -1000),
                CreateLayer("Mountains_025x",     mountains, 0.25f, mountainVerticalOffset, -900),
                CreateLayer("Forest_050x",        forest,    0.50f, forestVerticalOffset,   -800),
                CreateLayer("Farm_Windmill_075x", farm,      0.75f, farmVerticalOffset,     -700)
            }
            : new[]
            {
                CreateLayer("Sky_0x",            sky,       0.00f, 0.00f,                 -1000),
                CreateLayer("Mountains_025x",     mountains, 0.25f, mountainVerticalOffset, -900),
                CreateLayer("Forest_050x",        forest,    0.50f, forestVerticalOffset,   -800)
            };
        ready = true;
        PositionLayers();
    }

    private ParallaxLayer CreateLayer(string label, Texture2D texture, float factor, float yOffset, int order)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect
        );

        ParallaxLayer layer = new ParallaxLayer(factor, yOffset, sprite);
        GameObject group = new GameObject(label);
        group.transform.SetParent(transform, false);

        for (int i = 0; i < TilesPerLayer; i++)
        {
            GameObject piece = new GameObject(label + "_Tile_" + i);
            piece.transform.SetParent(group.transform, false);
            SpriteRenderer renderer = piece.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = order;
            layer.renderers[i] = renderer;
        }

        return layer;
    }

    private void LateUpdate()
    {
        // Guard against delayed scene initialization/teardown: an enabled component
        // can be called while its generated renderer data is no longer available.
        if (!ready || layers == null || targetCamera == null)
            return;
        PositionLayers();
    }

    private void PositionLayers()
    {
        // Do not evaluate layers.Length before verifying that the array exists.
        if (!ready || layers == null || targetCamera == null)
            return;

        float viewHeight = targetCamera.orthographicSize * 2f;
        float viewWidth = viewHeight * targetCamera.aspect;
        float cameraX = targetCamera.transform.position.x;
        float cameraY = targetCamera.transform.position.y;

        // All supplied PNGs are 1600x900. Scale every layer so it covers the
        // ENTIRE screen even if the Game tab's aspect ratio changes.
        for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            ParallaxLayer layer = layers[layerIndex];
            if (layer == null || layer.sprite == null || layer.renderers == null)
                continue;

            float aspect = (float)layer.sprite.rect.width / layer.sprite.rect.height;
            float tileWidth = Mathf.Max(viewWidth, viewHeight * aspect) * coverage;
            float tileScale = tileWidth / (layer.sprite.rect.width / PixelsPerUnit);

            // Wrap the movement instead of letting scenery drift off-screen.
            // factor 0: fixed relative to the screen; factor .75: 75% of camera movement.
            float travelled = cameraX - startCameraX;
            float offset = Mathf.Repeat(-travelled * layer.factor + tileWidth * 0.5f, tileWidth)
                           - tileWidth * 0.5f;

            for (int i = 0; i < TilesPerLayer; i++)
            {
                SpriteRenderer renderer = layer.renderers[i];
                if (renderer == null)
                    continue;

                Transform tile = renderer.transform;
                tile.position = new Vector3(
                    cameraX + offset + (i - 2) * tileWidth,
                    cameraY + layer.yOffset * viewHeight,
                    1f
                );
                tile.localScale = new Vector3(tileScale, tileScale, 1f);
            }
        }
    }

    private void OnDestroy()
    {
        ready = false;
        // Only runtime Sprite wrappers are ours. Resource PNGs stay untouched.
        if (layers == null) return;
        foreach (ParallaxLayer layer in layers)
        {
            if (layer != null && layer.sprite != null)
                Destroy(layer.sprite);
        }
    }
}
