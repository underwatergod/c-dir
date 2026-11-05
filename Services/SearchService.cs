using CDir.Models;

namespace CDir.Services;

public sealed record SearchOutcome(IReadOnlyList<FileItem> Items, string Provider, string? Note, string? Error = null);

/// <summary>Search facade: prefers Everything, transparently falls back to the built-in index.</summary>
public sealed class SearchService
{
    private static readonly TimeSpan EverythingRegexTimeout = TimeSpan.FromSeconds(5);

    private volatile bool _everythingBroken;
    private volatile bool _everythingTimedOut;

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
        if (_everythingTimedOut)
        {
            Everything.UnavailableReason = "이전 정규식 검색이 아직 실행 중입니다. 앱을 다시 시작해 주세요.";
            return false;
        }

        _everythingBroken = false;
        Everything.ResetProbe();
        Everything.Probe();
        return EverythingAvailable;
    }

    public async Task<SearchOutcome> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return new SearchOutcome([], ProviderName, null);

        var regexMode = SearchQuery.IsRegexQuery(query);
        if (!regexMode && query.Contains(SearchQuery.RegexPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return new SearchOutcome([], ProviderName, null, "regex:는 검색어 맨 앞에서만 사용할 수 있습니다.");
        }

        if (regexMode)
        {
            try
            {
                SearchQuery.CompileRegex(SearchQuery.GetRegexPattern(query));
            }
            catch (SearchQueryException ex)
            {
                return new SearchOutcome([], ProviderName, null, ex.Message);
            }
        }

        if (EverythingAvailable)
        {
            Task<List<FileItem>>? task = null;
            try
            {
                task = Task.Run(() => Everything.Search(query, limit), cancellationToken);
                var items = regexMode
                    ? await task.WaitAsync(EverythingRegexTimeout, cancellationToken)
                    : await task;
                return new SearchOutcome(items, "Everything", null);
            }
            catch (TimeoutException)
            {
                _everythingTimedOut = true;
                _everythingBroken = true;
                if (task is not null)
                    _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                Everything.UnavailableReason = "정규식 검색이 시간 내에 끝나지 않았습니다. 앱을 다시 시작해 주세요.";
                return new SearchOutcome([], "Everything", null,
                    "정규식 검색이 너무 오래 걸려 중단했습니다. 더 단순한 패턴을 사용해 주세요.");
            }
            catch (SearchQueryException ex)
            {
                return new SearchOutcome([], "Everything", null, ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is EverythingUnavailableException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                // Everything died mid-session: switch to the built-in index for the rest of the session.
                _everythingBroken = true;
            }
        }

        if (!Index.HasData)
        {
            return new SearchOutcome([], "내장 인덱스", "인덱스가 아직 없습니다. 툴바의 ⟳ 버튼으로 인덱스를 만들어 주세요.");
        }

        try
        {
            var results = await Index.SearchAsync(query, limit, cancellationToken);
            return new SearchOutcome(results, "내장 인덱스", $"{Index.Count:N0}개 항목");
        }
        catch (SearchQueryException ex)
        {
            return new SearchOutcome([], "내장 인덱스", null, ex.Message);
        }
    }
}
