using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CDir.Models;

namespace CDir.Services;

/// <summary>
/// Built-in "Everything style" file index used when the Everything SDK is not available.
/// Paths are held in memory and persisted to %LOCALAPPDATA%\CDir\fileindex.bin.
/// </summary>
public sealed class NativeFileIndex
{
    private const string Magic = "CDIRIDX2";
    private string[] _paths = [];

    public int Count => _paths.Length;
    public bool HasData => _paths.Length > 0;
    public DateTime LastUpdated { get; private set; }

    public event Action<long>? Progress;
    public event Action<int>? Completed;
    public event Action<string>? Failed;

    private static string CachePath => Path.Combine(SettingsStore.DataDirectory, "fileindex.bin");

    public async Task<bool> LoadCacheAsync()
    {
        if (!File.Exists(CachePath)) return false;
        try
        {
            var (paths, stamp) = await Task.Run(ReadCache);
            if (paths.Length == 0) return false;
            _paths = paths;
            LastUpdated = stamp;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> BuildAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        var bag = new ConcurrentBag<string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Task.Run(() =>
            {
                var options = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Max(2, Math.Min(16, Environment.ProcessorCount)),
                };

                Parallel.ForEach(roots.Distinct(StringComparer.OrdinalIgnoreCase), options, root =>
                {
                    if (Directory.Exists(root))
                        Walk(root, bag, cancellationToken);
                });
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
            return false;
        }

        var paths = bag.ToArray();
        _paths = paths;
        LastUpdated = DateTime.Now;
        Completed?.Invoke(paths.Length);

        _ = Task.Run(() =>
        {
            try { SaveCache(paths, LastUpdated); } catch { /* cache is best-effort */ }
        });

        stopwatch.Stop();
        return true;
    }

    public async Task<List<FileItem>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var terms = ParseQuery(query);
        if (terms.Count == 0) return [];

        var paths = _paths;
        if (paths.Length == 0) return [];

        var matches = await Task.Run(() => Match(paths, terms, Math.Max(limit * 4, 200), cancellationToken), cancellationToken);
        if (matches.Count == 0) return [];

        var selected = matches
            .OrderBy(m => m.NameMatch ? 0 : 1)
            .ThenBy(m => m.Path.Length)
            .Take(limit)
            .Select(m => m.Path)
            .ToList();

        return await Task.Run(() => BuildItems(selected, cancellationToken), cancellationToken);
    }

