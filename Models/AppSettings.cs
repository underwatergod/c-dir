namespace CDir.Models;

public sealed class AppSettings
{
    public int PaneCount { get; set; } = 2;
    public List<string> PanePaths { get; set; } = [];
    public List<List<string>> PaneTabs { get; set; } = [];
    public List<int> PaneActiveTab { get; set; } = [];
    public bool DarkTheme { get; set; }
    public bool ShowHidden { get; set; }
    public List<string> Favorites { get; set; } = [];
    public List<string> SearchRoots { get; set; } = [];
    public double WindowWidth { get; set; } = 1300;
    public double WindowHeight { get; set; } = 820;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool Maximized { get; set; }
}
