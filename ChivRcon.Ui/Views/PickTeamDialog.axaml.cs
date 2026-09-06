using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ChivRcon.App.Platform;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>
/// Choose a team by colour.
///
/// Deadliest Warrior runs up to six teams and they are told apart on the scoreboard by
/// COLOUR, not by name — a team restricted to one class is renamed after that class, so a
/// six-team match reads "Vikings", "Ninjas", "Samurai" with nothing saying which is blue.
/// Six near-identical "Move to …" rows in a context menu made that worse, so the teams are
/// shown here with the game's own team colour instead.
///
/// The teams come from the server's SERVER_INFO reply, so this never invents a team that
/// does not exist on the current map.
/// </summary>
public partial class PickTeamDialog : Window
{
    public PickTeamDialog()
    {
        InitializeComponent();
        TeamBox.ItemTemplate = RowTemplate();

        // Double-click is the fast path; OK is still there because the overlay build on
        // Android has no equivalent.
        TeamBox.DoubleTapped += (_, _) =>
        {
            if (TeamBox.SelectedItem is ServerTeam t) Close(t);
        };

        Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows())
                WindowsChrome.Attach(this, Avalonia.Media.Color.Parse("#22252C"));
        };
    }

    /// <summary>A colour chip and the team's label. Built in code — see the XAML note.</summary>
    private static FuncDataTemplate<ServerTeam> RowTemplate() =>
        new((team, _) =>
        {
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = SwatchBrush(team?.ColorHex),
                // White is a real team colour here, so a chip needs an outline to exist at
                // all against a light row.
                BorderBrush = new SolidColorBrush(Color.Parse("#55000000")),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var label = new TextBlock
            {
                Text = team?.Label ?? "",
                VerticalAlignment = VerticalAlignment.Center,
            };

            var index = new TextBlock
            {
                Text = team is null ? "" : $"team {team.Index}",
                Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(swatch);
            row.Children.Add(label);
            row.Children.Add(index);
            return row;
        }, true);

    /// <summary>The server sends the game's markup colour ("#388FF2"). Never trust it blindly.</summary>
    private static IBrush SwatchBrush(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* fall through to neutral */ }
        }
        return new SolidColorBrush(Color.Parse("#9AA4B2"));
    }

    /// <summary>Returns the chosen team, or null if cancelled or there is nothing to choose.</summary>
    public static async Task<ServerTeam?> ShowAsync(Visual anchor, string title, string message,
        IReadOnlyList<ServerTeam> teams)
    {
        if (teams.Count == 0) return null;

        if (TopLevel.GetTopLevel(anchor) is not Window owner)
        {
            var list = new ListBox
            {
                ItemsSource = teams,
                SelectedIndex = 0,
                ItemTemplate = RowTemplate(),
                MaxHeight = 280,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            return await DialogOverlay.ShowAsync<ServerTeam>(anchor, title, message, list,
                new DialogOverlay.Choice<ServerTeam>("Cancel", () => null, false),
                new DialogOverlay.Choice<ServerTeam>("Move", () => list.SelectedItem as ServerTeam, true));
        }

        var dlg = new PickTeamDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.TeamBox.ItemsSource = teams;
        dlg.TeamBox.SelectedIndex = 0;

        return await dlg.ShowDialog<ServerTeam?>(owner);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Close(TeamBox.SelectedItem as ServerTeam);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
