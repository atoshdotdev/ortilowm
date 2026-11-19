using System.Runtime.InteropServices;
using Microsoft.Win32;
using Tessel.Core.Models;
using Tessel.Platform.Windows.Interop.DwmApi;
using Tessel.Platform.Windows.Interop.Gdi32;
using Tessel.Platform.Windows.Interop.User32;

namespace Tessel.Platform.Windows.Windows;

internal static class NativeChrome
{
    internal const uint ThemeChanged = 0x031A;
    internal const uint ColorizationChanged = 0x0320;
    internal const uint SystemColorsChanged = 0x0015;
    internal const uint SettingsChanged = 0x001A;
    internal const uint DpiChanged = 0x02E0;
    internal const uint RefreshLayout = 0x8003;
    internal const uint NoActivateStyle = 0x08000000;
    internal const uint MouseMove = 0x0200;
    internal const uint MouseLeave = 0x02A3;

    internal const uint TextFlags = 0x0004 | 0x0020 | 0x0800 | 0x8000;
    internal const uint Center = 0x0001;
    internal const uint Right = 0x0002;
    internal const int TransparentBackground = 1;
    internal const string BodyFontFamily = "Segoe UI Variable Text";
    internal const int BodyFontSize = 14;

    private static Palette? _palette;

    internal static event Action? LayoutInvalidated;
    internal static event Action? PaletteChanged;

    internal readonly record struct Palette(
        uint Background, uint Text, uint SecondaryText, uint Divider, uint Hover,
        uint Selected, uint SelectedText, uint Accent, bool HighContrast, bool Dark = false,
        bool Transparency = true)
    {
        internal uint Indicator((uint? Light, uint? Dark) custom) =>
            HighContrast ? Accent : (Dark ? custom.Dark : custom.Light) ?? Accent;
    }

    internal static Palette Colors => _palette ??= ReadPalette();

    internal static int Scale(int value, uint dpi) => (int)((value * dpi + 48) / 96);

    internal static uint DpiForWindow(nint hwnd)
    {
        uint dpi = WindowDpi.Get(hwnd);
        return dpi == 0 ? 96 : dpi;
    }

    internal static void ThemeChangedFor(nint hwnd)
    {
        _palette = null;
        WindowApi.InvalidateRect(hwnd, nint.Zero, false);
        PaletteChanged?.Invoke();
    }

    internal static uint DpiFromMessage(nuint wParam) => (uint)(wParam & 0xffff);

    internal static void QueueLayoutRefresh(nint hwnd)
    {
        // The layout coordinator owns the overlay bounds and reserved space.
        // Defer relayout until the current SetWindowPos call has returned.
        if (!WindowApi.PostMessage(hwnd, RefreshLayout, 0, nint.Zero))
            Console.Error.WriteLine($"Could not queue overlay relayout. Win32 error: {Marshal.GetLastPInvokeError()}");
    }

    internal static void RequestLayout() => LayoutInvalidated?.Invoke();

    internal static void TrackMouseLeave(nint hwnd)
    {
        var tracking = new WindowPaintingApi.MouseTracking
        {
            Size = (uint)Marshal.SizeOf<WindowPaintingApi.MouseTracking>(),
            Flags = 0x00000002,
            Window = hwnd
        };
        WindowPaintingApi.TrackMouseEvent(ref tracking);
    }

    internal static nint CreateFont(uint dpi, string face = BodyFontFamily, int size = BodyFontSize, int weight = 400)
    {
        const uint DefaultCharset = 1;
        const uint AntialiasedQuality = 4;
        return GdiApi.CreateFont(
            -Scale(size, dpi), 0, 0, 0, weight, 0, 0, 0, DefaultCharset,
            0, 0, AntialiasedQuality, 0, face);
    }

    internal static NativeWindowRect Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    internal static bool TryApplyRoundedRegion(nint window, int width, int height, int radius,
        LayoutDockEdges dockedEdges = LayoutDockEdges.None)
    {
        if (radius <= 0)
            return WindowApi.SetWindowRgn(window, 0, true) != 0;

        int diameter = radius * 2;
        nint region = GdiApi.CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        if (region == 0)
            return false;

        try
        {
            if ((dockedEdges.HasFlag(LayoutDockEdges.Top) && !Include(0, 0, width, radius)) ||
                (dockedEdges.HasFlag(LayoutDockEdges.Bottom) && !Include(0, height - radius, width, height)) ||
                (dockedEdges.HasFlag(LayoutDockEdges.Left) && !Include(0, 0, radius, height)) ||
                (dockedEdges.HasFlag(LayoutDockEdges.Right) && !Include(width - radius, 0, width, height)))
                return false;

            if (WindowApi.SetWindowRgn(window, region, true) == 0)
                return false;

            region = 0; // Windows owns the region after success.
            return true;
        }
        finally
        {
            if (region != 0)
                GdiApi.DeleteObject(region);
        }

        bool Include(int left, int top, int right, int bottom)
        {
            nint edge = GdiApi.CreateRectRgn(left, top, right, bottom);
            if (edge == 0)
                return false;

            const int union = 2;
            int result = GdiApi.CombineRgn(region, region, edge, union);
            GdiApi.DeleteObject(edge);
            return result != 0;
        }
    }

