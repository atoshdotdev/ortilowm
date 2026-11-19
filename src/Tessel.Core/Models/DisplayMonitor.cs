namespace Tessel.Core.Models;

public sealed record DisplayMonitor(
    nint Handle,
    string DeviceName,
    LayoutArea Bounds,
    LayoutArea WorkArea,
    bool IsPrimary
);
