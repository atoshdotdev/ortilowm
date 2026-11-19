using Tessel.Core.Models;

namespace Tessel.Core.Tests;

public class WorkspaceTests
{
    [Fact]
    public void Constructor_CreatesIndependentLayoutTree()
    {
        var first = new Workspace(1);
        var second = new Workspace(2);

        Assert.NotSame(first.LayoutTree, second.LayoutTree);
    }

    [Fact]
    public void LayoutTree_IsIndependentBetweenWorkspaces()
    {
        var first = new Workspace(1);
        var second = new Workspace(2);

        first.LayoutTree.AddWindow((nint)100);

        Assert.Contains((nint)100, first.LayoutTree.WindowHandles);
        Assert.Empty(second.LayoutTree.WindowHandles);
    }

    [Fact]
    public void LayoutTree_PersistsWhenWorkspaceIsReused()
    {
        var workspace = new Workspace(1);

        workspace.LayoutTree.AddWindow((nint)100);
        workspace.LayoutTree.AddWindow((nint)200);

        Assert.Equal(
            [(nint)100, (nint)200],
            workspace.LayoutTree.WindowHandles);
    }
}
