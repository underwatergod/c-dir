using System.Runtime.InteropServices;

namespace CDir.Services;

/// <summary>Explorer-style logical sort (file10 after file9).</summary>
public static class NaturalComparer
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static int Compare(string a, string b)
    {
        try { return StrCmpLogicalW(a, b); }
        catch { return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase); }
    }
}
