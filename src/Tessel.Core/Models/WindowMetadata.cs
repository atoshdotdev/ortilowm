namespace Tessel.Core.Models;

public sealed record WindowMetadata(
    nint Handle,
    string Title,
    uint ProcessId,
    bool HasOwner,
    bool IsToolWindow,
    bool IsCloaked,
    bool IsMinimized,
    bool HasHigherIntegrity = false
);
