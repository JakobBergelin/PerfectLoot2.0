using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>
    /// Bends the level grid: every grid corner is moved by smooth noise (wobble sideways, hills up and down) and the
    /// whole grid can curve along its width (bend). Inside a cell, positions blend between its 4 moved corners, so
    /// neighboring tiles always meet. Positions are in grid space (meters from the layout's south-west corner, before
    /// the layout's position and turn). With every setting at 0 the grid is flat and regular.
    /// </summary>
    public struct LevelDeformation
    {
        public int Width, Height;
        public float TileSize;
        public uint Seed;
        /// <summary>Sideways corner movement as a fraction of TileSize (0 to 0.2).</summary>
        public float WobbleStrength;
        /// <summary>Noise frequency per cell: small values give broad, smooth wobble.</summary>
        public float WobbleScale;
        /// <summary>Degrees the grid curves over its whole width (0 = straight).</summary>
        public float BendAngle;
        /// <summary>Meters corners rise or sink at most.</summary>
        public float HillHeight;
        public float HillScale;

        public const float MaxWobble = 0.2f;

        public bool IsFlat => WobbleStrength <= 0f && HillHeight == 0f && BendAngle == 0f;

        /// <summary>Every corner's moved position, (Width + 1) * (Height + 1), row by row from the south-west.</summary>
        public float3[] BuildCorners()
        {
            var corners = new float3[(Width + 1) * (Height + 1)];
            for (int y = 0; y <= Height; y++)
                for (int x = 0; x <= Width; x++)
                    corners[x + y * (Width + 1)] = Corner(x, y);
            return corners;
        }

        float3 Corner(int x, int y)
        {
            var p = new float3(x * TileSize, 0f, y * TileSize);
            var seedOffset = SeedOffset();
            if (WobbleStrength > 0f)
            {
                float strength = math.min(WobbleStrength, MaxWobble) * TileSize;
                var q = new float2(x, y) * WobbleScale + seedOffset;
                p.x += noise.snoise(q) * strength;
                p.z += noise.snoise(q + new float2(31.7f, 47.3f)) * strength;
            }
            if (HillHeight != 0f)
                p.y += noise.snoise(new float2(x, y) * HillScale + seedOffset + new float2(113.1f, 71.9f)) * HillHeight;
            if (BendAngle != 0f) p = Bend(p);
            return p;
        }

        // Rows curve around a circle: the middle column stays put, the sides swing forward or back
        float3 Bend(float3 p)
        {
            float length = Width * TileSize;
            float radius = length / math.radians(BendAngle);
            float angle = (p.x - length * 0.5f) / radius;
            float distance = radius + p.z;
            return new float3(length * 0.5f + distance * math.sin(angle), p.y, distance * math.cos(angle) - radius);
        }

        float2 SeedOffset()
        {
            uint hash = math.hash(new uint2(Seed, 0x9E3779B9u));
            return new float2(hash & 0xFFFF, hash >> 16) * (1000f / 65535f);
        }

        /// <summary>
        /// Grid-space position of a point given in cells (x across, y forward): blended from the moved corners of the cell
        /// it's in. Points outside the grid extend the nearest edge cell.
        /// </summary>
        public static float3 Sample(float3[] corners, int width, int height, float x, float y)
        {
            int cx = math.clamp((int)math.floor(x), 0, width - 1);
            int cy = math.clamp((int)math.floor(y), 0, height - 1);
            float fx = x - cx, fy = y - cy;
            int stride = width + 1;
            var south = math.lerp(corners[cx + cy * stride], corners[cx + 1 + cy * stride], fx);
            var north = math.lerp(corners[cx + (cy + 1) * stride], corners[cx + 1 + (cy + 1) * stride], fx);
            return math.lerp(south, north, fy);
        }
    }
}
