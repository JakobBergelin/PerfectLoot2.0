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

        // Connectivity repair (see Repair)
        public bool Connect;
        /// <summary>A walkable corner every walkable area must connect to (the player start), or -1.</summary>
        public int StartCorner;
        /// <summary>Per corner: 1 when the repair may never change it (painted rules, pieces, forced tiles, start, border).</summary>
        public NativeArray<byte> LockedCorners;
        /// <summary>Pockets with fewer walkable corners than this (and none locked) are filled with wall; larger ones are carved to.</summary>
        public int FillPocketsSmallerThan;
        /// <summary>Output per corner: RepairNone, RepairCarved or RepairFilled.</summary>
        public NativeArray<byte> Repairs;
        /// <summary>Output: pockets that couldn't be connected without changing locked corners.</summary>
        public int UnconnectedPockets;

        public const byte RepairNone = 0, RepairCarved = 1, RepairFilled = 2;
        const int MaxRepairRounds = 4;

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
                if (TrySolve(ref random, InitialCornerMasks, InitialCells) && (!Connect || Repair(ref random)))
                {
                    for (int c = 0; c < cells; c++)
                        result[c] = InitialCells[c] == Occupied ? Occupied : FirstVariant(c);
                    return attempt + 1;
                }
            }
            return 0;
        }

        // Solves from these corner masks and cells (Free, Occupied or a forced variant)
        bool TrySolve(ref Random random, NativeArray<byte> cornerMasks, NativeArray<int> startCells)
        {
            int cells = Width * Height;
            _cornerMasks.CopyFrom(cornerMasks);
            _queue.Clear();
            for (int c = 0; c < cells; c++)
            {
                int initial = startCells[c];
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

        /// <summary>
        /// Connects every walkable area to the start corner. Walkable corners next to each other (not diagonally) are
        /// connected; anything not reached from the start is a pocket. Small pockets (no locked corners) become wall,
        /// the rest get the shortest corridor to the reached area carved through wall or water, never through locked
        /// corners. Then only the cells touching changed corners are solved again (every other cell keeps its tile) and
        /// the check repeats. False when a re-solve fails (no tile fits): the caller rerolls.
        /// </summary>
        bool Repair(ref Random random)
        {
            int corners = _cornerMasks.Length;
            for (int c = 0; c < corners; c++) Repairs[c] = RepairNone;
            UnconnectedPockets = 0;
            if (StartCorner < 0 || StartCorner >= corners) return true;

            byte walkable = WfcTerrains.Mask(WfcTerrain.Walkable);
            byte wall = WfcTerrains.Mask(WfcTerrain.Wall);
            var component = new NativeArray<int>(corners, Allocator.Temp);
            var queue = new NativeList<int>(corners, Allocator.Temp);
            var parent = new NativeArray<int>(corners, Allocator.Temp);
            var masks = new NativeArray<byte>(corners, Allocator.Temp);
            var changed = new NativeArray<bool>(corners, Allocator.Temp);
            var cells = new NativeArray<int>(Width * Height, Allocator.Temp);

            for (int round = 0; round < MaxRepairRounds; round++)
            {
                // Label walkable areas: 0 is the start's, 1.. are pockets; -1 is not walkable
                for (int c = 0; c < corners; c++) component[c] = -1;
                if (_cornerMasks[StartCorner] != walkable) return true;
                int components = 0;
                var pocketSize = new NativeList<int>(Allocator.Temp);
                var pocketLocked = new NativeList<bool>(Allocator.Temp);
                for (int seed = -1; seed < corners; seed++)
                {
                    int from = seed < 0 ? StartCorner : seed;
                    if (component[from] >= 0 || _cornerMasks[from] != walkable) continue;
                    int label = components++;
                    int size = 0;
                    bool locked = false;
                    queue.Clear();
                    queue.Add(from);
                    component[from] = label;
                    for (int head = 0; head < queue.Length; head++)
                    {
                        int corner = queue[head];
                        size++;
                        locked |= LockedCorners[corner] != 0;
                        for (int n = 0; n < 4; n++)
                        {
                            int next = Neighbor(corner, n);
                            if (next < 0 || component[next] >= 0 || _cornerMasks[next] != walkable) continue;
                            component[next] = label;
                            queue.Add(next);
                        }
                    }
                    pocketSize.Add(size);
                    pocketLocked.Add(locked);
                }
                if (components <= 1) return true;

                // Start from the solved corners and change pockets: fill small ones, carve corridors to the rest
                for (int c = 0; c < corners; c++) { masks[c] = _cornerMasks[c]; changed[c] = false; }
                int unconnected = 0;
                bool anyChange = false;
                for (int pocket = 1; pocket < components; pocket++)
                {
                    if (pocketSize[pocket] < FillPocketsSmallerThan && !pocketLocked[pocket])
                    {
                        for (int c = 0; c < corners; c++)
                        {
                            if (component[c] != pocket || LockedCorners[c] != 0) continue;
                            masks[c] = wall;
                            changed[c] = true;
                            Repairs[c] = RepairFilled;
                            anyChange = true;
                        }
                        continue;
                    }

                    // Shortest path from the pocket to the start's area, through any corner that isn't locked non-walkable
                    for (int c = 0; c < corners; c++) parent[c] = -2;
                    queue.Clear();
                    for (int c = 0; c < corners; c++)
                        if (component[c] == pocket) { parent[c] = -1; queue.Add(c); }
                    int reached = -1;
                    for (int head = 0; head < queue.Length && reached < 0; head++)
                    {
                        int corner = queue[head];
                        for (int n = 0; n < 4; n++)
                        {
                            int next = Neighbor(corner, n);
                            if (next < 0 || parent[next] != -2) continue;
                            if (_cornerMasks[next] != walkable && LockedCorners[next] != 0) continue;
                            parent[next] = corner;
                            if (component[next] == 0) { reached = next; break; }
                            queue.Add(next);
                        }
                    }
                    if (reached < 0) { unconnected++; continue; }
                    for (int c = parent[reached]; c >= 0; c = parent[c])
                    {
                        if (masks[c] == walkable) continue;
                        masks[c] = walkable;
                        changed[c] = true;
                        Repairs[c] = RepairCarved;
                        anyChange = true;
                    }
                }
                UnconnectedPockets = unconnected;
                if (!anyChange) return true;

                // Solve only the cells touching a changed corner again; the rest keep their tiles
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int cell = x + y * Width;
                        if (InitialCells[cell] == Occupied) { cells[cell] = Occupied; continue; }
                        bool touched = false;
                        for (int k = 0; k < 4; k++) touched |= changed[CornerIndex(x, y, k)];
                        cells[cell] = touched ? Free : FirstVariant(cell);
                    }
                if (!TrySolve(ref random, masks, cells)) return false;
            }
            return true;
        }

        // Corner next to corner in direction n (0 east, 1 north, 2 west, 3 south), or -1 off the grid
        int Neighbor(int corner, int n)
        {
            int x = corner % _cornerWidth, y = corner / _cornerWidth;
            switch (n)
            {
                case 0: return x + 1 < _cornerWidth ? corner + 1 : -1;
                case 1: return y + 1 < Height + 1 ? corner + _cornerWidth : -1;
                case 2: return x > 0 ? corner - 1 : -1;
                default: return y > 0 ? corner - _cornerWidth : -1;
            }
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

        // Connectivity repair (WfcSolver.Repair); LockedCorners and Repairs are always allocated (one per corner)
        public bool Connect;
        public int StartCorner;
        public int FillPocketsSmallerThan;
        [ReadOnly] public NativeArray<byte> LockedCorners;

        /// <summary>Per cell: the chosen variant, or WfcSolver.Occupied. Only valid when Attempts &gt; 0.</summary>
        public NativeArray<int> Result;
        /// <summary>The attempt that succeeded (1-based), or 0 when every attempt failed.</summary>
        public NativeReference<int> Attempts;
        /// <summary>Per corner: what the repair did (WfcSolver.RepairNone / RepairCarved / RepairFilled).</summary>
        public NativeArray<byte> Repairs;
        public NativeReference<int> UnconnectedPockets;

        public void Execute()
        {
            var solver = new WfcSolver
            {
                Width = Width, Height = Height,
                VariantCorners = VariantCorners, VariantWeights = VariantWeights,
                InitialCornerMasks = InitialCornerMasks, InitialCells = InitialCells,
                Seed = Seed, MaxAttempts = MaxAttempts,
                Connect = Connect, StartCorner = StartCorner, FillPocketsSmallerThan = FillPocketsSmallerThan,
                LockedCorners = LockedCorners, Repairs = Repairs,
            };
            Attempts.Value = solver.Solve(Result);
            UnconnectedPockets.Value = solver.UnconnectedPockets;
        }
    }
}
