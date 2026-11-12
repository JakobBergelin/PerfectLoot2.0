using System.Collections.Generic;
using LevelGeneration;
using UnityEngine;

/// <summary>
/// Procedural geometry for one WFC tile of a biome tile set, made from its four corner terrains (no Blender model).
/// Each terrain gets a score field: its corners blended bilinearly, plus a little noise that fades to zero at the
/// tile's border. The terrain with the highest score wins each point, and marching squares turn the winners into
/// floor, rock walls and liquid pools. Along a border the fields depend only on the two corners of that edge, so any
/// two tiles whose corners match meet seamlessly, in every rotation.
///
/// Output: one visual mesh with a submesh per used <see cref="Part"/>, and collider meshes on the same layers as the
/// placeholder tiles (ground, wall, liquid surface, liquid blocker). UV0 carries what the biome shaders need after the
/// level merges meshes: floor texture coordinates, distance from the shore for liquids and glow, depth for chasms.
/// </summary>
public class BiomeTileGenerator
{
    public const float TileSize = 5f;
    const float Half = TileSize * 0.5f;
    const int N = 20;                       // Samples per side - 1 (0.25 m cells)
    const float Step = TileSize / N;
    const float MetersPerScore = TileSize * 0.5f;   // A score difference of 1 is about 2.5 m from a boundary

    public enum Part { Floor, FloorAlt, WallSide, WallTop, Bank, Chasm, Liquid, Glow, Rock, Accent, Count }

    /// <summary>The look of a biome's geometry. Colors live on the materials.</summary>
    public class Style
    {
        public float WallHeight = 3f;
        public float WallTopJitter = 0.45f;     // Meters the wall top rises and falls
        public float WallBulge = 0.3f;          // Meters the wall faces push in and out between the rows
        public int WallRows = 3;
        public float BoundaryNoise = 0.16f;     // How organic the borders between terrains are (0: smooth curves)
        public float NoiseScale = 0.8f;
        public float LiquidSurfaceY = -0.3f;
        public float LiquidBedY = -1.1f;
        public bool Chasm;                      // Liquid corners are a bottomless drop instead of a pool
        public float ChasmDepth = 12f;
        public float GlowReach;                 // > 0: a light pool on the floor around the liquid
        public int CrownRocks = 3;              // Fallen rocks on top of a solid wall tile
        public float RimRockChance = 0.35f;     // Chance per bank segment of a stone on the rim
        public float FootRockChance = 0.12f;    // Chance per wall segment of a pebble at its foot
        public float RockSize = 0.45f;
        public Accent Accent;                   // Biome decoration in the Accent material
        public float AccentChance = 0.25f;
        public FloorFeature Feature;           // Drawn on the plain floor variant tile
    }

    public enum Accent { None, MagmaSeam, Crystals, Mushrooms, Runes }

    /// <summary>A flat feature on a plain floor tile (walkable, decoration only).</summary>
    public enum FloorFeature { None, Cracks, RuneCircle, Puddles }

    // ---------- result ----------

    public Mesh Visual;
    public readonly List<Part> VisualParts = new List<Part>();     // Material slot order of Visual's submeshes
    public Mesh Ground, Wall, LiquidSurface, LiquidBlocker;        // Null when the tile has none

    // ---------- state ----------

    readonly Style _style;
    readonly WfcTerrain[] _corners;
    readonly System.Random _random;
    readonly int _seed;
    readonly float[] _dF = new float[(N + 1) * (N + 1)];
    readonly float[] _dW = new float[(N + 1) * (N + 1)];
    readonly float[] _dL = new float[(N + 1) * (N + 1)];
    readonly Builder[] _parts = new Builder[(int)Part.Count];

    public BiomeTileGenerator(Style style, WfcTerrain[] corners, int seed)
    {
        _style = style;
        _corners = corners;
        _seed = seed;
        _random = new System.Random(seed);
        for (int i = 0; i < _parts.Length; i++) _parts[i] = new Builder();
    }

