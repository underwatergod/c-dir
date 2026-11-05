using System.IO;
using System.Text.Json;
using CDir.Models;

namespace CDir.Services;

public static class SettingsStore
{
    public static string DataDirectory { get; } = EnsureDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CDir"));

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Settings are best-effort.
        }
    }

    public static List<string> DefaultRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch
        {
            return [Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\"];
        }
    }

    private static string EnsureDirectory(string path)
    {
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }
}
