using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>One row of the mute list, shaped for display.</summary>
public sealed record MuteRow(
    ulong SteamId64, string Name, string Steam3, string Status, string Team, bool Online);

/// <summary>
/// Live view of the server's stored text mutes (opcodes 62/63/64) with unmute.
///
/// Unlike vanilla, mute is not per-connection state here: the server keeps a globalconfig
/// array beside its ban list and re-applies it as the player rejoins, so this list holds
/// people who are currently offline too. Their name is whatever they were called when
/// muted, and they have no team until they come back.
/// </summary>
public partial class MutesView : UserControl
{
    private readonly ObservableCollection<MuteRow> _rows = new();
    private readonly List<MuteInfoEvent> _pending = new();
    private Session _session = null!;
    private IShell _shell = null!;

    /// <summary>Bumped per request so a late reply to a superseded one cannot overwrite the status.</summary>
    private int _requestId;
    private bool _awaitingEnd;

    /// <summary>Set while the checkbox is being synced from settings, so it does not save back.</summary>
    private bool _syncingLookup;

    public MutesView()
    {
        InitializeComponent();
        Mutes.ItemsSource = _rows;
    }

    public void Bind(Session session, IShell shell)
    {
        _session = session;
        _shell = shell;
        _session.Client.EventReceived += OnEvent;
        SyncLookupBox();
        LookupBox.IsCheckedChanged += Lookup_Changed;
        UpdateAvailability();
    }

    /// <summary>One switch, held in Core, so this page and the bans page cannot disagree.</summary>
    private void SyncLookupBox()
    {
        _syncingLookup = true;
        SteamNames.Enabled = _session.Settings.LookUpSteamNames;
        LookupBox.IsChecked = SteamNames.Enabled;
        _syncingLookup = false;
    }

    private void Lookup_Changed(object? sender, RoutedEventArgs e)
    {
        if (_syncingLookup) return;

        bool on = LookupBox.IsChecked == true;
        SteamNames.Enabled = on;
        _session.Settings.LookUpSteamNames = on;
        _session.SaveSettings();

        if (!on) return;

        SteamNames.ClearFailures();
        _ = ResolveNamesAsync(_requestId);
    }

    /// <summary>Called when the page is navigated to, so the list is never stale on arrival.</summary>
    public void RefreshIfConnected()
    {
        SyncLookupBox();
        UpdateAvailability();
        if (_session.Client.State == RconState.Connected) _ = RefreshAsync();
        else StatusText.Text = "Not connected.";
    }

    public void UpdateAvailability()
    {
        bool connected = _session.Client.State == RconState.Connected;
        RefreshBtn.IsEnabled = UnmuteBtn.IsEnabled = ProfileBtn.IsEnabled = connected;
        if (!connected) _rows.Clear();
    }

    private async Task RefreshAsync()
    {
        if (_session.Client.State != RconState.Connected)
        {
            StatusText.Text = "Not connected.";
            return;
        }

        int id = ++_requestId;
        _pending.Clear();
        _awaitingEnd = true;
        StatusText.Text = "Requesting\u2026";
        try { await _session.Client.RequestMuteListAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; _awaitingEnd = false; return; }

        // Same reasoning as the ban list: no answer at all reads exactly like "nobody is
        // muted", and only one of those is a server the tool can actually administer.
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (id == _requestId && _awaitingEnd && _session.Client.State == RconState.Connected)
            StatusText.Text = "No reply to the mute-list request \u2014 this server does not implement "
                            + "opcode 62. It is running stock RCON, or a mod build older than AdminMod 1.4.";
    }

