using Avalonia;
using Avalonia.Controls;

namespace ChivRcon.App.Views;

/// <summary>
/// Hand a URL to the OS. Avalonia's Launcher rather than Process.Start, because this UI is
/// shared with the Android head, where UseShellExecute does not exist.
/// </summary>
internal static class OpenLink
{
    public static async Task<bool> InBrowserAsync(Visual from, string url)
    {
        var top = TopLevel.GetTopLevel(from);
        if (top is null) return false;

        try { return await top.Launcher.LaunchUriAsync(new Uri(url)); }
        catch { return false; }
    }
}
