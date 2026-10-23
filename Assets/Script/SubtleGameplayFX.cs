using UnityEngine;
using UnityEngine.SceneManagement;

// Small scene-owned pool: effects never alter Unity's gameplay random sequence.
public sealed class SubtleGameplayFX : MonoBehaviour
{
    private const int Capacity = 40;
    private static SubtleGameplayFX instance;
    private readonly System.Random random = new System.Random(7129);
    private readonly Puff[] puffs = new Puff[Capacity];
    private Texture2D texture;
    private Sprite sprite;
    private int cursor;

    private struct Puff
    {
        public SpriteRenderer renderer;
        public Vector3 velocity;
        public Color colour;
        public float age, lifetime, size, growth;
    }

    public static void WarmUp(Component owner)
    {
        if (instance != null || owner == null) return;
        var root = new GameObject("Subtle Gameplay FX (Runtime)");
        SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        instance = root.AddComponent<SubtleGameplayFX>();
    }

    private void Awake()
    {
        instance = this;
        texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
        { name = "Soft Dust Dot", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[32 * 32];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float radius = new Vector2(x - 15.5f, y - 15.5f).magnitude / 15.5f;
            float alpha = Mathf.Clamp01(1f - radius);
            pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha * alpha);
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * 0.5f, 32f);
        for (int i = 0; i < Capacity; i++)
        {
            var particle = new GameObject("Puff " + i);
            particle.transform.SetParent(transform, false);
            var renderer = particle.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.enabled = false;
            puffs[i].renderer = renderer;
        }
    }

    public static void Dust(Vector2 position, float horizontalSpeed, SpriteRenderer appearance)
    {
        if (appearance == null || Time.timeScale <= 0f) return;
        WarmUp(appearance);
        instance.Emit(position, new Vector2(-Mathf.Sign(horizontalSpeed) * instance.Between(0.25f, 0.6f), instance.Between(0.3f, 0.65f)),
            new Color(0.65f, 0.52f, 0.34f, 0.32f), instance.Between(0.35f, 0.55f), 0.3f, 1.5f, appearance);
    }

    public static void Pickup(Vector3 position, SpriteRenderer appearance, bool fuel)
    {
        if (appearance == null || Time.timeScale <= 0f) return;
        WarmUp(appearance);
        Color colour = fuel ? new Color(0.65f, 0.95f, 0.55f, 0.85f) : new Color(1f, 0.8f, 0.25f, 0.9f);
        for (int i = 0; i < 6; i++)
        {
            float angle = (i / 6f) * Mathf.PI * 2f;
            Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * instance.Between(0.65f, 1.1f) + Vector2.up * 0.4f;
            instance.Emit(position, velocity, colour, 0.38f, 0.16f, -0.5f, appearance);
        }
    }

    private void Emit(Vector3 position, Vector2 velocity, Color colour, float lifetime, float size, float growth, SpriteRenderer appearance)
    {
        // Drop surplus particles rather than allocating or replacing an active effect.
        for (int attempt = 0; attempt < Capacity; attempt++)
        {
            int index = cursor;
            cursor = (cursor + 1) % Capacity;
            if (puffs[index].renderer.enabled) continue;
            Puff puff = puffs[index];
            puff.age = 0f; puff.lifetime = lifetime; puff.velocity = velocity;
            puff.colour = colour; puff.size = size; puff.growth = growth;
            puff.renderer.sharedMaterial = appearance.sharedMaterial;
            puff.renderer.sortingLayerID = appearance.sortingLayerID;
            puff.renderer.sortingOrder = appearance.sortingOrder + 1;
            puff.renderer.transform.position = position;
            puff.renderer.transform.localScale = Vector3.one * size;
            puff.renderer.color = colour;
            puff.renderer.enabled = true;
            puffs[index] = puff;
            return;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        for (int i = 0; i < Capacity; i++)
        {
            Puff puff = puffs[i];
            if (!puff.renderer.enabled) continue;
            puff.age += dt;
            if (puff.age >= puff.lifetime) puff.renderer.enabled = false;
            else
            {
                float t = puff.age / puff.lifetime;
                puff.renderer.transform.position += puff.velocity * dt;
                puff.renderer.transform.localScale = Vector3.one * (puff.size * (1f + puff.growth * t));
                Color colour = puff.colour; colour.a *= (1f - t) * (1f - t);
                puff.renderer.color = colour;
            }
            puffs[i] = puff;
        }
    }

    private float Between(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (sprite != null) Destroy(sprite);
        if (texture != null) Destroy(texture);
    }
}
