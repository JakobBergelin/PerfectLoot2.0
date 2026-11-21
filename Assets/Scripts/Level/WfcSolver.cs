using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LevelGeneration
{
    /// <summary>
    /// Wave function collapse over tiles described by connections. Every cell has 8 slots it shares with its neighbors:
    /// its 4 corners and its 4 edges. Every slot keeps the connections it can still be (a bit mask), every cell the tile
    /// variants it can still become (a bit set). A variant stays possible only while each of its slots can still be its
    /// connection there, and a slot keeps only connections some remaining variant next to it has. The cell with the fewest
    /// options is collapsed (weighted random) and that spreads until nothing changes. On a contradiction the whole grid
    /// restarts with the next seed. Used by the editor preview and at runtime, so the same inputs and seed give the same
    /// level. Run it through WfcSolveJob (Burst).
    /// </summary>
    public struct WfcSolver
    {
        public const int Free = -1;
        public const int Occupied = -2;
        /// <summary>Slots per cell: corners SW, SE, NE, NW, then edges S, E, N, W.</summary>
        public const int SlotsPerCell = 8;

        public int Width, Height;
        /// <summary>8 connection indices per variant: corners SW, SE, NE, NW, then edges S, E, N, W.</summary>
        public NativeArray<byte> VariantSlots;
        public NativeArray<float> VariantWeights;
        /// <summary>(Width + 1) * (Height + 1) corner connection masks, row by row from the south-west.</summary>
        public NativeArray<ulong> InitialCornerMasks;
        /// <summary>Edge connection masks: Width * (Height + 1) edges running east-west (row by row), then (Width + 1) * Height running north-south.</summary>
        public NativeArray<ulong> InitialEdgeMasks;
        /// <summary>Per cell: Free, Occupied (covered by a piece) or a forced variant index.</summary>
        public NativeArray<int> InitialCells;
        public uint Seed;
        public int MaxAttempts;

        // Connectivity repair (see Repair)
        public bool Connect;
        /// <summary>A walkable corner every walkable area must connect to (the player start), or -1.</summary>
        public int StartCorner;
        /// <summary>Corner connections that are Walkable / NotWalkable (bits by index).</summary>
        public ulong WalkableCorners, NotWalkableCorners;
        /// <summary>Per corner: 1 when the repair may never change it (painted rules, pieces, forced tiles, start, border).</summary>
        public NativeArray<byte> LockedCorners;
        /// <summary>Pockets with fewer walkable corners than this (and none locked) are filled with not walkable; larger ones are carved to.</summary>
        public int FillPocketsSmallerThan;
        /// <summary>Output per corner: RepairNone, RepairCarved or RepairFilled.</summary>
        public NativeArray<byte> Repairs;
        /// <summary>Output: pockets that couldn't be connected without changing locked corners.</summary>
        public int UnconnectedPockets;

        public const byte RepairNone = 0, RepairCarved = 1, RepairFilled = 2;
        const int MaxRepairRounds = 4;

        int _variants, _words, _cornerWidth, _corners, _edgesAcross;
        NativeArray<ulong> _support;    // [slot s][connection][word]: variants with that connection in slot s
        NativeArray<ulong> _domains;    // [cell][word]
        NativeArray<ulong> _slotMasks;  // corners, then edges running east-west, then edges running north-south
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
            _corners = InitialCornerMasks.Length;
            _edgesAcross = Width * (Height + 1);
            if (_variants == 0 || cells == 0) return 0;

            _support = new NativeArray<ulong>(SlotsPerCell * WfcConnections.Max * _words, Allocator.Temp);
            for (int v = 0; v < _variants; v++)
                for (int s = 0; s < SlotsPerCell; s++)
                {
                    int connection = VariantSlots[v * SlotsPerCell + s];
                    _support[(s * WfcConnections.Max + connection) * _words + v / 64] |= 1ul << (v % 64);
                }

            var initialMasks = new NativeArray<ulong>(_corners + InitialEdgeMasks.Length, Allocator.Temp);
            NativeArray<ulong>.Copy(InitialCornerMasks, 0, initialMasks, 0, _corners);
            NativeArray<ulong>.Copy(InitialEdgeMasks, 0, initialMasks, _corners, InitialEdgeMasks.Length);
            _domains = new NativeArray<ulong>(cells * _words, Allocator.Temp);
            _slotMasks = new NativeArray<ulong>(initialMasks.Length, Allocator.Temp);
            _queue = new NativeList<int>(cells, Allocator.Temp);
            _queued = new NativeArray<bool>(cells, Allocator.Temp);

            for (int attempt = 0; attempt < math.max(1, MaxAttempts); attempt++)
            {
                var random = new Random(math.max(1u, math.hash(new uint2(Seed, (uint)attempt))));
                if (TrySolve(ref random, initialMasks, InitialCells) && (!Connect || Repair(ref random, initialMasks)))
                {
                    for (int c = 0; c < cells; c++)
                        result[c] = InitialCells[c] == Occupied ? Occupied : FirstVariant(c);
                    return attempt + 1;
                }
            }
            return 0;
        }

        // Solves from these slot masks and cells (Free, Occupied or a forced variant)
        bool TrySolve(ref Random random, NativeArray<ulong> slotMasks, NativeArray<int> startCells)
        {
            int cells = Width * Height;
            _slotMasks.CopyFrom(slotMasks);
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

        // Arc consistency between cells and their 8 slots
        bool Propagate()
        {
            while (_queue.Length > 0)
            {
                int cell = _queue[_queue.Length - 1];
                _queue.RemoveAt(_queue.Length - 1);
                _queued[cell] = false;

                int x = cell % Width, y = cell / Width;
                // Keep the variants whose connections all fit their slot's mask
                bool any = false;
                for (int w = 0; w < _words; w++)
                {
                    ulong allowed = ~0ul;
                    for (int s = 0; s < SlotsPerCell; s++)
                    {
                        ulong mask = _slotMasks[Slot(x, y, s)];
                        ulong fits = 0ul;
                        for (ulong bits = mask; bits != 0ul; bits &= bits - 1)
                            fits |= _support[(s * WfcConnections.Max + math.tzcnt(bits)) * _words + w];
                        allowed &= fits;
                    }
                    ulong domain = _domains[cell * _words + w] & allowed;
                    _domains[cell * _words + w] = domain;
                    any |= domain != 0ul;
                }
                if (!any) return false;

                // Narrow each slot to the connections this cell's remaining variants have there
                for (int s = 0; s < SlotsPerCell; s++)
                {
                    int slot = Slot(x, y, s);
                    ulong mask = _slotMasks[slot];
                    ulong kept = 0ul;
                    for (ulong bits = mask; bits != 0ul; bits &= bits - 1)
                    {
                        int connection = math.tzcnt(bits);
                        for (int w = 0; w < _words; w++)
                            if ((_domains[cell * _words + w] & _support[(s * WfcConnections.Max + connection) * _words + w]) != 0ul)
                            {
                                kept |= 1ul << connection;
                                break;
                            }
                    }
                    if (kept == 0ul) return false;
                    if (kept == mask) continue;
                    _slotMasks[slot] = kept;
                    EnqueueCellsAround(slot);
                }
            }
            return true;
        }

        // Slot s of cell (x, y): corners 0 SW, 1 SE, 2 NE, 3 NW; edges 4 S, 5 E, 6 N, 7 W
        int Slot(int x, int y, int s) => s switch
        {
            0 => x + y * _cornerWidth,
            1 => x + 1 + y * _cornerWidth,
            2 => x + 1 + (y + 1) * _cornerWidth,
            3 => x + (y + 1) * _cornerWidth,
            4 => _corners + x + y * Width,
            5 => _corners + _edgesAcross + x + 1 + y * _cornerWidth,
            6 => _corners + x + (y + 1) * Width,
            _ => _corners + _edgesAcross + x + y * _cornerWidth,
        };

        // The cells sharing a slot: up to 4 around a corner, the 2 on either side of an edge
        void EnqueueCellsAround(int slot)
        {
            if (slot < _corners)
            {
                int cx = slot % _cornerWidth, cy = slot / _cornerWidth;
                for (int dy = -1; dy <= 0; dy++)
                    for (int dx = -1; dx <= 0; dx++)
                        EnqueueCell(cx + dx, cy + dy);
            }
            else if (slot < _corners + _edgesAcross)
            {
                int edge = slot - _corners, ex = edge % Width, ey = edge / Width;
                EnqueueCell(ex, ey - 1);
                EnqueueCell(ex, ey);
            }
            else
            {
                int edge = slot - _corners - _edgesAcross, ex = edge % _cornerWidth, ey = edge / _cornerWidth;
                EnqueueCell(ex - 1, ey);
                EnqueueCell(ex, ey);
            }
        }

        void EnqueueCell(int x, int y)
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height) Enqueue(x + y * Width);
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

        // A solved corner is walkable when its one connection is
        bool IsWalkable(ulong mask) => mask != 0ul && (mask & ~WalkableCorners) == 0ul;

        /// <summary>
        /// Connects every walkable area to the start corner. Walkable corners next to each other (not diagonally) are
        /// connected; anything not reached from the start is a pocket. Small pockets (no locked corners) become not
        /// walkable, the rest get the shortest corridor to the reached area carved through anything else, never through
        /// locked corners. Then only the cells touching changed corners are solved again (every other cell keeps its tile)
        /// and the check repeats. False when a re-solve fails (no tile fits): the caller rerolls.
        /// </summary>
        bool Repair(ref Random random, NativeArray<ulong> initialMasks)
        {
            int corners = _corners;
            for (int c = 0; c < corners; c++) Repairs[c] = RepairNone;
            UnconnectedPockets = 0;
            if (StartCorner < 0 || StartCorner >= corners) return true;

            var component = new NativeArray<int>(corners, Allocator.Temp);
            var queue = new NativeList<int>(corners, Allocator.Temp);
            var parent = new NativeArray<int>(corners, Allocator.Temp);
            var masks = new NativeArray<ulong>(_slotMasks.Length, Allocator.Temp);
            var changed = new NativeArray<bool>(corners, Allocator.Temp);
            var cells = new NativeArray<int>(Width * Height, Allocator.Temp);

            for (int round = 0; round < MaxRepairRounds; round++)
            {
                // Label walkable areas: 0 is the start's, 1.. are pockets; -1 is not walkable
                for (int c = 0; c < corners; c++) component[c] = -1;
                if (!IsWalkable(_slotMasks[StartCorner])) return true;
                int components = 0;
                var pocketSize = new NativeList<int>(Allocator.Temp);
                var pocketLocked = new NativeList<bool>(Allocator.Temp);
                for (int seed = -1; seed < corners; seed++)
                {
                    int from = seed < 0 ? StartCorner : seed;
                    if (component[from] >= 0 || !IsWalkable(_slotMasks[from])) continue;
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
                            if (next < 0 || component[next] >= 0 || !IsWalkable(_slotMasks[next])) continue;
                            component[next] = label;
                            queue.Add(next);
                        }
                    }
                    pocketSize.Add(size);
                    pocketLocked.Add(locked);
                }
                if (components <= 1) return true;

                // Start from the solved slots and change pockets: fill small ones, carve corridors to the rest
                masks.CopyFrom(_slotMasks);
                for (int c = 0; c < corners; c++) changed[c] = false;
                int unconnected = 0;
                bool anyChange = false;
                for (int pocket = 1; pocket < components; pocket++)
                {
                    if (pocketSize[pocket] < FillPocketsSmallerThan && !pocketLocked[pocket] && NotWalkableCorners != 0ul)
                    {
                        for (int c = 0; c < corners; c++)
                        {
                            if (component[c] != pocket || LockedCorners[c] != 0) continue;
                            masks[c] = NotWalkableCorners;
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
                            if (!IsWalkable(_slotMasks[next]) && LockedCorners[next] != 0) continue;
                            parent[next] = corner;
                            if (component[next] == 0) { reached = next; break; }
                            queue.Add(next);
                        }
                    }
                    if (reached < 0) { unconnected++; continue; }
                    for (int c = parent[reached]; c >= 0; c = parent[c])
                    {
                        if (IsWalkable(masks[c])) continue;
                        masks[c] = WalkableCorners;
                        changed[c] = true;
                        Repairs[c] = RepairCarved;
                        anyChange = true;
                    }
                }
                UnconnectedPockets = unconnected;
                if (!anyChange) return true;

                // The edges touching a changed corner are free again (within their initial rules)
                for (int c = 0; c < corners; c++)
                {
                    if (!changed[c]) continue;
                    int cx = c % _cornerWidth, cy = c / _cornerWidth;
                    if (cx > 0) masks[_corners + cx - 1 + cy * Width] = initialMasks[_corners + cx - 1 + cy * Width];
                    if (cx < Width) masks[_corners + cx + cy * Width] = initialMasks[_corners + cx + cy * Width];
                    int below = _corners + _edgesAcross + cx + (cy - 1) * _cornerWidth, above = _corners + _edgesAcross + cx + cy * _cornerWidth;
                    if (cy > 0) masks[below] = initialMasks[below];
                    if (cy < Height) masks[above] = initialMasks[above];
                }

                // Solve only the cells touching a changed corner again; the rest keep their tiles
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int cell = x + y * Width;
                        if (InitialCells[cell] == Occupied) { cells[cell] = Occupied; continue; }
                        bool touched = false;
                        for (int k = 0; k < 4; k++) touched |= changed[Slot(x, y, k)];
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
        [ReadOnly] public NativeArray<byte> VariantSlots;
        [ReadOnly] public NativeArray<float> VariantWeights;
        [ReadOnly] public NativeArray<ulong> InitialCornerMasks;
        [ReadOnly] public NativeArray<ulong> InitialEdgeMasks;
        [ReadOnly] public NativeArray<int> InitialCells;
        public uint Seed;
        public int MaxAttempts;

        // Connectivity repair (WfcSolver.Repair); LockedCorners and Repairs are always allocated (one per corner)
        public bool Connect;
        public int StartCorner;
        public int FillPocketsSmallerThan;
        public ulong WalkableCorners, NotWalkableCorners;
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
                VariantSlots = VariantSlots, VariantWeights = VariantWeights,
                InitialCornerMasks = InitialCornerMasks, InitialEdgeMasks = InitialEdgeMasks, InitialCells = InitialCells,
                Seed = Seed, MaxAttempts = MaxAttempts,
                Connect = Connect, StartCorner = StartCorner, FillPocketsSmallerThan = FillPocketsSmallerThan,
                WalkableCorners = WalkableCorners, NotWalkableCorners = NotWalkableCorners,
                LockedCorners = LockedCorners, Repairs = Repairs,
            };
            Attempts.Value = solver.Solve(Result);
            UnconnectedPockets.Value = solver.UnconnectedPockets;
        }

        public void DisposeArrays()
        {
            VariantSlots.Dispose();
            VariantWeights.Dispose();
            InitialCornerMasks.Dispose();
            InitialEdgeMasks.Dispose();
            InitialCells.Dispose();
            LockedCorners.Dispose();
            Result.Dispose();
            Attempts.Dispose();
            Repairs.Dispose();
            UnconnectedPockets.Dispose();
        }
    }
}
