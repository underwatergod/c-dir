namespace CDir.Services;

public enum ClipboardMode { None, Copy, Cut }

/// <summary>Internal copy/cut buffer with a fallback to the Windows shell clipboard.</summary>
public static class FileClipboard
{
    private static readonly object Gate = new();
    private static List<string> _paths = [];
    private static ClipboardMode _mode = ClipboardMode.None;

    public static ClipboardMode Mode
    {
        get { lock (Gate) return _mode; }
    }

    public static void Set(IEnumerable<string> paths, ClipboardMode mode)
    {
        lock (Gate)
        {
            _paths = paths
                .Where(p => System.IO.File.Exists(p) || System.IO.Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _mode = _paths.Count == 0 ? ClipboardMode.None : mode;
        }
    }

    public static IReadOnlyList<string> GetPastePaths()
    {
        lock (Gate)
        {
            if (_paths.Count > 0) return _paths.ToArray();
        }

        try
        {
            var drop = System.Windows.Clipboard.GetFileDropList();
            if (drop is not null)
                return drop.Cast<string>().Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        }
        catch
        {
            // Clipboard may be locked by another process.
        }

        return [];
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _paths = [];
            _mode = ClipboardMode.None;
        }
    }
}
