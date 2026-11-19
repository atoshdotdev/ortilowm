using System.Runtime.CompilerServices;
using Tessel.Platform.Windows.Interop.Gdi32;
using Tessel.Platform.Windows.Interop.User32;
using Tessel.Platform.Windows.Interop.UxTheme;

namespace Tessel.Platform.Windows.Windows;

internal readonly unsafe struct BufferedChromePaint(
    nint dc, nint theme, nint pixels, int stride, NativeWindowRect bounds)
{
    [ThreadStatic]
    internal static BufferedChromePaint Current;

    internal bool TryDrawText(nint target, string text, NativeWindowRect rect, uint color, uint flags)
    {
        if (dc != target || theme == 0)
            return false;

        // GDI fills write RGB but leave alpha unset. Make them opaque before
        // drawing text over them, while preserving edges from earlier glyphs.
        CompleteGdiAlpha(rect);
        const uint composited = 1 << 13;
        const uint textColor = 1;
        var options = new ThemeTextApi.Options
        {
            Size = (uint)Unsafe.SizeOf<ThemeTextApi.Options>(),
            Flags = composited | textColor,
            TextColor = color
        };
        // Part 0 uses the font already selected into the DC.
        return ThemeTextApi.DrawThemeTextEx(theme, dc, 0, 0, text, -1, flags, ref rect, in options) >= 0;
    }

    internal void CompleteGdiAlpha() => CompleteGdiAlpha(bounds);

    internal bool TryFillRoundedRect(nint target, NativeWindowRect rect, uint color, double cornerRadius)
    {
        if (dc != target || pixels == 0)
            return false;

        double radius = Math.Min(cornerRadius, Math.Min(rect.Right - rect.Left, rect.Bottom - rect.Top) / 2.0);
        if (radius <= 0)
            return true;

        CompleteGdiAlpha(rect);
        double leftCenter = rect.Left + radius;
        double rightCenter = rect.Right - radius;
        double topCenter = rect.Top + radius;
        double bottomCenter = rect.Bottom - radius;
        for (int y = Math.Max(rect.Top, bounds.Top); y < Math.Min(rect.Bottom, bounds.Bottom); y++)
        {
            uint* row = (uint*)pixels + (y - bounds.Top) * stride;
            double dy = y + 0.5 - Math.Clamp(y + 0.5, topCenter, bottomCenter);
            for (int x = Math.Max(rect.Left, bounds.Left); x < Math.Min(rect.Right, bounds.Right); x++)
            {
                double dx = x + 0.5 - Math.Clamp(x + 0.5, leftCenter, rightCenter);
                double coverage = Math.Clamp(radius + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0.0, 1.0);
                uint alpha = (uint)Math.Round(coverage * 255);
                ref uint pixel = ref row[x - bounds.Left];
                pixel = Blend(pixel, color, alpha);
            }
        }
        return true;
    }

    // COLORREF is BGR, while a top-down DIB stores premultiplied BGRA bytes.
    private static uint Blend(uint pixel, uint color, uint alpha)
    {
        uint blue = BlendChannel(color >> 16, pixel, alpha);
        uint green = BlendChannel(color >> 8, pixel >> 8, alpha);
        uint red = BlendChannel(color, pixel >> 16, alpha);
        uint opacity = BlendChannel(255, pixel >> 24, alpha);
        return opacity << 24 | red << 16 | green << 8 | blue;
    }

    private static uint BlendChannel(uint source, uint destination, uint alpha) =>
        (((source & 255) * alpha) + ((destination & 255) * (255 - alpha)) + 127) / 255;

    private void CompleteGdiAlpha(NativeWindowRect area)
    {
        int left = Math.Max(area.Left, bounds.Left) - bounds.Left;
        int right = Math.Min(area.Right, bounds.Right) - bounds.Left;
        int top = Math.Max(area.Top, bounds.Top) - bounds.Top;
        int bottom = Math.Min(area.Bottom, bounds.Bottom) - bounds.Top;
        if (left >= right || top >= bottom)
            return;

        GdiApi.GdiFlush();
        for (int y = top; y < bottom; y++)
        {
            var row = new Span<uint>((uint*)pixels + y * stride + left, right - left);
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x] is > 0 and <= 0x00FFFFFF)
                    row[x] |= 0xFF000000;
            }
        }
    }
}
