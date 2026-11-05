using System.IO;

namespace CDir.Services;

/// <summary>Folder favorites shared by every pane. Persisted with the app settings on shutdown.</summary>
public static class FavoritesStore
{
    private static readonly List<string> Paths = [];

    public static IReadOnlyList<string> Items => Paths;

    public static event Action? Changed;

    public static void Initialize(IEnumerable<string>? paths)
    {
        Paths.Clear();

        if (paths is not null)
        {
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                if (Contains(path)) continue;
                Paths.Add(path);
            }
        }

        Changed?.Invoke();
    }

    public static bool Contains(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

    public static bool Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || Contains(path)) return false;

        Paths.Add(path);
        Changed?.Invoke();
        return true;
    }

    public static bool Remove(string path)
    {
        var index = Paths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;

        Paths.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Adds the folder when missing, removes it when present. Returns the new favorite state.</summary>
    public static bool Toggle(string path)
    {
        if (Contains(path))
        {
            Remove(path);
            return false;
        }

        Add(path);
        return Contains(path);
    }

    public static string DisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
