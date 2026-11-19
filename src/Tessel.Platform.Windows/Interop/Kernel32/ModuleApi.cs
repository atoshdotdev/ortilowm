using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.Kernel32;

internal static partial class ModuleApi
{
    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetModuleHandleW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint GetModuleHandle(string? moduleName);
}
