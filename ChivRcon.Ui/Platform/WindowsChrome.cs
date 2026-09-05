using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ChivRcon.App.Platform;

/// <summary>
/// Makes the Windows-drawn window border follow the app's dark theme.
/// </summary>
/// <remarks>
/// With <c>SystemDecorations="BorderOnly"</c> Windows still paints a frame, and on Windows 11 that
/// frame defaults to a light colour regardless of the app's theme — a white hairline along the top
/// of a dark window. DWM exposes the border colour directly.
/// <para>
/// The awkward part is timing. Setting the attributes once when the window opens is not reliable:
/// the frame is composited before the managed <c>Opened</c> event runs, and neither the attribute
/// write nor a <c>SWP_FRAMECHANGED</c> repaint is guaranteed to be picked up. Minimising and
/// restoring always fixes it, because that forces the frame to be built again from scratch.
/// </para>
/// <para>
/// So the attributes are re-applied a few times while the window settles, and again whenever it is
/// restored or resized. Each call is a handful of cheap syscalls, and once the frame has taken the
/// colour the repeats are no-ops.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private const uint RdwFrame = 0x0400;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwUpdateNow = 0x0100;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprc, IntPtr hrgn, uint flags);

    /// <summary>
    /// Applies dark chrome and tints the border, then keeps it applied as the window settles.
    /// Call once, from the window's <c>Opened</c> handler.
    /// </summary>
    public static void Attach(Window window, Color borderColor)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        Apply(window, borderColor);

        // The first paint often lands after Opened, so re-apply once the UI thread is idle.
        Dispatcher.UIThread.Post(() => Apply(window, borderColor), DispatcherPriority.Background);

        // And a few more times over the first half second, covering the window becoming visible
        // and DWM finishing its own composition. Cheap, and self-cancelling.
        int attempts = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };

        timer.Tick += (_, _) =>
        {
            Apply(window, borderColor);

            if (++attempts >= 5)
                timer.Stop();
        };

        timer.Start();

        // Restoring from minimised rebuilds the frame, which is exactly when the colour is lost.
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
                Apply(window, borderColor);
        };
    }

    /// <summary>Writes the DWM attributes and forces the frame to redraw. Safe to call repeatedly.</summary>
    public static void Apply(Window window, Color borderColor)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        IntPtr handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            int enabled = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int));

            // DWM wants COLORREF: 0x00BBGGRR, not the usual RGB order.
            int colorRef = borderColor.R | (borderColor.G << 8) | (borderColor.B << 16);

            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colorRef, sizeof(int));
            DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorRef, sizeof(int));

            SetWindowPos(
                handle, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

            RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RdwFrame | RdwInvalidate | RdwUpdateNow);
        }
        catch (DllNotFoundException)
        {
            // dwmapi is absent on very old Windows; the border simply stays default.
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}
