using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace ChivRcon.App.Views;

/// <summary>
/// Dialogs for single-view platforms (Android), where Window and ShowDialog do not exist.
/// Renders into the TopLevel's OverlayLayer instead. Desktop keeps its real windows.
/// </summary>
internal static class DialogOverlay
{
    internal sealed record Choice<T>(string Label, Func<T?> Result, bool IsPrimary) where T : class;

    private static IBrush Brush(string key, string fallback) =>
        Application.Current?.TryFindResource(key, out var v) == true && v is IBrush b
            ? b : Avalonia.Media.Brush.Parse(fallback);

    /// <summary>Shows a card over the current view. Returns null when dismissed.</summary>
    public static async Task<T?> ShowAsync<T>(
        Visual anchor, string title, string? message, Control? body, params Choice<T>[] choices)
        where T : class
    {
        var layer = OverlayLayer.GetOverlayLayer(anchor)
            ?? throw new InvalidOperationException("No overlay layer available for this visual.");

        var tcs = new TaskCompletionSource<T?>();

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextPrimary", "#E6E8EC"),
        });

        if (!string.IsNullOrWhiteSpace(message))
        {
            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("TextSecondary", "#A8AEBA"),
            });
        }

        if (body is not null) stack.Children.Add(body);

        // Touch targets, not desktop targets.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
        };
        foreach (var choice in choices)
        {
            var btn = new Button
            {
                Content = choice.Label,
                MinWidth = 96,
                MinHeight = 44,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            if (choice.IsPrimary) btn.Classes.Add("accent");
            var captured = choice;
            btn.Click += (_, _) => tcs.TrySetResult(captured.Result());
            buttons.Children.Add(btn);
        }
        stack.Children.Add(buttons);

        var card = new Border
        {
            Background = Brush("BgPanel", "#22252C"),
            BorderBrush = Brush("BorderBrush", "#33373F"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            MaxWidth = 460,
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack,
        };

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.55),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new ScrollViewer
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Content = card,
            },
        };
        // Tapping the backdrop dismisses; tapping the card must not.
        backdrop.PointerPressed += (_, e) => { if (ReferenceEquals(e.Source, backdrop)) tcs.TrySetResult(null); };

        layer.Children.Add(backdrop);
        try { return await tcs.Task; }
        finally { layer.Children.Remove(backdrop); }
    }
}
