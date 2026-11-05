using System.IO;

namespace CDir.Services;

public static class PathUtil
{
    public static bool IsValidFileName(string name) =>
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not "." and not "..";

    /// <summary>True when the path is a drive or UNC share root.</summary>
    public static bool IsRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var trimmedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var trimmedRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? "");
        return trimmedPath.Equals(trimmedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="path"/> is the same as, or below, <paramref name="root"/>.</summary>
    public static bool IsSameOrChild(string path, string root)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

        if (normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        return normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
