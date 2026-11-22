using System.Collections.Generic;
using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>
    /// Which way the water flows at each corner of the solved level, for PerfectLoot/Liquid's ripples: rivers run toward open
    /// water (a lake or pond: water corners whose eight neighbours are all water), and open water lies still. A stretch of
    /// water with no open water runs from one end to the other. Built from the solved terrain, so every machine gets the
    /// same flow. Directions are in grid space (x east, y north), one per corner of the bigger grid the chunk jobs use (the
    /// level plus its ring, which has no water of its own).
    /// </summary>
    public static class LevelFlow
    {
        /// <param name="terrain">Per corner of the level (width + 1 by height + 1), a WfcTerrain</param>
        public static float2[] Build(byte[] terrain, int width, int height, int margin)
        {
            int cw = width + 1, ch = height + 1;
            int total = cw * ch;
            bool Liquid(int x, int y) => x >= 0 && y >= 0 && x < cw && y < ch && terrain[x + y * cw] == (byte)WfcTerrain.Water;
            // A crossing (a bridge: dry with water on both sides) carries the river on under it, so it doesn't split in two
            var wet = new bool[total];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    wet[x + y * cw] = Liquid(x, y) || (Liquid(x - 1, y) && Liquid(x + 1, y)) || (Liquid(x, y - 1) && Liquid(x, y + 1));
            bool Water(int x, int y) => x >= 0 && y >= 0 && x < cw && y < ch && wet[x + y * cw];

            // How far each water corner is from where its water flows to (-1: not water, or not reached yet)
            var distance = new int[total];
            for (int i = 0; i < total; i++) distance[i] = -1;
            var component = new List<int>();
            var queue = new Queue<int>();
            var seen = new bool[total];
            for (int start = 0; start < total; start++)
            {
                if (seen[start] || !wet[start]) continue;
                Collect(start, cw, ch, Water, seen, component, queue);

                // Sinks: the open water in this stretch, or else the far end of a river
                var sinks = new List<int>();
                foreach (int c in component)
                    if (Open(c % cw, c / cw, Water)) sinks.Add(c);
                if (sinks.Count == 0)
                {
                    int far = Farthest(component[0], cw, ch, Water, queue);
                    sinks.Add(Farthest(far, cw, ch, Water, queue));
                }
                foreach (int s in sinks) { distance[s] = 0; queue.Enqueue(s); }
                Spread(cw, ch, Water, distance, queue);
            }

            // Downhill on the distance, then smoothed once over the water, so a river's turns are gradual
            var flow = new float2[total];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    int i = x + y * cw;
                    if (distance[i] <= 0) continue;     // Dry, or open water lying still
                    var sum = float2.zero;
                    foreach (var d in Neighbours)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        if (!Water(nx, ny)) continue;
                        int n = distance[nx + ny * cw];
                        if (n >= 0 && n < distance[i]) sum += math.normalize((float2)d);
                    }
                    flow[i] = math.normalizesafe(sum);
                }
            var smooth = new float2[total];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                {
                    if (!Water(x, y)) continue;
                    var sum = float2.zero;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (Water(x + dx, y + dy)) sum += flow[x + dx + (y + dy) * cw];
                    // Keeps the strength of rivers; open water next to a river picks up a little of its current
                    float strength = math.max(math.length(flow[x + y * cw]), math.length(sum) / 9f);
                    smooth[x + y * cw] = math.normalizesafe(sum) * strength;
                }

            // Into the bigger grid of the chunk jobs (the ring around the level has no water)
            int bw = width + 2 * margin + 1, bh = height + 2 * margin + 1;
            var result = new float2[bw * bh];
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    result[x + margin + (y + margin) * bw] = smooth[x + y * cw];
            return result;
        }

        static readonly int2[] Neighbours =
        {
            new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1),
            new int2(1, 1), new int2(-1, 1), new int2(1, -1), new int2(-1, -1),
        };

        delegate bool IsWater(int x, int y);

        static bool Open(int x, int y, IsWater water)
        {
            foreach (var d in Neighbours) if (!water(x + d.x, y + d.y)) return false;
            return true;
        }

        // Every water corner connected to start (side by side)
        static void Collect(int start, int cw, int ch, IsWater water, bool[] seen, List<int> component, Queue<int> queue)
        {
            component.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                component.Add(c);
                int x = c % cw, y = c / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Neighbours[k].x, ny = y + Neighbours[k].y;
                    if (!water(nx, ny) || seen[nx + ny * cw]) continue;
                    seen[nx + ny * cw] = true;
                    queue.Enqueue(nx + ny * cw);
                }
            }
        }

        // The water corner farthest (in steps) from start
        static int Farthest(int start, int cw, int ch, IsWater water, Queue<int> queue)
        {
            var steps = new Dictionary<int, int> { [start] = 0 };
            queue.Enqueue(start);
            int far = start;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                if (steps[c] > steps[far]) far = c;
                int x = c % cw, y = c / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Neighbours[k].x, ny = y + Neighbours[k].y;
                    int n = nx + ny * cw;
                    if (!water(nx, ny) || steps.ContainsKey(n)) continue;
                    steps[n] = steps[c] + 1;
                    queue.Enqueue(n);
                }
            }
            return far;
        }

        // Steps from the sinks already queued, over the water
        static void Spread(int cw, int ch, IsWater water, int[] distance, Queue<int> queue)
        {
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % cw, y = c / cw;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + Neighbours[k].x, ny = y + Neighbours[k].y;
                    int n = nx + ny * cw;
                    if (!water(nx, ny) || distance[n] >= 0) continue;
                    distance[n] = distance[c] + 1;
                    queue.Enqueue(n);
                }
            }
        }

        /// <summary>The flow at a point of the bigger grid (in cells), between its four corners.</summary>
        public static float2 Sample(Unity.Collections.NativeArray<float2> flow, int width, int height, float x, float y)
        {
            int cw = width + 1;
            x = math.clamp(x, 0f, width); y = math.clamp(y, 0f, height);
            int x0 = math.min((int)x, width - 1), y0 = math.min((int)y, height - 1);
            float fx = x - x0, fy = y - y0;
            var a = math.lerp(flow[x0 + y0 * cw], flow[x0 + 1 + y0 * cw], fx);
            var b = math.lerp(flow[x0 + (y0 + 1) * cw], flow[x0 + 1 + (y0 + 1) * cw], fx);
            return math.lerp(a, b, fy);
        }
    }
}
