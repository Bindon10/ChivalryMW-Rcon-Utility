using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ChivRcon.App.Views;

// Lays each view out in a real headless window at phone size, then walks the visual tree
// for anything whose right edge sticks out past the screen. Content inside a horizontal
// ScrollViewer is allowed to exceed -- that is what scrolling is for.
class Probe
{
    static double PhoneW = 400, PhoneH = 800;

    static int Main(string[] rawArgs)
    {
        if (rawArgs.Length >= 2) { PhoneW = double.Parse(rawArgs[0]); PhoneH = double.Parse(rawArgs[1]); }
        AppBuilder.Configure<ChivRcon.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .WithInterFont()
            .SetupWithoutStarting();

        var views = new (string, Func<Control>)[]
        {
            ("ServerView", () => new ServerView()),
            ("DashboardView", () => new DashboardView()),
            ("ConsoleView", () => new ConsoleView()),
            ("BansView", () => new BansView()),
            ("TournamentView", () => new TournamentView()),
            ("LoadoutView", () => new LoadoutView()),
            ("MapView", () => new MapView()),
            ("AuditView", () => new AuditView()),
            ("ReferenceView", () => new ReferenceView()),
        };

        int bad = 0;
        Console.WriteLine($"viewport {PhoneW}x{PhoneH}\n");

        foreach (var (name, make) in views)
        {
            try
            {
                var view = make();
                var win = new Window { Width = PhoneW, Height = PhoneH, Content = view };
                win.Show();
                Dispatcher.UIThread.RunJobs();
                win.Measure(new Size(PhoneW, PhoneH));
                win.Arrange(new Rect(0, 0, PhoneW, PhoneH));
                Dispatcher.UIThread.RunJobs();

                var offenders = new List<string>();
                Walk(view, view, offenders, 0);
                FindOverlaps(view, offenders, 0);

                if (view.Bounds.Width <= 0)
                {
                    Console.WriteLine($"  NOT LAID OUT  {name}");
                    bad++;
                }
                else if (offenders.Count > 0)
                {
                    bad++;
                    Console.WriteLine($"  OVERFLOW  {name,-16} width={view.Bounds.Width:0}");
                    foreach (var o in offenders.Take(4)) Console.WriteLine($"              {o}");
                }
                else
                {
                    Console.WriteLine($"  ok        {name,-16} width={view.Bounds.Width:0} height={view.Bounds.Height:0}");
                }
                win.Close();
            }
            catch (Exception ex)
            {
                bad++;
                Console.WriteLine($"  ERROR     {name,-16} {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        Console.WriteLine(bad == 0
            ? "\nEvery view fits the phone viewport (scrollable tables excluded by design)."
            : $"\n{bad} view(s) still overflow.");
        return bad == 0 ? 0 : 1;
    }

    // A Grid whose columns cannot fit lets children bleed into each other's cells: the
    // control is inside the viewport, so the right-edge check above never sees it. This is
    // what "the Say button sits on top of the message box" looks like to the layout engine.
    static void FindOverlaps(Visual v, List<string> hits, int depth)
    {
        if (depth > 40) return;
        // Only grids written in our own XAML. Control templates (scrollbars over content,
        // a combo's chevron over its border) overlap deliberately and are not our bug.
        if (v is Grid g && g.TemplatedParent is null)
        {
            var kids = g.Children.OfType<Control>()
                .Where(c => c.IsVisible && c.TemplatedParent is null
                            && c.Bounds.Width > 1 && c.Bounds.Height > 1)
                .ToList();
            for (int i = 0; i < kids.Count; i++)
                for (int j = i + 1; j < kids.Count; j++)
                {
                    // Children deliberately sharing a cell are not a bug; different cells are.
                    if (Grid.GetRow(kids[i]) == Grid.GetRow(kids[j]) &&
                        Grid.GetColumn(kids[i]) == Grid.GetColumn(kids[j])) continue;

                    var a = kids[i].Bounds; var b = kids[j].Bounds;
                    double ox = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    double oy = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                    if (ox > 2 && oy > 2)
                        hits.Add($"OVERLAP {kids[i].GetType().Name}({kids[i].Name}) x {kids[j].GetType().Name}({kids[j].Name}) by {ox:0}x{oy:0}px");
                }
        }
        foreach (var child in v.GetVisualChildren()) FindOverlaps(child, hits, depth + 1);
    }

    // Scrollbars legitimately sit at the very edge of the viewport.
    static bool IsChrome(Control c) =>
        c is Avalonia.Controls.Primitives.ScrollBar or Avalonia.Controls.Primitives.Thumb
        || c.TemplatedParent is Avalonia.Controls.Primitives.ScrollBar;

    static void Walk(Visual root, Visual v, List<string> hits, int depth)
    {
        if (depth > 40) return;
        foreach (var child in v.GetVisualChildren())
        {
            // anything that scrolls horizontally is allowed to be wider than the screen
            if (child is ScrollViewer sv && sv.HorizontalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled)
                continue;

            if (child is Control c && c.Bounds.Width > 0 && !IsChrome(c))
            {
                // Translate the far corner rather than adding raw width: a Viewbox scales its
                // child by transform, so Bounds.Width is the pre-scale size and adding it
                // reports a chevron as 2000px wide.
                var tl = c.TranslatePoint(new Point(0, 0), root);
                var br = c.TranslatePoint(new Point(c.Bounds.Width, 0), root);
                if (tl.HasValue && br.HasValue)
                {
                    double right = br.Value.X;
                    if (right > PhoneW + 1 && c is not Panel)
                        hits.Add($"{c.GetType().Name} right={right:0} (x={tl.Value.X:0} w={right - tl.Value.X:0})");
                }
            }
            Walk(root, child, hits, depth + 1);
        }
    }
}
