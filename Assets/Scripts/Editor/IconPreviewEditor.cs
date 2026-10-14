using UnityEditor;
using UnityEngine;

/// <summary>Shows an asset's Icon as its Project window thumbnail and as a preview in the Inspector.</summary>
public abstract class IconPreviewEditor : Editor
{
    protected abstract Sprite Icon { get; }
    protected abstract string PreviewTitle { get; }

    public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
    {
        var icon = Icon;
        if (icon == null || icon.texture == null)
            return base.RenderStaticPreview(assetPath, subAssets, width, height);

        // Fit the sprite inside width x height while keeping its aspect ratio
        var spriteRect = icon.textureRect;
        float scale = Mathf.Min(width / spriteRect.width, height / spriteRect.height);
        int fittedWidth = Mathf.Max(1, Mathf.RoundToInt(spriteRect.width * scale));
        int fittedHeight = Mathf.Max(1, Mathf.RoundToInt(spriteRect.height * scale));

        // Blit through a RenderTexture so the source texture doesn't need Read/Write enabled
        var texture = icon.texture;
        var uvScale = new Vector2(spriteRect.width / texture.width, spriteRect.height / texture.height);
        var uvOffset = new Vector2(spriteRect.x / texture.width, spriteRect.y / texture.height);

        var renderTexture = RenderTexture.GetTemporary(fittedWidth, fittedHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previousActive = RenderTexture.active;
        Graphics.Blit(texture, renderTexture, uvScale, uvOffset);

        RenderTexture.active = renderTexture;
        var fitted = new Texture2D(fittedWidth, fittedHeight, TextureFormat.RGBA32, false);
        fitted.ReadPixels(new Rect(0, 0, fittedWidth, fittedHeight), 0, 0);
        fitted.Apply();
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);

        // Center the fitted image on a transparent canvas of the requested size
        var preview = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var clear = new Color32[width * height];
        preview.SetPixels32(clear);
        preview.SetPixels((width - fittedWidth) / 2, (height - fittedHeight) / 2, fittedWidth, fittedHeight, fitted.GetPixels());
        preview.Apply();
        DestroyImmediate(fitted);

        return preview;
    }

    public override bool HasPreviewGUI() => Icon != null && Icon.texture != null;

    public override GUIContent GetPreviewTitle() => new GUIContent(PreviewTitle);

    public override void OnPreviewGUI(Rect rect, GUIStyle background)
    {
        var icon = Icon;
        if (icon == null || icon.texture == null) return;

        var texture = icon.texture;
        var spriteRect = icon.textureRect;
        var texCoords = new Rect(spriteRect.x / texture.width, spriteRect.y / texture.height,
                                 spriteRect.width / texture.width, spriteRect.height / texture.height);

        // Keep the sprite's aspect ratio inside the preview area
        float scale = Mathf.Min(rect.width / spriteRect.width, rect.height / spriteRect.height);
        var size = new Vector2(spriteRect.width * scale, spriteRect.height * scale);
        var drawRect = new Rect(rect.center - size / 2, size);

        GUI.DrawTextureWithTexCoords(drawRect, texture, texCoords);
    }
}
