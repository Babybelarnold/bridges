using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bridges.Core;

/// <summary>Shape category of a puzzle, used by the UI to pick a device-appropriate set.</summary>
public enum PuzzleShape
{
    /// <summary>Square (rows == cols). Suits desktop/landscape.</summary>
    Square,

    /// <summary>Portrait (rows &gt; cols). Suits phone screens.</summary>
    Portrait
}

/// <summary>A single island clue in a serialized puzzle.</summary>
public sealed class IslandDef
{
    /// <summary>Row (0-based).</summary>
    [JsonPropertyName("r")] public int Row { get; set; }

    /// <summary>Column (0-based).</summary>
    [JsonPropertyName("c")] public int Col { get; set; }

    /// <summary>Required bridge count (the printed number).</summary>
    [JsonPropertyName("n")] public int Required { get; set; }
}

/// <summary>A serialized, uniquely-solvable puzzle plus a stable id for progress tracking.</summary>
public sealed class PuzzleDef
{
    /// <summary>Stable unique id (GUID string). Used to track completion per player.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("rows")] public int Rows { get; set; }
    [JsonPropertyName("cols")] public int Cols { get; set; }

    [JsonPropertyName("shape")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PuzzleShape Shape { get; set; }

    [JsonPropertyName("islands")] public List<IslandDef> Islands { get; set; } = new();

    /// <summary>Build a playable (empty) <see cref="Board"/> from this definition.</summary>
    public Board ToBoard()
    {
        var islands = new List<Island>(Islands.Count);
        for (int i = 0; i < Islands.Count; i++)
        {
            var d = Islands[i];
            islands.Add(new Island(i, d.Row, d.Col, d.Required));
        }
        return new Board(Rows, Cols, islands);
    }

    /// <summary>Create a definition from a solved/known board, assigning a fresh unique id.</summary>
    public static PuzzleDef FromBoard(Board board)
    {
        var shape = board.Rows > board.Cols ? PuzzleShape.Portrait : PuzzleShape.Square;
        return new PuzzleDef
        {
            Id = Guid.NewGuid().ToString("N"),
            Rows = board.Rows,
            Cols = board.Cols,
            Shape = shape,
            Islands = board.Islands
                .Select(i => new IslandDef { Row = i.Row, Col = i.Col, Required = i.Required })
                .ToList()
        };
    }
}

/// <summary>A pack of generated puzzles. This is the file the Razor app loads at runtime.</summary>
public sealed class PuzzlePack
{
    [JsonPropertyName("generatedUtc")] public DateTime GeneratedUtc { get; set; }

    [JsonPropertyName("puzzles")] public List<PuzzleDef> Puzzles { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static PuzzlePack FromJson(string json) =>
        JsonSerializer.Deserialize<PuzzlePack>(json, Options) ?? new PuzzlePack();
}
