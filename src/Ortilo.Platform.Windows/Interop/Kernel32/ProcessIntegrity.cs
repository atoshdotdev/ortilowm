using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ortilo.Platform.Windows.Interop.Kernel32;

internal static unsafe partial class ProcessIntegrity
{
    internal static uint? Current { get; } = ReadLevel(GetCurrentProcess());

    internal static uint? ReadLevel(uint processId)
    {
        const uint queryLimitedInformation = 0x1000;
        using var process = OpenProcess(queryLimitedInformation, false, processId);
        return process.IsInvalid ? null : ReadLevel(process.DangerousGetHandle());
    }

    private static uint? ReadLevel(nint process)
    {
        const uint tokenQuery = 0x0008;
        if (!OpenProcessToken(process, tokenQuery, out var token))
        {
            token.Dispose();
            return null;
        }

        using (token)
        {
            const int tokenIntegrityLevel = 25;
            const int maxSidSize = 68;
            // TOKEN_MANDATORY_LABEL starts with a SID pointer and attributes.
            // Use pointer-aligned storage for the header and the SID payload.
            int slots = 2 + (maxSidSize + sizeof(nint) - 1) / sizeof(nint);
            nint* buffer = stackalloc nint[slots];
            if (!GetTokenInformation(token, tokenIntegrityLevel, buffer, slots * sizeof(nint), out _))
                return null;

            nint sid = buffer[0];
            byte count = *GetSidSubAuthorityCount(sid);
            return count == 0 ? null : *GetSidSubAuthority(sid, (uint)(count - 1));
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    private static partial SafeProcessHandle OpenProcess(
        uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(
        SafeAccessTokenHandle token, int informationClass, void* buffer, int length, out int requiredLength);

    [LibraryImport("advapi32.dll")]
    private static partial byte* GetSidSubAuthorityCount(nint sid);

    [LibraryImport("advapi32.dll")]
    private static partial uint* GetSidSubAuthority(nint sid, uint index);
}