    /// <summary>Builds the tile. floorAlt: use the second floor material (variety on plain floor tiles).</summary>
    public void Build(bool floorAlt = false, bool feature = false)
    {
        Fields();
        var floor = floorAlt ? Part.FloorAlt : Part.Floor;

        // Floor at y = 0 wherever floor wins
        Fill(_dF, 0f, floor, p => new Vector2((p.x + Half) / TileSize, (p.y + Half) / TileSize));
        if (feature) BuildFeature();

        if (HasAny(_dW)) BuildWalls();
        if (HasAny(_dL)) BuildLiquid();

        Visual = Combine(out _);
        Ground = Collider(InvertedField(_dL), 0f, 0f, cap: true, sides: false);
        Wall = HasAny(_dW) ? Collider(_dW, 0f, _style.WallHeight, cap: true, sides: true) : null;
        if (HasAny(_dL))
        {
            LiquidSurface = Collider(_dL, _style.Chasm ? -0.35f : _style.LiquidSurfaceY, 0f, cap: true, sides: false);
            LiquidBlocker = Collider(_dL, 0f, 2f, cap: true, sides: true);
        }
    }

    // ---------- fields ----------

    void Fields()
    {
        for (int j = 0; j <= N; j++)
            for (int i = 0; i <= N; i++)
            {
                float u = i / (float)N, v = j / (float)N;
                float x = -Half + i * Step, z = -Half + j * Step;
                float fade = Mathf.Clamp01(4f * u * (1f - u) * 1.5f) * Mathf.Clamp01(4f * v * (1f - v) * 1.5f);
                float sF = Score(WfcTerrain.Walkable, u, v) + Noise(x, z, 1) * fade * _style.BoundaryNoise;
                float sW = Score(WfcTerrain.Wall, u, v) + Noise(x, z, 2) * fade * _style.BoundaryNoise;
                float sL = Score(WfcTerrain.Water, u, v) + Noise(x, z, 3) * fade * _style.BoundaryNoise;
                int k = i + j * (N + 1);
                _dF[k] = sF - Mathf.Max(sW, sL);
                _dW[k] = sW - Mathf.Max(sF, sL);
                _dL[k] = sL - Mathf.Max(sF, sW);
            }
    }

    float Score(WfcTerrain terrain, float u, float v)
    {
        float Is(int c) => _corners[c] == terrain ? 1f : 0f;
        return Is(0) * (1 - u) * (1 - v) + Is(1) * u * (1 - v) + Is(2) * u * v + Is(3) * (1 - u) * v;
    }

    /// <summary>Smooth value noise in -0.5..0.5, two octaves, different per channel and per tile seed.</summary>
    float Noise(float x, float z, int channel)
    {
        float s = _style.NoiseScale;
        return (Value(x * s, z * s, channel) * 0.7f + Value(x * s * 2.3f, z * s * 2.3f, channel + 17) * 0.3f) - 0.5f;
    }

