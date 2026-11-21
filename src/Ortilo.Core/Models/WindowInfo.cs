namespace Ortilo.Core.Models;

public sealed record WindowInfo(
    nint Handle,
    string Title,
    uint ProcessId
);
