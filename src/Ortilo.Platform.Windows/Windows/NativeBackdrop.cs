using Ortilo.Platform.Windows.Interop.DwmApi;
using Ortilo.Platform.Windows.Interop.Gdi32;
using Ortilo.Platform.Windows.Interop.User32;
using Ortilo.Platform.Windows.Interop.UxTheme;
using System.Runtime.CompilerServices;

namespace Ortilo.Platform.Windows.Windows;

internal sealed class NativeBackdrop(nint window, bool suppressFrameShadow = false) : IDisposable
{
    internal const uint CompositionChanged = 0x031E;
    internal const uint FramedWindowStyle = 0x00C00000; // WS_CAPTION

    // Keep a caption in the window style for DWM, while giving every visible pixel to the client.
    internal static bool HandleFrameMessage(uint message, out nint result)
    {
        const uint calculateNonClientSize = 0x0083;
        const uint nonClientHitTest = 0x0084;
        const int clientHit = 1;
        result = message == nonClientHitTest ? clientHit : nint.Zero;
        return message is calculateNonClientSize or nonClientHitTest;
    }

    private readonly bool _bufferedPainting = TrayApi.BufferedPaintInit() >= 0;
    private bool _configured;
    private bool _hasBackdrop;
    private bool _disposed;
    private nint _font;
    private nint _textTheme;
    private uint _fontDpi;
    private bool _roundWindowCorners;

    internal bool SetSystemCorners(bool rounded)
    {
        _roundWindowCorners = rounded;
        const uint cornerPreference = 33;
        int preference = rounded ? 2 : 1; // DWMWCP_ROUND or DWMWCP_DONOTROUND
        return DwmWindowApi.DwmSetWindowAttribute(window, cornerPreference, in preference, sizeof(int)) >= 0;
    }

    internal void Refresh(bool suppressSystemBackdrop = false)
    {
        if (_disposed)
            return;

        if (_textTheme != 0)
            ThemeTextApi.CloseThemeData(_textTheme);
        _textTheme = _bufferedPainting ? ThemeTextApi.OpenThemeData(window, "WINDOW") : 0;

        var colors = NativeChrome.Colors;
        const uint nonClientRendering = 2;
        const uint immersiveDarkMode = 20;
        const uint borderColor = 34;
        const uint systemBackdropType = 38;
        int framePolicy = suppressFrameShadow ? 1 : 2; // DWMNCRP_DISABLED or DWMNCRP_ENABLED
        int dark = colors.Dark ? 1 : 0;
        const int desktopAcrylic = 3; // DWMSBT_TRANSIENTWINDOW
        int backdrop = !suppressFrameShadow && !suppressSystemBackdrop && _bufferedPainting &&
            colors.Transparency && !colors.HighContrast ? desktopAcrylic : 1;
        DwmWindowApi.DwmSetWindowAttribute(window, nonClientRendering, in framePolicy, sizeof(int));
        DwmWindowApi.DwmSetWindowAttribute(window, immersiveDarkMode, in dark, sizeof(int));
        SetSystemCorners(_roundWindowCorners);
        int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmWindowApi.DwmSetWindowAttribute(window, borderColor, in noBorder, sizeof(int));

        int applied = DwmWindowApi.DwmSetWindowAttribute(window, systemBackdropType, in backdrop, sizeof(int));
        _hasBackdrop = backdrop != 1 && applied >= 0;
        int margin = _hasBackdrop ? -1 : 0;
        var frame = new DwmWindowApi.FrameMargins(margin, margin, margin, margin);
        if (DwmWindowApi.DwmExtendFrameIntoClientArea(window, in frame) < 0)
            _hasBackdrop = false;

        if (!_configured)
        {
            _configured = true;
            const uint recalculateFrame = 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020;
            WindowApi.SetWindowPos(window, nint.Zero, 0, 0, 0, 0, recalculateFrame);
        }
    }

