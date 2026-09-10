namespace Bridges.Core;

/// <summary>
/// Holds the full state of a Bridges (Hashiwokakero) puzzle: the grid dimensions,
/// the islands, and every candidate bridge between neighbouring islands. Enforces
/// the game rules and reports when the puzzle is solved.
/// </summary>
public sealed class Board
{
    private readonly Island?[,] _grid;
    private readonly List<Bridge> _bridges = new();

    // Fast lookup of the candidate bridge between two islands (keyed by unordered id pair).
    private readonly Dictionary<(int, int), Bridge> _bridgeByPair = new();

    public Board(int rows, int cols, IReadOnlyList<Island> islands)
    {
        Rows = rows;
        Cols = cols;
        Islands = islands;
        _grid = new Island?[rows, cols];
        foreach (var island in islands)
        {
            _grid[island.Row, island.Col] = island;
        }

        BuildCandidateBridges();
    }

    public int Rows { get; }
    public int Cols { get; }
    public IReadOnlyList<Island> Islands { get; }
    public IReadOnlyList<Bridge> Bridges => _bridges;

    public Island? IslandAt(int row, int col) =>
        row >= 0 && row < Rows && col >= 0 && col < Cols ? _grid[row, col] : null;

    /// <summary>
    /// For each island, find the nearest island directly to its right and directly below it.
    /// Those adjacencies (with nothing between them) are the only places a bridge can ever go.
    /// </summary>
    private void BuildCandidateBridges()
    {
        foreach (var island in Islands)
        {
            // Nearest island to the right on the same row.
            for (int c = island.Col + 1; c < Cols; c++)
            {
                var other = _grid[island.Row, c];
                if (other != null)
                {
                    AddCandidate(island, other);
                    break;
                }
            }

            // Nearest island below on the same column.
            for (int r = island.Row + 1; r < Rows; r++)
            {
                var other = _grid[r, island.Col];
                if (other != null)
                {
                    AddCandidate(island, other);
                    break;
                }
            }
        }
    }

    private void AddCandidate(Island a, Island b)
    {
        var bridge = new Bridge(a, b);
        _bridges.Add(bridge);
        _bridgeByPair[Key(a.Id, b.Id)] = bridge;
    }

    private static (int, int) Key(int idA, int idB) => idA < idB ? (idA, idB) : (idB, idA);

    public Bridge? BridgeBetween(Island a, Island b) =>
        _bridgeByPair.TryGetValue(Key(a.Id, b.Id), out var bridge) ? bridge : null;

    /// <summary>Current number of bridge-ends connected to an island.</summary>
    public int DegreeOf(Island island)
    {
        int total = 0;
        foreach (var bridge in _bridges)
        {
            if (bridge.A.Id == island.Id || bridge.B.Id == island.Id)
            {
                total += bridge.Count;
            }
        }
        return total;
    }

    /// <summary>
    /// Cycle a bridge's count 0 -> 1 -> 2 -> 0, but only apply the change if it stays legal
    /// (does not physically cross another drawn bridge). Returns the resulting count, or -1
    /// if the pair has no candidate bridge.
    /// </summary>
    public int CycleBridge(Island a, Island b)
    {
        var bridge = BridgeBetween(a, b);
        if (bridge == null)
        {
            return -1;
        }

        int next = (bridge.Count + 1) % 3;

        // Only need to check crossings when going from 0 to 1 (adding the line).
        if (bridge.Count == 0 && next == 1 && WouldCross(bridge))
        {
            // Blocked by a crossing bridge; leave it as-is.
            return bridge.Count;
        }

        bridge.Count = next;
        return bridge.Count;
    }

    public void SetBridge(Island a, Island b, int count)
    {
        var bridge = BridgeBetween(a, b);
        if (bridge != null)
        {
            bridge.Count = Math.Clamp(count, 0, 2);
        }
    }

    public void ClearAllBridges()
    {
        foreach (var bridge in _bridges)
        {
            bridge.Count = 0;
        }
    }

    /// <summary>
    /// True if drawing <paramref name="candidate"/> would intersect any other currently-drawn
    /// bridge. A horizontal and vertical bridge cross when their spans overlap at a cell.
    /// </summary>
    private bool WouldCross(Bridge candidate)
    {
        foreach (var other in _bridges)
        {
            if (other == candidate || other.Count == 0)
            {
                continue;
            }

            if (candidate.IsHorizontal && other.IsVertical)
            {
                if (Crosses(candidate, other))
                {
                    return true;
                }
            }
            else if (candidate.IsVertical && other.IsHorizontal)
            {
                if (Crosses(other, candidate))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Does horizontal bridge <paramref name="h"/> intersect vertical bridge <paramref name="v"/>?</summary>
    private static bool Crosses(Bridge h, Bridge v)
    {
        int row = h.A.Row;             // horizontal bridge sits on this row
        int col = v.A.Col;             // vertical bridge sits on this column
        bool colWithinH = col > h.A.Col && col < h.B.Col;
        bool rowWithinV = row > v.A.Row && row < v.B.Row;
        return colWithinH && rowWithinV;
    }

    /// <summary>
    /// The puzzle is solved when every island's degree matches its required number and all
    /// islands form a single connected component via the drawn bridges.
    /// </summary>
    public bool IsSolved()
    {
        foreach (var island in Islands)
        {
            if (DegreeOf(island) != island.Required)
            {
                return false;
            }
        }
        return AllConnected();
    }

    /// <summary>Breadth-first search across drawn bridges to confirm one connected group.</summary>
    private bool AllConnected()
    {
        if (Islands.Count == 0)
        {
            return true;
        }

        var adjacency = new Dictionary<int, List<int>>();
        foreach (var island in Islands)
        {
            adjacency[island.Id] = new List<int>();
        }
        foreach (var bridge in _bridges)
        {
            if (bridge.Count > 0)
            {
                adjacency[bridge.A.Id].Add(bridge.B.Id);
                adjacency[bridge.B.Id].Add(bridge.A.Id);
            }
        }

        var visited = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(Islands[0].Id);
        visited.Add(Islands[0].Id);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int neighbour in adjacency[current])
            {
                if (visited.Add(neighbour))
                {
                    queue.Enqueue(neighbour);
                }
            }
        }

        return visited.Count == Islands.Count;
    }
}
