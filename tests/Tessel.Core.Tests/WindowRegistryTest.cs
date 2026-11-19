using Tessel.Core.Models;
using Tessel.Core.Services;

namespace Tessel.Core.Tests;

public class WindowRegistryTests
{
    [Theory]
    [InlineData(false, false, false, false, WindowClassification.Tiled)]
    [InlineData(false, false, false, true, WindowClassification.Tiled)]
    [InlineData(true, false, false, false, WindowClassification.Floating)]
    [InlineData(true, false, false, true, WindowClassification.Floating)]
    [InlineData(true, true, false, false, WindowClassification.Ignored)]
    [InlineData(true, false, true, false, WindowClassification.Ignored)]
    [InlineData(false, true, false, false, WindowClassification.Ignored)]
    [InlineData(false, false, true, false, WindowClassification.Ignored)]
    public void Classify_UsesWindowOwnershipAndVisibilityFlags(
        bool owned, bool tool, bool cloaked, bool minimized, WindowClassification expected)
    {
        var window = new WindowMetadata(1, "Window", 100, owned, tool, cloaked, minimized);

        Assert.Equal(expected, WindowClassifier.Classify(window));
    }

    [Theory]
    [InlineData(false, WindowClassification.Tiled)]
    [InlineData(true, WindowClassification.Ignored)]
    public void Classify_NormalWindowStyleRespectsIntegrityBoundary(bool higherIntegrity, WindowClassification expected)
    {
        var window = new WindowMetadata(1, "Window", 100, false, false, false, false, higherIntegrity);

        Assert.Equal(expected, WindowClassifier.Classify(window));
    }

    [Fact]
    public void Synchronize_RemovesStaleHandlesAndReclassifiesUpdatedWindows()
    {
        var registry = new WindowRegistry();
        var window = new WindowMetadata(1, "Window", 100, false, false, false, false);
        var stale = window with { Handle = 2 };
        registry.Synchronize([window, stale]);

        var updated = window with { Title = "Modal dialog", HasOwner = true };
        var ignored = window with { Handle = 3, IsCloaked = true };
        var minimized = window with { Handle = 4, IsMinimized = true };
        registry.Synchronize([updated, ignored, minimized]);

        Assert.Equal(3, registry.Windows.Count);
        Assert.False(registry.TryGet(stale.Handle, out _));
        Assert.True(registry.TryGet(window.Handle, out var found));
        Assert.Equal(updated, found);
        Assert.Equal(updated, Assert.Single(registry.FloatingWindows));
        Assert.Equal(ignored, Assert.Single(registry.IgnoredWindows));
        Assert.Equal(minimized, Assert.Single(registry.TiledWindows));

        registry.Synchronize([]);

        Assert.Empty(registry.Windows);
    }
}
