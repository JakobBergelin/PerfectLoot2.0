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
    /// connection there, and a slot keeps only connections some remaining variant next to it has. The cell with the lowest
    /// weighted entropy (the least uncertain) is collapsed (weighted random) and that spreads until nothing changes.
    /// Every change is written to an undo log: on a contradiction the last choice is undone and ruled out (backtracking),
    /// and only after one backtrack per cell does the whole grid restart with the next seed. Used by the editor preview and
    /// at runtime, so the same inputs and seed give the same level on every machine (WfcSolveJob uses deterministic
    /// floats). Run it through WfcSolveJob (Burst).
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
        /// <summary>Edge connections units can walk across (bits by index); a cliff edge isn't one.</summary>
        public ulong WalkableEdges;
        /// <summary>Per corner: 1 when the repair may never change it (painted rules, pieces, forced tiles, start, border).</summary>
        public NativeArray<byte> LockedCorners;
        /// <summary>Pockets with fewer walkable corners than this (and none locked) are filled with not walkable; larger ones are carved to.</summary>
        public int FillPocketsSmallerThan;
        /// <summary>Output per corner: RepairNone, RepairCarved or RepairFilled.</summary>
        public NativeArray<byte> Repairs;
        /// <summary>Output: pockets that couldn't be connected without changing locked corners.</summary>
        public int UnconnectedPockets;
        /// <summary>Output: choices undone over every attempt. Many means the tile set lacks tiles for combinations that come up.</summary>
        public int Backtracks;

        public const byte RepairNone = 0, RepairCarved = 1, RepairFilled = 2;
        const int MaxRepairRounds = 4;
        const int MaxRepairRing = 2;    // Rings of cells around the changes the repair may solve again when no tile fits

        int _variants, _words, _cornerWidth, _corners, _edgesAcross;
        NativeArray<ulong> _support;    // [slot s][connection][word]: variants with that connection in slot s
        NativeArray<ulong> _domains;    // [cell][word]
        NativeArray<ulong> _slotMasks;  // corners, then edges running east-west, then edges running north-south
        NativeList<int> _queue;
        NativeArray<bool> _queued;
        NativeArray<float> _weightLogWeight;    // Per variant: w ln w, for the entropy
        NativeArray<float> _entropy;            // Per cell, valid while not dirty
        NativeArray<int> _options;              // Per cell: variants left, valid while not dirty
        NativeArray<bool> _dirty;

        // Undo log: every change to a domain word (Index below _domains.Length) or slot mask (Index - _domains.Length)
        struct Change { public int Index; public ulong Old; }
        // A collapse: where the undo log was before it, so it can be undone, and what was chosen
        struct Decision { public int Log, Cell, Variant; }
        NativeList<Change> _log;
        NativeList<Decision> _decisions;

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
            _entropy = new NativeArray<float>(cells, Allocator.Temp);
            _options = new NativeArray<int>(cells, Allocator.Temp);
            _dirty = new NativeArray<bool>(cells, Allocator.Temp);
            _log = new NativeList<Change>(cells * 8, Allocator.Temp);
            _decisions = new NativeList<Decision>(cells, Allocator.Temp);
            _weightLogWeight = new NativeArray<float>(_variants, Allocator.Temp);
            for (int v = 0; v < _variants; v++)
                _weightLogWeight[v] = VariantWeights[v] > 0f ? VariantWeights[v] * math.log(VariantWeights[v]) : 0f;

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
                _dirty[c] = true;
                Enqueue(c);
            }
            _log.Clear();
            _decisions.Clear();
            if (!Propagate()) return false;

            int backtracks = 0;
            while (true)
            {
                int cell = LowestEntropyCell(ref random);
                if (cell < 0) return true;
                int chosen = Pick(cell, ref random);
                _decisions.Add(new Decision { Log = _log.Length, Cell = cell, Variant = chosen });
                for (int w = 0; w < _words; w++) SetDomain(cell * _words + w, chosen / 64 == w ? 1ul << (chosen % 64) : 0ul);
                Enqueue(cell);

                // A contradiction: undo the latest choice and rule it out, as far back as it takes
                while (!Propagate())
                {
                    if (_decisions.Length == 0 || ++backtracks > cells) return false;
                    Backtracks++;
                    var decision = _decisions[_decisions.Length - 1];
                    _decisions.RemoveAt(_decisions.Length - 1);
                    Undo(decision.Log);
                    int word = decision.Cell * _words + decision.Variant / 64;
                    SetDomain(word, _domains[word] & ~(1ul << (decision.Variant % 64)));
                    Enqueue(decision.Cell);
                }
            }
        }

        void SetDomain(int word, ulong value)
        {
            if (_domains[word] == value) return;
            _log.Add(new Change { Index = word, Old = _domains[word] });
            _domains[word] = value;
            _dirty[word / _words] = true;
        }

        void SetSlot(int slot, ulong value)
        {
            _log.Add(new Change { Index = _domains.Length + slot, Old = _slotMasks[slot] });
            _slotMasks[slot] = value;
        }

        // Restores every domain and slot changed since the undo log was this long
        void Undo(int length)
        {
            while (_log.Length > length)
            {
                var change = _log[_log.Length - 1];
                _log.RemoveAt(_log.Length - 1);
                if (change.Index < _domains.Length)
                {
                    _domains[change.Index] = change.Old;
                    _dirty[change.Index / _words] = true;
                }
                else _slotMasks[change.Index - _domains.Length] = change.Old;
            }
            for (int i = 0; i < _queue.Length; i++) _queued[_queue[i]] = false;
            _queue.Clear();
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
                    SetDomain(cell * _words + w, domain);
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
                    SetSlot(slot, kept);
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

        // Shannon entropy of the cell's remaining variants, weighted: ln(W) - Σ(w ln w) / W. Low when one option dominates.
        void UpdateEntropy(int cell)
        {
            float total = 0f, weightLogWeight = 0f;
            int count = 0;
            for (int w = 0; w < _words; w++)
                for (ulong bits = _domains[cell * _words + w]; bits != 0ul; bits &= bits - 1)
                {
                    int v = w * 64 + math.tzcnt(bits);
                    total += VariantWeights[v];
                    weightLogWeight += _weightLogWeight[v];
                    count++;
                }
            _options[cell] = count;
            _entropy[cell] = total > 0f ? math.log(total) - weightLogWeight / total : math.log(math.max(1, count));
            _dirty[cell] = false;
        }

        // Undecided cell with the lowest entropy; a little noise breaks ties at random. -1 when every cell is decided.
        int LowestEntropyCell(ref Random random)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int c = 0; c < Width * Height; c++)
            {
                if (InitialCells[c] == Occupied) continue;
                if (_dirty[c]) UpdateEntropy(c);
                if (_options[c] <= 1) continue;
                float score = _entropy[c] + random.NextFloat() * 0.001f;
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        // One of the cell's remaining variants, at random by weight
        int Pick(int cell, ref Random random)
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
            return chosen;
        }

        // A solved corner is walkable when its one connection is
        bool IsWalkable(ulong mask) => mask != 0ul && (mask & ~WalkableCorners) == 0ul;

        /// <summary>
        /// Connects every walkable area to the start corner. Walkable corners next to each other (not diagonally) are
        /// connected unless the edge between them can't be walked across (a cliff); anything not reached from the start is
        /// a pocket. Small pockets (no locked corners) become not walkable, the rest get the shortest corridor to the
        /// reached area carved through anything else, never through locked corners, and across a cliff only when there's
        /// no other way: its corners become walkable and its edges crossable (the cliff becomes a ramp). Then only the cells
        /// touching changed corners are solved again (every other cell keeps its tile), with up to two rings of cells
        /// around them when no tile fits, and the check repeats. When even that fails, the level stays as it was and those
        /// pockets are reported as unconnected. Always true (kept for the caller's reroll check).
        /// </summary>
        bool Repair(ref Random random, NativeArray<ulong> initialMasks)
        {
            int corners = _corners, cellCount = Width * Height;
            for (int c = 0; c < corners; c++) Repairs[c] = RepairNone;
            UnconnectedPockets = 0;
            if (StartCorner < 0 || StartCorner >= corners) return true;

            var component = new NativeArray<int>(corners, Allocator.Temp);
            var queue = new NativeList<int>(corners, Allocator.Temp);
            var parent = new NativeArray<int>(corners, Allocator.Temp);
            var masks = new NativeArray<ulong>(_slotMasks.Length, Allocator.Temp);
            var changed = new NativeArray<bool>(corners, Allocator.Temp);
            var cells = new NativeArray<int>(cellCount, Allocator.Temp);
            var openEdges = new NativeList<int>(Allocator.Temp);
            var start = new NativeArray<ulong>(_slotMasks.Length, Allocator.Temp);
            var near = new NativeArray<bool>(cellCount, Allocator.Temp);
            var grown = new NativeArray<bool>(cellCount, Allocator.Temp);
            var kept = new NativeArray<int>(cellCount, Allocator.Temp);
            var keptSlots = new NativeArray<ulong>(_slotMasks.Length, Allocator.Temp);
            var repairsBefore = new NativeArray<byte>(corners, Allocator.Temp);

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
                            if (next < 0 || component[next] >= 0 || !IsWalkable(_slotMasks[next]) || !CanCross(_slotMasks[EdgeBetween(corner, next)])) continue;
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
                repairsBefore.CopyFrom(Repairs);
                for (int c = 0; c < corners; c++) changed[c] = false;
                openEdges.Clear();
                int unconnected = 0, changedPockets = 0;
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
                        }
                        changedPockets++;
                        continue;
                    }

                    // The shortest path to the start's area: without crossing a cliff if there is one, else across one
                    int reached = ShortestPath(pocket, false, component, parent, queue);
                    if (reached < 0) reached = ShortestPath(pocket, true, component, parent, queue);
                    if (reached < 0) { unconnected++; continue; }
                    changedPockets++;
                    // Walk the path back to the pocket: every corner on it becomes walkable, every edge on it crossable
                    for (int c = reached; parent[c] >= 0; c = parent[c])
                    {
                        int from = parent[c], edge = EdgeBetween(c, from);
                        if (!CanCross(masks[edge]))
                        {
                            openEdges.Add(edge);
                            changed[c] = changed[from] = true;
                        }
                        if (IsWalkable(masks[from])) continue;
                        masks[from] = WalkableCorners;
                        changed[from] = true;
                        Repairs[from] = RepairCarved;
                    }
                }
                UnconnectedPockets = unconnected;
                if (changedPockets == 0) return true;

                // The re-solve starts from the layout's rules with the changed corners on top, and an edge the path crosses
                // may only become one units can walk across (a ramp instead of a cliff). Kept cells fix their slots again.
                start.CopyFrom(initialMasks);
                for (int c = 0; c < corners; c++)
                    if (changed[c]) start[c] = masks[c];
                for (int i = 0; i < openEdges.Length; i++)
                {
                    int edge = openEdges[i];
                    if ((initialMasks[edge] & WalkableEdges) != 0ul) start[edge] = initialMasks[edge] & WalkableEdges;
                }

                // Solve the cells touching a changed corner again; when no tile fits, also the cells around them (room for,
                // say, a ramp's other side), a ring at a time. Every other cell keeps its tile.
                for (int c = 0; c < cellCount; c++) kept[c] = InitialCells[c] == Occupied ? Occupied : FirstVariant(c);
                keptSlots.CopyFrom(_slotMasks);
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        bool touched = false;
                        for (int k = 0; k < 4; k++) touched |= changed[Slot(x, y, k)];
                        near[x + y * Width] = touched;
                    }
                bool solved = false;
                for (int ring = 0; ring <= MaxRepairRing && !solved; ring++)
                {
                    if (ring > 0) Grow(near, grown);
                    for (int c = 0; c < cellCount; c++)
                        cells[c] = kept[c] == Occupied || InitialCells[c] >= 0 ? kept[c] : near[c] ? Free : kept[c];
                    solved = TrySolve(ref random, start, cells);
                }
                if (!solved)
                {
                    // No tile fits even with room around: keep the level as it was, and those pockets stay unreachable
                    _slotMasks.CopyFrom(keptSlots);
                    for (int c = 0; c < cellCount; c++)
                        for (int w = 0; w < _words; w++)
                            _domains[c * _words + w] = kept[c] >= 0 && kept[c] / 64 == w ? 1ul << (kept[c] % 64) : 0ul;
                    Repairs.CopyFrom(repairsBefore);
                    UnconnectedPockets = unconnected + changedPockets;
                    return true;
                }
            }
            return true;
        }

        // Every cell next to a marked one (not diagonally) gets marked too
        void Grow(NativeArray<bool> marked, NativeArray<bool> grown)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int cell = x + y * Width;
                    grown[cell] = marked[cell] || (x > 0 && marked[cell - 1]) || (x < Width - 1 && marked[cell + 1]) ||
                                  (y > 0 && marked[cell - Width]) || (y < Height - 1 && marked[cell + Width]);
                }
            marked.CopyFrom(grown);
        }

        // Breadth-first from a pocket to the start's area (component 0) through any corner that isn't locked non-walkable,
        // across cliff edges only when allowed. Returns the corner reached (parent leads back to the pocket), or -1.
        int ShortestPath(int pocket, bool crossCliffs, NativeArray<int> component, NativeArray<int> parent, NativeList<int> queue)
        {
            for (int c = 0; c < _corners; c++) parent[c] = -2;
            queue.Clear();
            for (int c = 0; c < _corners; c++)
                if (component[c] == pocket) { parent[c] = -1; queue.Add(c); }
            for (int head = 0; head < queue.Length; head++)
            {
                int corner = queue[head];
                for (int n = 0; n < 4; n++)
                {
                    int next = Neighbor(corner, n);
                    if (next < 0 || parent[next] != -2) continue;
                    if (!IsWalkable(_slotMasks[next]) && LockedCorners[next] != 0) continue;
                    if (!crossCliffs && !CanCross(_slotMasks[EdgeBetween(corner, next)])) continue;
                    parent[next] = corner;
                    if (component[next] == 0) return next;
                    queue.Add(next);
                }
            }
            return -1;
        }

        // The edge slot between two neighboring corners: east-west edges run from (x, y) east, north-south ones from (x, y) north
        int EdgeBetween(int a, int b)
        {
            int low = math.min(a, b), x = low % _cornerWidth, y = low / _cornerWidth;
            return math.abs(a - b) == 1 ? _corners + x + y * Width : _corners + _edgesAcross + x + y * _cornerWidth;
        }

        // An edge units can walk across (a solved edge has one connection; one inside a piece is still open and counts)
        bool CanCross(ulong mask) => (mask & WalkableEdges) != 0ul;

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

    /// <summary>
    /// Runs WfcSolver with Burst. The working arrays live inside the solver, so they never sit on the job. Deterministic
    /// floats: every machine in a multiplayer game builds the level from the same seed and must get the same tiles.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic)]
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
        public ulong WalkableCorners, NotWalkableCorners, WalkableEdges;
        [ReadOnly] public NativeArray<byte> LockedCorners;

        /// <summary>Per cell: the chosen variant, or WfcSolver.Occupied. Only valid when Attempts &gt; 0.</summary>
        public NativeArray<int> Result;
        /// <summary>The attempt that succeeded (1-based), or 0 when every attempt failed.</summary>
        public NativeReference<int> Attempts;
        /// <summary>Per corner: what the repair did (WfcSolver.RepairNone / RepairCarved / RepairFilled).</summary>
        public NativeArray<byte> Repairs;
        public NativeReference<int> UnconnectedPockets;
        /// <summary>Choices undone over every attempt (WfcSolver.Backtracks).</summary>
        public NativeReference<int> Backtracks;

        public void Execute()
        {
            var solver = new WfcSolver
            {
                Width = Width, Height = Height,
                VariantSlots = VariantSlots, VariantWeights = VariantWeights,
                InitialCornerMasks = InitialCornerMasks, InitialEdgeMasks = InitialEdgeMasks, InitialCells = InitialCells,
                Seed = Seed, MaxAttempts = MaxAttempts,
                Connect = Connect, StartCorner = StartCorner, FillPocketsSmallerThan = FillPocketsSmallerThan,
                WalkableCorners = WalkableCorners, NotWalkableCorners = NotWalkableCorners, WalkableEdges = WalkableEdges,
                LockedCorners = LockedCorners, Repairs = Repairs,
            };
            Attempts.Value = solver.Solve(Result);
            UnconnectedPockets.Value = solver.UnconnectedPockets;
            Backtracks.Value = solver.Backtracks;
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
            Backtracks.Dispose();
        }
    }
}
