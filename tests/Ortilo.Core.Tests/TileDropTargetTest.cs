using Ortilo.Core.Models;
using Ortilo.Core.Services;

namespace Ortilo.Core.Tests;

public class TileDropTargetTests
{
    private static readonly WindowPlacement[] Tiles =
    [
        new(1, -200, 100, 100, 100),
        new(2, -100, 100, 100, 100)
    ];

    [Theory]
    [InlineData(-150, 150, 1, null)]
    [InlineData(-92, 150, 2, FocusDirection.Left)]
    [InlineData(-8, 150, 2, FocusDirection.Right)]
    [InlineData(-50, 108, 2, FocusDirection.Up)]
    [InlineData(-50, 192, 2, FocusDirection.Down)]
    [InlineData(-100, 150, 2, FocusDirection.Left)]
    public void Find_PicksTileAndDropZone(int x, int y, int handle, FocusDirection? edge)
    {
        Assert.Equal(new TileDropTarget(handle, edge), TileDropTarget.Find(Tiles, x, y));
    }

    [Theory]
    [InlineData(0, 150)]
    [InlineData(-50, 99)]
    [InlineData(-50, 200)]
    public void Find_ReturnsNullOutsideTiles(int x, int y)
    {
        Assert.Null(TileDropTarget.Find(Tiles, x, y));
    }
}
