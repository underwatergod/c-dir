using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CDir.Models;

namespace CDir.Services;

/// <summary>Small shell icons, cached per extension / folder.</summary>
public static class ShellIcons
{
    private const uint SHGFI_ICON = 0x00000100;
    private const uint SHGFI_SMALLICON = 0x00000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x00000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static ImageSource? GetIcon(FileItem item)
    {
        var key = item.IsDirectory
            ? "::dir"
            : string.IsNullOrEmpty(item.Extension) ? "::file" : item.Extension.ToLowerInvariant();
        return GetOrCreate(key, item.IsDirectory, item.Extension);
    }

    private static ImageSource? GetOrCreate(string key, bool isDirectory, string extension)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        ImageSource? image = null;
        try
        {
            var info = new SHFILEINFO();
            var path = isDirectory ? "folder" : "file" + extension;
            var attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                image = source;
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            image = null;
        }

        lock (Gate)
        {
            Cache[key] = image;
        }

        return image;
    }
}
