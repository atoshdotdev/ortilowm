
using Ortilo.Core.Models;
using Ortilo.Core.Services;

namespace Ortilo.Core.Tests;

public class WindowLayoutEngineTests
{
    private static WindowMetadata CreateWindow(long handle)
    {
        return new WindowMetadata(
            (nint)handle,
            $"Window {handle}",
            1,
            false,
            false,
            false,
            false
        );
    }

    [Fact]
    public void Calculate_WithNoWindows_ReturnsEmpty()
    {
        var result = WindowLayoutEngine.Calculate(
            [],
            new LayoutArea(0, 0, 1920, 1080),
            LayoutType.MasterStack
        );

        Assert.Empty(result);
    }

    [Fact]
    public void Calculate_WithInvalidArea_ReturnsEmpty()
    {
        var result = WindowLayoutEngine.Calculate(
            [CreateWindow(1)],
            new LayoutArea(0, 0, 0, 1080),
            LayoutType.MasterStack
        );

        Assert.Empty(result);
    }

    [Fact]
    public void MasterStack_SplitsFirstWindowFromRemainingWindows()
    {
        var windows = new[]
        {
            CreateWindow(1),
            CreateWindow(2),
            CreateWindow(3)
        };

        var result = WindowLayoutEngine.Calculate(
            windows,
            new LayoutArea(100, 50, 1000, 600),
            LayoutType.MasterStack
        );

        Assert.Equal(3, result.Count);

        Assert.Equal(
            new WindowPlacement(1, 100, 50, 500, 600),
            result[0]
        );

        Assert.Equal(
            new WindowPlacement(2, 600, 50, 500, 300),
            result[1]
        );

        Assert.Equal(
            new WindowPlacement(3, 600, 350, 500, 300),
            result[2]
        );
    }

    [Fact]
    public void Columns_DistributesRemainingPixelsWithoutGaps()
    {
        var windows = new[]
        {
            CreateWindow(1),
            CreateWindow(2),
            CreateWindow(3)
        };

        var result = WindowLayoutEngine.Calculate(
            windows,
            new LayoutArea(0, 0, 1000, 600),
            LayoutType.Columns
        );

        Assert.Equal(3, result.Count);

        Assert.Equal(333, result[0].Width);
        Assert.Equal(333, result[1].Width);
        Assert.Equal(334, result[2].Width);

        Assert.Equal(1000, result.Sum(p => p.Width));
    }

    [Fact]
    public void Rows_DistributesRemainingPixelsWithoutGaps()
    {
        var windows = new[]
        {
            CreateWindow(1),
            CreateWindow(2)
        };

        var result = WindowLayoutEngine.Calculate(
            windows,
            new LayoutArea(0, 0, 1000, 601),
            LayoutType.Rows
        );

        Assert.Equal(2, result.Count);
        Assert.Equal(300, result[0].Height);
        Assert.Equal(301, result[1].Height);
        Assert.Equal(601, result.Sum(p => p.Height));
    }

    [Theory]
    [InlineData(LayoutType.Stacking)]
    [InlineData(LayoutType.Tabbed)]
    public void StackingAndTabbed_GiveEveryWindowSameArea(
        LayoutType layout)
    {
        var windows = new[]
        {
            CreateWindow(1),
            CreateWindow(2),
            CreateWindow(3)
        };

        var area = new LayoutArea(100, 50, 1200, 800);

        var result = WindowLayoutEngine.Calculate(
            windows,
            area,
            layout
        );

        Assert.Equal(3, result.Count);

        foreach (var placement in result)
        {
            Assert.Equal(area.X, placement.X);
            Assert.Equal(area.Y, placement.Y);
            Assert.Equal(area.Width, placement.Width);
            Assert.Equal(area.Height, placement.Height);
        }
    }
}
