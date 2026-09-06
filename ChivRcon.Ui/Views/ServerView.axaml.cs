using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

public partial class ServerView : UserControl
{
    /// <summary>Combo entry. The first row is a placeholder with a null bookmark.</summary>
    private sealed record BookmarkEntry(string Label, ServerBookmark? Bookmark)
    {
        public override string ToString() => Label;
    }

    /// <summary>Combo entry for the game picker.</summary>
    private sealed record GameEntry(string Label, GameFlavor Flavor)
    {
        public override string ToString() => Label;
    }

    private static readonly GameEntry[] Games =
    {
        new("Medieval Warfare", GameFlavor.Chivalry),
        new("Deadliest Warrior", GameFlavor.DeadliestWarrior),
    };

    private readonly ObservableCollection<BookmarkEntry> _bookmarks = new();
    private Session _session = null!;
    private IShell _shell = null!;

    /// <summary>Set while we are writing GameBox ourselves, so the handler does not answer back.</summary>
    private bool _settingGameBox;

    public ServerView()
    {
        InitializeComponent();
        Bookmarks.ItemsSource = _bookmarks;
        GameBox.ItemsSource = Games;
        GameBox.SelectedIndex = 0;

        // Auto-detection can correct the choice mid-session, once a player on the server has
        // picked a class and PLAYER_INFO carries a family name. Follow it, so the picker
        // never disagrees with the labels the rest of the app is drawing.
        GameProfile.Changed += OnGameProfileChanged;

        // Wired here rather than in XAML so ReloadBookmarks can detach it while it rebuilds
        // the list. The previous version used a _loadingBookmark bool for that, which only
        // holds if every selection event is raised synchronously inside the guarded region --
        // and a ComboBox whose ItemsSource is being cleared and refilled is exactly where
        // that assumption is worth not making.
        Bookmarks.SelectionChanged += Bookmarks_SelectionChanged;
    }

    // The controls are named HostBox/PortBox/PasswordBox rather than Host/Port/Password
    // because the XAML generator emits a field per x:Name, which would collide with these.
    public string Host => (HostBox.Text ?? "").Trim();
    public int Port => (int)(PortBox.Value ?? 27960);
    public string Password => PasswordBox.Text ?? "";
    public bool AutoReconnect => AutoReconnectBox.IsChecked == true;
    public GameFlavor Game => (GameBox.SelectedItem as GameEntry)?.Flavor ?? GameFlavor.Chivalry;

    public void Bind(Session session, IShell shell)
    {
        _session = session;
        _shell = shell;
        ApplySettings();
        UpdateConnectionUi();
    }

    // ================= settings =================

    private void ApplySettings()
    {
        var s = _session.Settings;
        HostBox.Text = s.Host;
        PortBox.Value = Math.Clamp(s.Port, 1, 65535);
        SavePassword.IsChecked = s.SavePassword;
        if (s.SavePassword) PasswordBox.Text = s.Password;
        AutoReconnectBox.IsChecked = s.AutoReconnect;
        SetGameBox(GameFlavors.Parse(s.Game));
        _session.ShowKills = s.ShowKills;
        _session.LogToFile = s.LogToFile;
        ReloadBookmarks(s.LastBookmark);
        _session.AddressText = Host.Length == 0 ? "no server set" : $"{Host}:{Port}";
    }

    public void PersistSettings()
    {
        var s = _session.Settings;
        s.Host = Host;
        s.Port = Port;
        s.SavePassword = SavePassword.IsChecked == true;
        s.Password = s.SavePassword ? Password : "";
        s.LogToFile = _session.LogToFile;
        s.ShowKills = _session.ShowKills;
        s.AutoReconnect = AutoReconnect;
        s.Game = GameFlavors.Store(Game);
        s.LastBookmark = SelectedBookmark?.Name ?? "";
        _session.SaveSettings();
    }

    // ================= connection =================

    public void UpdateConnectionUi()
    {
        bool connected = _session.Client.State == RconState.Connected;

        ConnectBtn.Content = connected ? "Disconnect" : "Connect";
        StatusText.Text = _session.StatusText;
        StatusText.Foreground = _session.StatusBrush;

        HostBox.IsEnabled = PortBox.IsEnabled = PasswordBox.IsEnabled = !connected;
        DeleteBookmarkBtn.IsEnabled = SelectedBookmark is not null;

        _session.AddressText = Host.Length == 0 ? "no server set" : $"{Host}:{Port}";
    }

