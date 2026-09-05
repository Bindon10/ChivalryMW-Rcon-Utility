using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App;

/// <summary>One line in the event feed.</summary>
public sealed record LogLine(string Text, IBrush Brush);

/// <summary>A destination in the sidebar. Mutable so the unread dot can be toggled.</summary>
public sealed class NavItem : INotifyPropertyChanged
{
    private bool _hasBadge;

    public NavItem(string title) => Title = title;

    public string Title { get; }

    /// <summary>
    /// Unread marker. The event feed lives on its own page, so this is what says
    /// "the server said something while you were looking at the roster".
    /// </summary>
    public bool HasBadge
    {
        get => _hasBadge;
        set
        {
            if (_hasBadge == value) return;
            _hasBadge = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasBadge)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Everything the four pages share: the connection, the roster, the feed and the settings.
/// Views get a reference to this rather than talking to each other.
/// </summary>
public sealed class Session : IDisposable, INotifyPropertyChanged
{
    private const int MaxLogLines = 3000;

    public RconClient Client { get; } = new();
    public PlayerTracker Tracker { get; } = new();
    public PlayerNotes Notes { get; } = new();
    public AuditLog Audit { get; } = new();
    public AppSettings Settings { get; private set; } = SettingsStore.Load();
    public ObservableCollection<LogLine> Log { get; } = new();

    public bool ShowKills { get; set; } = true;
    public bool LogToFile { get; set; } = true;

    private StreamWriter? _logWriter;
    private string _statusText = "Disconnected";
    private IBrush _statusBrush = Palette.Danger;
    private string _addressText = "no server set";
    private string _mapText = "";

    /// <summary>Raised after a line is appended, for autoscroll and the unread dot.</summary>
    public event Action? LogAppended;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }
    public string AddressText { get => _addressText; set => Set(ref _addressText, value); }
    public string MapText { get => _mapText; set => Set(ref _mapText, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Recomputes the sidebar status line from the client's current state.</summary>
    public void RefreshStatus()
    {
        StatusText = Client.State switch
        {
            RconState.Connected => "Connected",
            RconState.Authenticating => "Authenticating…",
            RconState.Connecting => "Connecting…",
            _ => "Disconnected",
        };
        StatusBrush = Client.State switch
        {
            RconState.Connected => Palette.Success,
            RconState.Disconnected => Palette.Danger,
            _ => Palette.Warning,
        };
        if (Client.State == RconState.Disconnected) MapText = "";
    }

    public void Append(string text, IBrush brush)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {text}";

        void Add()
        {
            Log.Add(new LogLine(line, brush));
            // Trim from the front rather than letting the panel grow without bound; each line
            // is a realised TextBlock, so an unbounded feed is a real memory cost here in a
            // way it was not in a RichTextBox.
            while (Log.Count > MaxLogLines) Log.RemoveAt(0);
            LogAppended?.Invoke();
        }

        if (Dispatcher.UIThread.CheckAccess()) Add();
        else Dispatcher.UIThread.Post(Add);

        _logWriter?.WriteLine(line);
    }

    public void OpenLogFile()
    {
        _logWriter?.Dispose();
        _logWriter = null;
        if (!LogToFile) return;
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ChivRconUtility", "logs");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"chivrcon-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _logWriter = new StreamWriter(file, append: false) { AutoFlush = true };
        }
        catch { /* logging must never break the app */ }
    }

    public void SaveSettings() => SettingsStore.Save(Settings);

    public void Dispose()
    {
        Client.Dispose();
        Audit.Dispose();
        Notes.Save();
        _logWriter?.Dispose();
    }
}

/// <summary>
/// The palette as brushes, for the places that pick a colour in code rather than in XAML
/// (feed lines, status text). Values must match Theme/Palette.axaml.
/// </summary>
public static class Palette
{
    public static readonly IBrush TextPrimary = Brush.Parse("#E6E8EB");
    public static readonly IBrush TextSecondary = Brush.Parse("#9AA4B2");
    public static readonly IBrush TextFaint = Brush.Parse("#6B7482");
    public static readonly IBrush Accent = Brush.Parse("#4DA3FF");
    public static readonly IBrush Success = Brush.Parse("#4CC964");
    public static readonly IBrush Warning = Brush.Parse("#D1A04A");
    public static readonly IBrush Danger = Brush.Parse("#D14A4A");

    // Feed-only tones. Deliberately outside the palette: these are severity colours for a
    // log, not part of the launcher's shared visual language.
    public static readonly IBrush Chat = Brush.Parse("#E6E8EB");
    public static readonly IBrush Join = Brush.Parse("#7FBFEA");
    public static readonly IBrush Kill = Brush.Parse("#767E8B");
    public static readonly IBrush Command = Brush.Parse("#C9B06A");
    public static readonly IBrush Highlight = Brush.Parse("#B48AD8");
}
