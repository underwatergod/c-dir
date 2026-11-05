using System.Text.RegularExpressions;

namespace CDir.Services;

/// <summary>Raised when a search expression is invalid or takes too long to evaluate.</summary>
public sealed class SearchQueryException(string message) : Exception(message);

/// <summary>Shared parsing rules for the C-Dir search box.</summary>
internal static class SearchQuery
{
    public const string RegexPrefix = "regex:";
    public const int MaxRegexLength = 512;

    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan TotalRegexTimeout = TimeSpan.FromSeconds(5);

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>True when the whole query should be interpreted as a regular expression.</summary>
    public static bool IsRegexQuery(string query) =>
        query.StartsWith(RegexPrefix, StringComparison.OrdinalIgnoreCase);

    public static string GetRegexPattern(string query)
    {
        var pattern = query[RegexPrefix.Length..].Trim();

        // In regex mode the whole remainder is the pattern; allow one optional pair of
        // surrounding quotes so users can paste a pattern without worrying about spaces.
        if (pattern.Length >= 2 && pattern[0] == '"' && pattern[^1] == '"')
            pattern = pattern[1..^1];

        return pattern;
    }

    public static Regex CompileRegex(string pattern)
    {
        if (pattern.Length == 0)
            throw new SearchQueryException("정규식 패턴을 입력해 주세요. 예: regex:^IMG_\\d{4}");

        if (pattern.Length > MaxRegexLength)
            throw new SearchQueryException($"정규식이 너무 깁니다. {MaxRegexLength}자 이하로 입력해 주세요.");

        try
        {
            return new Regex(pattern, Options, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new SearchQueryException($"정규식이 올바르지 않습니다. {ex.Message}");
        }
    }

    public static bool IsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new SearchQueryException("정규식 실행 시간이 너무 길어 중단했습니다. 더 단순한 패턴을 사용해 주세요.");
        }
    }
}
