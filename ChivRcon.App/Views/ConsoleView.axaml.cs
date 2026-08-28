using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

public partial class ConsoleView : UserControl
{
    private static readonly string[] SuggestedCommands =
    {
        "AdminForceTextMute <name>",
        "AdminForceTextUnmute <name>",
        "AddBots 10",
        "addRedBots 2",
        "addBlueBots 2",
        "KillBots",
        "ManuallyEndGame",
        "aoc_slomo 2",
        "aoc_slomo 1",
        "AdminRestartMap",
        "AdminChangeTeamDamageAmount 0.5",
        "aoc_showdamage 1",
        "pause",
    };

    private readonly ObservableCollection<string> _maps = new();
    private Session _session = null!;
    private MainWindow _owner = null!;

    public ConsoleView()
    {
        InitializeComponent();
        Command.ItemsSource = SuggestedCommands;
        Maps.ItemsSource = _maps;
    }

    public void Bind(Session session, MainWindow owner)
    {
        _session = session;
        _owner = owner;

        Feed.ItemsSource = _session.Log;
        _session.LogAppended += ScrollToEnd;

        ShowKills.IsChecked = _session.ShowKills;
        LogToFile.IsChecked = _session.LogToFile;
        ShowKills.IsCheckedChanged += (_, _) => _session.ShowKills = ShowKills.IsChecked == true;
        LogToFile.IsCheckedChanged += (_, _) => _session.LogToFile = LogToFile.IsChecked == true;

        UpdateCommandAvailability();
    }

    private void ScrollToEnd() =>
        // Posted at Background priority so the new line has been laid out; scrolling in the
        // same tick as the Add lands on the previous extent and stops one line short.
        Dispatcher.UIThread.Post(() => FeedScroll.ScrollToEnd(), DispatcherPriority.Background);

    public void AddMap(string name)
    {
        if (!_maps.Any(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase)))
            _maps.Add(name);
    }

    public void ClearMaps() => _maps.Clear();

    public void UpdateCommandAvailability()
    {
        bool connected = _session.Client.State == RconState.Connected;
        SayBtn.IsEnabled = SayBigBtn.IsEnabled = Message.IsEnabled = connected;
        RunBtn.IsEnabled = Command.IsEnabled = connected;
        ChangeMapBtn.IsEnabled = NextMapBtn.IsEnabled = Maps.IsEnabled = connected;
    }

    // ---------------- handlers ----------------

    private void Clear_Click(object? sender, RoutedEventArgs e) => _session.Log.Clear();

    private async void Message_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SendSayAsync(big: false);
    }

    private async void Say_Click(object? sender, RoutedEventArgs e) => await SendSayAsync(big: false);

    private async void SayBig_Click(object? sender, RoutedEventArgs e) => await SendSayAsync(big: true);

    private async void Run_Click(object? sender, RoutedEventArgs e) => await SendConsoleCommandAsync();

    private async void ChangeMap_Click(object? sender, RoutedEventArgs e)
    {
        string map = CurrentMapText();
        if (map.Length == 0) return;
        await Guarded(() => _session.Client.ChangeMapAsync(map), $"Change map to {map}");
    }

    private async void NextMap_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.RotateMapAsync(), "Rotate map");

    // ---------------- work ----------------

    private string CurrentMapText() => (Maps.Text ?? "").Trim();

    private async Task SendSayAsync(bool big)
    {
        string msg = (Message.Text ?? "").Trim();
        if (msg.Length == 0) return;
        await Guarded(
            () => big ? _session.Client.SayAllBigAsync(msg) : _session.Client.SayAllAsync(msg),
            $"{(big ? "SAY_ALL_BIG" : "SAY_ALL")}: {msg}");
        Message.Text = "";
    }

    private async Task SendConsoleCommandAsync()
    {
        if (_session.Client.State != RconState.Connected)
        {
            await Prompt.NoticeAsync(_owner, "Chivalry RCON", "Not connected.");
            return;
        }

        string cmd = (Command.Text ?? "").Trim();
        if (cmd.Length == 0) return;

        if (cmd.Contains("<name>", StringComparison.Ordinal))
        {
            await Prompt.NoticeAsync(_owner, "Chivalry RCON",
                "That command needs a player name. Replace <name>, or use "
                + "\"Console command on player…\" from the roster's right-click menu.");
            return;
        }

        // Game speed still needs its own opcode: AOCGame.SetGameSpeed notifies clients and
        // republishes AOCGRI.Speed, which a bare GameInfo.ConsoleCommand does not do. That is
        // the reason the old relay carried a dedicated SLOMO verb.
        string lower = cmd.ToLowerInvariant();
        if (lower.StartsWith("aoc_slomo ", StringComparison.Ordinal)
            || lower.StartsWith("slomo ", StringComparison.Ordinal))
        {
            var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double speed))
            {
                await Guarded(() => _session.Client.SetGameSpeedAsync((int)Math.Round(speed * 100)),
                    $"Game speed -> {speed}x");
                return;
            }
        }

        await Guarded(() => _session.Client.ConsoleCommandAsync(0, ConsoleCommandScope.Game, cmd),
            $"Console: {cmd}");
    }

    private async Task Guarded(Func<Task> action, string description)
    {
        if (_session.Client.State != RconState.Connected)
        {
            await Prompt.NoticeAsync(_owner, "Chivalry RCON", "Not connected.");
            return;
        }
        try
        {
            await action();
            _session.Append($"> {description}", Palette.Command);
            _session.Audit.RecordSent("COMMAND", description);
        }
        catch (Exception ex)
        {
            _session.Append($"Command failed: {ex.Message}", Palette.Danger);
            _session.Audit.RecordSent("COMMAND_FAILED", $"{description} -- {ex.Message}");
        }
    }
}
