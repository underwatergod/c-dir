using System.Globalization;
using System.IO;
using System.Windows.Media;
using CDir.Services;

namespace CDir.Models;

/// <summary>A single row: either a file-system entry shown in a pane or a search hit.</summary>
public sealed class FileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Extension { get; init; } = "";

    public string SizeText => IsDirectory ? "" : FormatSize(Size);
    public string ModifiedText => Modified == default ? "" : Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string TypeText => IsDirectory
        ? "폴더"
        : string.IsNullOrEmpty(Extension) ? "파일" : $"{Extension.TrimStart('.').ToUpperInvariant()} 파일";

    public string DirectoryPath => IsDirectory ? FullPath : Path.GetDirectoryName(FullPath) ?? "";
    public string FolderText => DirectoryPath;
    public ImageSource? Icon => ShellIcons.GetIcon(this);

    /// <summary>Builds a row from the current file system state, or null when the path cannot be read.</summary>
    public static FileItem? FromPath(string fullPath)
    {
        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var info = new DirectoryInfo(fullPath);
                return new FileItem
                {
                    Name = info.Name,
                    FullPath = fullPath,
                    IsDirectory = true,
                    Modified = info.LastWriteTime,
                };
            }

            var file = new FileInfo(fullPath);
            return new FileItem
            {
                Name = file.Name,
                FullPath = fullPath,
                IsDirectory = false,
                Size = file.Length,
                Modified = file.LastWriteTime,
                Extension = file.Extension,
            };
        }
        catch
        {
            return null;
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB", "PB"];
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);

        return string.Format(CultureInfo.CurrentCulture, value >= 100 ? "{0:N0} {1}" : "{0:N1} {1}", value, units[unit]);
    }
}