    /// <summary>
    /// The picker only names things -- it changes nothing that goes on the wire. The two
    /// games share one protocol; what differs is that team 1 is Mason in Medieval Warfare and
    /// Red in Deadliest Warrior, and class index 2 is Vanguard in one and Viking in the other.
    /// </summary>
    private void Game_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingGameBox) return;
        GameProfile.Current = Game;
    }

    private void SetGameBox(GameFlavor flavor)
    {
        _settingGameBox = true;
        try
        {
            GameBox.SelectedItem = Games.First(g => g.Flavor == flavor);
            GameProfile.Current = flavor;
        }
        finally { _settingGameBox = false; }
    }

    private void OnGameProfileChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnGameProfileChanged);
            return;
        }

        if (Game == GameProfile.Current) return;
        SetGameBox(GameProfile.Current);
    }

    private async void Connect_Click(object? sender, RoutedEventArgs e)
    {
        PersistSettings();

        // The bookmark's answer is the starting point; the server can still correct it.
        GameProfile.ResetDetection();
        GameProfile.Current = Game;

        ConnectBtn.IsEnabled = false;
        try { await _shell.ToggleConnectAsync(Host, Port, Password); }
        finally { ConnectBtn.IsEnabled = true; }
    }

    // ================= bookmarks =================

    private ServerBookmark? SelectedBookmark => (Bookmarks.SelectedItem as BookmarkEntry)?.Bookmark;

    private void ReloadBookmarks(string? selectName)
    {
        Bookmarks.SelectionChanged -= Bookmarks_SelectionChanged;
        try
        {
            _bookmarks.Clear();
            _bookmarks.Add(new BookmarkEntry("(unsaved)", null));
            foreach (var b in _session.Settings.Bookmarks.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                _bookmarks.Add(new BookmarkEntry(b.Name, b));

            int idx = 0;
            if (!string.IsNullOrEmpty(selectName))
            {
                for (int i = 1; i < _bookmarks.Count; i++)
                {
                    if (_bookmarks[i].Bookmark is { } sb
                        && sb.Name.Equals(selectName, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            Bookmarks.SelectedIndex = idx;
        }
        finally
        {
            Bookmarks.SelectionChanged += Bookmarks_SelectionChanged;
        }

        DeleteBookmarkBtn.IsEnabled = SelectedBookmark is not null;
    }

    private void Bookmarks_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var b = SelectedBookmark;
        DeleteBookmarkBtn.IsEnabled = b is not null;

        if (b is null)
        {
            SetBookmarkNote(null);
            return;                     // "(unsaved)" -- leave the fields alone
        }

        // The connection fields are disabled while a session is live, so overwriting them
        // would describe a server we are not talking to. Say so instead of doing nothing
        // quietly, which is indistinguishable from the feature being broken.
        if (_session.Client.State != RconState.Disconnected)
        {
            SetBookmarkNote($"Disconnect first to load \"{b.Name}\" — the connection fields are locked while a session is live.");
            return;
        }

        LoadBookmarkIntoFields(b);
    }

    private void LoadBookmarkIntoFields(ServerBookmark b)
    {
        HostBox.Text = b.Host;
        PortBox.Value = Math.Clamp(b.Port, 1, 65535);
        SavePassword.IsChecked = b.SavePassword;
        PasswordBox.Text = b.SavePassword ? b.Password : "";
        SetGameBox(GameFlavors.Parse(b.Game));

        SetBookmarkNote($"Loaded \"{b.Name}\" — {b.Host}:{b.Port}");
        UpdateConnectionUi();
    }

    private const string BookmarkHelpText =
        "Picking one fills in the connection below. Stored in %AppData%\\ChivRconUtility\\settings.json"
        + " — passwords are base64-encoded there, obfuscated rather than encrypted.";

    private void SetBookmarkNote(string? text) =>
        BookmarkNote.Text = string.IsNullOrEmpty(text) ? BookmarkHelpText : text;

    private async void SaveBookmark_Click(object? sender, RoutedEventArgs e)
    {
        // Default the name to the selected bookmark (update) or the host (new).
        string suggested = SelectedBookmark?.Name ?? Host;
        string? name = await Prompt.TextAsync(this, "Save bookmark", "Bookmark name:", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();

        var existing = _session.Settings.Bookmarks.FirstOrDefault(
            b => b.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var bm = existing ?? new ServerBookmark();
        bm.Name = name;
        bm.Host = Host;
        bm.Port = Port;
        bm.SavePassword = SavePassword.IsChecked == true;
        bm.Password = bm.SavePassword ? Password : "";
        bm.Game = GameFlavors.Store(Game);
        if (existing is null) _session.Settings.Bookmarks.Add(bm);

        _session.Settings.LastBookmark = name;
        _session.SaveSettings();
        ReloadBookmarks(name);
        _session.Append($"Bookmark saved: {name}", Palette.Command);
    }

    private async void DeleteBookmark_Click(object? sender, RoutedEventArgs e)
    {
        var b = SelectedBookmark;
        if (b is null) return;
        if (!await Prompt.ConfirmAsync(this, "Delete bookmark",
                $"Delete bookmark \"{b.Name}\"?", "Delete"))
            return;

        _session.Settings.Bookmarks.RemoveAll(
            x => x.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase));
        if (_session.Settings.LastBookmark.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
            _session.Settings.LastBookmark = "";
        _session.SaveSettings();
        ReloadBookmarks(null);
    }
}
