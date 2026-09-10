using Microsoft.JSInterop;

namespace Bridges.Services;

/// <summary>
/// Tracks which puzzles the player has solved, keyed by the puzzle's unique id. Persists the set
/// to browser localStorage (the closest thing a WASM app has to a local file), so completion
/// survives page reloads on the device.
/// </summary>
public sealed class ProgressStore
{
    private const string StorageKey = "bridges.solvedIds";

    private readonly IJSRuntime _js;
    private readonly HashSet<string> _solved = new();
    private bool _loaded;

    public ProgressStore(IJSRuntime js) => _js = js;

    public IReadOnlyCollection<string> SolvedIds => _solved;

    public bool IsSolved(string id) => _solved.Contains(id);

    /// <summary>Load the persisted solved-id set from localStorage. Call once at startup.</summary>
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
                foreach (var id in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    _solved.Add(id);
                }
            }
        }
        catch
        {
            // localStorage may be unavailable (e.g. prerender); start empty.
        }

        _loaded = true;
    }

    /// <summary>Mark a puzzle solved and persist. Returns true if it was newly added.</summary>
    public async Task<bool> MarkSolvedAsync(string id)
    {
        if (!_solved.Add(id))
        {
            return false;
        }
        await PersistAsync();
        return true;
    }

    private async Task PersistAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, string.Join(',', _solved));
        }
        catch
        {
            // Ignore persistence failures; in-memory set still works for the session.
        }
    }

    /// <summary>Count of solved puzzles among a given set of ids.</summary>
    public int SolvedCount(IEnumerable<string> ids) => ids.Count(_solved.Contains);
}