    private void OnEvent(RconEvent evt)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnEvent(evt));
            return;
        }

        switch (evt)
        {
            case MuteInfoEvent m:
                _pending.Add(m);
                break;

            case MuteListEndEvent end:
                _awaitingEnd = false;
                Render();
                StatusText.Text = end.Count == 0 ? "Nobody is muted." : $"{end.Count} mute(s).";
                _ = ResolveNamesAsync(_requestId);
                break;

            // Any admin's mute change invalidates this list, including a re-apply fired by
            // someone rejoining, so follow the audit rather than making the user refresh.
            case AdminAuditEvent a when a.Action is "MUTE_PLAYER" or "UNMUTE_PLAYER" or "MUTE_REAPPLIED":
                StatusText.Text = $"{a.Action}: {a.Detail}";
                _ = RefreshAsync();
                break;
        }
    }

    // One table, in Core, so this cannot drift from the roster -- and so a Deadliest
    // Warrior server reads Blue/Red rather than Agatha/Mason.
    private static string TeamName(int teamId) =>
        teamId < 0 ? "\u2014" : Teams.Name(teamId);

    private static bool IsPlaceholder(string name) => string.IsNullOrWhiteSpace(name);

    private static string DisplayName(MuteInfoEvent m)
    {
        if (IsPlaceholder(m.Name) && SteamNames.TryGetCached(m.SteamId64, out var looked))
            return looked;

        return string.IsNullOrWhiteSpace(m.Name) ? "(unnamed)" : m.Name;
    }

    /// <summary>Same as the bans page: rows first, names when the lookup lands.</summary>
    private async Task ResolveNamesAsync(int id)
    {
        if (!SteamNames.Enabled) return;

        var unknown = _pending
            .Where(m => m.SteamId64 != 0 && IsPlaceholder(m.Name))
            .Select(m => m.SteamId64)
            .ToList();

        if (unknown.Count == 0) return;
        if (!await SteamNames.ResolveAsync(unknown)) return;
        if (id == _requestId) Render();
    }

    private void Render()
    {
        _rows.Clear();

        // Online first: those are the ones an admin is likely acting on right now.
        foreach (var m in _pending
                     .OrderByDescending(m => m.Online)
                     .ThenBy(DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _rows.Add(new MuteRow(
                m.SteamId64,
                DisplayName(m),
                SteamId.ToSteam3(m.SteamId64),
                // "not stored" is a live in-game admin mute the mod never recorded: real, but
                // gone the moment that player disconnects.
                m.Online ? (m.Stored ? "Online" : "Online (not stored)") : "Offline",
                m.Online ? TeamName(m.TeamId) : "\u2014",
                m.Online));
        }

        MutesHeader.Text = $"STORED MUTES ({_rows.Count})";
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Profile_Click(object? sender, RoutedEventArgs e)
    {
        var targets = Mutes.SelectedItems?.Cast<MuteRow>().Where(m => m.SteamId64 != 0).ToList()
                      ?? new List<MuteRow>();

        if (targets.Count == 0)
        {
            StatusText.Text = "Select a mute first.";
            return;
        }

        if (targets.Count > 5)
        {
            StatusText.Text = $"{targets.Count} selected \u2014 pick 5 or fewer to open profiles.";
            return;
        }

        foreach (var m in targets)
            await OpenLink.InBrowserAsync(this, SteamNames.ProfileUrl(m.SteamId64));

        StatusText.Text = targets.Count == 1
            ? $"Opened the Steam profile for {targets[0].Name}."
            : $"Opened {targets.Count} Steam profiles.";
    }

    private async void Unmute_Click(object? sender, RoutedEventArgs e)
    {
        if (_session.Client.State != RconState.Connected)
        {
            StatusText.Text = "Not connected.";
            return;
        }

        var targets = Mutes.SelectedItems?.Cast<MuteRow>().Where(m => m.SteamId64 != 0).ToList()
                      ?? new List<MuteRow>();

        if (targets.Count == 0)
        {
            StatusText.Text = "Select a mute first.";
            return;
        }

        if (!await Prompt.ConfirmAsync(this, "Confirm unmute",
                targets.Count == 1
                    ? $"Unmute {targets[0].Name}?"
                    : $"Unmute {targets.Count} players?",
                "Unmute"))
            return;

        // Opcode 42 clears the stored entry whether or not they are connected.
        foreach (var m in targets)
            await _session.Client.MutePlayerAsync(m.SteamId64, false);

        StatusText.Text = $"Unmute sent for {targets.Count} entry(s).";
    }
}
