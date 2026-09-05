using Avalonia;
using ChivRcon.App.Views;

namespace ChivRcon.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.MainWindowFactory = () => new MainWindow();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Referenced by name by the Avalonia XAML previewer; do not rename.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
