#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

// Place in Assets/Editor. Generates new assets only when you run the menu command.
public static class PotholeArtSetup
{
    private const string Folder = "Assets/PotholeUpgrade";
    private const string ProfilePath = Folder + "/PotholeGroundProfile.asset";

    [MenuItem("Tools/HCR/Create Pothole Surface Assets")]
    private static void CreateAssets()
    {
        if (Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Pothole setup", "Exit Play Mode first.", "OK");
            return;
        }
        SpriteShapeController controller = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<SpriteShapeController>() : null;
        if (controller == null || controller.spriteShape == null)
        {
            EditorUtility.DisplayDialog("Pothole setup", "Select Ground, which has the Sprite Shape Controller.", "OK");
            return;
        }
        SpriteShape existing = AssetDatabase.LoadAssetAtPath<SpriteShape>(ProfilePath);
        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            EditorUtility.DisplayDialog("Pothole setup", "The profile already exists. Assign it to Ground and enable Use Surface Variants.", "OK");
            return;
        }
        SpriteShape source = controller.spriteShape;
        if (source.angleRanges.Count == 0)
        {
            Debug.LogError("The selected profile needs an edge angle range.");
            return;
        }
        foreach (AngleRange range in source.angleRanges)
            if (range.sprites == null || range.sprites.Count == 0 || range.sprites[0] == null)
            {
                Debug.LogError("Each angle range needs a grass sprite in its first slot.");
                return;
            }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "PotholeUpgrade");
        SpriteShape profile = Object.Instantiate(source);
        profile.name = "PotholeGroundProfile";
        for (int i = 0; i < profile.angleRanges.Count; i++)
        {
            Sprite grass = source.angleRanges[i].sprites[0];
            Sprite dirt = MakeEdge(grass, "BareDirtEdge_" + i, false);
            Sprite gravel = MakeEdge(grass, "GravelEdge_" + i, true);
            profile.angleRanges[i].sprites = new List<Sprite> { grass, dirt, gravel };
        }
        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = profile;
        EditorGUIUtility.PingObject(profile);
        EditorUtility.DisplayDialog("Pothole assets created",
            "Assign PotholeGroundProfile to Ground's Sprite Shape Controller.\n\n" +
            "On EnvironmentGenerator enable Use Surface Variants. Indices: grass 0, dirt 1, gravel 2.\n\n" +
            "The original profile and scene assignments were not changed.", "OK");
    }

    private static Sprite MakeEdge(Sprite template, string name, bool gravel)
    {
        int width = Mathf.Max(4, Mathf.RoundToInt(template.rect.width));
        int height = Mathf.Max(4, Mathf.RoundToInt(template.rect.height));
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[width * height];
        System.Random random = new System.Random(gravel ? 8123 : 4021);
        Color low = gravel ? new Color(0.43f, 0.36f, 0.26f) : new Color(0.32f, 0.22f, 0.13f);
        Color high = gravel ? new Color(0.71f, 0.61f, 0.44f) : new Color(0.52f, 0.37f, 0.21f);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float t = y / (float)(height - 1);
            float variation = ((float)random.NextDouble() - 0.5f) * (gravel ? 0.22f : 0.09f);
            Color c = Color.Lerp(low, high, Mathf.Clamp01(t + variation));
            // A narrow irregular upper edge, rather than a grass silhouette.
            int notch = ((x / 5) % 3 == 0) ? 1 : 0;
            c.a = y >= height - notch ? 0f : 1f;
            pixels[y * width + x] = c;
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = template.pixelsPerUnit;
        importer.spriteBorder = template.border;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(template.pivot.x / template.rect.width, template.pivot.y / template.rect.height);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
#endif
