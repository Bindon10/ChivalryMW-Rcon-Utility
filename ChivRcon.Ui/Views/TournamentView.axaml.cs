using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>
/// Everything needed to referee a match, in the order you use it: gate the round on
/// readiness, then control the match, then fix the score if something went wrong.
///
/// Every button here is a XangMod opcode. Against a vanilla server they all do nothing --
/// unknown opcodes are ignored rather than erroring -- so the page stays enabled but the
/// event feed will show a command sent with no audit line coming back.
/// </summary>
public partial class TournamentView : UserControl
{
    private Session _session = null!;
    private IShell _shell = null!;

    public TournamentView() => InitializeComponent();

    public void Bind(Session session, IShell shell)
    {
        _session = session;
        _shell = shell;
        ApplyGameLabels();
        GameProfile.Changed += OnGameProfileChanged;
        UpdateAvailability();
    }

    /// <summary>Name the two score rows for the game we are talking to.</summary>
    private void ApplyGameLabels()
    {
        var (first, second) = GameProfile.TeamPair();
        Team0Label.Text = first;
        Team1Label.Text = second;
    }

    private void OnGameProfileChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnGameProfileChanged);
            return;
        }

        ApplyGameLabels();
    }

    public void UpdateAvailability()
    {
        // Listed explicitly rather than walked with GetLogicalDescendants: that is an
        // extension method in Avalonia.LogicalTree, and a missing using for exactly that
        // kind of extension has already cost one build round-trip on this project.
        bool connected = _session.Client.State == RconState.Connected;
        foreach (var b in new[]
                 {
                     TournOnBtn, TournOffBtn, ReadyAllBtn, UnreadyAllBtn,
                     PauseBtn, UnpauseBtn, RestartBtn, EndMatchBtn,
                     ApplyScoreBtn, SpeedQuarterBtn, SpeedHalfBtn, SpeedNormalBtn,
                     SpeedOneAndHalfBtn, SpeedDoubleBtn,
                 })
            b.IsEnabled = connected;
    }

    // ---------------- ready gate ----------------

    private int Threshold => (int)(ThresholdBox.Value ?? 100);

    private async void TournOn_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.SetTournamentAsync(true, Threshold),
            $"Tournament mode ON (ready threshold {Threshold}%)");

    private async void TournOff_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.SetTournamentAsync(false), "Tournament mode OFF");

    private async void ReadyAll_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.ReadyAllAsync(true), "Force all players ready");

    private async void UnreadyAll_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.ReadyAllAsync(false), "Clear all ready flags");

    // ---------------- match control ----------------

    private async void Pause_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.SetPauseAsync(true), "Pause match");

    private async void Unpause_Click(object? sender, RoutedEventArgs e) =>
        await Guarded(() => _session.Client.SetPauseAsync(false), "Unpause match");

    private async void Restart_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Prompt.ConfirmAsync(this, "Restart match",
                "Restart the current map? Everyone returns to the pre-round.", "Restart"))
            return;
        await Guarded(() => _session.Client.RestartMatchAsync(), "Restart match");
    }

    private async void EndMatch_Click(object? sender, RoutedEventArgs e)
    {
        var (first, second) = GameProfile.TeamPair();
        var team = await Prompt.TextAsync(this, "End match",
            $"Winning team (0 = {first}, 1 = {second}, -1 = draw):", "-1");
        if (team is null || !int.TryParse(team, out int teamId)) return;

        var reason = await Prompt.TextAsync(this, "End match", "Reason shown to players:",
            "Match ended by admin");
        if (reason is null) return;

        await Guarded(() => _session.Client.EndMatchAsync(teamId, reason),
            $"End match (winner {teamId}): {reason}");
    }

    // ---------------- score ----------------

    private async void ApplyScore_Click(object? sender, RoutedEventArgs e)
    {
        int team0 = (int)(Team0Score.Value ?? 0);
        int team1 = (int)(Team1Score.Value ?? 0);
        var (first, second) = GameProfile.TeamPair();

        // Two opcodes rather than one: SET_TEAM_SCORE is per team, and keeping it that way
        // means the client is not inventing a combined verb the protocol does not have.
        await Guarded(() => _session.Client.SetTeamScoreAsync(0, team0), $"{first} score -> {team0}");
        await Guarded(() => _session.Client.SetTeamScoreAsync(1, team1), $"{second} score -> {team1}");
    }

    // ---------------- speed ----------------

    private async void SpeedQuarter_Click(object? sender, RoutedEventArgs e) => await SetSpeed(25);

    private async void SpeedHalf_Click(object? sender, RoutedEventArgs e) => await SetSpeed(50);

    private async void SpeedNormal_Click(object? sender, RoutedEventArgs e) => await SetSpeed(100);

    private async void SpeedOneAndHalf_Click(object? sender, RoutedEventArgs e) => await SetSpeed(150);

    private async void SpeedDouble_Click(object? sender, RoutedEventArgs e) => await SetSpeed(200);

    private async Task SetSpeed(int percent) =>
        await Guarded(() => _session.Client.SetGameSpeedAsync(percent),
            $"Game speed -> {percent / 100.0}x");

    // ---------------- shared ----------------

    private async Task Guarded(Func<Task> action, string description)
    {
        if (_session.Client.State != RconState.Connected)
        {
            await Prompt.NoticeAsync(this, "Chivalry RCON", "Not connected.");
            return;
        }
        try
        {
            await action();
            _session.Append($"> {description}", Palette.Command);
            _session.Audit.RecordSent("TOURNAMENT", description);
        }
        catch (Exception ex)
        {
            _session.Append($"Command failed: {ex.Message}", Palette.Danger);
            _session.Audit.RecordSent("TOURNAMENT_FAILED", $"{description} -- {ex.Message}");
        }
    }
}
