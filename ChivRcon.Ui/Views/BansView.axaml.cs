using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>One row of the ban list, shaped for display.</summary>
public sealed record BanRow(
    ulong SteamId64, string Name, string Steam3, string Duration, string IpPolicy, string Reason);

/// <summary>
/// Live view of the server's ban list (XangMod opcodes 39/40/41) with unban.
///
/// AOCAccessControl.Bans is globalconfig, i.e. it only exists server-side in the ini --
/// there is no way for a client to read it directly. The server walks the array and sends
/// one BAN_INFO per entry, so what is shown here is whatever the server currently holds,
/// including bans added by other admins or by votekick.
///
/// Chivalry has three ban stores, not one, and all three are enforced: AOCAccessControl.Bans
/// (rich entries, written by the RCON ban and by votekick), Engine.AccessControl.BannedIDs
/// (bare uids, written by the console "admin kickban"), and Engine.AccessControl.IPPolicies
/// (DENY lines, no uid at all). The mod reports all three; the last two arrive named
/// "(uid ban list)" and "(ip ban)" because the game records nothing else about them.
/// </summary>
public partial class BansView : UserControl
{
    private readonly ObservableCollection<BanRow> _rows = new();
    private readonly List<BanInfoEvent> _pending = new();
    private Session _session = null!;
    private IShell _shell = null!;

    /// <summary>Bumped per request so a late reply to a superseded one cannot overwrite the status.</summary>
    private int _requestId;
    private bool _awaitingEnd;

    /// <summary>Set while the checkbox is being synced from settings, so it does not save back.</summary>
    private bool _syncingLookup;

    public BansView()
    {
        InitializeComponent();
        Bans.ItemsSource = _rows;
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

    /// <summary>One switch, held in Core, so this page and the mutes page cannot disagree.</summary>
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

        // Ids that failed while lookups were off should get another go, not stay poisoned.
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
        RefreshBtn.IsEnabled = UnbanBtn.IsEnabled = ProfileBtn.IsEnabled = connected;
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
        StatusText.Text = "Requesting…";
        try { await _session.Client.RequestBanListAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; _awaitingEnd = false; return; }

        // Silence and an empty list look identical on screen, and they mean opposite things.
        // A stock AOCRCon server has no opcode 39 at all and simply never answers, so without
        // this the page just sits there and reads as "this server has no bans".
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (id == _requestId && _awaitingEnd && _session.Client.State == RconState.Connected)
            StatusText.Text = "No reply to the ban-list request \u2014 this server does not implement "
                            + "opcode 39. It is running stock RCON, or a mod build older than AdminMod 1.4.";
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
            case BanInfoEvent b:
                _pending.Add(b);
                break;

            case BanListEndEvent end:
                _awaitingEnd = false;
                Render();
                StatusText.Text = end.Count == 0 ? "No bans on this server." : $"{end.Count} ban(s).";
                _ = ResolveNamesAsync(_requestId);
                break;

            // The server audits every unban, including ones that matched nothing, so the
            // outcome is visible rather than silent like vanilla's opcode 20.
            case AdminAuditEvent a when a.Action is "UNBAN" or "UNBAN_FAILED":
                StatusText.Text = $"{a.Action}: {a.Detail}";
                if (a.Action == "UNBAN") _ = RefreshAsync();
                break;
        }
    }

    /// <summary>
    /// A console kickban records nothing but the uid, so the server sends the placeholder it
    /// has. Prefer a name a Steam lookup filled in; failing that keep the placeholder, which
    /// at least says where the ban came from.
    /// </summary>
    private static bool IsPlaceholder(string name) =>
        string.IsNullOrWhiteSpace(name) || name is "(uid ban list)" or "(ip ban)";

    private static string DisplayName(BanInfoEvent b)
    {
        if (IsPlaceholder(b.Name) && SteamNames.TryGetCached(b.SteamId64, out var looked))
            return looked;

        return string.IsNullOrWhiteSpace(b.Name) ? "(unnamed)" : b.Name;
    }

    /// <summary>
    /// Fill in the names the server could not, then redraw. Runs after the rows are already on
    /// screen, so the list never waits on the network, and does nothing if the page has since
    /// issued another request.
    /// </summary>
    private async Task ResolveNamesAsync(int id)
    {
        if (!SteamNames.Enabled) return;

        var unknown = _pending
            .Where(b => b.SteamId64 != 0 && IsPlaceholder(b.Name))
            .Select(b => b.SteamId64)
            .ToList();

        if (unknown.Count == 0) return;
        if (!await SteamNames.ResolveAsync(unknown)) return;
        if (id == _requestId) Render();
    }

    private void Render()
    {
        _rows.Clear();
        foreach (var b in _pending.OrderBy(DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, b.DurationSeconds));
            string duration = b.DurationSeconds <= 0
                ? "permanent"
                : span.TotalDays >= 1
                    ? $"{(int)span.TotalDays}d {span.Hours}h"
                    : $"{(int)span.TotalHours}h {span.Minutes}m";

            _rows.Add(new BanRow(
                b.SteamId64,
                DisplayName(b),
                SteamId.ToSteam3(b.SteamId64),
                duration,
                b.IpPolicy,
                b.Reason));
        }

        BansHeader.Text = $"SERVER BAN LIST ({_rows.Count})";
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Profile_Click(object? sender, RoutedEventArgs e)
    {
        var targets = Bans.SelectedItems?.Cast<BanRow>().Where(b => b.SteamId64 != 0).ToList()
                      ?? new List<BanRow>();

        if (targets.Count == 0)
        {
            StatusText.Text = "Select a ban with a Steam ID first (an IP-only ban has no profile).";
            return;
        }

        // Selecting the whole grid and getting thirty browser tabs is not a favour.
        if (targets.Count > 5)
        {
            StatusText.Text = $"{targets.Count} selected \u2014 pick 5 or fewer to open profiles.";
            return;
        }

        foreach (var b in targets)
            await OpenLink.InBrowserAsync(this, SteamNames.ProfileUrl(b.SteamId64));

        StatusText.Text = targets.Count == 1
            ? $"Opened the Steam profile for {targets[0].Name}."
            : $"Opened {targets.Count} Steam profiles.";
    }

    private async void Unban_Click(object? sender, RoutedEventArgs e)
    {
        if (_session.Client.State != RconState.Connected)
        {
            StatusText.Text = "Not connected.";
            return;
        }

        var targets = Bans.SelectedItems?.Cast<BanRow>().Where(b => b.SteamId64 != 0).ToList()
                      ?? new List<BanRow>();

        if (targets.Count == 0)
        {
            StatusText.Text = "Select a ban with a Steam ID first (IP-only bans cannot be lifted by UID).";
            return;
        }

        if (!await Prompt.ConfirmAsync(this, "Confirm unban",
                targets.Count == 1
                    ? $"Lift the ban on {targets[0].Name}?"
                    : $"Lift {targets.Count} bans?",
                "Unban"))
            return;

        foreach (var b in targets)
            await _session.Client.UnbanAsync(b.SteamId64);

        StatusText.Text = $"Unban sent for {targets.Count} entry(s).";
    }
}
