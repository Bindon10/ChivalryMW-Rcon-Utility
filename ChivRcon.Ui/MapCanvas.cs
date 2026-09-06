using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ChivRcon.Core;

namespace ChivRcon.App;

/// <summary>
/// Top-down plot of where everyone is.
///
/// There is no map metadata to scale against — the server knows world coordinates and
/// nothing ships the playable bounds — so the view auto-fits to the bounding box of the
/// players themselves. That means the scale drifts as people spread out or bunch up, which
/// is the honest trade: it works on every map including custom ones, with no per-map data
/// to maintain, at the cost of not being a to-scale minimap.
///
/// Drawn with a custom Render rather than shapes-in-a-Canvas because the whole thing is
/// replaced on every poll; allocating a control per player twice a second would be worse.
/// </summary>
public sealed class MapCanvas : Control
{
    private IReadOnlyList<PlayerPosEvent> _players = Array.Empty<PlayerPosEvent>();

    // Named by index rather than by faction: team 0 is Agatha in Medieval Warfare and Blue
    // in Deadliest Warrior, team 1 is Mason and Red. The colours suit both.
    private static readonly IBrush Team0 = Brush.Parse("#5B8FD6");
    private static readonly IBrush Team1 = Brush.Parse("#C4564E");
    private static readonly IBrush Neutral = Brush.Parse("#9AA4B2");
    private static readonly IBrush DeadBrush = Brush.Parse("#4A5160");
    private static readonly IPen GridPen = new Pen(Brush.Parse("#2A2E37"), 1);

    /// <summary>Draw dead players in place, greyed, rather than dropping them.</summary>
    public bool ShowDead { get; set; } = true;

    public bool ShowNames { get; set; } = true;

    /// <summary>Fit to everywhere players have been, not to where they are now. Fitting the
    /// instantaneous box recentres a lone player every frame, so they never appear to
    /// move.</summary>
    public bool StickyBounds { get; set; } = true;

    private int _accMinX, _accMaxX, _accMinY, _accMaxY;
    private bool _hasAcc;

    /// <summary>Forget the accumulated extent. Call on map change.</summary>
    public void ResetBounds()
    {
        _hasAcc = false;
        InvalidateVisual();
    }

    public void SetPlayers(IReadOnlyList<PlayerPosEvent> players)
    {
        _players = players;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var full = new Rect(Bounds.Size);
        ctx.FillRectangle(Brush.Parse("#171A20"), full);

        // Grid first, so it sits under everything and gives some sense of movement between
        // polls even when the auto-fit rescales.
        const int Divisions = 8;
        for (int i = 1; i < Divisions; i++)
        {
            double x = full.Width * i / Divisions;
            double y = full.Height * i / Divisions;
            ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, full.Height));
            ctx.DrawLine(GridPen, new Point(0, y), new Point(full.Width, y));
        }

        var shown = _players.Where(p => ShowDead || p.Alive).ToList();
        if (shown.Count == 0)
        {
            DrawCentredNote(ctx, full, "No players to plot.");
            return;
        }

        int minX = shown.Min(p => p.X), maxX = shown.Max(p => p.X);
        int minY = shown.Min(p => p.Y), maxY = shown.Max(p => p.Y);

        if (StickyBounds)
        {
            if (!_hasAcc)
            {
                _accMinX = minX; _accMaxX = maxX; _accMinY = minY; _accMaxY = maxY;
                _hasAcc = true;
            }
            else
            {
                _accMinX = Math.Min(_accMinX, minX); _accMaxX = Math.Max(_accMaxX, maxX);
                _accMinY = Math.Min(_accMinY, minY); _accMaxY = Math.Max(_accMaxY, maxY);
            }

            minX = _accMinX; maxX = _accMaxX; minY = _accMinY; maxY = _accMaxY;
        }

        // Floor so a stacked or single-player box does not divide by zero. 512uu, not
        // 2048: the old floor swallowed whole duel arenas.
        const int MinSpan = 512;
        if (maxX - minX < MinSpan) { int c = (maxX + minX) / 2; minX = c - MinSpan / 2; maxX = c + MinSpan / 2; }
        if (maxY - minY < MinSpan) { int c = (maxY + minY) / 2; minY = c - MinSpan / 2; maxY = c + MinSpan / 2; }

        const double Pad = 26;
        double sx = (full.Width - Pad * 2) / (maxX - minX);
        double sy = (full.Height - Pad * 2) / (maxY - minY);
        double scale = Math.Min(sx, sy);            // uniform, so the plot is not stretched

        double offX = Pad + (full.Width - Pad * 2 - (maxX - minX) * scale) / 2;
        double offY = Pad + (full.Height - Pad * 2 - (maxY - minY) * scale) / 2;

        foreach (var p in shown)
        {
            // UE3 X/Y is a ground plane with +Y running the opposite way to screen Y.
            double px = offX + (p.X - minX) * scale;
            double py = offY + ((maxY - minY) - (p.Y - minY)) * scale;
            var at = new Point(px, py);

            IBrush fill = !p.Alive ? DeadBrush
                        : p.TeamId == 0 ? Team0
                        : p.TeamId == 1 ? Team1
                        : Neutral;

            double r = p.Alive ? 6 : 4;
            ctx.DrawEllipse(fill, null, at, r, r);

            if (p.Alive)
            {
                // Facing tick. Yaw arrives in degrees, 0 along +X, and screen Y is flipped.
                double rad = p.Yaw * Math.PI / 180.0;
                ctx.DrawLine(new Pen(fill, 2),
                    at, new Point(px + Math.Cos(rad) * 14, py - Math.Sin(rad) * 14));
            }

            if (ShowNames)
            {
                var text = new FormattedText(p.Name, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 11,
                    p.Alive ? Palette.TextSecondary : Palette.TextFaint);
                ctx.DrawText(text, new Point(px + 9, py - 7));
            }
        }

        var scaleNote = new FormattedText(
            $"{shown.Count} plotted · {(maxX - minX)} × {(maxY - minY)} uu · auto-fit",
            System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 11, Palette.TextFaint);
        ctx.DrawText(scaleNote, new Point(10, full.Height - 20));
    }

    private static void DrawCentredNote(DrawingContext ctx, Rect full, string message)
    {
        var text = new FormattedText(message, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface.Default, 12, Palette.TextFaint);
        ctx.DrawText(text, new Point((full.Width - text.Width) / 2, (full.Height - text.Height) / 2));
    }
}
