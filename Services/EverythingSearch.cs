using System.Runtime.InteropServices;
using System.Text;

namespace CDir.Services;

/// <summary>P/Invoke surface of the Everything SDK (Everything64.dll).</summary>
internal static class EverythingSdk
{
    public const string Dll = "Everything64.dll";

    public const uint RequestFileName = 0x00000001;
    public const uint RequestPath = 0x00000002;
    public const uint RequestFullPathAndName = 0x00000004;
    public const uint RequestSize = 0x00000010;
    public const uint RequestDateModified = 0x00000040;

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern void Everything_SetSearchW(string lpSearchString);

    [DllImport(Dll)]
    public static extern bool Everything_QueryW(bool bWait);

    [DllImport(Dll)]
    public static extern uint Everything_GetNumResults();

    [DllImport(Dll, CharSet = CharSet.Unicode)]
    public static extern void Everything_GetResultFullPathNameW(uint nIndex, StringBuilder lpString, uint nMaxCount);

    [DllImport(Dll)]
    public static extern bool Everything_GetResultSize(uint nIndex, out long lpFileSize);

    [DllImport(Dll)]
    public static extern bool Everything_GetResultDateModified(uint nIndex, out long lpDateModified);

    [DllImport(Dll)]
    public static extern bool Everything_IsFolderResult(uint nIndex);

    [DllImport(Dll)]
    public static extern void Everything_SetMatchPath(bool bEnable);

    [DllImport(Dll)]
    public static extern void Everything_SetMatchCase(bool bEnable);

    [DllImport(Dll)]
    public static extern void Everything_SetRegex(bool bEnable);

    [DllImport(Dll)]
    public static extern void Everything_SetMax(uint dwMax);

    [DllImport(Dll)]
    public static extern void Everything_SetRequestFlags(uint dwRequestFlags);

    [DllImport(Dll)]
    public static extern uint Everything_GetMajorVersion();

    [DllImport(Dll)]
    public static extern uint Everything_GetMinorVersion();

    [DllImport(Dll)]
    public static extern bool Everything_IsDBLoaded();

    [DllImport(Dll)]
    public static extern uint Everything_GetLastError();
}

public sealed class EverythingUnavailableException(string message) : Exception(message);

/// <summary>Thin, thread-safe wrapper around the Everything search SDK.</summary>
public sealed class EverythingSearchService
{
    private readonly object _gate = new();
    private bool _probed;
    private bool _available;

    private const uint EverythingErrorIpc = 2;
    private const uint EverythingErrorInvalidParameter = 9;

    public bool IsAvailable => _available;
    public string? UnavailableReason { get; internal set; }

    /// <summary>Forces the next Probe() to retry loading/using the SDK.</summary>
    public void ResetProbe()
    {
        lock (_gate)
        {
            _probed = false;
            _available = false;
        }
    }

    public bool Probe()
    {
        lock (_gate)
        {
            if (_probed) return _available;
            _probed = true;

            try
            {
                EverythingSdk.Everything_GetMajorVersion();

                if (!EverythingSdk.Everything_IsDBLoaded())
                {
                    _available = false;
                    UnavailableReason = "Everything이 실행 중이 아닙니다.";
                    return false;
                }

                _available = true;
                UnavailableReason = null;
            }
            catch (DllNotFoundException)
            {
                _available = false;
                UnavailableReason = "Everything64.dll을 찾을 수 없습니다.";
            }
            catch (EntryPointNotFoundException)
            {
                _available = false;
                UnavailableReason = "Everything SDK 버전이 호환되지 않습니다.";
            }
            catch (BadImageFormatException)
            {
                _available = false;
                UnavailableReason = "Everything SDK 아키텍처가 맞지 않습니다.";
            }

            return _available;
        }
    }

    public List<Models.FileItem> Search(string query, int limit)
    {
        lock (_gate)
        {
            if (!Probe()) throw new EverythingUnavailableException(UnavailableReason ?? "Everything을 사용할 수 없습니다.");

            var regexMode = SearchQuery.IsRegexQuery(query);
            var searchText = regexMode ? SearchQuery.GetRegexPattern(query) : query;

            EverythingSdk.Everything_SetMatchPath(!regexMode);
            EverythingSdk.Everything_SetMatchCase(false);
            EverythingSdk.Everything_SetRegex(regexMode);
            EverythingSdk.Everything_SetSearchW(searchText);
            EverythingSdk.Everything_SetMax((uint)Math.Max(1, limit));
            EverythingSdk.Everything_SetRequestFlags(
                EverythingSdk.RequestFileName |
                EverythingSdk.RequestPath |
                EverythingSdk.RequestFullPathAndName |
                EverythingSdk.RequestSize |
                EverythingSdk.RequestDateModified);

            if (!EverythingSdk.Everything_QueryW(true))
            {
                var error = EverythingSdk.Everything_GetLastError();
                if (!EverythingSdk.Everything_IsDBLoaded() || error == EverythingErrorIpc)
                    throw new EverythingUnavailableException("Everything이 실행 중이 아닙니다.");

                if (regexMode || error == EverythingErrorInvalidParameter)
                    throw new SearchQueryException("Everything이 검색어를 처리하지 못했습니다. 정규식 패턴을 확인해 주세요.");

                throw new EverythingUnavailableException("Everything 검색에 실패했습니다.");
            }

            var count = EverythingSdk.Everything_GetNumResults();
            var results = new List<Models.FileItem>((int)count);
            var buffer = new StringBuilder(1024);

            for (uint i = 0; i < count; i++)
            {
                buffer.Clear();
                EverythingSdk.Everything_GetResultFullPathNameW(i, buffer, (uint)buffer.Capacity);
                var fullPath = buffer.ToString();
                if (string.IsNullOrWhiteSpace(fullPath)) continue;

                var isDirectory = EverythingSdk.Everything_IsFolderResult(i);
                var size = EverythingSdk.Everything_GetResultSize(i, out var fileSize) ? fileSize : 0;
                var modified = default(DateTime);
                if (EverythingSdk.Everything_GetResultDateModified(i, out var fileTime) && fileTime > 0)
                {
                    try { modified = DateTime.FromFileTimeUtc(fileTime).ToLocalTime(); }
                    catch { modified = default; }
                }

                var name = System.IO.Path.GetFileName(fullPath.TrimEnd('\\'));
                if (string.IsNullOrEmpty(name)) name = fullPath;

                results.Add(new Models.FileItem
                {
                    Name = name,
                    FullPath = fullPath,
                    IsDirectory = isDirectory,
                    Size = isDirectory ? 0 : size,
                    Modified = modified,
                    Extension = isDirectory ? "" : System.IO.Path.GetExtension(fullPath),
                });
            }

            return results;
        }
    }
}
