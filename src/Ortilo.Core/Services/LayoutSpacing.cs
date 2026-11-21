using Ortilo.Core.Models;

namespace Ortilo.Core.Services;

public static class LayoutSpacing
{
    public static LayoutArea Inset(LayoutArea area, int left, int top, int right, int bottom)
    {
        int width = Math.Max(0, area.Width);
        int height = Math.Max(0, area.Height);
        left = Math.Min(left, width);
        top = Math.Min(top, height);
        return new(area.X + left, area.Y + top,
            Math.Max(0, width - left - right), Math.Max(0, height - top - bottom));
    }

    public static IReadOnlyList<WindowPlacement> ApplyInnerGap(IReadOnlyList<WindowPlacement> placements, int gap)
    {
        if (gap == 0 || placements.Count < 2)
            return placements;

        return SpaceWindows(placements, [], gap);
    }

    public static ContainerLayoutResult ApplyInnerGap(ContainerLayoutResult layout, int gap)
    {
        if (gap == 0)
            return layout;
        if (layout.Headers.Count == 0)
            return layout with { WindowPlacements = ApplyInnerGap(layout.WindowPlacements, gap) };

        var headers = new ContainerHeaderPlacement[layout.Headers.Count];
        for (int index = 0; index < headers.Length; index++)
        {
            var header = layout.Headers[index];
            var bounds = ApplyGap(layout.WindowPlacements, layout.Headers, layout.WindowPlacements.Count + index, gap);
            headers[index] = header with { Area = new(bounds.X, bounds.Y, bounds.Width, bounds.Height) };
        }

        return layout with
        {
            Headers = headers,
            WindowPlacements = SpaceWindows(layout.WindowPlacements, layout.Headers, gap)
        };
    }

    public static LayoutDockEdges GetHeaderDockEdges(LayoutArea header, LayoutArea monitor, int? topBarBottom)
    {
        var edges = LayoutDockEdges.None;
        if (header.X == monitor.X) edges |= LayoutDockEdges.Left;
        if (header.Y == monitor.Y || header.Y == topBarBottom) edges |= LayoutDockEdges.Top;
        if (header.X + header.Width == monitor.X + monitor.Width) edges |= LayoutDockEdges.Right;
        if (header.Y + header.Height == monitor.Y + monitor.Height) edges |= LayoutDockEdges.Bottom;
        return edges;
    }

    private static WindowPlacement[] SpaceWindows(IReadOnlyList<WindowPlacement> windows,
        IReadOnlyList<ContainerHeaderPlacement> headers, int gap)
    {
        var result = new WindowPlacement[windows.Count];
        for (int index = 0; index < windows.Count; index++)
        {
            var area = ApplyGap(windows, headers, index, gap);
            result[index] = new(windows[index].Handle, area.X, area.Y, area.Width, area.Height);
        }
        return result;
    }

    private static (int X, int Y, int Width, int Height) ApplyGap(
        IReadOnlyList<WindowPlacement> windows, IReadOnlyList<ContainerHeaderPlacement> headers, int index, int gap)
    {
        int leading = gap / 2;
        int trailing = gap - leading;
        bool isHeader = index >= windows.Count;
        var tile = GetBounds(windows, headers, index);
        int left = 0, top = 0, right = 0, bottom = 0;
        for (int neighborIndex = 0; neighborIndex < windows.Count + headers.Count; neighborIndex++)
        {
            bool neighborIsHeader = neighborIndex >= windows.Count;
            if (neighborIndex == index || (!isHeader && !neighborIsHeader &&
                windows[index].Handle == windows[neighborIndex].Handle))
                continue;

            var neighbor = GetBounds(windows, headers, neighborIndex);
            if (tile.Y < neighbor.Y + neighbor.Height && neighbor.Y < tile.Y + tile.Height)
            {
                if (neighbor.X + neighbor.Width == tile.X) left = leading;
                if (tile.X + tile.Width == neighbor.X) right = trailing;
            }
            if (tile.X < neighbor.X + neighbor.Width && neighbor.X < tile.X + tile.Width)
            {
                // The space below a header belongs to its own content. The
                // header's configured vertical gap already governs that seam.
                if (!neighborIsHeader && neighbor.Y + neighbor.Height == tile.Y) top = leading;
                if (!isHeader && tile.Y + tile.Height == neighbor.Y) bottom = trailing;
            }
        }

        int width = tile.Width - left - right;
        int height = tile.Height - top - bottom;
        return (width > 0 ? tile.X + left : tile.X, height > 0 ? tile.Y + top : tile.Y,
            width > 0 ? width : tile.Width, height > 0 ? height : tile.Height);
    }

    private static (int X, int Y, int Width, int Height) GetBounds(
        IReadOnlyList<WindowPlacement> windows, IReadOnlyList<ContainerHeaderPlacement> headers, int index)
    {
        if (index < windows.Count)
        {
            var window = windows[index];
            return (window.X, window.Y, window.Width, window.Height);
        }

        var area = headers[index - windows.Count].Area;
        return (area.X, area.Y, area.Width, area.Height);
    }
}
