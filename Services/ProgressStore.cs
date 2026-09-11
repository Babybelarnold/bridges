using System.Text.Json;
using Microsoft.JSInterop;

namespace Bridges.Services;

/// <summary>A per-puzzle completion record. Kept small and leaderboard-ready.</summary>
public sealed class PuzzleResult
{
    /// <summary>Best (lowest) solve time in whole seconds.</summary>
    public int BestSeconds { get; set; }

    /// <summary>UTC timestamp of when the best time was achieved.</summary>
    public DateTime SolvedUtc { get; set; }
}

/// <summary>
/// Tracks the player's puzzle completions, keyed by the puzzle's unique id. For each solved puzzle
/// it stores the best time and when it was achieved, persisted to browser localStorage as JSON so
/// progress and times survive page reloads on the device.
///
/// This is intentionally leaderboard-ready: best-time-per-puzzle is exactly the record a future
/// server-backed leaderboard would submit. A static WASM app can't share times between players,
/// so a real leaderboard needs a backend; this stores the local best in the meantime.
/// </summary>
public sealed class ProgressStore
{
    private const string StorageKey = "bridges.results.v1";

    private readonly IJSRuntime _js;
    private readonly Dictionary<string, PuzzleResult> _results = new();
    private bool _loaded;

    public ProgressStore(IJSRuntime js) => _js = js;

    public IReadOnlyCollection<string> SolvedIds => _results.Keys;

    public bool IsSolved(string id) => _results.ContainsKey(id);

    /// <summary>The stored best result for a puzzle, or null if never solved.</summary>
    public PuzzleResult? ResultFor(string id) => _results.TryGetValue(id, out var r) ? r : null;

    /// <summary>Load persisted results from localStorage. Call once at startup.</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var raw = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, PuzzleResult>>(raw);
                if (loaded != null)
                {
                    foreach (var kvp in loaded)
                    {
                        _results[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        catch
        {
            // localStorage unavailable or corrupt; start empty.
        }

        _loaded = true;
    }

    /// <summary>
    /// Record a solve. Keeps the fastest time seen for the puzzle. Returns true if this became the
    /// new best (or the first) time, false if a previous solve was faster.
    /// </summary>
    public async Task<bool> RecordSolveAsync(string id, int seconds)
    {
        bool isNewBest = !_results.TryGetValue(id, out var existing) || seconds < existing.BestSeconds;
        if (isNewBest)
        {
            _results[id] = new PuzzleResult { BestSeconds = seconds, SolvedUtc = DateTime.UtcNow };
            await PersistAsync();
        }
        return isNewBest;
    }

    private async Task PersistAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_results);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch
        {
            // Ignore persistence failures; in-memory results still work for the session.
        }
    }

    /// <summary>Count of solved puzzles among a given set of ids.</summary>
    public int SolvedCount(IEnumerable<string> ids) => ids.Count(_results.ContainsKey);
}
