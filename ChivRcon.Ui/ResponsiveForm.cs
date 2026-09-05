using Avalonia.Controls;

namespace ChivRcon.App;

/// <summary>
/// Label / input / buttons sit on one row when there is room and stack when there is not.
/// A Grid cannot do that on its own: below a certain width the star column collapses past
/// the input's own minimum and the input bleeds across its neighbours.
/// </summary>
public static class ResponsiveForm
{
    /// <summary>Below this a form row stacks instead of sitting on one line.</summary>
    public const double NarrowWidth = 620;

    public static void Place(Control c, int row, int col, int colSpan = 1)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        Grid.SetColumnSpan(c, colSpan);
    }

    /// <summary>
    /// Lays rows out as (label, input, buttons) triples: side by side when wide, each on its
    /// own line when narrow.
    /// </summary>
    public static void Apply(Grid grid, bool narrow, string wideColumns, params Control[][] rows)
    {
        if (narrow)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions(
                string.Join(",", Enumerable.Repeat("Auto", rows.Sum(r => r.Length))));

            int row = 0;
            foreach (var group in rows)
                foreach (var c in group)
                    Place(c, row++, 0);
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitions(wideColumns);
            grid.RowDefinitions = new RowDefinitions(
                string.Join(",", Enumerable.Repeat("Auto", rows.Length)));

            for (int r = 0; r < rows.Length; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    Place(rows[r][c], r, c);
        }
    }
}
