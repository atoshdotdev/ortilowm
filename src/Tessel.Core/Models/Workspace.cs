namespace Tessel.Core.Models;

public sealed class Workspace(int number)
{
    public int Number { get; } = number;

    public LayoutType Layout { get; set; } = LayoutType.ContainerTree;

    public List<nint> WindowHandles { get; } = [];

    public HashSet<nint> FloatingWindowHandles { get; } = [];

    public nint? SelectedWindowHandle { get; set; }

    public ContainerTree LayoutTree { get; private init; } = new();

    public Workspace CopyForLayoutPreview()
    {
        var copy = new Workspace(Number)
        {
            Layout = Layout,
            SelectedWindowHandle = SelectedWindowHandle,
            LayoutTree = LayoutTree.Copy()
        };
        copy.WindowHandles.AddRange(WindowHandles);
        copy.FloatingWindowHandles.UnionWith(FloatingWindowHandles);
        return copy;
    }
}
