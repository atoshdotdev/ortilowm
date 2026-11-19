using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.Gdi32;

internal static partial class GdiApi
{
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GdiFlush();

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateFont(
        int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charSet,
        uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily,
        string face);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint handle);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateRoundRectRgn(
        int left, int top, int right, int bottom, int width, int height);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    internal static partial int CombineRgn(nint destination, nint first, nint second, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FillRgn(nint hdc, nint region, nint brush);

    [LibraryImport("gdi32.dll")]
    internal static partial int SetBkMode(
        nint hdc,
        int mode);

    [LibraryImport("gdi32.dll")]
    internal static partial uint SetTextColor(
        nint hdc,
        uint color);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateSolidBrush(
        uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(
        nint handle);
}
