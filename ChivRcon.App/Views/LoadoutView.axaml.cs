using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>One weapon choice, carrying the server-side index the protocol wants.</summary>
public sealed record WeaponChoice(int Index, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One roster entry in the target dropdown. ToString keeps this off the
/// compiled-binding path.</summary>
public sealed record TargetChoice(ulong SteamId64, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Class, weapons and freeze for one player. The target is picked here; arriving
/// from the Dashboard preselects whoever was highlighted there.</summary>
public partial class LoadoutView : UserControl
{
    private readonly ObservableCollection<WeaponChoice> _primary = new();
    private readonly ObservableCollection<WeaponChoice> _secondary = new();
    private readonly ObservableCollection<WeaponChoice> _tertiary = new();

    private readonly ObservableCollection<TargetChoice> _targets = new();

    private Session _session = null!;
    private MainWindow _owner = null!;
    private Player? _target;

    // Suppresses the SelectionChanged raised while repopulating.
    private bool _syncingTargets;

    public LoadoutView()
    {
        InitializeComponent();
        PrimaryBox.ItemsSource = _primary;
        SecondaryBox.ItemsSource = _secondary;
        TertiaryBox.ItemsSource = _tertiary;
        TargetBox.ItemsSource = _targets;
    }

    public void Bind(Session session, MainWindow owner)
    {
        _session = session;
        _owner = owner;
        _session.Client.EventReceived += OnEvent;
        UpdateAvailability();
    }

    /// <summary>Rebuild the target dropdown, but only when the roster actually changed --
    /// refilling it closes an open dropdown.</summary>
    public void RefreshTargets()
    {
        var live = _session.Tracker.Players
            .OrderBy(p => p.TeamId)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new TargetChoice(p.SteamId64,
                $"{p.Name}  ·  {p.TeamName}{(p.IsBot ? "  ·  bot" : "")}"))
            .ToList();

        if (live.Count == _targets.Count && !live.Where((t, i) => t != _targets[i]).Any())
            return;

        _syncingTargets = true;
        try
        {
            _targets.Clear();
            foreach (var t in live) _targets.Add(t);
            TargetBox.SelectedItem = _targets.FirstOrDefault(t => t.SteamId64 == _target?.SteamId64);
        }
        finally { _syncingTargets = false; }
    }

    private void Target_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingTargets) return;
        if (TargetBox.SelectedItem is not TargetChoice choice) return;
        if (choice.SteamId64 == _target?.SteamId64) return;

        SetTarget(_session.Tracker.Find(choice.SteamId64));
    }

    /// <summary>Called when the page is shown, with whoever is selected on the Dashboard.</summary>
    public void SetTarget(Player? player)
    {
        _target = player;
        TargetText.Text = player is null
            ? "No player selected"
            : $"{player.TeamName}  ·  {player.IdentityText}";

        RefreshTargets();
        _syncingTargets = true;
        TargetBox.SelectedItem = _targets.FirstOrDefault(t => t.SteamId64 == player?.SteamId64);
        _syncingTargets = false;

        _primary.Clear();
        _secondary.Clear();
        _tertiary.Clear();
        LoadoutStatus.Text = "";

        UpdateAvailability();
        if (player is not null) _ = RequestOptionsAsync();
    }

    public void UpdateAvailability()
    {
        bool ready = _session.Client.State == RconState.Connected && _target is not null;
        foreach (var b in new[]
                 {
                     ArcherBtn, MaaBtn, VanguardBtn, KnightBtn,
                     ApplyLoadoutBtn, FreezeBtn, UnfreezeBtn, ReloadBtn,
                 })
            b.IsEnabled = ready;
    }

    private async Task RequestOptionsAsync()
    {
        if (_target is null || _session.Client.State != RconState.Connected) return;
        LoadoutStatus.Text = "Asking the server what this class can carry…";
        try { await _session.Client.RequestLoadoutAsync(_target.SteamId64); }
        catch (Exception ex) { LoadoutStatus.Text = ex.Message; }
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
            // Ignore replies about anyone other than the current target -- another admin on
            // the same connection could have asked about someone else.
            case LoadoutOptionEvent o when o.SteamId64 == _target?.SteamId64:
                var choice = new WeaponChoice(o.Index, o.Weapon);
                if (o.Slot == 0) _primary.Add(choice);
                else if (o.Slot == 1) _secondary.Add(choice);
                else _tertiary.Add(choice);
                break;

            case LoadoutEndEvent e when e.SteamId64 == _target?.SteamId64:
                LoadoutStatus.Text =
                    $"{e.PrimaryCount} primary · {e.SecondaryCount} secondary · {e.TertiaryCount} tertiary";
                break;
        }
    }

    // ---------------- handlers ----------------

    private async void Reload_Click(object? sender, RoutedEventArgs e)
    {
        _primary.Clear();
        _secondary.Clear();
        _tertiary.Clear();
        await RequestOptionsAsync();
    }

    private async void Class_Click(object? sender, RoutedEventArgs e)
    {
        if (_target is null || sender is not Button b || b.Tag is not string tag) return;
        if (!int.TryParse(tag, out int classIndex)) return;

        bool immediate = ImmediateBox.IsChecked == true;
        await Guarded(() => _session.Client.SetClassAsync(_target.SteamId64, classIndex, immediate),
            $"{_target.Name} -> {b.Content}{(immediate ? " (now)" : " (next spawn)")}");

        // The weapon lists belong to the old class until the server has moved them.
        _primary.Clear();
        _secondary.Clear();
        _tertiary.Clear();
        LoadoutStatus.Text = "Class changed — reload options to see its weapons.";
    }

    private async void ApplyLoadout_Click(object? sender, RoutedEventArgs e)
    {
        if (_target is null) return;

        // -1 means "leave this slot alone", which is also what an unset combo should mean.
        int p = (PrimaryBox.SelectedItem as WeaponChoice)?.Index ?? -1;
        int s = (SecondaryBox.SelectedItem as WeaponChoice)?.Index ?? -1;
        int t = (TertiaryBox.SelectedItem as WeaponChoice)?.Index ?? -1;

        if (p < 0 && s < 0 && t < 0)
        {
            LoadoutStatus.Text = "Nothing selected.";
            return;
        }

        await Guarded(() => _session.Client.SetLoadoutAsync(_target.SteamId64, p, s, t),
            $"{_target.Name} loadout -> {Describe(PrimaryBox)} / {Describe(SecondaryBox)} / {Describe(TertiaryBox)}");
    }

    private static string Describe(ComboBox box) =>
        (box.SelectedItem as WeaponChoice)?.Name ?? "(unchanged)";

    private async void Freeze_Click(object? sender, RoutedEventArgs e)
    {
        if (_target is null) return;
        await Guarded(() => _session.Client.SetFrozenAsync(_target.SteamId64, true),
            $"Freeze {_target.Name}");
    }

    private async void Unfreeze_Click(object? sender, RoutedEventArgs e)
    {
        if (_target is null) return;
        await Guarded(() => _session.Client.SetFrozenAsync(_target.SteamId64, false),
            $"Release {_target.Name}");
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
            _session.Audit.RecordSent("LOADOUT", description);
        }
        catch (Exception ex)
        {
            _session.Append($"Command failed: {ex.Message}", Palette.Danger);
            _session.Audit.RecordSent("LOADOUT_FAILED", $"{description} -- {ex.Message}");
        }
    }
}
