using Ortilo.Core.Models;

namespace Ortilo.Core.Services;

public sealed class WindowOrderManager
{
    private readonly List<nint> _handles = [];

    public IReadOnlyList<nint> Handles => _handles;

    public void Synchronize(IEnumerable<WindowMetadata> windows)
    {
        var currentHandles = windows
            .Select(window => window.Handle)
            .ToHashSet();

        _handles.RemoveAll(handle => !currentHandles.Contains(handle));

        foreach (var handle in currentHandles)
        {
            if (!_handles.Contains(handle))
                _handles.Add(handle);
        }
    }

    public bool Swap(nint firstHandle, nint secondHandle)
    {
        int firstIndex = _handles.IndexOf(firstHandle);
        int secondIndex = _handles.IndexOf(secondHandle);

        if (firstIndex < 0 || secondIndex < 0)
            return false;

        (_handles[firstIndex], _handles[secondIndex]) =
            (_handles[secondIndex], _handles[firstIndex]);

        return true;
    }

    public bool Move(
    IReadOnlyList<WindowPlacement> placements,
    nint currentHandle,
    FocusDirection direction)
    {
        nint? nextHandle = FocusNavigator.FindNext(
            placements,
            currentHandle,
            direction
        );

        if (nextHandle is null)
            return false;

        return Swap(currentHandle, nextHandle.Value);
    }
}