    internal static bool Contains(NativeWindowRect rect, nint mousePosition)
    {
        int x = unchecked((short)mousePosition);
        int y = unchecked((short)(mousePosition >> 16));
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    internal static void Fill(nint hdc, NativeWindowRect rect, uint color, int radius = 0)
    {
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return;

        if (DirectChromePaint.Current?.Fill(hdc, rect, color, radius) == true)
            return;

        nint brush = GdiApi.CreateSolidBrush(color);
        if (brush == nint.Zero)
            return;

        try
        {
            nint region = radius > 0
                ? GdiApi.CreateRoundRectRgn(
                    rect.Left, rect.Top, rect.Right, rect.Bottom, radius * 2, radius * 2)
                : nint.Zero;

            if (region == nint.Zero)
            {
                WindowPaintingApi.FillRect(hdc, in rect, brush);
                return;
            }

            try
            {
                GdiApi.FillRgn(hdc, region, brush);
            }
            finally
            {
                GdiApi.DeleteObject(region);
            }
        }
        finally
        {
            GdiApi.DeleteObject(brush);
        }
    }

    internal static void FillRounded(nint dc, NativeWindowRect rect, uint color, double radius)
    {
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return;
        if (DirectChromePaint.Current?.Fill(dc, rect, color, radius) == true)
            return;

        if (radius <= 0 || !BufferedChromePaint.Current.TryFillRoundedRect(dc, rect, color, radius))
            Fill(dc, rect, color, (int)Math.Ceiling(radius));
    }

    internal static void Text(
        nint hdc, string text, NativeWindowRect rect, uint color, uint alignment = 0, int symbolSize = 0)
    {
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return;

        if (DirectChromePaint.Current?.Text(hdc, text, rect, color, alignment, symbolSize) == true)
            return;

        if (BufferedChromePaint.Current.TryDrawText(hdc, text, rect, color, TextFlags | alignment))
            return;

        GdiApi.SetTextColor(hdc, color);
        WindowPaintingApi.DrawText(hdc, text, -1, ref rect, TextFlags | alignment);
    }

    private static Palette ReadPalette()
    {
        var contrast = new WindowPaintingApi.HighContrast
        {
            Size = (uint)Marshal.SizeOf<WindowPaintingApi.HighContrast>()
        };

        const uint GetHighContrast = 0x0042;
        if (WindowPaintingApi.SystemParametersInfo(GetHighContrast, contrast.Size, ref contrast, 0) &&
            (contrast.Flags & 1) != 0)
        {
            const int Window = 5;
            const int WindowText = 8;
            const int Highlight = 13;
            const int HighlightText = 14;
            uint background = WindowPaintingApi.GetSysColor(Window);
            uint text = WindowPaintingApi.GetSysColor(WindowText);
            uint selected = WindowPaintingApi.GetSysColor(Highlight);
            uint selectedText = WindowPaintingApi.GetSysColor(HighlightText);
            return new(background, text, text, text, background, selected, selectedText, selectedText, true);
        }

        bool dark = false;
        bool transparency = true;
        try
        {
            const string personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            dark = Registry.GetValue(personalize, "SystemUsesLightTheme", 1) is 0;
            transparency = Registry.GetValue(personalize, "EnableTransparency", 1) is not 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Use the light palette when the preference is inaccessible.
        }

        uint selectedBackground = dark ? 0x00393939u : 0x00FFFFFFu;
        uint accent = dark ? 0x00FFCB60u : 0x00C46700u;
        if (WindowsAccent.TryGetColor(out uint userAccent))
            accent = userAccent;
        else if (DwmWindowApi.DwmGetColorizationColor(out uint argb, out _) >= 0)
        {
            // DWM supplies ARGB. GDI expects COLORREF (BGR).
            uint systemAccent = ((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF);
            accent = systemAccent;
        }

        return dark
            ? new(0x00202020, 0x00F5F5F5, 0x00C5C5C5, 0x00383838, 0x002C2C2C, selectedBackground, 0x00FFFFFF, accent, false, Dark: true, Transparency: transparency)
            : new(0x00FAF6F3, 0x001B1B1B, 0x00565656, 0x00E2DEDA, 0x00F0ECE9, selectedBackground, 0x001B1B1B, accent, false, Transparency: transparency);
    }

}
