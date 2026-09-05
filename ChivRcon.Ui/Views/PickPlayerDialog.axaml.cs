using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ChivRcon.App.Platform;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>
/// Choose a second player. Teleport is the only action that needs two, and picking the
/// other end from a list beats typing a name that has to match exactly.
/// </summary>
public partial class PickPlayerDialog : Window
{
    /// <summary>Wrapper so the combo shows something readable without a compiled binding.</summary>
    private sealed record Entry(Player Player)
    {
        public override string ToString() => $"{Player.Name}  ·  {Player.TeamName}";
    }

    public PickPlayerDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsChrome.Attach(this, Avalonia.Media.Color.Parse("#22252C"));
        };
    }

    /// <summary>Returns the chosen player, or null if cancelled or nobody to choose from.</summary>
    public static async Task<Player?> ShowAsync(Visual anchor, string title, string message,
        IEnumerable<Player> choices)
    {
        var list = choices
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new Entry(p))
            .ToList();

        if (list.Count == 0) return null;

        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            var box = new ComboBox { ItemsSource = list, SelectedIndex = 0, MinHeight = 44,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            return await DialogOverlay.ShowAsync<Player>(anchor, title, message, box,
                new DialogOverlay.Choice<Player>("Cancel", () => null, false),
                new DialogOverlay.Choice<Player>("OK", () => (box.SelectedItem as Entry)?.Player, true));
        }

        var dlg = new PickPlayerDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.PlayerBox.ItemsSource = list;
        dlg.PlayerBox.SelectedIndex = 0;

        return await dlg.ShowDialog<Player?>(owner);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) =>
        Close((PlayerBox.SelectedItem as Entry)?.Player);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
