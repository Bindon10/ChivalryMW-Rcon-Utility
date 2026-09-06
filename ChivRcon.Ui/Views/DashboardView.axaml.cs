using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;   // IClipboard.SetTextAsync is an extension method here
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

public partial class DashboardView : UserControl
{
    private readonly ObservableCollection<Player> _rows = new();
    private Session _session = null!;
    private IShell _shell = null!;

    /// <summary>
    /// "Move to &lt;team&gt;" rows. Six, because Deadliest Warrior runs anywhere from one to six
    /// teams (FFA and Duel force 1, the tutorial 6, everything else defaults to 2 and takes
    /// ?NumTeams= clamped 2-6). Created once and shown/hidden per server rather than rebuilt:
    /// rebuilding the menu while it is open is what used to close it.
    /// </summary>
    private const int MaxTeams = 6;
    private readonly MenuItem?[] _moveToTeam = new MenuItem?[MaxTeams];

    /// <summary>Team index each row currently targets. -1 = row is hidden.</summary>
    private readonly int[] _moveToTeamIndex = Enumerable.Repeat(-1, MaxTeams).ToArray();

    /// <summary>
    /// The Deadliest Warrior alternative to the rows above: one entry that opens a colour
    /// picker. Six near-identical rows in a context menu is the wrong shape when the teams
    /// are told apart by colour and a restricted team is renamed after its class.
    /// </summary>
    private MenuItem? _changeTeam;

