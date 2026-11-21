namespace Ortilo.Core.Models;

public sealed record ContainerPlacement(
    Guid NodeId,
    LayoutArea Area,
    bool IsSplit,
    nint? WindowHandle
);

public sealed record ContainerHeaderPlacement(
    Guid NodeId,
    LayoutArea Area,
    LayoutType Layout,
    IReadOnlyList<nint> WindowHandles,
    Guid SelectedBranchId,
    nint SelectedHandle
);

public sealed record ContainerLayoutResult(
    IReadOnlyList<WindowPlacement> WindowPlacements,
    IReadOnlyList<ContainerPlacement> ContainerPlacements,
    IReadOnlyList<ContainerHeaderPlacement> Headers
);