    float Value(float x, float z, int channel)
    {
        int xi = Mathf.FloorToInt(x), zi = Mathf.FloorToInt(z);
        float fx = x - xi, fz = z - zi;
        fx = fx * fx * (3 - 2 * fx);
        fz = fz * fz * (3 - 2 * fz);
        float a = Hash(xi, zi, channel), b = Hash(xi + 1, zi, channel), c = Hash(xi, zi + 1, channel), d = Hash(xi + 1, zi + 1, channel);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    float Hash(int x, int z, int channel)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + channel * 2147483647 + _seed * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    /// <summary>0 at the tile's border, 1 in its middle: noise that moves geometry is multiplied by this.</summary>
    static float Fade(Vector2 p)
    {
        float u = (p.x + Half) / TileSize, v = (p.y + Half) / TileSize;
        return Mathf.Clamp01(4f * u * (1f - u) * 1.5f) * Mathf.Clamp01(4f * v * (1f - v) * 1.5f);
    }

    static bool HasAny(float[] field)
    {
        foreach (var value in field) if (value > 0f) return true;
        return false;
    }

    static float[] InvertedField(float[] field)
    {
        var result = new float[field.Length];
        for (int i = 0; i < field.Length; i++) result[i] = -field[i];
        return result;
    }

    /// <summary>The field's value at a point, bilinear between samples.</summary>
    static float Sample(float[] field, Vector2 p)
    {
        float fx = Mathf.Clamp((p.x + Half) / Step, 0f, N - 0.0001f), fz = Mathf.Clamp((p.y + Half) / Step, 0f, N - 0.0001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float a = field[i + j * (N + 1)], b = field[i + 1 + j * (N + 1)], c = field[i + (j + 1) * (N + 1)], d = field[i + 1 + (j + 1) * (N + 1)];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    // ---------- marching squares ----------

    static Vector2 Point(int i, int j) => new Vector2(-Half + i * Step, -Half + j * Step);

    /// <summary>Each cell's inside polygon (field > 0), as points in xz.</summary>
    static IEnumerable<List<Vector2>> Polygons(float[] field)
    {
        var polygon = new List<Vector2>(8);
        var corner = new int[4];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                corner[0] = i + j * (N + 1); corner[1] = i + 1 + j * (N + 1); corner[2] = i + 1 + (j + 1) * (N + 1); corner[3] = i + (j + 1) * (N + 1);
                polygon.Clear();
                for (int c = 0; c < 4; c++)
                {
                    int next = (c + 1) % 4;
                    float a = field[corner[c]], b = field[corner[next]];
                    Vector2 pa = CornerPoint(i, j, c), pb = CornerPoint(i, j, next);
                    if (a > 0f) polygon.Add(pa);
                    if ((a > 0f) != (b > 0f)) polygon.Add(Vector2.Lerp(pa, pb, a / (a - b)));
                }
                if (polygon.Count >= 3) yield return polygon;
            }
    }

    static Vector2 CornerPoint(int i, int j, int c) => c switch
    {
        0 => Point(i, j),
        1 => Point(i + 1, j),
        2 => Point(i + 1, j + 1),
        _ => Point(i, j + 1),
    };

    /// <summary>Boundary segments of the region field > 0, with the horizontal normal pointing out of the region.</summary>
    static IEnumerable<(Vector2 A, Vector2 B, Vector2 Out)> Contour(float[] field)
    {
        var crossings = new List<Vector2>(4);
        var corner = new int[4];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                corner[0] = i + j * (N + 1); corner[1] = i + 1 + j * (N + 1); corner[2] = i + 1 + (j + 1) * (N + 1); corner[3] = i + (j + 1) * (N + 1);
                crossings.Clear();
                for (int c = 0; c < 4; c++)
                {
                    int next = (c + 1) % 4;
                    float a = field[corner[c]], b = field[corner[next]];
                    if ((a > 0f) != (b > 0f)) crossings.Add(Vector2.Lerp(CornerPoint(i, j, c), CornerPoint(i, j, next), a / (a - b)));
                }
                if (crossings.Count == 2) yield return Oriented(field, crossings[0], crossings[1]);
                else if (crossings.Count == 4)
                {
                    // Saddle: the inside corners 0 and 2 (or 1 and 3) connect through the middle
                    bool zeroInside = field[corner[0]] > 0f;
                    if (zeroInside) { yield return Oriented(field, crossings[0], crossings[1]); yield return Oriented(field, crossings[2], crossings[3]); }
                    else { yield return Oriented(field, crossings[3], crossings[0]); yield return Oriented(field, crossings[1], crossings[2]); }
                }
            }
    }

    static (Vector2, Vector2, Vector2) Oriented(float[] field, Vector2 a, Vector2 b)
    {
        var direction = (b - a).normalized;
        var normal = new Vector2(direction.y, -direction.x);
        var middle = (a + b) * 0.5f;
        if (Sample(field, middle + normal * 0.06f) > Sample(field, middle - normal * 0.06f)) normal = -normal;
        return (a, b, normal);
    }

    // ---------- building blocks ----------

    /// <summary>A flat cap over the region at height y; uv maps a point to its UV0.</summary>
    void Fill(float[] field, float y, Part part, System.Func<Vector2, Vector2> uv, System.Func<Vector2, float> height = null)
    {
        var builder = _parts[(int)part];
        foreach (var polygon in Polygons(field))
        {
            int first = builder.Shared(At(polygon[0], height, y), Vector3.up, uv(polygon[0]));
            for (int k = 1; k < polygon.Count - 1; k++)
            {
                int b = builder.Shared(At(polygon[k], height, y), Vector3.up, uv(polygon[k]));
                int c = builder.Shared(At(polygon[k + 1], height, y), Vector3.up, uv(polygon[k + 1]));
                builder.TriangleFacing(first, b, c, Vector3.up);
            }
        }
    }

    static Vector3 At(Vector2 p, System.Func<Vector2, float> height, float y) => new Vector3(p.x, height != null ? height(p) : y, p.y);

    float WallTop(Vector2 p) => _style.WallHeight + Noise(p.x, p.y, 7) * 2f * _style.WallTopJitter * Fade(p);

    /// <summary>Horizontal push of a wall face point at a row: the same for every segment sharing the point.</summary>
    Vector3 Bulge(Vector2 p, int row)
    {
        float fade = Fade(p) * _style.WallBulge * 2f;
        return new Vector3(Noise(p.x * 1.7f, p.y * 1.7f, 20 + row), 0f, Noise(p.x * 1.7f, p.y * 1.7f, 40 + row)) * fade;
    }

    void BuildWalls()
    {
        var s = _style;
        float bottom = s.Chasm ? -s.ChasmDepth : s.LiquidBedY - 0.1f;
        Fill(_dW, 0f, Part.WallTop, p => new Vector2(p.x * 0.3f, p.y * 0.3f), WallTop);

        var side = _parts[(int)Part.WallSide];
        var chasm = _parts[(int)Part.Chasm];
        foreach (var (a, b, normal) in Contour(_dW))
        {
            var outward = new Vector3(normal.x, 0f, normal.y);
            float length = Vector2.Distance(a, b);
            // Below the floor: chasm material where the wall drops into a chasm, plain side otherwise (hidden)
            var underBuilder = s.Chasm ? chasm : side;
            Vector3 a0 = new Vector3(a.x, bottom, a.y), b0 = new Vector3(b.x, bottom, b.y);
            Vector3 a1 = new Vector3(a.x, 0f, a.y), b1 = new Vector3(b.x, 0f, b.y);
            underBuilder.Quad(a0, b0, b1, a1, outward, new Vector2(0f, -bottom), new Vector2(length, -bottom), new Vector2(length, 0f), new Vector2(0f, 0f));

            // Above the floor: rows that bulge in and out, flat shaded like hewn rock
            Vector3 lowA = a1, lowB = b1;
            for (int row = 1; row <= s.WallRows; row++)
            {
                bool top = row == s.WallRows;
                float t = row / (float)s.WallRows;
                Vector3 highA = top ? new Vector3(a.x, WallTop(a), a.y) : new Vector3(a.x, Mathf.Lerp(0f, WallTop(a), t), a.y) + Bulge(a, row);
                Vector3 highB = top ? new Vector3(b.x, WallTop(b), b.y) : new Vector3(b.x, Mathf.Lerp(0f, WallTop(b), t), b.y) + Bulge(b, row);
                var part = row == 1 && s.Accent == Accent.MagmaSeam && Noise(a.x, a.y, 60) > 0.05f ? _parts[(int)Part.Accent] : side;
                if (part != side)
                {
                    // A glowing seam at the foot of the wall, then the rest of the row in rock
                    Vector3 seamA = Vector3.Lerp(lowA, highA, 0.3f), seamB = Vector3.Lerp(lowB, highB, 0.3f);
                    part.Quad(lowA, lowB, seamB, seamA, outward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    side.QuadFlat(seamA, seamB, highB, highA, outward);
                }
                else side.QuadFlat(lowA, lowB, highB, highA, outward);
                lowA = highA; lowB = highB;
            }

            // Pebbles at the foot of the wall, on the floor side
            if (_random.NextDouble() < s.FootRockChance)
            {
                var middle = (a + b) * 0.5f + normal * (0.25f + (float)_random.NextDouble() * 0.3f);
                if (Sample(_dF, middle) > 0f) Rock(new Vector3(middle.x, 0f, middle.y), s.RockSize * (0.4f + (float)_random.NextDouble() * 0.4f));
            }
            if (s.Accent == Accent.Crystals && _random.NextDouble() < s.AccentChance * 0.5f)
            {
                var foot = (a + b) * 0.5f + normal * 0.15f;
                Crystal(new Vector3(foot.x, 0f, foot.y), 0.5f + (float)_random.NextDouble() * 0.7f, new Vector3(normal.x, 0.9f, normal.y).normalized);
            }
            if (s.Accent == Accent.Mushrooms && _random.NextDouble() < s.AccentChance * 0.5f)
            {
                var foot = (a + b) * 0.5f + normal * (0.2f + (float)_random.NextDouble() * 0.25f);
                if (Sample(_dF, foot) > 0f) Mushroom(new Vector3(foot.x, 0f, foot.y), 0.35f + (float)_random.NextDouble() * 0.35f);
            }
        }

        // Fallen rocks and growths on the wall tops, well inside the wall
        int attempts = s.CrownRocks * 6;
        int placed = 0;
        for (int k = 0; k < attempts && placed < s.CrownRocks; k++)
        {
            var p = new Vector2(((float)_random.NextDouble() - 0.5f) * TileSize * 0.9f, ((float)_random.NextDouble() - 0.5f) * TileSize * 0.9f);
            if (Sample(_dW, p) < 0.3f) continue;
            placed++;
            var basePoint = new Vector3(p.x, WallTop(p) - 0.1f, p.y);
            if (s.Accent == Accent.Crystals && _random.NextDouble() < 0.6) Crystal(basePoint, 1f + (float)_random.NextDouble(), Vector3.up);
            else Rock(basePoint, s.RockSize * (1.2f + (float)_random.NextDouble() * 1.2f));
        }
    }

    void BuildLiquid()
    {
        var s = _style;
        float depth = s.Chasm ? s.ChasmDepth : -s.LiquidBedY;
        var bank = _parts[(int)(s.Chasm ? Part.Chasm : Part.Bank)];

        // Bank faces drop from the floor's edge and face into the pool (the contour's normal points out of it)
        foreach (var (a, b, normal) in Contour(_dL))
        {
            var inward = new Vector3(-normal.x, 0f, -normal.y);
            float length = Vector2.Distance(a, b);
            Vector3 a1 = new Vector3(a.x, 0f, a.y), b1 = new Vector3(b.x, 0f, b.y);
            Vector3 a0 = new Vector3(a.x, -depth, a.y), b0 = new Vector3(b.x, -depth, b.y);
            // Face from inside the pool: b, a order keeps it facing inward
            bank.Quad(b1, a1, a0, b0, inward, new Vector2(0f, 0f), new Vector2(length, 0f), new Vector2(length, depth), new Vector2(0f, depth));

            if (_random.NextDouble() < s.RimRockChance)
            {
                var rim = (a + b) * 0.5f + normal * (0.1f + (float)_random.NextDouble() * 0.2f);
                Rock(new Vector3(rim.x, -0.05f, rim.y), s.RockSize * (0.6f + (float)_random.NextDouble() * 0.6f));
            }
        }

        if (s.Chasm)
        {
            // The bottom of the drop, all fog
            Fill(_dL, -depth, Part.Chasm, p => new Vector2(0f, depth));
        }
        else
        {
            // The surface reaches a little under the banks so no gap shows; UV0.x is meters from the shore
            var surface = new float[_dL.Length];
            for (int k = 0; k < surface.Length; k++) surface[k] = _dL[k] + 0.06f;
            Fill(surface, s.LiquidSurfaceY, Part.Liquid, p => new Vector2(Mathf.Max(0f, Sample(_dL, p)) * MetersPerScore, 0f));
        }

        if (s.GlowReach > 0f)
        {
            // A band on the floor beside the pool, lying just above it: UV0.x is meters from the pool's edge
            float reach = s.GlowReach / MetersPerScore;
            var band = new float[_dL.Length];
            for (int k = 0; k < band.Length; k++) band[k] = Mathf.Min(_dL[k] + reach, -_dL[k] + 0.02f);
            Fill(band, 0.03f, Part.Glow, p => new Vector2(Mathf.Max(0f, -Sample(_dL, p)) * MetersPerScore, 0f));
        }
    }

    // ---------- decoration ----------

    void BuildFeature()
    {
        var accent = _parts[(int)Part.Accent];
        switch (_style.Feature)
        {
            case FloorFeature.Cracks:
                // Glowing cracks branching from near the middle, thinning as they go
                for (int branch = 0; branch < 4; branch++)
                {
                    var p = new Vector2(((float)_random.NextDouble() - 0.5f) * 0.8f, ((float)_random.NextDouble() - 0.5f) * 0.8f);
                    float angle = branch * Mathf.PI * 0.5f + (float)_random.NextDouble() * 0.9f;
                    float width = 0.14f;
                    for (int step = 0; step < 6; step++)
                    {
                        angle += ((float)_random.NextDouble() - 0.5f) * 1.1f;
                        var next = p + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (0.3f + (float)_random.NextDouble() * 0.25f);
                        if (Mathf.Abs(next.x) > Half - 0.3f || Mathf.Abs(next.y) > Half - 0.3f) break;
                        float nextWidth = width * 0.78f;
                        FlatStrip(accent, p, next, width, nextWidth, 0.012f);
                        p = next; width = nextWidth;
                    }
                }
                break;
            case FloorFeature.RuneCircle:
                // A landmark: two glowing rings with glyphs between them
                FlatRing(accent, Vector2.zero, 1.55f, 1.7f, 0.012f, 40);
                FlatRing(accent, Vector2.zero, 0.85f, 0.95f, 0.012f, 28);
                for (int g = 0; g < 6; g++)
                {
                    float a = g * Mathf.PI / 3f + 0.3f;
                    var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 1.25f;
                    var radial = c.normalized; var side = new Vector2(-radial.y, radial.x);
                    FlatQuad(accent, c + radial * 0.18f, c + side * 0.09f, c - radial * 0.18f, c - side * 0.09f, 0.013f);
                }
                break;
            case FloorFeature.Puddles:
                // Shallow puddles of the biome's water lying on the floor
                var liquid = _parts[(int)Part.Liquid];
                for (int k = 0; k < 1; k++)
                {
                    var center = new Vector2(((float)_random.NextDouble() - 0.5f) * 1.6f, ((float)_random.NextDouble() - 0.5f) * 1.6f);
                    float radius = 0.75f + (float)_random.NextDouble() * 0.45f;
                    const int sides = 14;
                    int middle = liquid.Add(new Vector3(center.x, 0.015f, center.y), Vector3.up, new Vector2(radius, 0f));
                    int first = -1, previous = -1;
                    for (int s = 0; s <= sides; s++)
                    {
                        float a = s % sides * Mathf.PI * 2f / sides;
                        float r = radius * (0.55f + Value(center.x * 3f + Mathf.Cos(a) * 1.3f, center.y * 3f + Mathf.Sin(a) * 1.3f, 90) * 0.9f);
                        int index = s == sides ? first : liquid.Add(new Vector3(center.x + Mathf.Cos(a) * r, 0.015f, center.y + Mathf.Sin(a) * r), Vector3.up, Vector2.zero);
                        if (s == 0) first = index; else liquid.TriangleFacing(middle, previous, index, Vector3.up);
                        previous = index;
                    }
                }
                break;
        }
    }

    /// <summary>A flat tapered strip lying on the floor from a to b.</summary>
    static void FlatStrip(Builder builder, Vector2 a, Vector2 b, float widthA, float widthB, float y)
    {
        var direction = (b - a).normalized;
        var side = new Vector2(-direction.y, direction.x);
        FlatQuad(builder, a - side * widthA * 0.5f, b - side * widthB * 0.5f, b + side * widthB * 0.5f, a + side * widthA * 0.5f, y);
    }

    static void FlatQuad(Builder builder, Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y)
    {
        builder.Quad(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(c.x, y, c.y), new Vector3(d.x, y, d.y), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
    }

    static void FlatRing(Builder builder, Vector2 center, float inner, float outer, float y, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            float a0 = i * Mathf.PI * 2f / steps, a1 = (i + 1) * Mathf.PI * 2f / steps;
            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            FlatQuad(builder, center + d0 * inner, center + d1 * inner, center + d1 * outer, center + d0 * outer, y);
        }
    }

    /// <summary>A chunky rock: a box with its corners pushed around, sitting on basePoint.</summary>
    void Rock(Vector3 basePoint, float size)
    {
        var rock = _parts[(int)Part.Rock];
        float yaw = (float)_random.NextDouble() * Mathf.PI * 2f;
        var turn = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
        var scale = new Vector3(size * (0.8f + (float)_random.NextDouble() * 0.5f), size * (0.45f + (float)_random.NextDouble() * 0.4f), size * (0.8f + (float)_random.NextDouble() * 0.5f));
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            var offset = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? 0f : 1f, (i & 4) == 0 ? -0.5f : 0.5f);
            if ((i & 2) != 0) offset = new Vector3(offset.x * (0.55f + (float)_random.NextDouble() * 0.35f), offset.y * (0.8f + (float)_random.NextDouble() * 0.4f), offset.z * (0.55f + (float)_random.NextDouble() * 0.35f));
            corners[i] = basePoint + turn * Vector3.Scale(offset, scale) + Vector3.down * size * 0.15f;
        }
        rock.Box(corners);
    }

