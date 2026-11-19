using System.Runtime.InteropServices;

namespace Tessel.Platform.Windows.Interop.Kernel32;

public static partial class CommandConsole
{
    internal static bool IsAttached => GetConsoleCP() != 0;

    public static void AttachToParent()
    {
        const int stdout = -11;
        const int stderr = -12;
        nint output = GetStdHandle(stdout);
        nint error = GetStdHandle(stderr);
        if (IsValid(output) && IsValid(error))
            return;

        const uint parentProcess = uint.MaxValue;
        if (!AttachConsole(parentProcess))
            return;

        if (IsValid(output))
            SetStdHandle(stdout, output);
        if (IsValid(error))
            SetStdHandle(stderr, error);
    }

    private static bool IsValid(nint handle) => handle != 0 && handle != -1;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int kind);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int kind, nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetConsoleCP();
}
