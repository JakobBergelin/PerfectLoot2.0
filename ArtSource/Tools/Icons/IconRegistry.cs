// The list of icons, plus writing PNGs, sprite .meta files and review sheets. Run through make_icons.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;

public class IconEntry
{
    public string Folder, Name; public Action<Ic> Draw;
}

public static class IconRegistry
{
    public static readonly List<IconEntry> All = new List<IconEntry>();

    public static void Reg(string folder, string name, Action<Ic> draw)
    {
        All.Add(new IconEntry { Folder = folder, Name = name, Draw = draw });
    }

    static List<IconEntry> Matching(string pattern)
    {
        if (All.Count == 0) { SkillIcons.Register(); PassiveIcons.Register(); ItemIcons.Register(); PowerIcons.Register(); }
        var regex = new Regex("^(" + pattern + ")$", RegexOptions.IgnoreCase);
        return All.FindAll(e => regex.IsMatch(e.Name) || regex.IsMatch(e.Folder));
    }

    /// <summary>Draws every icon whose name or folder matches the regex and writes a .meta beside any new PNG.</summary>
    public static void Run(string imagesRoot, string pattern)
    {
        foreach (var entry in Matching(pattern))
        {
            string dir = Folder(imagesRoot, entry.Folder);
            string file = Path.Combine(dir, entry.Name + ".png");
            using (var ic = new Ic())
            {
                entry.Draw(ic);
                using (var bmp = ic.Result()) bmp.Save(file, ImageFormat.Png);
            }
            if (!File.Exists(file + ".meta"))
                File.WriteAllText(file + ".meta", SpriteMeta.Text(Guid.NewGuid().ToString("N"), entry.Folder == "SkillIcons" ? 128 : 2048));
        }
    }

    /// <summary>The folder for an icon, created with a .meta for each new level so Unity doesn't make its own.</summary>
    static string Folder(string imagesRoot, string relative)
    {
        string dir = imagesRoot;
        foreach (var name in relative.Split('/'))
        {
            dir = Path.Combine(dir, name);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(dir + ".meta"))
                File.WriteAllText(dir + ".meta", "fileFormatVersion: 2\nguid: " + Guid.NewGuid().ToString("N") + "\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        }
        return dir;
    }

    /// <summary>A review sheet: each icon at 128 px with a 48 px copy on a dark slot beneath it.</summary>
    public static void Sheet(string imagesRoot, string pattern, string output, int columns)
    {
        var entries = Matching(pattern);
        const int cell = 136, rowHeight = 196;
        int rows = (entries.Count + columns - 1) / columns;
        using (var sheet = new Bitmap(columns * cell + 8, rows * rowHeight + 8))
        using (var g = Graphics.FromImage(sheet))
        using (var font = new Font("Arial", 9f))
        {
            g.Clear(Pal.Hex("#26222C"));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            for (int i = 0; i < entries.Count; i++)
            {
                int x = 8 + i % columns * cell, y = 8 + i / columns * rowHeight;
                using (var icon = new Bitmap(Path.Combine(Path.Combine(imagesRoot, entries[i].Folder), entries[i].Name + ".png")))
                {
                    g.DrawImage(icon, x, y, 128, 128);
                    g.DrawImage(icon, x + 40, y + 130, 48, 48);
                }
                g.DrawString(entries[i].Name, font, Brushes.White, x, y + 179);
            }
            sheet.Save(output, ImageFormat.Png);
        }
    }
}

/// <summary>
/// Sprite importer settings matching the existing icons. Icons are drawn at 256 px but shown at 34 to 64 px, so they need
/// mipmaps to stay smooth. AffixIconImporter turns mipmaps off in SkillIcons, so those import at 128 px instead.
/// </summary>
public static class SpriteMeta
{
    public static string Text(string guid, int maxSize)
    {
        return string.Join("\n", new[] {
"fileFormatVersion: 2",
"guid: " + guid,
"TextureImporter:",
"  internalIDToNameTable: []",
"  externalObjects: {}",
"  serializedVersion: 13",
"  mipmaps:",
"    mipMapMode: 0",
"    enableMipMap: 1",
"    sRGBTexture: 1",
"    linearTexture: 0",
"    fadeOut: 0",
"    borderMipMap: 0",
"    mipMapsPreserveCoverage: 0",
"    alphaTestReferenceValue: 0.5",
"    mipMapFadeDistanceStart: 1",
"    mipMapFadeDistanceEnd: 3",
"  bumpmap:",
"    convertToNormalMap: 0",
"    externalNormalMap: 0",
"    heightScale: 0.25",
"    normalMapFilter: 0",
"    flipGreenChannel: 0",
"  isReadable: 0",
"  streamingMipmaps: 0",
"  streamingMipmapsPriority: 0",
"  vTOnly: 0",
"  ignoreMipmapLimit: 0",
"  grayScaleToAlpha: 0",
"  generateCubemap: 6",
"  cubemapConvolution: 0",
"  seamlessCubemap: 0",
"  textureFormat: 1",
"  maxTextureSize: " + maxSize,
"  textureSettings:",
"    serializedVersion: 2",
"    filterMode: 1",
"    aniso: 1",
"    mipBias: 0",
"    wrapU: 1",
"    wrapV: 1",
"    wrapW: 1",
"  nPOTScale: 0",
"  lightmap: 0",
"  compressionQuality: 50",
"  spriteMode: 1",
"  spriteExtrude: 1",
"  spriteMeshType: 1",
"  alignment: 0",
"  spritePivot: {x: 0.5, y: 0.5}",
"  spritePixelsToUnits: 100",
"  spriteBorder: {x: 0, y: 0, z: 0, w: 0}",
"  spriteGenerateFallbackPhysicsShape: 1",
"  alphaUsage: 1",
"  alphaIsTransparency: 1",
"  spriteTessellationMethod: 0",
"  spriteTessellationDetail: -1",
"  spriteGeometrySubdivision: -1",
"  textureType: 8",
"  textureShape: 1",
"  singleChannelComponent: 0",
"  flipbookRows: 1",
"  flipbookColumns: 1",
"  maxTextureSizeSet: 0",
"  compressionQualitySet: 0",
"  textureFormatSet: 0",
"  ignorePngGamma: 0",
"  applyGammaDecoding: 0",
"  swizzle: 50462976",
"  cookieLightType: 0",
"  platformSettings:",
"  - serializedVersion: 4",
"    buildTarget: DefaultTexturePlatform",
"    maxTextureSize: " + maxSize,
"    resizeAlgorithm: 0",
"    textureFormat: -1",
"    textureCompression: 0",
"    compressionQuality: 50",
"    crunchedCompression: 0",
"    allowsAlphaSplitting: 0",
"    overridden: 0",
"    ignorePlatformSupport: 0",
"    androidETC2FallbackOverride: 0",
"    forceMaximumCompressionQuality_BC6H_BC7: 0",
"  spriteSheet:",
"    serializedVersion: 2",
"    sprites: []",
"    outline: []",
"    customData: ",
"    physicsShape: []",
"    bones: []",
"    spriteID: 5e97eb03825dee720800000000000000",
"    internalID: 0",
"    vertices: []",
"    indices: ",
"    edges: []",
"    weights: []",
"    secondaryTextures: []",
"    spriteCustomMetadata:",
"      entries: []",
"    nameFileIdTable: {}",
"  mipmapLimitGroupName: ",
"  pSDRemoveMatte: 0",
"  userData: ",
"  assetBundleName: ",
"  assetBundleVariant: ",
""});
    }
}
