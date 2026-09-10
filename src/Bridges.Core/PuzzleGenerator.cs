namespace Bridges.Core;

/// <summary>
/// Generates uniquely-solvable Bridges puzzles by incremental, uniqueness-preserving growth.
///
/// It grows the solution one island at a time. After each candidate addition it derives the clues
/// and asks the solver whether the puzzle still has exactly one solution. Additions that preserve
/// uniqueness are committed; additions that introduce ambiguity are rolled back and a different
/// move is tried. Because uniqueness is an invariant at every step, the board can grow large while
/// staying unique.
///
/// This runs offline (in the pack generator), so the time budgets are generous — there is no UI
/// thread to keep responsive.
/// </summary>
public sealed class PuzzleGenerator
{
    // Wall-clock cap for a single grow attempt. Offline generation can afford a generous budget.
    private readonly int _growthBudgetMs;

    private readonly Random _random;

    public PuzzleGenerator(int? seed = null, int growthBudgetMs = 2000)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _growthBudgetMs = growthBudgetMs;
    }

    private sealed record SolutionBridge(int R1, int C1, int R2, int C2, int Count);

    /// <summary>Generate a uniquely-solvable square puzzle of the given size.</summary>
    public Board Generate(int gridSize) => Generate(gridSize, gridSize);

    /// <summary>
    /// Generate a uniquely-solvable puzzle by growing within an approximately
    /// <paramref name="rows"/> x <paramref name="cols"/> area and then compacting away empty rows
    /// and columns. The returned board's dimensions are the compacted size (at most the requested
    /// size), and the board is fully packed with no empty lines. Supports portrait, square, and
    /// landscape shapes. Callers that need specific sizes should read the actual dimensions off
    /// the returned board.
    /// </summary>
    public Board Generate(int rows, int cols)
    {
        rows = Math.Clamp(rows, 3, 40);
        cols = Math.Clamp(cols, 3, 40);

        // Aim for roughly a third of the cells to be islands (based on the requested area).
        int targetIslands = Math.Max(2, rows * cols / 3);

        // Grow in a slightly padded grid so that after compaction the board frequently lands at
        // exactly the requested size. Growing in exactly rows x cols almost always compacts to
        // something smaller because growth rarely reaches every edge.
        var board = Grow(rows + 1, cols + 1, targetIslands);
        board.ClearAllBridges(); // hand out an empty board to solve
        return board;
    }

    private Board Grow(int rows, int cols, int targetIslands)
    {
        var occupied = new bool[rows, cols];
        var hSeg = new bool[rows, cols];
        var vSeg = new bool[rows, cols];
        var placed = new List<(int row, int col)>();
        var bridges = new List<SolutionBridge>();

        int startR = _random.Next(rows);
        int startC = _random.Next(cols);
        occupied[startR, startC] = true;
        placed.Add((startR, startC));

        int stalls = 0;
        int maxStalls = targetIslands * 8;
        var deadline = DateTime.UtcNow.AddMilliseconds(_growthBudgetMs);

        while (placed.Count < targetIslands && stalls < maxStalls)
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            if (TryGrowOnce(rows, cols, occupied, hSeg, vSeg, placed, bridges))
            {
                stalls = 0;
            }
            else
            {
                stalls++;
            }
        }

        var (cRows, cCols) = Compact(rows, cols, placed, bridges);
        var board = BuildBoard(cRows, cCols, placed, bridges);
        return board ?? BuildTrivial();
    }

    /// <summary>
    /// Remove every completely-empty row and column. Because bridges are strictly horizontal or
    /// vertical, a line with nothing on it has nothing passing through it, so collapsing it cannot
    /// change adjacency, crossings, or the solution.
    /// </summary>
    private static (int rows, int cols) Compact(int rows, int cols,
        List<(int row, int col)> placed, List<SolutionBridge> bridges)
    {
        var rowUsed = new bool[rows];
        var colUsed = new bool[cols];

        foreach (var (r, c) in placed)
        {
            rowUsed[r] = true;
            colUsed[c] = true;
        }

        foreach (var b in bridges)
        {
            for (int r = Math.Min(b.R1, b.R2); r <= Math.Max(b.R1, b.R2); r++)
            {
                rowUsed[r] = true;
            }
            for (int c = Math.Min(b.C1, b.C2); c <= Math.Max(b.C1, b.C2); c++)
            {
                colUsed[c] = true;
            }
        }

        var newRow = BuildIndexMap(rowUsed, out int newRows);
        var newCol = BuildIndexMap(colUsed, out int newCols);

        for (int i = 0; i < placed.Count; i++)
        {
            placed[i] = (newRow[placed[i].row], newCol[placed[i].col]);
        }
        for (int i = 0; i < bridges.Count; i++)
        {
            var b = bridges[i];
            bridges[i] = b with
            {
                R1 = newRow[b.R1],
                C1 = newCol[b.C1],
                R2 = newRow[b.R2],
                C2 = newCol[b.C2]
            };
        }

        return (newRows, newCols);
    }

    private static int[] BuildIndexMap(bool[] used, out int newCount)
    {
        var map = new int[used.Length];
        int next = 0;
        for (int i = 0; i < used.Length; i++)
        {
            map[i] = next;
            if (used[i])
            {
                next++;
            }
        }
        newCount = next;
        return map;
    }

    private bool TryGrowOnce(int rows, int cols, bool[,] occupied, bool[,] hSeg, bool[,] vSeg,
        List<(int row, int col)> placed, List<SolutionBridge> bridges)
    {
        var (fromR, fromC) = placed[_random.Next(placed.Count)];

        foreach (var (dr, dc) in ShuffledDirections())
        {
            int maxDist = (dr, dc) switch
            {
                (0, 1) => cols - 1 - fromC,
                (0, -1) => fromC,
                (1, 0) => rows - 1 - fromR,
                _ => fromR
            };
            if (maxDist < 2)
            {
                continue;
            }

            int dist = PickShortDistance(maxDist);
            int toR = fromR + dr * dist;
            int toC = fromC + dc * dist;

            if (occupied[toR, toC] || !PathIsClear(fromR, fromC, toR, toC, occupied, hSeg, vSeg, dr, dc))
            {
                continue;
            }

            foreach (int count in _random.Next(100) < 20 ? new[] { 2, 1 } : new[] { 1, 2 })
            {
                occupied[toR, toC] = true;
                placed.Add((toR, toC));
                MarkPath(fromR, fromC, toR, toC, hSeg, vSeg, dr, dc);
                bridges.Add(new SolutionBridge(fromR, fromC, toR, toC, count));

                var candidate = BuildBoard(rows, cols, placed, bridges);
                if (candidate != null && new Solver(candidate).HasUniqueSolution())
                {
                    return true;
                }

                bridges.RemoveAt(bridges.Count - 1);
                placed.RemoveAt(placed.Count - 1);
                occupied[toR, toC] = false;
                UnmarkPath(fromR, fromC, toR, toC, hSeg, vSeg, dr, dc);
            }
        }

        return false;
    }

    private static Board? BuildBoard(int rows, int cols,
        List<(int row, int col)> placed, List<SolutionBridge> bridges)
    {
        if (placed.Count < 2)
        {
            return null;
        }

        var idByCell = new Dictionary<(int, int), int>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            idByCell[placed[i]] = i;
        }

        var required = new int[placed.Count];
        foreach (var b in bridges)
        {
            required[idByCell[(b.R1, b.C1)]] += b.Count;
            required[idByCell[(b.R2, b.C2)]] += b.Count;
        }

        var islands = new List<Island>(placed.Count);
        for (int i = 0; i < placed.Count; i++)
        {
            if (required[i] == 0)
            {
                return null;
            }
            islands.Add(new Island(i, placed[i].row, placed[i].col, required[i]));
        }

        return new Board(rows, cols, islands);
    }

    private static bool PathIsClear(int fromR, int fromC, int toR, int toC,
        bool[,] occupied, bool[,] hSeg, bool[,] vSeg, int dr, int dc)
    {
        int r = fromR + dr;
        int c = fromC + dc;
        bool horizontal = dr == 0;
        while (r != toR || c != toC)
        {
            if (occupied[r, c])
            {
                return false;
            }
            if (horizontal && vSeg[r, c])
            {
                return false;
            }
            if (!horizontal && hSeg[r, c])
            {
                return false;
            }
            r += dr;
            c += dc;
        }
        return true;
    }

    private static void MarkPath(int fromR, int fromC, int toR, int toC,
        bool[,] hSeg, bool[,] vSeg, int dr, int dc) =>
        SetPath(fromR, fromC, toR, toC, hSeg, vSeg, dr, dc, true);

    private static void UnmarkPath(int fromR, int fromC, int toR, int toC,
        bool[,] hSeg, bool[,] vSeg, int dr, int dc) =>
        SetPath(fromR, fromC, toR, toC, hSeg, vSeg, dr, dc, false);

    private static void SetPath(int fromR, int fromC, int toR, int toC,
        bool[,] hSeg, bool[,] vSeg, int dr, int dc, bool value)
    {
        int r = fromR + dr;
        int c = fromC + dc;
        bool horizontal = dr == 0;
        while (r != toR || c != toC)
        {
            if (horizontal)
            {
                hSeg[r, c] = value;
            }
            else
            {
                vSeg[r, c] = value;
            }
            r += dr;
            c += dc;
        }
    }

    private int PickShortDistance(int maxDist)
    {
        if (maxDist <= 2)
        {
            return 2;
        }
        if (_random.Next(100) < 75)
        {
            return 2;
        }
        return _random.Next(2, Math.Min(maxDist, 4) + 1);
    }

    private IEnumerable<(int, int)> ShuffledDirections()
    {
        var dirs = new List<(int, int)> { (0, 1), (0, -1), (1, 0), (-1, 0) };
        for (int i = dirs.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }
        return dirs;
    }

    private static Board BuildTrivial()
    {
        var islands = new List<Island>
        {
            new(0, 0, 0, 2),
            new(1, 0, 2, 2)
        };
        return new Board(3, 3, islands);
    }
}
