using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChivRcon.App.Platform;

namespace ChivRcon.App.Views;

/// <summary>
/// One dialog covering "ask for a string", "are you sure" and "here is a message".
/// WinForms had InputDialog plus MessageBox; Avalonia ships neither, and three flavours of
/// the same small window is not worth the duplication.
///
/// Shaped after Supremacy Launcher's ConfirmDialog, including keeping the OS title bar --
/// a borderless dialog cannot be dragged, and there is nothing to gain from that here.
/// IsDefault / IsCancel give Enter and Escape for free.
/// </summary>
public partial class Prompt : Window
{
    public Prompt()
    {
        InitializeComponent();

        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsChrome.Attach(this, Avalonia.Media.Color.Parse("#22252C"));
        };
    }

    /// <summary>Asks for a line of text. Returns null if cancelled.</summary>
    public static async Task<string?> TextAsync(
        Visual anchor, string title, string message, string initial = "")
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            var box = new TextBox { Text = initial, MinHeight = 44, FontSize = 15 };
            return await DialogOverlay.ShowAsync(anchor, title, message, box,
                new DialogOverlay.Choice<string>("Cancel", () => null, false),
                new DialogOverlay.Choice<string>("OK", () => box.Text ?? "", true));
        }

        var dlg = new Prompt();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.Input.IsVisible = true;
        dlg.Input.Text = initial;
        dlg.Opened += (_, _) => { dlg.Input.Focus(); dlg.Input.SelectAll(); };
        return await dlg.ShowDialog<string?>(owner);
    }

    /// <summary>Yes/no. Returns false if dismissed.</summary>
    public static async Task<bool> ConfirmAsync(
        Visual anchor, string title, string message, string confirmText = "Confirm")
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            return await DialogOverlay.ShowAsync(anchor, title, message, null,
                new DialogOverlay.Choice<string>("Cancel", () => null, false),
                new DialogOverlay.Choice<string>(confirmText, () => "", true)) is not null;
        }

        var dlg = new Prompt();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.OkBtn.Content = confirmText;
        return await dlg.ShowDialog<string?>(owner) is not null;
    }

    /// <summary>A message with a single dismiss button.</summary>
    public static async Task NoticeAsync(Visual anchor, string title, string message)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            await DialogOverlay.ShowAsync(anchor, title, message, null,
                new DialogOverlay.Choice<string>("Close", () => "", true));
            return;
        }

        var dlg = new Prompt();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.CancelBtn.IsVisible = false;
        dlg.OkBtn.Content = "Close";
        await dlg.ShowDialog<string?>(owner);
    }

    // Confirm- and notice-style dialogs have no input, so "" stands in for "the user said yes".
    // Closing with the X returns default(string?) = null, which reads as a cancel.
    private void Ok_Click(object? sender, RoutedEventArgs e) =>
        Close(Input.IsVisible ? Input.Text ?? "" : "");

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