    private void Walk(string start, ConcurrentBag<string> bag, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var stack = new Stack<string>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested) return;
            var directory = stack.Pop();

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory, "*", options))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    bag.Add(child);
                    stack.Push(child);
                    Report(bag);
                }

                foreach (var file in Directory.EnumerateFiles(directory, "*", options))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    bag.Add(file);
                    Report(bag);
                }
            }
            catch
            {
                // inaccessible folder: skip
            }
        }
    }

    private long _scanCount;
    private long _lastReport;

    private void Report(ConcurrentBag<string> bag)
    {
        var count = Interlocked.Increment(ref _scanCount);
        if (count - Interlocked.Read(ref _lastReport) < 2000) return;

        Interlocked.Exchange(ref _lastReport, count);
        Progress?.Invoke(count);

        // Publish partial results so search works while the scan is still running.
        if (count % 50_000 < 2_000)
        {
            try { _paths = bag.ToArray(); } catch { /* scan may race with ToArray on some runtimes */ }
        }
    }

    private static List<(string Path, bool NameMatch)> Match(string[] paths, List<Term> terms, int maxMatches, CancellationToken cancellationToken)
    {
        var results = new List<(string, bool)>(Math.Min(maxMatches, 512));
        var array = terms.ToArray();
        var counter = 0;
        var regexSearch = array.Any(term => term.Kind == TermKind.Regex);
        var started = regexSearch ? Stopwatch.StartNew() : null;
        var checkMask = regexSearch ? 0x07 : 0xFFF;

        foreach (var path in paths)
        {
            if ((++counter & checkMask) == 0)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (started is not null && started.Elapsed > SearchQuery.TotalRegexTimeout)
                    throw new SearchQueryException("정규식 검색이 너무 오래 걸려 중단했습니다. 검색 범위를 좁히거나 패턴을 단순하게 만들어 주세요.");
            }

            int separator = path.LastIndexOf('\\');
            var name = separator >= 0 ? path[(separator + 1)..] : path;

            var (ok, nameMatch) = Matches(path, name, array);
            if (!ok) continue;

            results.Add((path, nameMatch));
            if (results.Count >= maxMatches) break;
        }

        return results;
    }

    private static (bool Ok, bool NameMatch) Matches(string path, string name, Term[] terms)
    {
        var allInName = true;

        foreach (var term in terms)
        {
            switch (term.Kind)
            {
                case TermKind.Extension:
                    var extension = Path.GetExtension(name).TrimStart('.');
                    if (!term.Extensions!.Contains(extension, StringComparer.OrdinalIgnoreCase))
                        return (false, false);
                    break;

                case TermKind.Wildcard:
                    var inName = term.Regex!.IsMatch(name);
                    if (!inName)
                    {
                        if (!term.Regex.IsMatch(path)) return (false, false);
                        allInName = false;
                    }
                    break;

                case TermKind.Regex:
                    if (!SearchQuery.IsMatch(term.Regex!, name)) return (false, false);
                    break;

                default:
                    if (!name.Contains(term.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!path.Contains(term.Text, StringComparison.OrdinalIgnoreCase)) return (false, false);
                        allInName = false;
                    }
                    break;
            }
        }

        return (true, allInName);
    }

    private static List<Term> ParseQuery(string query)
    {
        if (SearchQuery.IsRegexQuery(query))
        {
            var regex = SearchQuery.CompileRegex(SearchQuery.GetRegexPattern(query));
            return [Term.RegexTerm(regex)];
        }

        var terms = new List<Term>();

        foreach (var raw in Tokenize(query))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            if (token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var extensions = token[4..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim().TrimStart('.'))
                    .Where(x => x.Length > 0)
                    .ToArray();
                if (extensions.Length > 0) terms.Add(Term.Extension(extensions));
                continue;
            }

            if (token.Contains('*') || token.Contains('?'))
            {
                var pattern = "^" + Regex.Escape(token).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                terms.Add(Term.Wildcard(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)));
                continue;
            }

            terms.Add(Term.Plain(token));
        }

        return terms;
    }

    private static IEnumerable<string> Tokenize(string query)
    {
        var current = new StringBuilder();
        var quoted = false;

        foreach (var c in query)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) yield return current.ToString();
    }

    private static List<FileItem> BuildItems(List<string> paths, CancellationToken cancellationToken)
    {
        var items = new FileItem?[paths.Count];

        Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            items[i] = FileItem.FromPath(paths[i]);
        });

        return items.Where(i => i is not null).Cast<FileItem>().ToList();
    }

    private static void SaveCache(string[] paths, DateTime stamp)
    {
        var temporary = CachePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(stamp.Ticks);
            writer.Write(paths.Length);
            foreach (var path in paths) writer.Write(path);
        }

        File.Move(temporary, CachePath, true);
    }

    private static (string[] Paths, DateTime Stamp) ReadCache()
    {
        using var stream = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (reader.ReadString() != Magic) return ([], default);

        var stamp = new DateTime(reader.ReadInt64());
        var count = reader.ReadInt32();
        if (count <= 0 || count > 50_000_000) return ([], default);

        var paths = new string[count];
        for (var i = 0; i < count; i++) paths[i] = reader.ReadString();

        return (paths, stamp);
    }

    private enum TermKind { Plain, Wildcard, Extension, Regex }

    private readonly struct Term
    {
        public TermKind Kind { get; private init; }
        public string Text { get; private init; }
        public Regex? Regex { get; private init; }
        public string[]? Extensions { get; private init; }

        public static Term Plain(string text) => new() { Kind = TermKind.Plain, Text = text };
        public static Term Wildcard(Regex regex) => new() { Kind = TermKind.Wildcard, Regex = regex, Text = regex.ToString() };
        public static Term Extension(string[] extensions) => new() { Kind = TermKind.Extension, Extensions = extensions, Text = "ext" };
        public static Term RegexTerm(Regex regex) => new() { Kind = TermKind.Regex, Regex = regex, Text = regex.ToString() };
    }
}