    /// <summary>A cluster of three pointed crystals leaning along up.</summary>
    void Crystal(Vector3 basePoint, float height, Vector3 up)
    {
        var accent = _parts[(int)Part.Accent];
        for (int k = 0; k < 3; k++)
        {
            float h = height * (k == 0 ? 1f : 0.5f + (float)_random.NextDouble() * 0.3f);
            var lean = (up + new Vector3((float)_random.NextDouble() - 0.5f, 0f, (float)_random.NextDouble() - 0.5f) * 0.8f).normalized;
            var offset = k == 0 ? Vector3.zero : new Vector3((float)_random.NextDouble() - 0.5f, 0f, (float)_random.NextDouble() - 0.5f) * height * 0.5f;
            accent.Spike(basePoint + offset, lean, h, h * 0.18f, 5);
        }
    }

    /// <summary>A glowing mushroom: a short stem (rock material) and a pointed cap (accent).</summary>
    void Mushroom(Vector3 basePoint, float size)
    {
        _parts[(int)Part.Rock].Spike(basePoint, Vector3.up, size * 1.1f, size * 0.15f, 5);
        var cap = basePoint + Vector3.up * size * 0.85f;
        var accent = _parts[(int)Part.Accent];
        accent.Spike(cap, Vector3.up, size * 0.55f, size * 0.55f, 7);
        accent.Spike(cap + Vector3.up * 0.01f, Vector3.down, size * 0.12f, size * 0.55f, 7);
    }

