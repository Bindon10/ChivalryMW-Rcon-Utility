using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ChivRcon.Core;

namespace ChivRcon.App.Views;

/// <summary>
/// Live overview of the server. Positions arrive as a burst of PlayerPosEvent followed by
/// PlayerPosEndEvent, so the incoming set is accumulated and swapped in whole on the End —
/// drawing each one as it lands would flicker half-built frames.
/// </summary>
public partial class MapView : UserControl
{
    private readonly List<PlayerPosEvent> _incoming = new();
    private readonly DispatcherTimer _poll = new();
    private Session _session = null!;
    private bool _visible;

    public MapView()
    {
        InitializeComponent();
        _poll.Tick += async (_, _) => await PollAsync();

        NamesBox.IsCheckedChanged += (_, _) => { Canvas.ShowNames = NamesBox.IsChecked == true; Canvas.InvalidateVisual(); };
        DeadBox.IsCheckedChanged += (_, _) => { Canvas.ShowDead = DeadBox.IsChecked == true; Canvas.InvalidateVisual(); };
        LockBox.IsCheckedChanged += (_, _) =>
        {
            Canvas.StickyBounds = LockBox.IsChecked == true;
            Canvas.ResetBounds();
        };
        AutoBox.IsCheckedChanged += (_, _) => Retime();
        IntervalBox.ValueChanged += (_, _) => Retime();
    }

    public void Bind(Session session)
    {
        _session = session;
        _session.Client.EventReceived += OnEvent;
        UpdateAvailability();
    }

    /// <summary>Polling only runs while the page is on screen — it is a per-tick round trip.</summary>
    public void SetActive(bool active)
    {
        _visible = active;
        Retime();
        if (active) _ = PollAsync();
    }

    public void UpdateAvailability()
    {
        RefreshBtn.IsEnabled = _session.Client.State == RconState.Connected;
        Retime();
    }

    private void Retime()
    {
        _poll.Stop();
        if (!_visible || AutoBox.IsChecked != true) return;
        if (_session is null || _session.Client.State != RconState.Connected) return;

        _poll.Interval = TimeSpan.FromSeconds(Math.Max(1, (int)(IntervalBox.Value ?? 2)));
        _poll.Start();
    }

    private async void Refresh_Click(object? sender, RoutedEventArgs e) => await PollAsync();

    private void Recentre_Click(object? sender, RoutedEventArgs e) => Canvas.ResetBounds();

    private async Task PollAsync()
    {
        if (_session.Client.State != RconState.Connected)
        {
            StatusText.Text = "Not connected.";
            return;
        }
        _incoming.Clear();
        try { await _session.Client.RequestPlayerPositionsAsync(); }
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
            case PlayerPosEvent p:
                _incoming.Add(p);
                break;

            // New world coordinates; the old extent would squash the new map.
            case MapChangedEvent:
                Canvas.ResetBounds();
                break;

            case PlayerPosEndEvent end:
                Canvas.SetPlayers(_incoming.ToList());
                StatusText.Text = end.Count == 0
                    ? "Server reported no players."
                    : $"{end.Count} player(s) at {DateTime.Now:HH:mm:ss}";
                break;
        }
    }
}
