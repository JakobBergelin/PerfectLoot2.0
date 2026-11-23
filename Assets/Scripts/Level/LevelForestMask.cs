namespace LevelGeneration
{
    /// <summary>
    /// How much forest is around each corner of the solved level, for the height fog (it reaches higher and is thicker
    /// toward the forest): 1 on not-walkable ground (trees, rocks) and on the background ring, 0 on walkable ground and
    /// water, blurred so the fog rises over a few meters. One value per corner of the bigger grid (the level plus its
    /// ring). Built from the solved terrain, so every machine gets the same mask.
    /// </summary>
    public static class LevelForestMask
    {
        public const int BlurRadius = 1;   // Corners: the mist rises within a few meters of the trees, clearings stay clear
        public const int BlurPasses = 2;

        /// <param name="terrain">Per corner of the level (width + 1 by height + 1), a WfcTerrain</param>
        public static float[] Build(byte[] terrain, int width, int height, int margin)
        {
            int cw = width + 1, ch = height + 1;
            int w = cw + 2 * margin, h = ch + 2 * margin;
            var mask = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int lx = x - margin, ly = y - margin;
                    bool inside = lx >= 0 && ly >= 0 && lx < cw && ly < ch;
                    mask[x + y * w] = !inside || terrain[lx + ly * cw] == (byte)WfcTerrain.NotWalkable ? 1f : 0f;
                }
            var scratch = new float[mask.Length];
            for (int pass = 0; pass < BlurPasses; pass++)
            {
                Blur(mask, scratch, w, h, 1, 0);
                Blur(scratch, mask, w, h, 0, 1);
            }
            return mask;
        }

        // A box blur along one axis, clamped at the edges
        static void Blur(float[] from, float[] to, int w, int h, int dx, int dy)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int k = -BlurRadius; k <= BlurRadius; k++)
                    {
                        int sx = System.Math.Clamp(x + k * dx, 0, w - 1), sy = System.Math.Clamp(y + k * dy, 0, h - 1);
                        sum += from[sx + sy * w];
                    }
                    to[x + y * w] = sum / (2 * BlurRadius + 1);
                }
        }
    }
}