    // ---------- output ----------

    Mesh Combine(out int submeshes)
    {
        var mesh = new Mesh { name = "Visual" };
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<List<int>>();
        VisualParts.Clear();
        for (int p = 0; p < _parts.Length; p++)
        {
            var part = _parts[p];
            if (part.Triangles.Count == 0) continue;
            int offset = vertices.Count;
            vertices.AddRange(part.Vertices);
            normals.AddRange(part.Normals);
            uvs.AddRange(part.Uvs);
            var list = new List<int>(part.Triangles.Count);
            foreach (var index in part.Triangles) list.Add(index + offset);
            indices.Add(list);
            VisualParts.Add((Part)p);
        }
        mesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = indices.Count;
        for (int s = 0; s < indices.Count; s++) mesh.SetTriangles(indices[s], s);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        submeshes = indices.Count;
        return mesh;
    }

    /// <summary>A collider mesh: the region's cap at top (or y), and with sides, walls from y to top.</summary>
    static Mesh Collider(float[] field, float y, float top, bool cap, bool sides)
    {
        var builder = new Builder();
        float capY = sides ? top : y;
        if (cap)
            foreach (var polygon in Polygons(field))
                for (int k = 1; k < polygon.Count - 1; k++)
                {
                    int a = builder.Shared(new Vector3(polygon[0].x, capY, polygon[0].y), Vector3.up, Vector2.zero);
                    int b = builder.Shared(new Vector3(polygon[k].x, capY, polygon[k].y), Vector3.up, Vector2.zero);
                    int c = builder.Shared(new Vector3(polygon[k + 1].x, capY, polygon[k + 1].y), Vector3.up, Vector2.zero);
                    builder.TriangleFacing(a, b, c, Vector3.up);
                }
        if (sides)
            foreach (var (a, b, normal) in Contour(field))
                builder.QuadFlat(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y), new Vector3(b.x, top, b.y), new Vector3(a.x, top, a.y), new Vector3(normal.x, 0f, normal.y));
        if (builder.Triangles.Count == 0) return null;
        var mesh = new Mesh { name = "Collider" };
        mesh.indexFormat = builder.Vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(builder.Vertices);
        mesh.SetTriangles(builder.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Vertices and triangles of one part. Caps share vertices; faces are flat shaded.</summary>
    class Builder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
        readonly Dictionary<Vector3Int, int> _shared = new Dictionary<Vector3Int, int>();

        public int Shared(Vector3 position, Vector3 normal, Vector2 uv)
        {
            var key = new Vector3Int(Mathf.RoundToInt(position.x * 2000f), Mathf.RoundToInt(position.y * 2000f), Mathf.RoundToInt(position.z * 2000f));
            if (_shared.TryGetValue(key, out int index)) return index;
            index = Add(position, normal, uv);
            _shared[key] = index;
            return index;
        }

        public int Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            return Vertices.Count - 1;
        }

