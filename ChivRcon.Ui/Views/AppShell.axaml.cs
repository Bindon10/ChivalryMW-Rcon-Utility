using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>
/// The whole app body: session, pages, timers and connection handling. Hosted by a Window on
/// desktop and by MainView under Android's single-view lifetime.
/// </summary>
public partial class AppShell : UserControl, IShell
{
    // Server leads: connecting is the first thing you do, so the app opens on it.
    private const int PageServer = 0;
    private const int PageDashboard = 1;
    private const int PageMap = 2;
    private const int PageConsole = 3;
    private const int PageLoadout = 4;
    private const int PageTournament = 5;
    private const int PageBans = 6;
    private const int PageMutes = 7;
    private const int PageAudit = 8;
    private const int PageReference = 9;

    private readonly Session _session = new();
    private readonly NavItem[] _navItems =
    {
        new("Server"), new("Dashboard"), new("Map"), new("Console"),
        new("Loadout"), new("Tournament"), new("Bans"), new("Mutes"), new("Audit"),
        new("Reference"),
    };

    private readonly ServerView _server = new();
    private readonly DashboardView _dashboard = new();
    private readonly ConsoleView _console = new();
    private readonly MapView _map = new();
    private readonly LoadoutView _loadout = new();
    private readonly TournamentView _tournament = new();
    private readonly BansView _bans = new();
    private readonly MutesView _mutes = new();
    private readonly AuditView _audit = new();
    private readonly ReferenceView _reference = new();

    private readonly Control[] _pages;

    private readonly DispatcherTimer _rosterTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    // Scoreboard sweep (29 -> 30s -> 31). Slower than the roster repaint: it is a
    // whole-server burst, not a delta.
    private readonly DispatcherTimer _scoreboardTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    // Only touched on the UI thread now that Apply is marshalled there.
    private bool _rosterDirty;
    private bool _userDisconnected;
    private int _reconnectAttempts;

    // Phone build carries every page, ordered by what is actually reached for away from a
    // desk rather than by the desktop's grouping.
    private static readonly int[] CompactPages =
    {
        PageServer, PageDashboard, PageConsole, PageBans, PageMutes,
        PageTournament, PageLoadout, PageMap, PageAudit, PageReference,
    };

    private int[] _visible = null!;

    public AppShell() : this(false) { }

    public AppShell(bool compact)
    {
        InitializeComponent();

        // Must stay in the same order as _navItems and the Page* constants.
        _pages = new Control[]
        {
            _server, _dashboard, _map, _console, _loadout, _tournament, _bans, _mutes,
            _audit, _reference,
        };

        // Server first: it loads the saved settings into the Session, and Console reads
        // ShowKills / LogToFile out of the Session when it binds its checkboxes.
        _server.Bind(_session, this);
        _dashboard.Bind(_session, this);
        _map.Bind(_session);
        _console.Bind(_session, this);
        _loadout.Bind(_session, this);
        _tournament.Bind(_session, this);
        _bans.Bind(_session, this);
        _mutes.Bind(_session, this);
        _audit.Bind(_session);

        _visible = compact ? CompactPages : Enumerable.Range(0, _pages.Length).ToArray();
        Nav.ItemsSource = _visible.Select(i => _navItems[i]).ToList();
        Nav.SelectedIndex = 0;
        PageHost.Content = _pages[_visible[0]];
        PageTitle.Text = _navItems[_visible[0]].Title;

        _session.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(RenderStatus);
        _session.LogAppended += OnLogAppended;
        RenderStatus();

        WireClient();

        _rosterTimer.Tick += (_, _) =>
        {
            if (!_rosterDirty) return;
            // Hold the flag while the roster's context menu is up, so the pending changes
            // land the moment it closes instead of being dropped.
            if (_dashboard.MenuIsOpen) return;
            _rosterDirty = false;
            _dashboard.RefreshRoster();
            _loadout.RefreshTargets();
        };
        _rosterTimer.Start();

        _scoreboardTimer.Tick += async (_, _) =>
        {
            if (_session.Client.State != RconState.Connected) return;
            try
            {
                await _session.Client.RequestPlayerListAsync();
            }
            catch { /* a dropped socket is the reconnect timer's problem */ }
        };
        _scoreboardTimer.Start();

        _reconnectTimer.Tick += async (_, _) => await TryReconnectAsync();

        // Narrow screens collapse the pane behind the hamburger; desktop keeps it pinned.
        Split.PaneClosed += (_, _) => { };
        SizeChanged += (_, _) => ApplyResponsiveNav();
        AttachedToVisualTree += (_, _) => ApplyResponsiveNav();
    }

    // ================= navigation =================

    private void Nav_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int slot = Nav.SelectedIndex;
        if (slot < 0 || slot >= _visible.Length) return;
        int index = _visible[slot];

        PageHost.Content = _pages[index];
        _navItems[index].HasBadge = false;
        PageTitle.Text = _navItems[index].Title;

