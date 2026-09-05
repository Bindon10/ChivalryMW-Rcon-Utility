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
/// </summary>
public partial class BansView : UserControl
{
    private readonly ObservableCollection<BanRow> _rows = new();
    private readonly List<BanInfoEvent> _pending = new();
    private Session _session = null!;
    private IShell _shell = null!;

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
        UpdateAvailability();
    }

    /// <summary>Called when the page is navigated to, so the list is never stale on arrival.</summary>
    public void RefreshIfConnected()
    {
        UpdateAvailability();
        if (_session.Client.State == RconState.Connected) _ = RefreshAsync();
        else StatusText.Text = "Not connected.";
    }

    public void UpdateAvailability()
    {
        bool connected = _session.Client.State == RconState.Connected;
        RefreshBtn.IsEnabled = UnbanBtn.IsEnabled = connected;
        if (!connected) _rows.Clear();
    }

    private async Task RefreshAsync()
    {
        if (_session.Client.State != RconState.Connected)
        {
            StatusText.Text = "Not connected.";
            return;
        }

        _pending.Clear();
        StatusText.Text = "Requesting…";
        try { await _session.Client.RequestBanListAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
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
                Render();
                StatusText.Text = end.Count == 0 ? "No bans on this server." : $"{end.Count} ban(s).";
                break;

            // The server audits every unban, including ones that matched nothing, so the
            // outcome is visible rather than silent like vanilla's opcode 20.
            case AdminAuditEvent a when a.Action is "UNBAN" or "UNBAN_FAILED":
                StatusText.Text = $"{a.Action}: {a.Detail}";
                if (a.Action == "UNBAN") _ = RefreshAsync();
                break;
        }
    }

    private void Render()
    {
        _rows.Clear();
        foreach (var b in _pending.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, b.DurationSeconds));
            string duration = b.DurationSeconds <= 0
                ? "permanent"
                : span.TotalDays >= 1
                    ? $"{(int)span.TotalDays}d {span.Hours}h"
                    : $"{(int)span.TotalHours}h {span.Minutes}m";

            _rows.Add(new BanRow(
                b.SteamId64,
                string.IsNullOrWhiteSpace(b.Name) ? "(unnamed)" : b.Name,
                SteamId.ToSteam3(b.SteamId64),
                duration,
                b.IpPolicy,
                b.Reason));
        }

        BansHeader.Text = $"SERVER BAN LIST ({_rows.Count})";
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await RefreshAsync();

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
