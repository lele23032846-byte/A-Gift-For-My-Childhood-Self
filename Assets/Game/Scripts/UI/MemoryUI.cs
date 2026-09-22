using TMPro;
using UnityEngine;

/// <summary>Shared typography, palette and restrained paper treatment for gameplay UI.</summary>
public static class MemoryUI
{
    public static readonly Color Paper = new Color32(233, 228, 218, 255);
    public static readonly Color PaperLight = new Color32(244, 240, 232, 255);
    public static readonly Color Ink = new Color32(73, 70, 64, 255);
    public static readonly Color Muted = new Color32(133, 128, 120, 255);
    public static readonly Color Line = new Color32(184, 177, 165, 255);
    public static readonly Color Memory = new Color32(141, 156, 134, 255);
    public static readonly Color Warm = new Color32(194, 155, 113, 255);

    private static TMP_FontAsset font;
    private static Material sharpMaterial;
    private static Texture2D paperTexture;

    public static TMP_FontAsset Font =>
        font != null ? font : (font = Resources.Load<TMP_FontAsset>("MemoryFontSerif"));

    /// <summary>
    /// The source TMP material contains a soft underlay intended for large title text.
    /// A dedicated material keeps small HUD text crisp without changing the title-menu material.
    /// </summary>
    public static Material SharpMaterial
    {
        get
        {
            if (sharpMaterial != null || Font == null)
            {
                return sharpMaterial;
            }

            sharpMaterial = new Material(Font.material)
            {
                name = "MemoryFont UI Sharp",
                hideFlags = HideFlags.DontSave
            };
            sharpMaterial.DisableKeyword("UNDERLAY_ON");
            sharpMaterial.DisableKeyword("OUTLINE_ON");
            SetFloatIfPresent(sharpMaterial, "_UnderlaySoftness", 0f);
            SetFloatIfPresent(sharpMaterial, "_UnderlayDilate", 0f);
            SetFloatIfPresent(sharpMaterial, "_OutlineWidth", 0f);
            SetFloatIfPresent(sharpMaterial, "_OutlineSoftness", 0f);
            SetFloatIfPresent(sharpMaterial, "_FaceDilate", 0.045f);
            return sharpMaterial;
        }
    }

    /// <summary>A quiet procedural grain used only inside paper panels.</summary>
    public static Texture2D PaperTexture
    {
        get
        {
            if (paperTexture != null)
            {
                return paperTexture;
            }

            const int size = 96;
            paperTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Memory Paper Grain",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float broad = Mathf.PerlinNoise(x * 0.075f + 13.2f, y * 0.075f + 7.4f);
                    float fiber = Mathf.Sin((x * 0.73f + y * 0.19f) * Mathf.Deg2Rad * 18f) * 0.5f + 0.5f;
                    float variation = (broad - 0.5f) * 0.055f + (fiber - 0.5f) * 0.012f;
                    Color pixel = new Color(
                        Mathf.Clamp01(0.92f + variation),
                        Mathf.Clamp01(0.90f + variation),
                        Mathf.Clamp01(0.85f + variation),
                        1f);
                    paperTexture.SetPixel(x, y, pixel);
                }
            }

            paperTexture.Apply(false, true);
            return paperTexture;
        }
    }

    public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    public static TMP_Text Text(
        string name,
        Transform parent,
        string value,
        float size,
        Vector2 min,
        Vector2 max,
        Color? color = null,
        FontStyles style = FontStyles.Normal)
    {
        TextMeshProUGUI text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        ApplyTypography(text, size, color ?? Paper, style);
        text.text = value;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        return text;
    }

    public static void ApplyTypography(
        TMP_Text text,
        float size,
        Color color,
        FontStyles style = FontStyles.Normal)
    {
        if (text == null)
        {
            return;
        }

        if (Font != null)
        {
            text.font = Font;
        }
        if (SharpMaterial != null)
        {
            text.fontSharedMaterial = SharpMaterial;
        }

        text.fontSize = size;
        text.enableAutoSizing = false;
        text.fontStyle = style;
        text.fontWeight = FontWeight.Regular;
        text.extraPadding = true;
        text.color = color;
        text.raycastTarget = false;
    }

    public static UnityEngine.UI.Image Image(
        string name,
        Transform parent,
        Color color,
        Vector2 min,
        Vector2 max)
    {
        UnityEngine.UI.Image image = Rect(name, parent, min, max).gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static UnityEngine.UI.RawImage AddPaperGrain(Transform parent, float alpha = 0.24f, float repeats = 4f)
    {
        RectTransform rect = Rect("PaperGrain", parent, Vector2.zero, Vector2.one);
        UnityEngine.UI.RawImage grain = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        grain.texture = PaperTexture;
        grain.uvRect = new Rect(0f, 0f, repeats, repeats);
        grain.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        grain.raycastTarget = false;
        return grain;
    }

    private static void SetFloatIfPresent(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }
}
