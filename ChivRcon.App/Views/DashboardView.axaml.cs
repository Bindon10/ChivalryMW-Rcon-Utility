using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;   // IClipboard.SetTextAsync is an extension method here
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

public partial class DashboardView : UserControl
{
    private readonly ObservableCollection<Player> _rows = new();
    private Session _session = null!;
    private MainWindow _owner = null!;

    public DashboardView()
    {
        InitializeComponent();
        Players.ItemsSource = _rows;

        // Avalonia's ListBox does not reliably select on right-click the way a WinForms
        // ListView did, and every item in the context menu acts on the selection. Tunnelling
        // means this runs before the menu opens.
        Players.AddHandler(InputElement.PointerPressedEvent, Players_PointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void Players_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Players).Properties.IsRightButtonPressed) return;

        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item?.DataContext is Player p) Players.SelectedItem = p;
    }

    public void Bind(Session session, MainWindow owner)
    {
        _session = session;
        _owner = owner;
        Players.ContextMenu = BuildPlayerMenu();
    }

    private Player? Selected => Players.SelectedItem as Player;

    /// <summary>The roster selection, for pages that act on "the player you were looking at".</summary>
    public Player? SelectedPlayer => Selected;

    /// <summary>
    /// Reconciles the roster in place: only genuinely new players are inserted, departed
    /// ones removed, and rows moved when the sort order actually changes. Everything else
    /// updates through Player's PropertyChanged.
    ///
    /// It deliberately does NOT clear and refill. Doing that replaced every ListBoxItem on
    /// every tick, which destroyed the focused row underneath an open context menu and made
    /// the menu light-dismiss itself about a second after it opened.
    /// </summary>
    public void RefreshRoster()
    {
        // A refresh while the menu is up can still pull a row out from under it (a player
        // disconnecting, say). The caller leaves the dirty flag set, so this catches up as
        // soon as the menu closes.
        if (Players.ContextMenu?.IsOpen == true) return;

        var desired = _session.Tracker.Players
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!desired.Contains(_rows[i]))
                _rows.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            // Refreshed here rather than on connect: a note can be written while the player
            // is on the server, and this is the tick that would otherwise show it stale.
            desired[i].NoteBadge = _session.Notes.BadgeFor(desired[i].SteamId64);

            int at = _rows.IndexOf(desired[i]);
            if (at < 0) _rows.Insert(i, desired[i]);
            else if (at != i) _rows.Move(at, i);
        }

        RosterHeader.Text = $"PLAYERS ({_rows.Count})";
    }

    /// <summary>True while the roster's context menu is open, so the tick can hold off.</summary>
    public bool MenuIsOpen => Players.ContextMenu?.IsOpen == true;

    private ContextMenu BuildPlayerMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Func<Player, Task> action, bool extension = false)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) =>
            {
                var p = Selected;
                if (p is null) return;
                if (extension && _session.Client.State != RconState.Connected)
                {
                    await Prompt.NoticeAsync(_owner, "Chivalry RCON", "Not connected.");
                    return;
                }
                await Guarded(() => action(p), $"{header} -> {p.Name}");
            };
            menu.Items.Add(item);
            return item;
        }

        Item("Say to player…", async p =>
        {
            var msg = await Prompt.TextAsync(_owner, "Say to player", $"Message to {p.Name}:");
            if (!string.IsNullOrWhiteSpace(msg)) await _session.Client.SayToAsync(p.SteamId64, msg);
        });
        Item("Kick…", async p =>
        {
            var reason = await Prompt.TextAsync(_owner, "Kick", $"Reason for kicking {p.Name}:", "Kicked by admin");
            if (reason is not null) await _session.Client.KickAsync(p.SteamId64, reason);
        });
        Item("Temp ban…", async p =>
        {
            var reason = await Prompt.TextAsync(_owner, "Temp ban", $"Reason for temp-banning {p.Name}:", "Temp ban");
            if (reason is null) return;
            var mins = await Prompt.TextAsync(_owner, "Temp ban", "Duration in minutes:", "60");
            if (mins is null || !int.TryParse(mins, out int m) || m <= 0) return;
            await _session.Client.TempBanAsync(p.SteamId64, reason, m * 60);
        });
        Item("Ban…", async p =>
        {
            var reason = await Prompt.TextAsync(_owner, "Ban", $"Reason for banning {p.Name}:", "Banned by admin");
            if (reason is null) return;
            if (await Prompt.ConfirmAsync(_owner, "Confirm ban",
                    $"Permanently ban {p.Name} ({p.Steam3})?", "Ban"))
                await _session.Client.BanAsync(p.SteamId64, reason);
        });

        menu.Items.Add(new Separator());
        // Local bookkeeping -- no server involved, so these work while disconnected too.
        Item("Notes…", async p =>
            await NotesDialog.ShowAsync(_owner, _session.Notes, p.SteamId64, p.Name));
        Item("Add warning…", async p =>
        {
            // Opens the notes dialog with the counter already incremented, so a warning is
            // never recorded without the chance to write down what it was for.
            if (await NotesDialog.ShowAsync(_owner, _session.Notes, p.SteamId64, p.Name, addWarnings: 1))
            {
                var note = _session.Notes.Find(p.SteamId64);
                _session.Audit.RecordSent("WARNING",
                    $"{p.Name} ({p.Steam3}) now at {note?.Warnings ?? 0} warning(s)");
                _session.Append($"> Warning recorded for {p.Name} (now {note?.Warnings ?? 0})",
                    Palette.Command);
            }
        });

        menu.Items.Add(new Separator());
        Item("Copy SteamID64", async p => await CopyAsync(p.SteamId64.ToString()));
        Item("Copy Steam3 ID", async p => await CopyAsync(p.Steam3));

        menu.Items.Add(new Separator());
        // Opcodes 24-26 originated in ChivAdmin's mutator, but BangModRCon implements them, so
        // BangMod is the requirement as far as this server is concerned. Labelled accordingly --
        // the provenance is on the Reference page, where it is useful, rather than in a menu.
        Item("Kill (BangMod)", async p =>
        {
            if (await Prompt.ConfirmAsync(_owner, "Confirm kill",
                    $"Kill {p.Name} where they stand?", "Kill"))
                await _session.Client.KillPlayerAsync(p.SteamId64);
        }, extension: true);
        Item("Set score… (BangMod)", async p =>
        {
            var v = await Prompt.TextAsync(_owner, "Set score", $"New score for {p.Name}:", p.Score.ToString());
            if (v is not null && int.TryParse(v, out int score))
                await _session.Client.ChangeScoreAsync(p.SteamId64, score);
        }, extension: true);
        Item("Slap (BangMod)", p => _session.Client.SlapAsync(p.SteamId64), extension: true);
        Item("Send to player… (BangMod)", async p =>
        {
            // Teleport is the one action needing two players, so the second is picked from
            // a list rather than typed. Everyone except the player being moved.
            var others = _session.Tracker.Players.Where(o => o.SteamId64 != p.SteamId64);
            var dest = await PickPlayerDialog.ShowAsync(_owner, "Send to player",
                $"Move {p.Name} to:", others);
            if (dest is not null)
                await _session.Client.TeleportAsync(p.SteamId64, dest.SteamId64);
        }, extension: true);
        Item("Inebriate (BangMod)", p => _session.Client.InebriateAsync(p.SteamId64), extension: true);
        Item("Sober up (BangMod)", p => _session.Client.SoberPlayerAsync(p.SteamId64), extension: true);

        menu.Items.Add(new Separator());
        // BangMod-only below. The server ignores opcodes it does not know, so these are safe
        // to offer against a vanilla server -- they simply do nothing there.
        Item("Text mute (BangMod)", p => _session.Client.MutePlayerAsync(p.SteamId64, true), extension: true);
        Item("Text unmute (BangMod)", p => _session.Client.MutePlayerAsync(p.SteamId64, false), extension: true);
        Item("Force spectate (BangMod)", p => _session.Client.ForceSpectateAsync(p.SteamId64), extension: true);
        // One entry per team beats asking an admin to remember that Mason is 1.
        Item("Move to Agatha (BangMod)", p => _session.Client.SetTeamAsync(p.SteamId64, 0), extension: true);
        Item("Move to Mason (BangMod)", p => _session.Client.SetTeamAsync(p.SteamId64, 1), extension: true);
        Item("Console command on player… (BangMod)", async p =>
        {
            var cmd = await Prompt.TextAsync(_owner, "Console command",
                $"Run against {p.Name}'s server-side controller:");
            if (!string.IsNullOrWhiteSpace(cmd))
                await _session.Client.ConsoleCommandAsync(p.SteamId64, ConsoleCommandScope.Player, cmd);
        }, extension: true);

        return menu;
    }

    private async Task CopyAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
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
