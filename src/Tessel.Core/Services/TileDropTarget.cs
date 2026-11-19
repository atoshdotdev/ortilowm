using Tessel.Core.Models;

namespace Tessel.Core.Services;

public sealed record TileDropTarget(nint Handle, FocusDirection? Edge)
{
    public static bool Contains(WindowPlacement tile, int x, int y)
    {
        long offsetX = (long)x - tile.X;
        long offsetY = (long)y - tile.Y;
        return offsetX >= 0 && offsetY >= 0 && offsetX < tile.Width && offsetY < tile.Height;
    }

    public static TileDropTarget? Find(IReadOnlyList<WindowPlacement> placements, int x, int y)
    {
        foreach (var tile in placements)
        {
            if (!Contains(tile, x, y))
                continue;

            long offsetX = (long)x - tile.X;
            long offsetY = (long)y - tile.Y;
            double horizontal = (offsetX + 0.5) / tile.Width;
            double vertical = (offsetY + 0.5) / tile.Height;
            double nearestHorizontal = Math.Min(horizontal, 1 - horizontal);
            double nearestVertical = Math.Min(vertical, 1 - vertical);

            if (Math.Min(nearestHorizontal, nearestVertical) >= 0.25)
                return new(tile.Handle, null);

            FocusDirection edge = nearestHorizontal <= nearestVertical
                ? (horizontal < 0.5 ? FocusDirection.Left : FocusDirection.Right)
                : (vertical < 0.5 ? FocusDirection.Up : FocusDirection.Down);
            return new(tile.Handle, edge);
        }

        return null;
    }
}
