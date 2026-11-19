using Tessel.Core.Models;

namespace Tessel.Core.Services;

public static class WindowLayoutEngine
{
    public static IReadOnlyList<WindowPlacement> Calculate(
        IReadOnlyList<WindowMetadata> windows,
        LayoutArea area,
        LayoutType layoutType)
    {
        if (windows.Count == 0 || area.Width <= 0 || area.Height <= 0)
            return [];

        return layoutType switch
        {
            LayoutType.MasterStack => CalculateMasterStack(windows, area),
            LayoutType.Columns => CalculateColumns(windows, area),
            LayoutType.Rows => CalculateRows(windows, area),
            LayoutType.Stacking => CalculateStacking(windows, area),
            LayoutType.Tabbed => CalculateTabbed(windows, area),
            _ => throw new ArgumentOutOfRangeException(nameof(layoutType))
        };
    }

    private static IReadOnlyList<WindowPlacement> CalculateMasterStack(
        IReadOnlyList<WindowMetadata> windows,
        LayoutArea area)
    {
        var placements = new List<WindowPlacement>();

        if (windows.Count == 1)
        {
            placements.Add(new WindowPlacement(
                windows[0].Handle,
                area.X,
                area.Y,
                area.Width,
                area.Height
            ));

            return placements;
        }

        int leftWidth = area.Width / 2;
        int rightWidth = area.Width - leftWidth;
        int stackCount = windows.Count - 1;

        placements.Add(new WindowPlacement(
            windows[0].Handle,
            area.X,
            area.Y,
            leftWidth,
            area.Height
        ));

        for (int i = 0; i < stackCount; i++)
        {
            int top = (int)((long)area.Height * i / stackCount);
            int bottom = (int)((long)area.Height * (i + 1) / stackCount);

            placements.Add(new WindowPlacement(
                windows[i + 1].Handle,
                area.X + leftWidth,
                area.Y + top,
                rightWidth,
                bottom - top
            ));
        }

        return placements;
    }

    private static IReadOnlyList<WindowPlacement> CalculateColumns(
        IReadOnlyList<WindowMetadata> windows,
        LayoutArea area)
    {
        var placements = new List<WindowPlacement>();

        for (int i = 0; i < windows.Count; i++)
        {
            int left = (int)((long)area.Width * i / windows.Count);
            int right = (int)((long)area.Width * (i + 1) / windows.Count);

            placements.Add(new WindowPlacement(
                windows[i].Handle,
                area.X + left,
                area.Y,
                right - left,
                area.Height
            ));
        }

        return placements;
    }

    private static IReadOnlyList<WindowPlacement> CalculateRows(
        IReadOnlyList<WindowMetadata> windows,
        LayoutArea area)
    {
        var placements = new List<WindowPlacement>();

        for (int i = 0; i < windows.Count; i++)
        {
            int top = (int)((long)area.Height * i / windows.Count);
            int bottom = (int)((long)area.Height * (i + 1) / windows.Count);

            placements.Add(new WindowPlacement(
                windows[i].Handle,
                area.X,
                area.Y + top,
                area.Width,
                bottom - top
            ));
        }

        return placements;
    }

    private static IReadOnlyList<WindowPlacement> CalculateStacking(
    IReadOnlyList<WindowMetadata> windows,
    LayoutArea area)
    {
        return [.. windows
            .Select(window => new WindowPlacement(
                window.Handle,
                area.X,
                area.Y,
                area.Width,
                area.Height
            ))];
    }

    private static IReadOnlyList<WindowPlacement> CalculateTabbed(
        IReadOnlyList<WindowMetadata> windows,
        LayoutArea area)
    {
        return [.. windows
            .Select(window => new WindowPlacement(
                window.Handle,
                area.X,
                area.Y,
                area.Width,
                area.Height
            ))];
    }
}