        // Overlay pane stays open over the page it just switched to otherwise.
        if (Split.DisplayMode == SplitViewDisplayMode.Overlay) Split.IsPaneOpen = false;

        // Polling costs a round trip per tick, so the map only runs while you are on it.
        _map.SetActive(index == PageMap);

        if (index == PageBans) _bans.RefreshIfConnected();
        if (index == PageMutes) _mutes.RefreshIfConnected();
        if (index == PageAudit) _audit.Reload();
        // Loadout acts on the roster selection, so it picks it up on arrival rather than
        // making you choose a player twice.
        if (index == PageLoadout) _loadout.SetTarget(_dashboard.SelectedPlayer);
    }

    private void OnLogAppended()
    {
        int current = Nav.SelectedIndex >= 0 && Nav.SelectedIndex < _visible.Length
            ? _visible[Nav.SelectedIndex] : -1;
        if (current != PageConsole) _navItems[PageConsole].HasBadge = true;
    }

    private void RenderStatus()
    {
        StatusLine.Text = "● " + _session.StatusText;
        StatusLine.Foreground = _session.StatusBrush;
        AddressLine.Text = _session.AddressText;
        MapLine.Text = _session.MapText;
    }

    // ================= connection =================

    private void WireClient()
    {
        _session.Client.StateChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            _session.RefreshStatus();
            _server.UpdateConnectionUi();
            _console.UpdateCommandAvailability();
            _bans.UpdateAvailability();
            _mutes.UpdateAvailability();
            _tournament.UpdateAvailability();
            _map.UpdateAvailability();
            _loadout.UpdateAvailability();
        });

        _session.Client.Disconnected += reason => Dispatcher.UIThread.Post(() =>
        {
            _session.Append(reason, Palette.Danger);
            _session.RefreshStatus();
            _server.UpdateConnectionUi();
            _console.UpdateCommandAvailability();
            _bans.UpdateAvailability();
            _mutes.UpdateAvailability();
            _tournament.UpdateAvailability();
            _map.UpdateAvailability();
            _loadout.UpdateAvailability();

            // The game's RCON listener is a level actor, so every map change drops the
            // connection. Quietly dial back in.
            if (!_userDisconnected && _server.AutoReconnect)
            {
                _reconnectAttempts = 0;
                _session.Append("Auto-reconnect: waiting for server (map change?)…", Palette.Warning);
                _reconnectTimer.Start();
            }
        });

        // Apply runs on the UI thread, not the socket thread. Player now raises
        // PropertyChanged inline, and Avalonia bindings must not be updated off-thread.
        _session.Client.EventReceived += evt => Dispatcher.UIThread.Post(() =>
        {
            if (_session.Tracker.Apply(evt)) _rosterDirty = true;
            RenderEvent(evt);
        });
    }

    /// <summary>Connect or disconnect, driven by the Server page's button.</summary>
    public async Task ToggleConnectAsync(string host, int port, string password)
    {
        if (_session.Client.State != RconState.Disconnected)
        {
            _userDisconnected = true;
            _reconnectTimer.Stop();
            _session.Client.Disconnect();
            return;
        }

        _userDisconnected = false;
        _reconnectTimer.Stop();
        _session.RefreshStatus();

        try
        {
            _session.Tracker.Clear();
            _dashboard.RefreshRoster();
            _console.ClearMaps();
            _session.OpenLogFile();
            _session.Audit.Server = $"{host}:{port}";
            await _session.Client.ConnectAsync(host, port, password);
            _session.Append($"Connected to {host}:{port}", Palette.Success);
            _session.Audit.RecordSent("CONNECT", $"{host}:{port}");
        }
        catch (Exception ex)
        {
            _session.Append(ex.Message, Palette.Danger);
            await Prompt.NoticeAsync(this, "Connection failed", ex.Message);
        }
        finally
        {
            _session.RefreshStatus();
            _server.UpdateConnectionUi();
            _console.UpdateCommandAvailability();
            _bans.UpdateAvailability();
            _mutes.UpdateAvailability();
            _tournament.UpdateAvailability();
            _map.UpdateAvailability();
            _loadout.UpdateAvailability();
        }
    }

    private async Task TryReconnectAsync()
    {
        if (_session.Client.State != RconState.Disconnected || _userDisconnected)
        {
            _reconnectTimer.Stop();
            return;
        }
        if (++_reconnectAttempts > 20)
        {
            _reconnectTimer.Stop();
            _session.Append("Auto-reconnect gave up after 20 attempts.", Palette.Danger);
            return;
        }

        _reconnectTimer.Stop();
        try
        {
            _session.Tracker.Clear();
            _rosterDirty = true;
            await _session.Client.ConnectAsync(
                _server.Host, _server.Port, _server.Password, TimeSpan.FromSeconds(5));
            _session.Append($"Reconnected to {_server.Host}:{_server.Port}", Palette.Success);
        }
        catch
        {
            if (!_userDisconnected && _server.AutoReconnect) _reconnectTimer.Start();
        }
    }

    // ================= events -> UI =================

    private void RenderEvent(RconEvent evt)
    {
        var log = _session;
        var tracker = _session.Tracker;

        switch (evt)
        {
            case PlayerChatEvent e:
                log.Append($"[{Teams.Name(e.TeamId)}] {tracker.NameOf(e.SteamId64)}: {e.Message}", Palette.Chat);
                break;
            case PlayerConnectEvent e:
                log.Append($"+ {e.Name} connected ({SteamId.ToSteam3(e.SteamId64)})", Palette.Join);
                _session.Notes.Observe(e.SteamId64, e.Name);
                // Surface a flagged player the moment they arrive -- the whole reason for
                // keeping notes is that nobody remembers the name three weeks later.
                var known = _session.Notes.Find(e.SteamId64);
                if (known is not null && !known.IsEmpty)
                {
                    log.Append($"  ^ {e.Name} has admin notes: {known.Badge}"
                        + (known.Note.Length > 0 ? $" -- {known.Note}" : ""), Palette.Warning);
                    _session.Audit.RecordServer("NOTED_PLAYER_JOINED",
                        $"{e.Name} ({SteamId.ToSteam3(e.SteamId64)}) {known.Badge}");
                }
                break;
            case PlayerDisconnectEvent e:
                log.Append($"- {tracker.NameOf(e.SteamId64)} disconnected", Palette.Join);
                break;
            case MapChangedEvent e:
                _session.MapText = e.MapName;
                log.Append($"Map changed to {e.MapName}", Palette.Highlight);
                break;
            case MapListEntryEvent e:
                _console.AddMap(e.MapName);
                break;
            case TeamChangedEvent e:
                log.Append($"{tracker.NameOf(e.SteamId64)} joined {Teams.Name(e.TeamId)}", Palette.Join);
                break;
            case NameChangedEvent e:
                log.Append($"{SteamId.ToSteam3(e.SteamId64)} is now known as {e.Name}", Palette.Join);
                // Renaming is the standard dodge, so the alias trail builds itself.
                _session.Notes.Observe(e.SteamId64, e.Name);
                break;
            case KillEvent e when _session.ShowKills:
                string weapon = string.IsNullOrEmpty(e.WeaponName) ? "" : $" [{e.WeaponName}]";
                log.Append($"{tracker.NameOf(e.KillerSteamId64)} killed {tracker.NameOf(e.VictimSteamId64)}{weapon}",
                    Palette.Kill);
                break;
            case SuicideEvent e when _session.ShowKills:
                log.Append($"{tracker.NameOf(e.SteamId64)} committed suicide", Palette.Kill);
                break;
            case RoundEndEvent e:
                log.Append($"Round over - {Teams.Name(e.WinningTeamId)} wins", Palette.Warning);
                break;
            case AuthSucceededEvent:
                log.Append("Authentication accepted", Palette.Success);
                break;
            case AdminAuditEvent e:
                log.Append($"[audit] {e.Action}: {e.Detail}",
                    e.Action.EndsWith("_FAILED", StringComparison.Ordinal) ? Palette.Danger : Palette.Success);
                // The server's own account of what it did, which is the half worth keeping:
                // a "sent" line with no matching "audit" is what an ignored opcode looks like.
                _session.Audit.RecordServer(e.Action, e.Detail);
                break;
            case ConsoleResultEvent e:
                log.Append($"> {e.Command}", Palette.Command);
                if (!string.IsNullOrWhiteSpace(e.Result)) log.Append(e.Result, Palette.TextPrimary);
                break;
            case ServerInfoEvent e:
                log.Append($"Server: {e.MapName} - {e.NumPlayers}/{e.MaxPlayers} players, "
                    + $"{e.NumSpectators} spectating, match {(e.MatchBegun ? "in progress" : "not started")}",
                    Palette.Highlight);
                break;
            case UnknownEvent e:
                log.Append($"Unknown message type {e.MessageType} ({e.Payload.Length} bytes)", Palette.Danger);
                break;
        }
    }

    // ================= responsive nav =================

    /// <summary>Below this the pane becomes an overlay behind the hamburger.</summary>
    private const double NarrowWidth = 700;

    private bool _narrow;

    private void ApplyResponsiveNav()
    {
        double w = Bounds.Width;
        if (w <= 0) return;

        bool narrow = w < NarrowWidth;
        if (narrow == _narrow && MobileBar.IsVisible == narrow) return;
        _narrow = narrow;

        MobileBar.IsVisible = narrow;
        Split.DisplayMode = narrow ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
        Split.IsPaneOpen = !narrow;
    }

    private void Hamburger_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Split.IsPaneOpen = !Split.IsPaneOpen;

    /// <summary>Persist settings and tear the session down. Called by the host on shutdown.</summary>
    public void Shutdown()
    {
        _server.PersistSettings();
        _session.Dispose();
    }
}
