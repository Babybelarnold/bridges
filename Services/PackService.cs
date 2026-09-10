using System.Net.Http.Json;
using Bridges.Core;

namespace Bridges.Services;

/// <summary>
/// Loads the pre-generated puzzle pack (produced offline by Bridges.PackGen) and serves puzzles
/// from it. The Razor app does no generation of its own — it just picks puzzles out of the pack.
/// </summary>
public sealed class PackService
{
    private readonly HttpClient _http;
    private readonly Random _random = new();

    private PuzzlePack _pack = new();
    private bool _loaded;

    public PackService(HttpClient http) => _http = http;

    /// <summary>Distinct shapes available in the loaded pack, e.g. (Square, 9, 9).</summary>
    public IReadOnlyList<PackSize> Sizes { get; private set; } = Array.Empty<PackSize>();

    public bool Loaded => _loaded;

    /// <summary>Fetch and parse the pack file. Safe to call once at startup.</summary>
    public async Task LoadAsync(string url = "puzzles/pack.json")
    {
        if (_loaded)
        {
            return;
        }

        var pack = await _http.GetFromJsonAsync<PuzzlePack>(url);
        _pack = pack ?? new PuzzlePack();

        Sizes = _pack.Puzzles
            .GroupBy(p => new PackSize(p.Shape, p.Rows, p.Cols))
            .Select(g => g.Key)
            .OrderBy(s => s.Shape)
            .ThenBy(s => s.Rows)
            .ThenBy(s => s.Cols)
            .ToList();

        _loaded = true;
    }

    /// <summary>All puzzle definitions for a given size (used for progress counts).</summary>
    public IReadOnlyList<PuzzleDef> PuzzlesOf(PackSize size) =>
        _pack.Puzzles.Where(p => p.Shape == size.Shape && p.Rows == size.Rows && p.Cols == size.Cols).ToList();

    /// <summary>
    /// Pick a random puzzle of the given size, preferring ones not in <paramref name="solvedIds"/>
    /// so the player progresses through unsolved puzzles first. Returns null if the size is empty.
    /// </summary>
    public PuzzleDef? PickRandom(PackSize size, ISet<string> solvedIds)
    {
        var all = PuzzlesOf(size);
        if (all.Count == 0)
        {
            return null;
        }

        var unsolved = all.Where(p => !solvedIds.Contains(p.Id)).ToList();
        var pool = unsolved.Count > 0 ? unsolved : all;
        return pool[_random.Next(pool.Count)];
    }

    /// <summary>Look up a puzzle by its unique id.</summary>
    public PuzzleDef? ById(string id) => _pack.Puzzles.FirstOrDefault(p => p.Id == id);
}

/// <summary>A distinct puzzle size/shape available in the pack.</summary>
public readonly record struct PackSize(PuzzleShape Shape, int Rows, int Cols)
{
    public bool IsPortrait => Shape == PuzzleShape.Portrait;

    /// <summary>Human label, e.g. "9 x 9".</summary>
    public string Label => $"{Cols} x {Rows}";
}