    internal void Paint(uint dpi, Action<nint, NativeChrome.Palette> draw)
    {
        if (!_configured)
            Refresh();

        nint target = WindowPaintingApi.BeginPaint(window, out var paint);
        try
        {
            var rect = paint.Paint;
            if (target == nint.Zero || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                return;

            var colors = NativeChrome.Colors;
            uint? background = _hasBackdrop ? null : colors.Background;
            if (RenderToBuffer(target, rect, dpi, colors, draw, background: background))
                return;

            NativeChrome.Fill(target, rect, colors.Background);
            PaintContent(target, dpi, colors, draw);
        }
        finally
        {
            WindowPaintingApi.EndPaint(window, in paint);
        }
    }

    // Without a presenter the buffer is copied back by UxTheme. A layered
    // surface can instead consume that same premultiplied bitmap directly.
    internal bool RenderToBuffer(nint target, NativeWindowRect rect, uint dpi,
        NativeChrome.Palette colors, Action<nint, NativeChrome.Palette> draw,
        Action<nint>? present = null, uint? background = null)
    {
        if (!_bufferedPainting)
            return false;
        const int topDownDib = 2;
        const uint noClip = 2;
        // Layered presentation replaces the whole bitmap, even on a partial
        // invalidation or when part of this window is covered by another app.
        var parameters = new TrayApi.PaintParameters((uint)Unsafe.SizeOf<TrayApi.PaintParameters>(),
            present is null ? 0 : noClip, 0, 0);
        nint buffer = TrayApi.BeginBufferedPaint(target, in rect, topDownDib, in parameters, out nint dc);
        if (buffer == nint.Zero)
            return false;

        bool update = false;
        int presented;
        try
        {
            int width = rect.Right - rect.Left;
            if (TrayApi.BufferedPaintClear(buffer, nint.Zero) < 0 ||
                TrayApi.GetBufferedPaintBits(buffer, out nint pixels, out int stride) < 0 ||
                pixels == nint.Zero || stride < width)
                return false;

            bool drawn = false;
            if (DirectChromePaint.TryBegin(dc, rect, dpi, background) is { } direct)
            {
                try
                {
                    PaintContent(dc, dpi, colors, draw);
                }
                finally
                {
                    drawn = direct.End();
                }

                if (!drawn && TrayApi.BufferedPaintClear(buffer, nint.Zero) < 0)
                    return false;
            }

            if (!drawn)
            {
                var previous = BufferedChromePaint.Current;
                var paint = new BufferedChromePaint(dc, _textTheme, pixels, stride, rect);
                BufferedChromePaint.Current = paint;
                try
                {
                    if (background is uint fill)
                        NativeChrome.Fill(dc, rect, fill);
                    PaintContent(dc, dpi, colors, draw);
                    paint.CompleteGdiAlpha();
                }
                finally
                {
                    BufferedChromePaint.Current = previous;
                }
            }
            present?.Invoke(dc);
            update = present is null;
        }
        finally
        {
            presented = TrayApi.EndBufferedPaint(buffer, update);
        }
        return presented >= 0;
    }

    private void PaintContent(nint dc, uint dpi, NativeChrome.Palette colors,
        Action<nint, NativeChrome.Palette> draw)
    {
        if (_fontDpi != dpi || _font == nint.Zero)
        {
            if (_font != nint.Zero)
                GdiApi.DeleteObject(_font);
            _font = NativeChrome.CreateFont(dpi);
            _fontDpi = dpi;
        }

        nint previous = _font != nint.Zero ? GdiApi.SelectObject(dc, _font) : nint.Zero;
        try
        {
            GdiApi.SetBkMode(dc, NativeChrome.TransparentBackground);
            draw(dc, colors);
        }
        finally
        {
            if (previous != nint.Zero && previous != (nint)(-1))
                GdiApi.SelectObject(dc, previous);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_font != nint.Zero)
            GdiApi.DeleteObject(_font);
        if (_textTheme != 0)
            ThemeTextApi.CloseThemeData(_textTheme);
        if (_bufferedPainting)
            TrayApi.BufferedPaintUnInit();
    }
}
