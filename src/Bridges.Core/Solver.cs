namespace Bridges.Core;

/// <summary>
/// Backtracking solver for Bridges puzzles, used to guarantee a generated puzzle has exactly
/// one solution. Naive branching on every edge is exponential, so this solver leans on
/// constraint propagation first: it repeatedly deduces forced bridge counts from island
/// requirements, and only branches (choosing the most-constrained island) when nothing more
/// can be deduced. It counts solutions, stopping as soon as a second is found.
/// </summary>
public sealed class Solver
{
    private const int Undecided = -1;

    private readonly int _islandCount;
    private readonly int[] _required;

    private readonly int _edgeCount;
    private readonly int[] _edgeA;
    private readonly int[] _edgeB;
    private readonly bool[] _edgeHorizontal;
    private readonly int[] _edgeRow;
    private readonly int[] _edgeCol;
    private readonly int[] _edgeMin;
    private readonly int[] _edgeMax;

    private readonly int[][] _edgesByIsland;
    private readonly int[][] _crossingEdges;

    private int _solutions;

    // When capturing, the first complete solution's per-edge counts are stored here.
    private bool _capture;
    private int[]? _captureSolution;

    public Solver(Board board)
    {
        _islandCount = board.Islands.Count;
        _required = new int[_islandCount];

        var indexById = new Dictionary<int, int>(_islandCount);
        for (int i = 0; i < _islandCount; i++)
        {
            var island = board.Islands[i];
            indexById[island.Id] = i;
            _required[i] = island.Required;
        }

        var bridges = board.Bridges;
        _edgeCount = bridges.Count;
        _edgeA = new int[_edgeCount];
        _edgeB = new int[_edgeCount];
        _edgeHorizontal = new bool[_edgeCount];
        _edgeRow = new int[_edgeCount];
        _edgeCol = new int[_edgeCount];
        _edgeMin = new int[_edgeCount];
        _edgeMax = new int[_edgeCount];

        var incident = new List<int>[_islandCount];
        for (int i = 0; i < _islandCount; i++)
        {
            incident[i] = new List<int>();
        }

        for (int e = 0; e < _edgeCount; e++)
        {
            var bridge = bridges[e];
            int a = indexById[bridge.A.Id];
            int b = indexById[bridge.B.Id];
            _edgeA[e] = a;
            _edgeB[e] = b;
            _edgeHorizontal[e] = bridge.IsHorizontal;
            if (bridge.IsHorizontal)
            {
                _edgeRow[e] = bridge.A.Row;
                _edgeMin[e] = Math.Min(bridge.A.Col, bridge.B.Col);
                _edgeMax[e] = Math.Max(bridge.A.Col, bridge.B.Col);
            }
            else
            {
                _edgeCol[e] = bridge.A.Col;
                _edgeMin[e] = Math.Min(bridge.A.Row, bridge.B.Row);
                _edgeMax[e] = Math.Max(bridge.A.Row, bridge.B.Row);
            }
            incident[a].Add(e);
            incident[b].Add(e);
        }

        _edgesByIsland = new int[_islandCount][];
        for (int i = 0; i < _islandCount; i++)
        {
            _edgesByIsland[i] = incident[i].ToArray();
        }

        var crossings = new List<int>[_edgeCount];
        for (int e = 0; e < _edgeCount; e++)
        {
            crossings[e] = new List<int>();
        }
        for (int e = 0; e < _edgeCount; e++)
        {
            for (int f = e + 1; f < _edgeCount; f++)
            {
                if (_edgeHorizontal[e] == _edgeHorizontal[f])
                {
                    continue;
                }
                int h = _edgeHorizontal[e] ? e : f;
                int v = _edgeHorizontal[e] ? f : e;
                bool colInside = _edgeCol[v] > _edgeMin[h] && _edgeCol[v] < _edgeMax[h];
                bool rowInside = _edgeRow[h] > _edgeMin[v] && _edgeRow[h] < _edgeMax[v];
                if (colInside && rowInside)
                {
                    crossings[e].Add(f);
                    crossings[f].Add(e);
                }
            }
        }
        _crossingEdges = new int[_edgeCount][];
        for (int e = 0; e < _edgeCount; e++)
        {
            _crossingEdges[e] = crossings[e].ToArray();
        }
    }

    public bool HasUniqueSolution() => CountSolutions() == 1;

    /// <summary>Number of complete solutions, capped at 2.</summary>
    public int CountSolutions()
    {
        _solutions = 0;
        _captureSolution = null;
        var counts = new int[_edgeCount];
        Array.Fill(counts, Undecided);
        Solve(counts);
        return _solutions;
    }

