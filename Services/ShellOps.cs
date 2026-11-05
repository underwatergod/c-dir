using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CDir.Services;

/// <summary>Windows shell operations (recycle bin delete, copy/move with progress, properties dialog).</summary>
public static class ShellOps
{
    public static event Action? FileSystemChanged;

    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOWNORMAL = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string lpParameters;
        public string lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError($"열 수 없습니다.\n{ex.Message}"); }
    }

    public static void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    public static void OpenTerminal(string directory)
    {
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                WorkingDirectory = Directory.Exists(directory) ? directory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            ShowError($"터미널을 열 수 없습니다.\n{ex.Message}");
        }
    }

    public static void ShowProperties(string path)
    {
        try
        {
            var info = new SHELLEXECUTEINFO
            {
                cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                fMask = SEE_MASK_INVOKEIDLIST,
                lpVerb = "properties",
                lpFile = path,
                nShow = SW_SHOWNORMAL,
            };
            ShellExecuteEx(ref info);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    public static bool DeleteToRecycleBin(IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return false;
        var ok = RunShellOperation(FO_DELETE, paths, null, FOF_ALLOWUNDO | FOF_WANTNUKEWARNING);
        if (ok) FileSystemChanged?.Invoke();
        return ok;
    }

    public static bool Copy(IReadOnlyCollection<string> paths, string destinationDirectory)
    {
        if (paths.Count == 0) return false;
        var ok = RunShellOperation(FO_COPY, paths, destinationDirectory, FOF_NOCONFIRMMKDIR);
        if (ok) FileSystemChanged?.Invoke();
        return ok;
    }

    public static bool Move(IReadOnlyCollection<string> paths, string destinationDirectory)
    {
        if (paths.Count == 0) return false;
        var ok = RunShellOperation(FO_MOVE, paths, destinationDirectory, FOF_NOCONFIRMMKDIR);
        if (ok) FileSystemChanged?.Invoke();
        return ok;
    }

    /// <summary>Returns a non-existing path inside <paramref name="directory"/> based on <paramref name="baseName"/>.</summary>
    public static string UniquePath(string directory, string baseName)
    {
        var candidate = Path.Combine(directory, baseName);
        var index = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(directory, $"{baseName} ({index++})");
        return candidate;
    }

    public static void NotifyChanged() => FileSystemChanged?.Invoke();

    private static bool RunShellOperation(uint function, IReadOnlyCollection<string> sources, string? destination, ushort flags)
    {
        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = function,
                pFrom = string.Join('\0', sources) + "\0\0",
                pTo = destination is null ? null! : destination + "\0\0",
                fFlags = flags,
            };

            var result = SHFileOperation(ref operation);
            return result == 0 && operation.fAnyOperationsAborted == 0;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            return false;
        }
    }

    private static void ShowError(string message) =>
        System.Windows.MessageBox.Show(message, "C-Dir", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
}
