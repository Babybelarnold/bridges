using System.Collections.Concurrent;
using System.Diagnostics;
using Bridges.Core;

// Bridges puzzle-pack generator.
//
// Generates uniquely-solvable puzzles in parallel across CPU cores and writes them to a single
// JSON pack the Razor app can load. This is where the heavy lifting lives: it can take as long as
// it needs per puzzle because there is no UI thread to keep responsive.
//
// Usage:
//   dotnet run -- [--count N] [--out path] [--threads N]
//     --count   puzzles per shape/size (default 40)
//     --out     output pack file (default ./pack.json)
//     --threads max degree of parallelism (default: processor count)

var options = ParseArgs(args);

// Shapes to generate. Squares suit desktop; portraits (taller than wide) suit phone screens.
// Each entry is (rows, cols).
var squareSizes = new[] { (5, 5), (7, 7), (9, 9), (11, 11), (13, 13) };
var portraitSizes = new[] { (8, 5), (10, 6), (12, 7), (14, 8) }; // rows > cols

var shapes = squareSizes.Concat(portraitSizes).ToList();

// Build the full work list: `count` puzzles for each shape.
var work = new List<(int rows, int cols)>();
foreach (var shape in shapes)
{
    for (int i = 0; i < options.Count; i++)
    {
        work.Add(shape);
    }
}

Console.WriteLine($"Generating {work.Count} puzzles " +
                  $"({shapes.Count} shapes x {options.Count}) on up to {options.Threads} threads...");

var results = new ConcurrentBag<PuzzleDef>();
int done = 0;
var sw = Stopwatch.StartNew();

// Each work item gets its own generator instance because System.Random is not thread-safe.
// Seeds are derived from a thread-safe counter so parallel workers don't produce identical output.
int seedCounter = 0;

Parallel.ForEach(
    work,
    new ParallelOptions { MaxDegreeOfParallelism = options.Threads },
    () => new Random(Interlocked.Increment(ref seedCounter) * 7919 + Environment.CurrentManagedThreadId),
    (item, _, localRandom) =>
    {
        // A single Generate is fast now, so retry until the compacted board is exactly the target
        // shape. Compaction shrinks boards unpredictably, so we discard mismatches to keep clean,
        // consistent sizes in the pack. Bounded so a stubborn target can't loop forever.
        Board? board = null;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            int seed = localRandom.Next();
            var generator = new PuzzleGenerator(seed);
            var candidate = generator.Generate(item.rows, item.cols);
            if (candidate.Rows == item.rows && candidate.Cols == item.cols)
            {
                board = candidate;
                break;
            }
        }

        if (board != null)
        {
            results.Add(PuzzleDef.FromBoard(board));
        }
        else
        {
            Console.WriteLine($"  (skipped {item.rows}x{item.cols}: no exact fit in budget)");
        }

        int completed = Interlocked.Increment(ref done);
        if (completed % 10 == 0 || completed == work.Count)
        {
            Console.WriteLine($"  {completed}/{work.Count}  ({sw.Elapsed.TotalSeconds:F1}s)");
        }

        return localRandom;
    },
    _ => { });

sw.Stop();

var pack = new PuzzlePack
{
    GeneratedUtc = DateTime.UtcNow,
    Puzzles = results.OrderBy(p => p.Rows).ThenBy(p => p.Cols).ToList()
};

var json = pack.ToJson();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Out))!);
File.WriteAllText(options.Out, json);

Console.WriteLine();
Console.WriteLine($"Done: {pack.Puzzles.Count} puzzles in {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"Wrote {options.Out} ({json.Length / 1024.0:F1} KB)");

// Quick breakdown by shape/size.
foreach (var group in pack.Puzzles
             .GroupBy(p => (p.Rows, p.Cols, p.Shape))
             .OrderBy(g => g.Key.Shape).ThenBy(g => g.Key.Rows))
{
    Console.WriteLine($"  {group.Key.Shape,-8} {group.Key.Rows}x{group.Key.Cols}: {group.Count()}");
}

return 0;

static Options ParseArgs(string[] args)
{
    var o = new Options();
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--count" when i + 1 < args.Length && int.TryParse(args[i + 1], out int c):
                o.Count = Math.Max(1, c);
                i++;
                break;
            case "--out" when i + 1 < args.Length:
                o.Out = args[i + 1];
                i++;
                break;
            case "--threads" when i + 1 < args.Length && int.TryParse(args[i + 1], out int t):
                o.Threads = Math.Max(1, t);
                i++;
                break;
        }
    }
    return o;
}

sealed class Options
{
    public int Count { get; set; } = 40;
    public string Out { get; set; } = "pack.json";
    public int Threads { get; set; } = Environment.ProcessorCount;
}
