using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>
    /// Wave function collapse over corner-described tiles. Every cell keeps the tile variants it can still become (a
    /// bit set), every corner the terrains it can still be (a bit mask). A variant stays possible only while each of
    /// its corners can still be its terrain, and a corner keeps only terrains some remaining variant next to it needs.
    /// The cell with the fewest options is collapsed (weighted random) and that spreads until nothing changes. On a
    /// contradiction the whole grid restarts with the next seed. Used by the editor preview and at runtime, so the
    /// same inputs and seed give the same level. Run it through WfcSolveJob (Burst).
    /// </summary>
    public struct WfcSolver
    {
        public const int Free = -1;
        public const int Occupied = -2;

        public int Width, Height;
        /// <summary>4 terrains per variant: SouthWest, SouthEast, NorthEast, NorthWest.</summary>
        public NativeArray<byte> VariantCorners;
        public NativeArray<float> VariantWeights;
        /// <summary>(Width + 1) * (Height + 1) terrain bit masks, row by row from the south-west.</summary>
        public NativeArray<byte> InitialCornerMasks;
        /// <summary>Per cell: Free, Occupied (covered by a piece) or a forced variant index.</summary>
        public NativeArray<int> InitialCells;
        public uint Seed;
        public int MaxAttempts;

        int _variants, _words, _cornerWidth;
        NativeArray<ulong> _support;    // [corner k][terrain t][word]: variants with terrain t at corner k
        NativeArray<ulong> _domains;    // [cell][word]
        NativeArray<byte> _cornerMasks;
        NativeList<int> _queue;
        NativeArray<bool> _queued;

        /// <summary>
        /// Fills result (per cell: the chosen variant, or Occupied) and returns the attempt that succeeded (1-based), or
        /// 0 when every attempt failed (result is then undefined).
        /// </summary>
        public int Solve(NativeArray<int> result)
        {
            int cells = Width * Height;
            _variants = VariantWeights.Length;
            _words = math.max(1, (_variants + 63) / 64);
            _cornerWidth = Width + 1;
            if (_variants == 0 || cells == 0) return 0;

            _support = new NativeArray<ulong>(4 * WfcTerrains.Count * _words, Allocator.Temp);
            for (int v = 0; v < _variants; v++)
                for (int k = 0; k < 4; k++)
                {
                    int t = VariantCorners[v * 4 + k];
                    _support[(k * WfcTerrains.Count + t) * _words + v / 64] |= 1ul << (v % 64);
                }

            _domains = new NativeArray<ulong>(cells * _words, Allocator.Temp);
            _cornerMasks = new NativeArray<byte>(InitialCornerMasks.Length, Allocator.Temp);
            _queue = new NativeList<int>(cells, Allocator.Temp);
            _queued = new NativeArray<bool>(cells, Allocator.Temp);

            for (int attempt = 0; attempt < math.max(1, MaxAttempts); attempt++)
            {
                var random = new Random(math.max(1u, math.hash(new uint2(Seed, (uint)attempt))));
                if (TrySolve(ref random))
                {
                    for (int c = 0; c < cells; c++)
                        result[c] = InitialCells[c] == Occupied ? Occupied : FirstVariant(c);
                    return attempt + 1;
                }
            }
            return 0;
        }

        bool TrySolve(ref Random random)
        {
            int cells = Width * Height;
            _cornerMasks.CopyFrom(InitialCornerMasks);
            _queue.Clear();
            for (int c = 0; c < cells; c++)
            {
                int initial = InitialCells[c];
                for (int w = 0; w < _words; w++)
                {
                    ulong all = w < _words - 1 || _variants % 64 == 0 ? ~0ul : (1ul << (_variants % 64)) - 1;
                    _domains[c * _words + w] = initial >= 0 ? (initial / 64 == w ? 1ul << (initial % 64) : 0ul)
                                             : initial == Occupied ? 0ul : all;
                }
                _queued[c] = false;
                Enqueue(c);
            }
            if (!Propagate()) return false;

            while (true)
            {
                int cell = LowestEntropyCell(ref random);
                if (cell < 0) return true;
                Collapse(cell, ref random);
                Enqueue(cell);
                if (!Propagate()) return false;
            }
        }

        void Enqueue(int cell)
        {
            if (cell < 0 || _queued[cell] || InitialCells[cell] == Occupied) return;
            _queued[cell] = true;
            _queue.Add(cell);
        }

        // Arc consistency between cells and their 4 corners
        bool Propagate()
        {
            while (_queue.Length > 0)
            {
                int cell = _queue[_queue.Length - 1];
                _queue.RemoveAt(_queue.Length - 1);
                _queued[cell] = false;

                int x = cell % Width, y = cell / Width;
                // Keep the variants whose corners all fit their corner's mask
                bool any = false;
                for (int w = 0; w < _words; w++)
                {
                    ulong allowed = ~0ul;
                    for (int k = 0; k < 4; k++)
                    {
                        byte mask = _cornerMasks[CornerIndex(x, y, k)];
                        ulong fits = 0ul;
                        for (int t = 0; t < WfcTerrains.Count; t++)
                            if ((mask & (1 << t)) != 0) fits |= _support[(k * WfcTerrains.Count + t) * _words + w];
                        allowed &= fits;
                    }
                    ulong domain = _domains[cell * _words + w] & allowed;
                    _domains[cell * _words + w] = domain;
                    any |= domain != 0ul;
                }
                if (!any) return false;

                // Narrow each corner to the terrains this cell's remaining variants have there
                for (int k = 0; k < 4; k++)
                {
                    int corner = CornerIndex(x, y, k);
                    byte terrains = 0;
                    for (int t = 0; t < WfcTerrains.Count; t++)
                        for (int w = 0; w < _words; w++)
                            if ((_domains[cell * _words + w] & _support[(k * WfcTerrains.Count + t) * _words + w]) != 0ul)
                            {
                                terrains |= (byte)(1 << t);
                                break;
                            }
                    byte narrowed = (byte)(_cornerMasks[corner] & terrains);
                    if (narrowed == 0) return false;
                    if (narrowed == _cornerMasks[corner]) continue;
                    _cornerMasks[corner] = narrowed;
                    EnqueueCellsAround(corner);
                }
            }
            return true;
        }

        // Corner k of cell (x, y): 0 SW, 1 SE, 2 NE, 3 NW
        int CornerIndex(int x, int y, int k) => k switch
        {
            0 => x + y * _cornerWidth,
            1 => x + 1 + y * _cornerWidth,
            2 => x + 1 + (y + 1) * _cornerWidth,
            _ => x + (y + 1) * _cornerWidth,
        };

        void EnqueueCellsAround(int corner)
        {
            int cx = corner % _cornerWidth, cy = corner / _cornerWidth;
            for (int dy = -1; dy <= 0; dy++)
                for (int dx = -1; dx <= 0; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < Width && y < Height) Enqueue(x + y * Width);
                }
        }

        int Count(int cell)
        {
            int count = 0;
            for (int w = 0; w < _words; w++) count += math.countbits(_domains[cell * _words + w]);
            return count;
        }

        // Undecided cell with the fewest options; random tie-break. -1 when every cell is decided.
        int LowestEntropyCell(ref Random random)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int c = 0; c < Width * Height; c++)
            {
                if (InitialCells[c] == Occupied) continue;
                int count = Count(c);
                if (count <= 1) continue;
                float score = count + random.NextFloat() * 0.5f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        void Collapse(int cell, ref Random random)
        {
            float total = 0f;
            for (int v = 0; v < _variants; v++)
                if (Has(cell, v)) total += VariantWeights[v];

            int chosen = -1;
            if (total > 0f)
            {
                float pick = random.NextFloat() * total;
                for (int v = 0; v < _variants && chosen < 0; v++)
                {
                    if (!Has(cell, v)) continue;
                    pick -= VariantWeights[v];
                    if (pick <= 0f) chosen = v;
                }
            }
            // Every option has weight 0 (or rounding): pick uniformly
            if (chosen < 0)
            {
                int index = random.NextInt(Count(cell));
                for (int v = 0; v < _variants && chosen < 0; v++)
                    if (Has(cell, v) && index-- == 0) chosen = v;
            }

            for (int w = 0; w < _words; w++)
                _domains[cell * _words + w] = chosen / 64 == w ? 1ul << (chosen % 64) : 0ul;
        }

        bool Has(int cell, int variant) => (_domains[cell * _words + variant / 64] & (1ul << (variant % 64))) != 0ul;

        int FirstVariant(int cell)
        {
            for (int w = 0; w < _words; w++)
            {
                ulong bits = _domains[cell * _words + w];
                if (bits != 0ul) return w * 64 + math.tzcnt(bits);
            }
            return Free;
        }
    }

    /// <summary>Runs WfcSolver with Burst. The working arrays live inside the solver, so they never sit on the job.</summary>
    [BurstCompile]
    public struct WfcSolveJob : IJob
    {
        public int Width, Height;
        [ReadOnly] public NativeArray<byte> VariantCorners;
        [ReadOnly] public NativeArray<float> VariantWeights;
        [ReadOnly] public NativeArray<byte> InitialCornerMasks;
        [ReadOnly] public NativeArray<int> InitialCells;
        public uint Seed;
        public int MaxAttempts;

        /// <summary>Per cell: the chosen variant, or WfcSolver.Occupied. Only valid when Attempts &gt; 0.</summary>
        public NativeArray<int> Result;
        /// <summary>The attempt that succeeded (1-based), or 0 when every attempt failed.</summary>
        public NativeReference<int> Attempts;

        public void Execute()
        {
            var solver = new WfcSolver
            {
                Width = Width, Height = Height,
                VariantCorners = VariantCorners, VariantWeights = VariantWeights,
                InitialCornerMasks = InitialCornerMasks, InitialCells = InitialCells,
                Seed = Seed, MaxAttempts = MaxAttempts,
            };
            Attempts.Value = solver.Solve(Result);
        }
    }
}
