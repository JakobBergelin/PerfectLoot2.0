using UnityEditor;
using UnityEngine;

/// <summary>
/// Shows an asset's Icon as its Project window thumbnail and as a preview in the Inspector,
/// optionally on a colored background (e.g. item rarity).
/// </summary>
public abstract class IconPreviewEditor : Editor
{
    // How much of the background the icon covers when a background is drawn
    const float IconInset = 0.8f;
    const float CornerRadiusFraction = 0.12f;

    protected abstract Sprite Icon { get; }
    protected abstract string PreviewTitle { get; }

    /// <summary>Background drawn behind the icon, or null for none.</summary>
    protected virtual Color? BackgroundColor => null;

    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
    {
        var icon = Icon;
        var background = BackgroundColor;
        bool hasIcon = icon != null && icon.texture != null;
        if (!hasIcon && background == null)
            return base.RenderStaticPreview(assetPath, subAssets, width, height);

        // Without an icon, show Unity's default icon for the asset so the background still has something on it
        Texture source = hasIcon ? icon.texture : AssetPreview.GetMiniThumbnail(target);

        var pixels = new Color[width * height];
        if (background != null) FillRoundedRect(pixels, width, height, background.Value, Mathf.Min(width, height) * CornerRadiusFraction);
        if (source == null) return ToTexture(pixels, width, height);

        // Fit the icon inside the (inset) area while keeping its aspect ratio
        Rect sourceRect = hasIcon ? icon.textureRect : new Rect(0, 0, source.width, source.height);
        float area = background != null ? IconInset : 1f;
        float scale = Mathf.Min(width * area / sourceRect.width, height * area / sourceRect.height);
        int fittedWidth = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width * scale));
        int fittedHeight = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height * scale));
        var iconPixels = ReadPixels(source, sourceRect, fittedWidth, fittedHeight);

        // Alpha-blend the icon centered on top
        int offsetX = (width - fittedWidth) / 2, offsetY = (height - fittedHeight) / 2;
        for (int y = 0; y < fittedHeight; y++)
        for (int x = 0; x < fittedWidth; x++)
        {
            var src = iconPixels[y * fittedWidth + x];
            int i = (y + offsetY) * width + (x + offsetX);
            var dst = pixels[i];
            float alpha = src.a + dst.a * (1f - src.a);
            pixels[i] = alpha <= 0f ? Color.clear
                : new Color((src.r * src.a + dst.r * dst.a * (1f - src.a)) / alpha,
                            (src.g * src.a + dst.g * dst.a * (1f - src.a)) / alpha,
                            (src.b * src.a + dst.b * dst.a * (1f - src.a)) / alpha,
                            alpha);
        }

        return ToTexture(pixels, width, height);
    }

    static Texture2D ToTexture(Color[] pixels, int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    public override bool HasPreviewGUI() => (Icon != null && Icon.texture != null) || BackgroundColor != null;

    public override GUIContent GetPreviewTitle() => new GUIContent(PreviewTitle);

    public override void OnPreviewGUI(Rect rect, GUIStyle background)
    {
        DrawIcon(rect, Icon, AssetPreview.GetMiniThumbnail(target), BackgroundColor);
    }

    /// <summary>
    /// Draws a sprite (or the fallback texture when there is no sprite) centered in rect,
    /// on a rounded background when one is given. Usable from editors and property drawers.
    /// </summary>
    public static void DrawIcon(Rect rect, Sprite sprite, Texture fallback, Color? background)
    {
        // Square area centered in rect
        float side = Mathf.Min(rect.width, rect.height);
        var square = new Rect(rect.center.x - side / 2, rect.center.y - side / 2, side, side);

        if (background != null)
        {
            GUI.DrawTexture(square, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, background.Value, 0, side * CornerRadiusFraction);
            float inset = side * (1f - IconInset) / 2;
            square = new Rect(square.x + inset, square.y + inset, square.width - inset * 2, square.height - inset * 2);
        }

        Texture texture;
        Rect texCoords;
        Vector2 sourceSize;
        if (sprite != null && sprite.texture != null)
        {
            texture = sprite.texture;
            var spriteRect = sprite.textureRect;
            texCoords = new Rect(spriteRect.x / texture.width, spriteRect.y / texture.height,
                                 spriteRect.width / texture.width, spriteRect.height / texture.height);
            sourceSize = spriteRect.size;
        }
        else if (fallback != null)
        {
            texture = fallback;
            texCoords = new Rect(0, 0, 1, 1);
            sourceSize = new Vector2(fallback.width, fallback.height);
        }
        else return;

        // Keep the source's aspect ratio inside the square
        float scale = Mathf.Min(square.width / sourceSize.x, square.height / sourceSize.y);
        var size = sourceSize * scale;
        GUI.DrawTextureWithTexCoords(new Rect(square.center - size / 2, size), texture, texCoords);
    }

    // Blit through a RenderTexture so the source texture doesn't need Read/Write enabled
    static Color[] ReadPixels(Texture source, Rect sourceRect, int width, int height)
    {
        var uvScale = new Vector2(sourceRect.width / source.width, sourceRect.height / source.height);
        var uvOffset = new Vector2(sourceRect.x / source.width, sourceRect.y / source.height);

        var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previousActive = RenderTexture.active;
        Graphics.Blit(source, renderTexture, uvScale, uvOffset);

        RenderTexture.active = renderTexture;
        var readback = new Texture2D(width, height, TextureFormat.RGBA32, false);
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply();
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);

        var pixels = readback.GetPixels();
        DestroyImmediate(readback);
        return pixels;
    }

    static void FillRoundedRect(Color[] pixels, int width, int height, Color color, float radius)
    {
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Distance from the pixel center to the nearest point of the rect shrunk by radius
            float dx = Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (width - radius), 0f);
            float dy = Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (height - radius), 0f);
            float coverage = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));  // soft 1px edge
            if (coverage > 0f) pixels[y * width + x] = new Color(color.r, color.g, color.b, color.a * coverage);
        }
    }
}
