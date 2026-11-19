namespace Tessel.Core.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    uint ProcessId
);