        /// <summary>Adds the triangle wound so it faces along facing (skips degenerate ones).</summary>
        public void TriangleFacing(int a, int b, int c, Vector3 facing)
        {
            var cross = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
            if (cross.sqrMagnitude < 1e-10f) return;
            if (Vector3.Dot(cross, facing) < 0f) (b, c) = (c, b);
            Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
        }

        /// <summary>A quad with its own vertices and the given UVs, facing along facing.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) normal = Vector3.Cross(c - a, d - a);
            normal = normal.normalized;
            if (Vector3.Dot(normal, facing) < 0f) normal = -normal;
            int ia = Add(a, normal, ua), ib = Add(b, normal, ub), ic = Add(c, normal, uc), id = Add(d, normal, ud);
            TriangleFacing(ia, ib, ic, normal);
            TriangleFacing(ia, ic, id, normal);
        }

        /// <summary>A quad with UVs from its world position (wall faces).</summary>
        public void QuadFlat(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
        {
            Vector2 Uv(Vector3 p) => new Vector2((p.x + p.z) * 0.3f, p.y * 0.3f);
            Quad(a, b, c, d, facing, Uv(a), Uv(b), Uv(c), Uv(d));
        }

        /// <summary>A closed box from 8 corners (bit 0: +x, bit 1: top, bit 2: +z).</summary>
        public void Box(Vector3[] k)
        {
            var center = Vector3.zero;
            foreach (var corner in k) center += corner;
            center /= 8f;
            void Face(int a, int b, int c, int d)
            {
                var middle = (k[a] + k[b] + k[c] + k[d]) * 0.25f;
                QuadFlat(k[a], k[b], k[c], k[d], middle - center);
            }
            Face(0, 1, 3, 2);   // -z
            Face(4, 5, 7, 6);   // +z
            Face(0, 4, 6, 2);   // -x
            Face(1, 5, 7, 3);   // +x
            Face(2, 3, 7, 6);   // top
        }

        /// <summary>A pointed prism (crystal, cap) from basePoint along axis.</summary>
        public void Spike(Vector3 basePoint, Vector3 axis, float height, float radius, int sides)
        {
            axis = axis.normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var other = Vector3.Cross(axis, side);
            var tip = basePoint + axis * height;
            var shoulder = basePoint + axis * height * 0.7f;
            for (int i = 0; i < sides; i++)
            {
                float from = i / (float)sides * Mathf.PI * 2f, to = (i + 1) / (float)sides * Mathf.PI * 2f;
                var da = side * Mathf.Cos(from) + other * Mathf.Sin(from);
                var db = side * Mathf.Cos(to) + other * Mathf.Sin(to);
                Vector3 a0 = basePoint + da * radius, b0 = basePoint + db * radius;
                Vector3 a1 = shoulder + da * radius, b1 = shoulder + db * radius;
                var facing = (da + db) * 0.5f;
                QuadFlat(a0, b0, b1, a1, facing);
                var n = Vector3.Cross(b1 - a1, tip - a1).normalized;
                if (Vector3.Dot(n, facing + axis * 0.3f) < 0f) n = -n;
                int ia = Add(a1, n, Vector2.zero), ib = Add(b1, n, Vector2.right), it = Add(tip, n, Vector2.up);
                TriangleFacing(ia, ib, it, n);
            }
        }
    }
}
