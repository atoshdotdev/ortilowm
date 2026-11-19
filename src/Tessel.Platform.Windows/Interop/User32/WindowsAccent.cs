using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.User32;

internal static unsafe partial class WindowsAccent
{
    internal static bool TryGetColor(out uint colorRef)
    {
        colorRef = 0;
        int initialized = RoInitialize(1);
        // An existing STA apartment can also activate UISettings.
        if (initialized < 0 && initialized != unchecked((int)0x80010106))
            return false;

        nint name = 0, instance = 0, settings = 0;
        try
        {
            const string className = "Windows.UI.ViewManagement.UISettings";
            if (WindowsCreateString(className, className.Length, out name) < 0 ||
                RoActivateInstance(name, out instance) < 0)
                return false;

            var iid = new Guid("03021be4-5254-4781-8194-5168f7d06d7b"); // IUISettings3
            if (Marshal.QueryInterface(instance, in iid, out settings) < 0)
                return false;

            const int accent = 5; // UIColorType.Accent
            uint color = 0;
            var getColorValue = (delegate* unmanaged[Stdcall]<nint, int, uint*, int>)(*(nint**)settings)[6];
            if (getColorValue(settings, accent, &color) < 0)
                return false;

            // Windows.UI.Color is four bytes A,R,G,B. COLORREF is R,G,B,0.
            colorRef = color >> 8;
            return true;
        }
        finally
        {
            if (settings != 0) Marshal.Release(settings);
            if (instance != 0) Marshal.Release(instance);
            if (name != 0) WindowsDeleteString(name);
            if (initialized >= 0) RoUninitialize();
        }
    }

    [LibraryImport("combase.dll")]
    private static partial int RoInitialize(uint type);

    [LibraryImport("combase.dll")]
    private static partial void RoUninitialize();

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint name);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint name);

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint name, out nint instance);
}
