using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tessel.Platform.Windows.Interop.Kernel32;
using Tessel.Platform.Windows.Interop.User32;

namespace Tessel.Platform.Windows.Windows;

internal sealed class OutlineOverlay : IDisposable
{
    private const string ClassName = "TesselOutlineOverlay";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int GWL_EXSTYLE = -20;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static readonly HashSet<nint> Windows = [];
    private static readonly WindowProcedure Procedure = WndProc;
    private static bool _classRegistered;

    private nint _handle;
    private bool _bufferedPainting;
    private (int Width, int Height, int Thickness, int Radius, uint Color) _image;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint hwnd, uint message, nuint wParam, nint lParam);

    internal static bool IsInternalWindow(nint handle) => Windows.Contains(handle);

    internal void Show(NativeWindowRect bounds, int thickness, int radius, uint color, nint referenceWindow)
    {
        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        int limit = Math.Min(width, height) / 2;
        if (limit <= 0 || thickness <= 0 || !EnsureWindow())
        {
            Hide();
            return;
        }

        thickness = Math.Min(thickness, limit);
        radius = Math.Clamp(radius, 0, limit);
        var image = (width, height, thickness, radius, color);
        if (_image != image)
        {
            if (!Present(width, height, thickness, radius, color))
            {
                Console.Error.WriteLine("Could not render the focus outline.");
                Hide();
                return;
            }
            _image = image;
        }

        // Keep the outline in the focused window's z-order band. A normal
        // outline is covered by its target when that target is always on top.
        bool targetTopmost = IsTopmost(referenceWindow);
        nint insertAfter = targetTopmost ? -1 : IsTopmost(_handle) ? -2 : 0;
        if (!WindowApi.SetWindowPos(_handle, insertAfter, bounds.Left, bounds.Top,
                0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW))
        {
            Console.Error.WriteLine($"Could not position the focus outline. Win32 error: {Marshal.GetLastPInvokeError()}");
            Hide();
        }
    }

    private static bool IsTopmost(nint window) =>
        (WindowApi.GetWindowLongPtr(window, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;

    internal void Hide()
    {
        if (_handle != 0)
            WindowApi.ShowWindow(_handle, 0);
    }

    private bool EnsureWindow()
    {
        if (_handle != 0)
            return true;

        if (!_bufferedPainting)
        {
            _bufferedPainting = TrayApi.BufferedPaintInit() >= 0;
            if (!_bufferedPainting)
            {
                Console.Error.WriteLine("Could not initialize buffered painting for the focus outline.");
                return false;
            }
        }

        if (!_classRegistered)
        {
            NativeWindowClassRegistrar.Register(ClassName, Marshal.GetFunctionPointerForDelegate(Procedure));
            _classRegistered = true;
        }

        _handle = WindowClassApi.CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED | NativeChrome.NoActivateStyle,
            ClassName, "Tessel Focus Outline", WS_POPUP, 0, 0, 1, 1,
            0, 0, ModuleApi.GetModuleHandle(null), 0);

        if (_handle == 0)
        {
            Console.Error.WriteLine($"Could not create focus outline. Win32 error: {Marshal.GetLastPInvokeError()}");
            return false;
        }

        Windows.Add(_handle);
        return true;
    }

    private unsafe bool Present(int width, int height, int thickness, int radius, uint color)
    {
        nint dc = WindowPaintingApi.GetDC(_handle);
        if (dc == 0)
            return false;

        try
        {
            const int topDownDib = 2;
            const uint noClip = 2;
            var rect = NativeChrome.Rect(0, 0, width, height);
            var options = new TrayApi.PaintParameters((uint)Unsafe.SizeOf<TrayApi.PaintParameters>(), noClip, 0, 0);
            nint buffer = TrayApi.BeginBufferedPaint(dc, in rect, topDownDib, in options, out nint source);
            if (buffer == 0)
                return false;

            try
            {
                if (TrayApi.GetBufferedPaintBits(buffer, out nint pixels, out int stride) < 0 ||
                    pixels == 0 || stride < width)
                    return false;

                Paint((uint*)pixels, stride, width, height, thickness, radius, color);
                return LayeredWindowApi.Present(_handle, source, width, height);
            }
            finally
            {
                TrayApi.EndBufferedPaint(buffer, false);
            }
        }
        finally
        {
            WindowPaintingApi.ReleaseDC(_handle, dc);
        }
    }

    private static unsafe void Paint(uint* pixels, int stride, int width, int height, int thickness, int radius, uint color)
    {
        uint red = color & 255, green = (color >> 8) & 255, blue = (color >> 16) & 255;
        uint opaque = 0xFF000000 | (red << 16) | (green << 8) | blue;
        // Straight edges fill whole pixels. Only the corner squares need coverage calculations.
        for (int y = 0; y < height; y++)
        {
            var row = new Span<uint>(pixels + y * stride, width);
            int edgeY = Math.Min(y, height - 1 - y);
            if (edgeY < thickness)
                row.Fill(opaque);
            else
            {
                row.Clear();
                row[..thickness].Fill(opaque);
                row[^thickness..].Fill(opaque);
            }

            if (edgeY >= radius)
                continue;

            for (int x = 0; x < radius; x++)
            {
                double dx = radius - x - 0.5;
                double dy = radius - edgeY - 0.5;
                double distance = Math.Sqrt(dx * dx + dy * dy) - radius;
                double outerCoverage = Math.Clamp(0.5 - distance, 0, 1);
                double innerCoverage = x >= thickness && edgeY >= thickness
                    ? Math.Clamp(0.5 - distance - thickness, 0, 1)
                    : 0;
                uint alpha = (uint)Math.Round(255 * (outerCoverage - innerCoverage));
                uint pixel = (alpha << 24) | ((red * alpha / 255) << 16) |
                    ((green * alpha / 255) << 8) | (blue * alpha / 255);
                row[x] = row[width - 1 - x] = pixel;
            }
        }
    }

    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam) => message switch
    {
        0x0084 => -1, // WM_NCHITTEST / HTTRANSPARENT
        0x0021 => 3,  // WM_MOUSEACTIVATE / MA_NOACTIVATE
        0x0014 => 1,  // WM_ERASEBKGND
        _ => WindowClassApi.DefWindowProc(hwnd, message, wParam, lParam)
    };

    public void Dispose()
    {
        if (_handle != 0)
        {
            Hide();
            if (!WindowApi.DestroyWindow(_handle))
            {
                Console.Error.WriteLine($"Could not destroy focus outline. Win32 error: {Marshal.GetLastPInvokeError()}");
                return;
            }

            Windows.Remove(_handle);
            _handle = 0;
            _image = default;
        }

        if (_bufferedPainting)
        {
            TrayApi.BufferedPaintUnInit();
            _bufferedPainting = false;
        }
    }
}
