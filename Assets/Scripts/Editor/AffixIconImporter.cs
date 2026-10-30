using UnityEditor;

/// <summary>
/// Imports every image in the icon folders (affixes, damage types, stats) as a single UI sprite: transparent, no
/// mipmaps, no compression. Applies to icons added or redrawn there later too.
/// </summary>
public class AffixIconImporter : AssetPostprocessor
{
    static readonly string[] Folders = { "Assets/Resources/AffixIcons/", "Assets/Resources/DamageTypeIcons/", "Assets/Resources/StatIcons/" };

    void OnPreprocessTexture()
    {
        if (!System.Array.Exists(Folders, folder => assetPath.StartsWith(folder))) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
