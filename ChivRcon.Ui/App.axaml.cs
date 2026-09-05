using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ChivRcon.App.Views;

namespace ChivRcon.App;

public partial class App : Application
{
    /// <summary>
    /// Supplied by the desktop head. The shared library cannot reference MainWindow, which
    /// lives in the desktop project along with the Win32 chrome, so the head hands one in.
    /// </summary>
    public static Func<Window>? MainWindowFactory { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && MainWindowFactory is not null)
        {
            desktop.MainWindow = MainWindowFactory();
        }

        // Android has no windows: the shell is the root view, with the nav behind a hamburger.
        // A throw in here would otherwise be an invisible launch crash on a sideloaded build,
        // so the failure is rendered instead.
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            try { single.MainView = new AppShell(compact: true); }
            catch (Exception ex) { single.MainView = StartupFailureView(ex); }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Control StartupFailureView(Exception ex) => new ScrollViewer
    {
        Padding = new Avalonia.Thickness(16),
        Content = new SelectableTextBlock
        {
            Text = "ChivRcon failed to start.\n\n" + ex,
            FontSize = 12,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        },
    };
}
