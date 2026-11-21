using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Interop.UxTheme;

internal static partial class ThemeTextApi
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Options
    {
        internal uint Size;
        internal uint Flags;
        internal uint TextColor;
        internal uint BorderColor;
        internal uint ShadowColor;
        internal int ShadowType;
        internal NativePoint ShadowOffset;
        internal int BorderSize;
        internal int FontProperty;
        internal int ColorProperty;
        internal int State;
        internal int ApplyOverlay;
        internal int GlowSize;
        internal nint Callback;
        internal nint CallbackData;
    }

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenThemeData(nint window, string classList);

    [LibraryImport("uxtheme.dll")]
    internal static partial int CloseThemeData(nint theme);

    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int DrawThemeTextEx(nint theme, nint dc, int part, int state,
        string text, int length, uint flags, ref NativeWindowRect rect, in Options options);
}
