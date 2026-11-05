using CDir.Models;

namespace CDir.Services;

public sealed record SearchOutcome(IReadOnlyList<FileItem> Items, string Provider, string? Note);

/// <summary>Search facade: prefers Everything, transparently falls back to the built-in index.</summary>
public sealed class SearchService
{
    private bool _everythingBroken;

    public SearchService(NativeFileIndex index, EverythingSearchService everything)
    {
        Index = index;
        Everything = everything;
        Everything.Probe();
    }

    public NativeFileIndex Index { get; }
    public EverythingSearchService Everything { get; }

    public bool EverythingAvailable => Everything.IsAvailable && !_everythingBroken;
    public string ProviderName => EverythingAvailable ? "Everything" : "내장 인덱스";

    /// <summary>Re-detects Everything (e.g. after the user installs or starts it).</summary>
    public bool RefreshProvider()
    {
        _everythingBroken = false;
        Everything.ResetProbe();
        Everything.Probe();
        return EverythingAvailable;
    }

    public async Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return new SearchOutcome([], ProviderName, null);

        if (EverythingAvailable)
        {
            try
            {
                var items = await Task.Run(() => Everything.Search(query, limit), cancellationToken);
                return new SearchOutcome(items, "Everything", null);
            }
            catch (Exception ex) when (ex is EverythingUnavailableException or DllNotFoundException or EntryPointNotFoundException)
            {
                // Everything died mid-session: switch to the built-in index for the rest of the session.
                _everythingBroken = true;
            }
        }

        if (!Index.HasData)
        {
            return new SearchOutcome([], "내장 인덱스", "인덱스가 아직 없습니다. 툴바의 ⟳ 버튼으로 인덱스를 만들어 주세요.");
        }

        var results = await Index.SearchAsync(query, limit, cancellationToken);
        return new SearchOutcome(results, "내장 인덱스", $"{Index.Count:N0}개 항목");
    }
}
