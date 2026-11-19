using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.Kernel32;

internal static partial class ThreadApi
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    internal static partial uint GetCurrentThreadId();
}