    /// <summary>
    /// Return the per-edge bridge counts of a solution, indexed to match the board's
    /// <see cref="Board.Bridges"/> order. Because generated puzzles are uniquely solvable this is
    /// the one true answer. Returns null if the puzzle has no solution.
    /// </summary>
    public int[]? SolveEdges()
    {
        _solutions = 0;
        _captureSolution = null;
        _capture = true;
        var counts = new int[_edgeCount];
        Array.Fill(counts, Undecided);
        Solve(counts);
        _capture = false;
        return _captureSolution;
    }

    private void Solve(int[] counts)
    {
        if (_solutions >= 2)
        {
            return;
        }

        var local = (int[])counts.Clone();

        if (!Propagate(local))
        {
            return;
        }

        int branchIsland = -1;
        int fewest = int.MaxValue;
        for (int i = 0; i < _islandCount; i++)
        {
            int undecided = 0;
            foreach (int e in _edgesByIsland[i])
            {
                if (local[e] == Undecided)
                {
                    undecided++;
                }
            }
            if (undecided > 0 && undecided < fewest)
            {
                fewest = undecided;
                branchIsland = i;
            }
        }

        if (branchIsland == -1)
        {
            if (AllConnected(local))
            {
                _solutions++;
                if (_capture && _captureSolution == null)
                {
                    // Undecided edges here are forced to 0 (propagation left them free but the
                    // requirements are already met), so treat any remaining -1 as 0.
                    _captureSolution = local.Select(v => v < 0 ? 0 : v).ToArray();
                }
            }
            return;
        }

        int edge = -1;
        foreach (int e in _edgesByIsland[branchIsland])
        {
            if (local[e] == Undecided)
            {
                edge = e;
                break;
            }
        }

        for (int value = 0; value <= 2; value++)
        {
            if (!CanAssign(local, edge, value))
            {
                continue;
            }
            var branch = (int[])local.Clone();
            branch[edge] = value;
            Solve(branch);
            if (_solutions >= 2)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Repeatedly apply forced deductions until nothing changes or a contradiction is found.
    /// Returns false on contradiction.
    /// </summary>
    private bool Propagate(int[] counts)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;

            for (int e = 0; e < _edgeCount; e++)
            {
                if (counts[e] > 0)
                {
                    foreach (int x in _crossingEdges[e])
                    {
                        if (counts[x] == Undecided)
                        {
                            counts[x] = 0;
                            changed = true;
                        }
                        else if (counts[x] > 0)
                        {
                            return false;
                        }
                    }
                }
            }

            for (int i = 0; i < _islandCount; i++)
            {
                int committed = 0;
                int capacity = 0;
                foreach (int e in _edgesByIsland[i])
                {
                    if (counts[e] == Undecided)
                    {
                        capacity += 2;
                    }
                    else
                    {
                        committed += counts[e];
                    }
                }

                int need = _required[i] - committed;
                if (need < 0 || need > capacity)
                {
                    return false;
                }

                if (need == capacity && capacity > 0)
                {
                    foreach (int e in _edgesByIsland[i])
                    {
                        if (counts[e] == Undecided && CanAssign(counts, e, 2))
                        {
                            counts[e] = 2;
                            changed = true;
                        }
                        else if (counts[e] == Undecided)
                        {
                            return false;
                        }
                    }
                }
                else if (need == 0 && capacity > 0)
                {
                    foreach (int e in _edgesByIsland[i])
                    {
                        if (counts[e] == Undecided)
                        {
                            counts[e] = 0;
                            changed = true;
                        }
                    }
                }
            }
        }

        return true;
    }

    private bool CanAssign(int[] counts, int e, int value)
    {
        if (value == 0)
        {
            return true;
        }

        if (ExceedsRequirement(counts, _edgeA[e], e, value) || ExceedsRequirement(counts, _edgeB[e], e, value))
        {
            return false;
        }

        foreach (int x in _crossingEdges[e])
        {
            if (counts[x] > 0)
            {
                return false;
            }
        }
        return true;
    }

    private bool ExceedsRequirement(int[] counts, int island, int edge, int value)
    {
        int committed = 0;
        foreach (int e in _edgesByIsland[island])
        {
            if (e == edge)
            {
                committed += value;
            }
            else if (counts[e] != Undecided)
            {
                committed += counts[e];
            }
        }
        return committed > _required[island];
    }

    private bool AllConnected(int[] counts)
    {
        if (_islandCount == 0)
        {
            return true;
        }

        var visited = new bool[_islandCount];
        var stack = new Stack<int>();
        stack.Push(0);
        visited[0] = true;
        int seen = 1;

        while (stack.Count > 0)
        {
            int current = stack.Pop();
            foreach (int e in _edgesByIsland[current])
            {
                if (counts[e] <= 0)
                {
                    continue;
                }
                int neighbour = _edgeA[e] == current ? _edgeB[e] : _edgeA[e];
                if (!visited[neighbour])
                {
                    visited[neighbour] = true;
                    seen++;
                    stack.Push(neighbour);
                }
            }
        }

        return seen == _islandCount;
    }
}
