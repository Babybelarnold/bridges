namespace Bridges.Core;

/// <summary>
/// An island (node) on the board. Its <see cref="Required"/> value is the number of
/// bridge-ends that must connect to it in a solved puzzle.
/// </summary>
public sealed class Island
{
    public Island(int id, int row, int col, int required)
    {
        Id = id;
        Row = row;
        Col = col;
        Required = required;
    }

    public int Id { get; }
    public int Row { get; }
    public int Col { get; }

    /// <summary>Total bridge count this island must end up with (its printed number).</summary>
    public int Required { get; }
}

/// <summary>
/// A potential connection between two axis-aligned, unobstructed islands. The
/// <see cref="Count"/> is how many bridges the player has currently drawn (0, 1 or 2).
/// </summary>
public sealed class Bridge
{
    public Bridge(Island a, Island b)
    {
        // Normalise so A is always the top/left island. This makes crossing checks simpler.
        if (a.Row < b.Row || (a.Row == b.Row && a.Col < b.Col))
        {
            A = a;
            B = b;
        }
        else
        {
            A = b;
            B = a;
        }
    }

    public Island A { get; }
    public Island B { get; }

    /// <summary>0 = none, 1 = single, 2 = double.</summary>
    public int Count { get; set; }

    public bool IsHorizontal => A.Row == B.Row;
    public bool IsVertical => A.Col == B.Col;
}
