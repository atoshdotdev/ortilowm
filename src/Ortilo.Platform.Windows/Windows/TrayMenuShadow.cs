using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

internal sealed class TrayMenuShadow : IDisposable
{
    private nint _window;

    internal bool OwnsWindow(nint handle) => _window != 0 && handle == _window;

    internal unsafe void Show(nint menu, uint dpi, int radius)
    {
        const uint getDropShadow = 0x1024;
        if (NativeChrome.Colors.HighContrast ||
            !WindowPaintingApi.SystemParametersInfo(getDropShadow, 0, out int enabled, 0) || enabled == 0 ||
            !WindowApi.GetWindowRect(menu, out var menuBounds))
        {
            Hide();
            return;
        }

        if (_window == 0)
        {
            const uint layeredToolWindow = 0x00080000 | 0x00000020 | 0x00000080;
            const uint popup = 0x80000000;
            _window = WindowClassApi.CreateWindowEx(layeredToolWindow | NativeChrome.NoActivateStyle,
                "STATIC", "", popup, 0, 0, 0, 0, 0, 0, 0, 0);
            if (_window == 0)
            {
                Console.Error.WriteLine($"Could not create the tray shadow. Win32 error: {Marshal.GetLastPInvokeError()}");
                return;
            }
        }

        int spread = NativeChrome.Scale(12, dpi);
        int width = menuBounds.Right - menuBounds.Left + 2 * spread;
        int height = menuBounds.Bottom - menuBounds.Top + 2 * spread;
        nint dc = WindowPaintingApi.GetDC(menu);
        if (dc == 0)
        {
            Hide();
            return;
        }

        bool presented = false;
        try
        {
            // The tray backdrop initializes buffered painting on this thread.
            var rect = NativeChrome.Rect(0, 0, width, height);
            const uint noClip = 2;
            const int topDownDib = 2;
            var options = new TrayApi.PaintParameters((uint)Unsafe.SizeOf<TrayApi.PaintParameters>(), noClip, 0, 0);
            nint buffer = TrayApi.BeginBufferedPaint(dc, in rect, topDownDib, in options, out nint source);
            if (buffer != 0)
            {
                try
                {
                    if (TrayApi.GetBufferedPaintBits(buffer, out nint pixels, out int stride) >= 0 &&
                        pixels != 0 && stride >= width)
                    {
                        Paint((uint*)pixels, stride, width, height, spread, radius);
                        presented = LayeredWindowApi.Present(_window, source, width, height);
                    }
                }
                finally
                {
                    TrayApi.EndBufferedPaint(buffer, false);
                }
            }
        }
        finally
        {
            WindowPaintingApi.ReleaseDC(menu, dc);
        }

        const uint showWithoutActivationOrResize = 0x0040 | 0x0010 | 0x0001;
        if (!presented || !WindowApi.SetWindowPos(_window, menu, menuBounds.Left - spread,
                menuBounds.Top - spread, 0, 0, showWithoutActivationOrResize))
            Hide();
    }

    private static unsafe void Paint(uint* pixels, int stride, int width, int height, int spread, int radius)
    {
        double centerX = width / 2.0;
        double centerY = height / 2.0;
        double innerX = centerX - spread - radius;
        double innerY = centerY - spread - radius;
        for (int y = 0; y < height; y++)
        {
            double dy = Math.Abs(y + 0.5 - centerY) - innerY;
            var row = new Span<uint>(pixels + y * stride, width);
            for (int x = 0; x < width; x++)
            {
                double dx = Math.Abs(x + 0.5 - centerX) - innerX;
                double outsideX = Math.Max(0, dx);
                double outsideY = Math.Max(0, dy);
                double distance = Math.Sqrt(outsideX * outsideX + outsideY * outsideY) +
                    Math.Min(0, Math.Max(dx, dy)) - radius;
                // Keep the menu interior clear so the glow cannot tint its backdrop.
                double falloff = distance >= 0 ? Math.Max(0, 1 - distance / spread) : 0;
                row[x] = (uint)Math.Round(44 * falloff * falloff) << 24;
            }
        }
    }

    internal void Hide()
    {
        if (_window != 0)
            WindowApi.ShowWindow(_window, 0);
    }

    public void Dispose()
    {
        if (_window != 0)
            WindowApi.DestroyWindow(_window);
        _window = 0;
    }
}