    public DashboardView()
    {
        InitializeComponent();

        // Avalonia opens a ContextMenu on long press when there is no mouse.
        RosterHint.Text = $"Live roster. {Gestures.SecondaryCapitalised} a player for admin actions.";
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

    public void Bind(Session session, IShell shell)
    {
        _session = session;
        _shell = shell;
        Players.ContextMenu = BuildPlayerMenu();
        ApplyGameLabels();
        GameProfile.Changed += OnGameProfileChanged;
    }

    /// <summary>
    /// Point the team rows at the teams that actually exist. The menu is built once, so these
    /// are relabelled in place rather than rebuilt -- rebuilding it while it is open is how the
    /// menu used to close itself.
    ///
    /// The server's list wins when we have one: it knows the map's real team count and names,
    /// which is the only way to be right about a six-team Deadliest Warrior mode, or to stop
    /// offering "move to red" on an FFA map that has exactly one team. Without a list (a server
    /// too old to send one) fall back to the usual two.
    /// </summary>
    private void ApplyGameLabels()
    {
        var teams = GameProfile.ServerTeams;

        // Deadliest Warrior gets the picker instead of a stack of rows: its teams are read off
        // the scoreboard by colour, and a team restricted to one class is renamed after that
        // class, so the row labels alone do not say which team is which. Needs the server's
        // list to have arrived — without it there is nothing to put in the dialog, so fall
        // through to rows.
        if (GameProfile.Current == GameFlavor.DeadliestWarrior && teams.Count > 0)
        {
            if (_changeTeam is not null) _changeTeam.IsVisible = true;
            for (int slot = 0; slot < MaxTeams; slot++) HideTeamRow(slot);
            return;
        }

        if (_changeTeam is not null) _changeTeam.IsVisible = false;

        if (teams.Count == 0)
        {
            var (first, second) = GameProfile.TeamPair();
            SetTeamRow(0, 0, first);
            SetTeamRow(1, 1, second);
            for (int slot = 2; slot < MaxTeams; slot++) HideTeamRow(slot);
            return;
        }

        int used = 0;
        foreach (var t in teams)
        {
            if (used >= MaxTeams) break;
            SetTeamRow(used, t.Index, string.IsNullOrWhiteSpace(t.Name) ? $"team {t.Index}" : t.Name);
            used++;
        }
        for (int slot = used; slot < MaxTeams; slot++) HideTeamRow(slot);
    }

    private void SetTeamRow(int slot, int teamIndex, string name)
    {
        if (_moveToTeam[slot] is not { } item) return;
        _moveToTeamIndex[slot] = teamIndex;
        item.Header = $"Move to {name}{ExtensionTag}";
        item.IsVisible = true;
    }

    private void HideTeamRow(int slot)
    {
        _moveToTeamIndex[slot] = -1;
        if (_moveToTeam[slot] is { } item) item.IsVisible = false;
    }

    private void OnGameProfileChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnGameProfileChanged);
            return;
        }

        // Relabelling while the menu is open swaps the text under the cursor. It will be
        // right the next time it opens.
        if (Players.ContextMenu?.IsOpen == true) return;
        ApplyGameLabels();
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

    /// <summary>
    /// Marks menu entries that need the extended opcode set. AdminMod is XangMod's RCon
    /// lifted out verbatim, so anything tagged here works against either.
    /// </summary>
    // The three mods share one RCon source, so an opcode is in all of them or in none.
    // AdminModDW is the Deadliest Warrior build of the same code.
    private const string ExtensionTag = " (XangMod / AdminMod / AdminModDW)";

    private ContextMenu BuildPlayerMenu()
    {
        var menu = new ContextMenu();

        MenuItem Item(string header, Func<Player, Task> action, bool extension = false)
        {
            // Appended here rather than typed into every label, so which servers implement
            // these opcodes is stated once. The audit line below keeps the untagged header.
            var item = new MenuItem { Header = extension ? header + ExtensionTag : header };
            item.Click += async (_, _) =>
            {
                var p = Selected;
                if (p is null) return;
                if (extension && _session.Client.State != RconState.Connected)
                {
                    await Prompt.NoticeAsync(this, "Chivalry RCON", "Not connected.");
                    return;
                }
                await Guarded(() => action(p), $"{header} -> {p.Name}");
            };
            menu.Items.Add(item);
            return item;
        }

        Item("Say to player…", async p =>
        {
            var msg = await Prompt.TextAsync(this, "Say to player", $"Message to {p.Name}:");
            if (!string.IsNullOrWhiteSpace(msg)) await _session.Client.SayToAsync(p.SteamId64, msg);
        });
        Item("Kick…", async p =>
        {
            var reason = await Prompt.TextAsync(this, "Kick", $"Reason for kicking {p.Name}:", "Kicked by admin");
            if (reason is not null) await _session.Client.KickAsync(p.SteamId64, reason);
        });
        Item("Temp ban…", async p =>
        {
            var reason = await Prompt.TextAsync(this, "Temp ban", $"Reason for temp-banning {p.Name}:", "Temp ban");
            if (reason is null) return;
            var mins = await Prompt.TextAsync(this, "Temp ban", "Duration in minutes:", "60");
            if (mins is null || !int.TryParse(mins, out int m) || m <= 0) return;
            await _session.Client.TempBanAsync(p.SteamId64, reason, m * 60);
        });
        Item("Ban…", async p =>
        {
            var reason = await Prompt.TextAsync(this, "Ban", $"Reason for banning {p.Name}:", "Banned by admin");
            if (reason is null) return;
            if (await Prompt.ConfirmAsync(this, "Confirm ban",
                    $"Permanently ban {p.Name} ({p.Steam3})?", "Ban"))
                await _session.Client.BanAsync(p.SteamId64, reason);
        });

        menu.Items.Add(new Separator());
        // Local bookkeeping -- no server involved, so these work while disconnected too.
        Item("Notes…", async p =>
            await NotesDialog.ShowAsync(this, _session.Notes, p.SteamId64, p.Name));
        Item("Add warning…", async p =>
        {
            // Opens the notes dialog with the counter already incremented, so a warning is
            // never recorded without the chance to write down what it was for.
            if (await NotesDialog.ShowAsync(this, _session.Notes, p.SteamId64, p.Name, addWarnings: 1))
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
        // Opcodes 24-26 originated in ChivAdmin's mutator, but XangModRCon and AdminModRCon
        // implement them, so that is the requirement as far as this server is concerned.
        // Provenance is on the Reference page, where it is useful, rather than in a menu.
        Item("Kill", async p =>
        {
            if (await Prompt.ConfirmAsync(this, "Confirm kill",
                    $"Kill {p.Name} where they stand?", "Kill"))
                await _session.Client.KillPlayerAsync(p.SteamId64);
        }, extension: true);
        Item("Set score…", async p =>
        {
            var v = await Prompt.TextAsync(this, "Set score", $"New score for {p.Name}:", p.Score.ToString());
            if (v is not null && int.TryParse(v, out int score))
                await _session.Client.ChangeScoreAsync(p.SteamId64, score);
        }, extension: true);
        Item("Slap", p => _session.Client.SlapAsync(p.SteamId64), extension: true);
        Item("Send to player…", async p =>
        {
            // Teleport is the one action needing two players, so the second is picked from
            // a list rather than typed. Everyone except the player being moved.
            var others = _session.Tracker.Players.Where(o => o.SteamId64 != p.SteamId64);
            var dest = await PickPlayerDialog.ShowAsync(this, "Send to player",
                $"Move {p.Name} to:", others);
            if (dest is not null)
                await _session.Client.TeleportAsync(p.SteamId64, dest.SteamId64);
        }, extension: true);
        Item("Inebriate", p => _session.Client.InebriateAsync(p.SteamId64), extension: true);
        Item("Sober up", p => _session.Client.SoberPlayerAsync(p.SteamId64), extension: true);

        menu.Items.Add(new Separator());
        // Extended opcodes below. The server ignores opcodes it does not know, so these are
        // safe to offer against a vanilla server -- they simply do nothing there.
        Item("Text mute", p => _session.Client.MutePlayerAsync(p.SteamId64, true), extension: true);
        Item("Text unmute", p => _session.Client.MutePlayerAsync(p.SteamId64, false), extension: true);
        Item("Force spectate", p => _session.Client.ForceSpectateAsync(p.SteamId64), extension: true);
        // One entry per team beats asking an admin to remember that Mason is 1. Names and
        // visibility come from the server's own team list in ApplyGameLabels -- the number of
        // teams is a per-map, per-command-line fact, not something to hardcode.
        for (int slot = 0; slot < MaxTeams; slot++)
        {
            int captured = slot;
            _moveToTeam[slot] = Item("Move to team " + slot, p =>
            {
                int team = _moveToTeamIndex[captured];
                return team < 0 ? Task.CompletedTask : _session.Client.SetTeamAsync(p.SteamId64, team);
            }, extension: true);
            _moveToTeam[slot]!.IsVisible = false;
        }

        _changeTeam = Item("Change team…", async p =>
        {
            var teams = GameProfile.ServerTeams;
            if (teams.Count == 0) return;

            var pick = await PickTeamDialog.ShowAsync(this, "Change team",
                $"Move {p.Name} to:", teams);
            if (pick is not null)
                await _session.Client.SetTeamAsync(p.SteamId64, pick.Index);
        }, extension: true);
        _changeTeam.IsVisible = false;
        Item("Console command on player…", async p =>
        {
            var cmd = await Prompt.TextAsync(this, "Console command",
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
            await Prompt.NoticeAsync(this, "Chivalry RCON", "Not connected.");
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
