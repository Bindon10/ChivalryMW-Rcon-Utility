using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace ChivRcon.App.Views;

/// <summary>Reads back the JSONL audit files. Local only — nothing here talks to the server.</summary>
public partial class AuditView : UserControl
{
    private readonly ObservableCollection<AuditEntry> _rows = new();
    private List<AuditEntry> _loaded = new();

    private Session _session = null!;
    private bool _loadingMonths;

    public AuditView()
    {
        InitializeComponent();
        Entries.ItemsSource = _rows;
        FilterBox.TextChanged += (_, _) => ApplyFilter();
        SentBox.IsCheckedChanged += (_, _) => ApplyFilter();
        ServerBox.IsCheckedChanged += (_, _) => ApplyFilter();
    }

    public void Bind(Session session) => _session = session;

    /// <summary>Called when the page is shown; the file grows while other pages are open.</summary>
    public void Reload()
    {
        var months = AuditLog.Months();
        string? keep = MonthBox.SelectedItem as string;

        _loadingMonths = true;
        MonthBox.ItemsSource = months;
        MonthBox.SelectedItem = months.Contains(keep ?? "") ? keep : months.FirstOrDefault();
        _loadingMonths = false;

        LoadSelected();
    }

    private void LoadSelected()
    {
        if (MonthBox.SelectedItem is not string month)
        {
            _loaded = new List<AuditEntry>();
            StatusText.Text = "No audit files yet.";
            ApplyFilter();
            return;
        }

        _loaded = _session.Audit.Read(month).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string needle = (FilterBox.Text ?? "").Trim();
        bool sent = SentBox.IsChecked == true;
        bool server = ServerBox.IsChecked == true;

        _rows.Clear();
        foreach (var e in _loaded)
        {
            if (e.Kind == "sent" && !sent) continue;
            if (e.Kind == "audit" && !server) continue;
            if (needle.Length > 0
                && !e.Action.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !e.Detail.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !e.Admin.Contains(needle, StringComparison.OrdinalIgnoreCase))
                continue;
            _rows.Add(e);
        }

        StatusText.Text = _loaded.Count == 0
            ? "Nothing recorded for this month."
            : $"{_rows.Count} of {_loaded.Count} line(s)";
    }

    private void Month_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingMonths) return;
        LoadSelected();
    }

    private void Refresh_Click(object? sender, RoutedEventArgs e) => Reload();

    private void Folder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AuditLog.Directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AuditLog.Directory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine,
            _rows.Select(r => $"{r.TimeText}  {r.Kind,-5}  {r.Admin}  {r.Action}  {r.Detail}"));

        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip is null) return;
        try { await clip.SetTextAsync(text); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
