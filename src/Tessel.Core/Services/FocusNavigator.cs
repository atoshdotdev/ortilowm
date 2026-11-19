using Tessel.Core.Models;

namespace Tessel.Core.Services;

public static class FocusNavigator
{
    public static nint? FindNextInHeader(
        IReadOnlyList<WindowPlacement> placements,
        nint currentHandle,
        LayoutType layout,
        FocusDirection direction,
        bool wrap = false) =>
        FindNextInHeader(placements, static placement => placement.Handle, currentHandle, layout, direction, wrap);

    public static nint? FindNextInHeader(
        ContainerHeaderPlacement header, nint currentHandle, FocusDirection direction, bool wrap = false) =>
        FindNextInHeader(header.WindowHandles, static handle => handle, currentHandle, header.Layout, direction, wrap);

    private static nint? FindNextInHeader<T>(
        IReadOnlyList<T> placements, Func<T, nint> getHandle, nint currentHandle,
        LayoutType layout, FocusDirection direction, bool wrap)
    {
        int step = (layout, direction) switch
        {
            (LayoutType.Tabbed, FocusDirection.Left) or
            (LayoutType.Stacking, FocusDirection.Up) => -1,
            (LayoutType.Tabbed, FocusDirection.Right) or
            (LayoutType.Stacking, FocusDirection.Down) => 1,
            _ => 0
        };

        if (step == 0 || placements.Count < 2)
            return null;

        for (int index = 0; index < placements.Count; index++)
        {
            if (getHandle(placements[index]) != currentHandle)
                continue;

            int next = index + step;
            if (next >= 0 && next < placements.Count)
                return getHandle(placements[next]);

            return wrap ? getHandle(placements[step < 0 ? placements.Count - 1 : 0]) : null;
        }

        return null;
    }

    public static nint? FindNext(
        IReadOnlyList<WindowPlacement> placements,
        nint currentHandle,
        FocusDirection direction)
    {
        var current = placements.FirstOrDefault(
            p => p.Handle == currentHandle
        );

        if (current is null)
            return null;

        double currentCenterX = current.X + current.Width / 2.0;
        double currentCenterY = current.Y + current.Height / 2.0;

        WindowPlacement? bestCandidate = null;
        double bestScore = double.MaxValue;

        foreach (var candidate in placements)
        {
            if (candidate.Handle == currentHandle)
                continue;

            double candidateCenterX =
                candidate.X + candidate.Width / 2.0;

            double candidateCenterY =
                candidate.Y + candidate.Height / 2.0;

            double primaryDistance;
            double perpendicularDistance;

            switch (direction)
            {
                case FocusDirection.Left:
                    primaryDistance = currentCenterX - candidateCenterX;
                    perpendicularDistance =
                        Math.Abs(currentCenterY - candidateCenterY);
                    break;

                case FocusDirection.Right:
                    primaryDistance = candidateCenterX - currentCenterX;
                    perpendicularDistance =
                        Math.Abs(currentCenterY - candidateCenterY);
                    break;

                case FocusDirection.Up:
                    primaryDistance = currentCenterY - candidateCenterY;
                    perpendicularDistance =
                        Math.Abs(currentCenterX - candidateCenterX);
                    break;

                case FocusDirection.Down:
                    primaryDistance = candidateCenterY - currentCenterY;
                    perpendicularDistance =
                        Math.Abs(currentCenterX - candidateCenterX);
                    break;

                default:
                    return null;
            }

            if (primaryDistance <= 0)
                continue;

            double score = primaryDistance + perpendicularDistance * 2;

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidate = candidate;
            }
        }

        return bestCandidate?.Handle;
    }

    public static nint? FindNext(
    ContainerTree tree,
    nint currentHandle,
    FocusDirection direction)
    {
        return tree.FindAdjacentWindow(currentHandle, direction);
    }
}
