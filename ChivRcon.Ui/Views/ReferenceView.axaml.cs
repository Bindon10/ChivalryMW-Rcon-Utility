using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ChivRcon.App.Views;

public partial class ReferenceView : UserControl
{
    private readonly ObservableCollection<CommandRef> _rows = new();

    public ReferenceView()
    {
        InitializeComponent();
        Entries.ItemsSource = _rows;
        ApplyFilter();
    }

    private void Filter_KeyUp(object? sender, KeyEventArgs e) => ApplyFilter();

    private void ClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        Filter.Text = "";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = (Filter.Text ?? "").Trim();

        _rows.Clear();
        foreach (var c in CommandReference.All)
        {
            if (q.Length > 0
                && !c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !c.Where.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !c.Needs.Contains(q, StringComparison.OrdinalIgnoreCase)
                && !c.Detail.Contains(q, StringComparison.OrdinalIgnoreCase))
                continue;

            _rows.Add(c);
        }

        CountText.Text = q.Length == 0
            ? $"{_rows.Count} commands"
            : $"{_rows.Count} of {CommandReference.All.Count} match \"{q}\"";
    }
}
