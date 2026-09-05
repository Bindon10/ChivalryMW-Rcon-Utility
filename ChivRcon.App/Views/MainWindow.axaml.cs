using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ChivRcon.App.Platform;

namespace ChivRcon.App.Views;

/// <summary>
/// Desktop host: custom title bar plus the shared AppShell. All app logic lives in AppShell
/// so the same body runs under Android's single-view lifetime.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The platform handle only exists once the window is opened.
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsChrome.Attach(this, Color.Parse("#22252C"));
        };

        Closing += (_, _) => Shell.Shutdown();
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Ignore drags that start on the caption buttons.
        if (e.Source is Button) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void TitleBar_DoubleTapped(object? sender, TappedEventArgs e) => ToggleMaximised();

    private void Minimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
