using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CDir.Services;

namespace CDir;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Everything SDK is optional: resolve Everything64.dll from the app folder or an Everything install.
        NativeLibrary.SetDllImportResolver(typeof(App).Assembly, ResolveNativeLibrary);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "C-Dir 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = SettingsStore.Load();
        ThemeManager.Apply(settings.DarkTheme);

        var window = new MainWindow(settings);
        MainWindow = window;
        window.Show();
        ThemeManager.ApplyWindowChrome(window, settings.DarkTheme);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("Everything64.dll", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        string[] candidates =
        [
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Everything"),
        ];

        foreach (var dir in candidates)
        {
            var candidate = Path.Combine(dir, libraryName);
            if (!File.Exists(candidate)) continue;

            try { return NativeLibrary.Load(candidate); }
            catch { return IntPtr.Zero; }
        }

        return IntPtr.Zero;
    }
}
