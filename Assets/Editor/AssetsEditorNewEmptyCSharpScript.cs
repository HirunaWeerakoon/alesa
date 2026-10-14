using UnityEngine;
using UnityEditor;
using System.IO;

public class FlashlightCookieGenerator
{
    [MenuItem("Tools/Generate Flashlight Cookie")]
    public static void GenerateCookie()
    {
        int size = 512;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float maxRadius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxRadius;

                if (dist >= 1f)
                {
                    texture.SetPixel(x, y, Color.black);
                    continue;
                }

                // 1. Core hotspot falloff
                float core = Mathf.Pow(Mathf.Clamp01(1f - dist * 1.8f), 2f) * 1.2f;

                // 2. Wide beam soft falloff
                float outer = Mathf.SmoothStep(1f, 0f, dist);

                // 3. Subtle lens ridges (rings)
                float ringPattern = Mathf.Sin(dist * 35f) * 0.05f * (1f - dist);

                // 4. Subtle glass noise
                float noise = (Mathf.PerlinNoise(x * 0.08f, y * 0.08f) - 0.5f) * 0.08f;

                float brightness = Mathf.Clamp01((core + outer + ringPattern + noise) * Mathf.SmoothStep(1f, 0f, dist));

                // Cookies use alpha or grayscale luminance
                texture.SetPixel(x, y, new Color(brightness, brightness, brightness, brightness));
            }
        }

        texture.Apply();

        // Save PNG to Assets folder
        byte[] bytes = texture.EncodeToPNG();
        string path = Application.dataPath + "/FlashlightCookie.png";
        File.WriteAllBytes(path, bytes);

        AssetDatabase.Refresh();

        // Automatically configure import settings for light cookies
        string relativePath = "Assets/FlashlightCookie.png";
        TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Cookie;
            //importer.lightType = LightType.Spot;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.SaveAndReimport();
        }

        EditorUtility.DisplayDialog("Flashlight Cookie", "FlashlightCookie.png created in Assets folder!", "OK");
    }
}