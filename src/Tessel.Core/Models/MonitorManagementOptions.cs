namespace Tessel.Core.Models;

public sealed record MonitorManagementOptions
{
    public MonitorManagementMode Mode { get; init; } =
        MonitorManagementMode.All;

    public IReadOnlyList<string> SelectedDeviceNames { get; init; } = [];
    public IReadOnlyDictionary<int, string> WorkspaceDeviceNames { get; init; } =
        new Dictionary<int, string>();
}
