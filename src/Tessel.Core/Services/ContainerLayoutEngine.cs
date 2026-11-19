using Tessel.Core.Models;

namespace Tessel.Core.Services;

public static class ContainerLayoutEngine
{
    public static IReadOnlyList<WindowPlacement> Calculate(
        ContainerTree tree, LayoutArea area, IReadOnlySet<nint>? includedHandles = null) =>
        CalculateLayout(tree, area, includedHandles).WindowPlacements;

    public static ContainerLayoutResult CalculateLayout(
        ContainerTree tree, LayoutArea area, IReadOnlySet<nint>? includedHandles = null,
        int headerRowHeight = 0, int headerGap = 0)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentOutOfRangeException.ThrowIfNegative(headerRowHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(headerGap);

        var windows = new List<WindowPlacement>();
        var containers = new List<ContainerPlacement>();
        var headers = new List<ContainerHeaderPlacement>();
        CalculateNode(tree.Root, area);
        return new(windows, containers, headers);

        void CalculateNode(ContainerNode node, LayoutArea bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            if (node.IsLeaf)
            {
                if (node.WindowHandle is not nint handle || !IsIncluded(handle, includedHandles))
                    return;

                containers.Add(new(node.Id, bounds, IsSplit: false, handle));
                windows.Add(new(handle, bounds.X, bounds.Y, bounds.Width, bounds.Height));
                return;
            }

            var first = node.Children[0];
            var second = node.Children[1];
            int firstWeight = GetVisibleWeight(first, node.Orientation, includedHandles);
            int secondWeight = GetVisibleWeight(second, node.Orientation, includedHandles);
            if (firstWeight == 0 && secondWeight == 0)
                return;

            containers.Add(new(node.Id, bounds, IsSplit: true, WindowHandle: null));

            if (node.GroupLayout is LayoutType.Tabbed or LayoutType.Stacking)
            {
                var entries = GetGroupChildren(node, node.Orientation)
                    .Select(child => (Child: child, Handle: GetRepresentative(child, includedHandles)))
                    .Where(entry => entry.Handle.HasValue)
                    .ToArray();

                int selected = Array.FindIndex(entries, entry => entry.Handle == node.LastFocusedWindow);
                if (selected < 0)
                    selected = 0;

                int rows = node.GroupLayout == LayoutType.Stacking ? entries.Length : 1;
                int rowHeight = Math.Min(headerRowHeight, (bounds.Height - 1) / rows);
                int headerHeight = rowHeight * rows;
                headers.Add(new(node.Id, bounds with { Height = headerHeight }, node.GroupLayout,
                    entries.Select(entry => entry.Handle!.Value).ToArray(),
                    entries[selected].Child.Id, entries[selected].Handle!.Value));
                int reserved = headerHeight > 0 ? Math.Min(bounds.Height - 1, headerHeight + headerGap) : 0;
                CalculateNode(entries[selected].Child, bounds with
                {
                    Y = bounds.Y + reserved,
                    Height = bounds.Height - reserved
                });
                return;
            }

            if (firstWeight == 0 || secondWeight == 0)
            {
                CalculateNode(firstWeight > 0 ? first : second, bounds);
                return;
            }

            double ratio = GetSplitRatio(node, firstWeight, secondWeight);
            if (node.Orientation == ContainerOrientation.Horizontal)
            {
                int width = CalculateFirstSize(bounds.Width, ratio);
                CalculateNode(first, bounds with { Width = width });
                CalculateNode(second, bounds with { X = bounds.X + width, Width = bounds.Width - width });
            }
            else
            {
                int height = CalculateFirstSize(bounds.Height, ratio);
                CalculateNode(first, bounds with { Height = height });
                CalculateNode(second, bounds with { Y = bounds.Y + height, Height = bounds.Height - height });
            }
        }
    }

    internal static IEnumerable<ContainerNode> GetGroupChildren(
        ContainerNode node, ContainerOrientation? orientation)
    {
        foreach (var child in node.Children)
        {
            if (child.IsImplicitSplit && child.Orientation == orientation &&
                child.GroupLayout == LayoutType.ContainerTree)
            {
                foreach (var descendant in GetGroupChildren(child, orientation))
                    yield return descendant;
            }
            else
            {
                yield return child;
            }
        }
    }

    private static nint? GetRepresentative(ContainerNode node, IReadOnlySet<nint>? includedHandles)
    {
        if (node.LastFocusedWindow is nint selected && IsIncluded(selected, includedHandles) &&
            node.ContainsWindow(selected))
            return selected;

        return node.GetLeaves().FirstOrDefault(leaf =>
            leaf.WindowHandle is nint handle && IsIncluded(handle, includedHandles))?.WindowHandle;
    }

    private static bool IsIncluded(nint handle, IReadOnlySet<nint>? includedHandles) =>
        includedHandles is null || includedHandles.Contains(handle);

    public static double? GetVisibleSplitRatio(
        ContainerNode split, IReadOnlySet<nint>? includedHandles = null)
    {
        if (!split.IsSplit || split.GroupLayout != LayoutType.ContainerTree)
            return null;

        int first = GetVisibleWeight(split.Children[0], split.Orientation, includedHandles);
        int second = GetVisibleWeight(split.Children[1], split.Orientation, includedHandles);
        return first > 0 && second > 0 ? GetSplitRatio(split, first, second) : null;
    }

    private static double GetSplitRatio(ContainerNode split, int firstWeight, int secondWeight) =>
        split.HasCustomRatio ? split.Ratio : (double)firstWeight / (firstWeight + secondWeight);

    // Implicit splits along the same axis represent siblings in a single row or column.
    private static int GetVisibleWeight(ContainerNode node, ContainerOrientation? orientation,
        IReadOnlySet<nint>? includedHandles)
    {
        if (node.IsLeaf)
            return node.WindowHandle is nint handle && IsIncluded(handle, includedHandles) ? 1 : 0;

        if (!node.IsImplicitSplit || node.Orientation != orientation ||
            node.GroupLayout != LayoutType.ContainerTree)
            return node.Children.Any(child => GetVisibleWeight(child, node.Orientation, includedHandles) > 0) ? 1 : 0;

        return GetVisibleWeight(node.Children[0], orientation, includedHandles) +
            GetVisibleWeight(node.Children[1], orientation, includedHandles);
    }

    private static int CalculateFirstSize(int total, double ratio) =>
        Math.Clamp((int)Math.Round(total * ratio, MidpointRounding.AwayFromZero), 0, total);
}
