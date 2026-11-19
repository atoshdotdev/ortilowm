namespace Tessel.Core.Models;

public sealed class ContainerNode
{
    private List<ContainerNode> _children = [];

    internal ContainerNode Copy(ContainerNode? parent)
    {
        var copy = (ContainerNode)MemberwiseClone();
        copy.Parent = parent;
        copy._children = _children.Select(child => child.Copy(copy)).ToList();
        return copy;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public ContainerNode? Parent { get; private set; }

    public IReadOnlyList<ContainerNode> Children => _children;

    public nint? WindowHandle { get; private set; }

    public ContainerOrientation? Orientation { get; private set; }
    public LayoutType GroupLayout { get; private set; } = LayoutType.ContainerTree;
    internal nint? LastFocusedWindow { get; set; }

    public ContainerOrientation? PendingSplitOrientation { get; private set; }

    public double Ratio { get; private set; } = 0.5;
    internal bool IsImplicitSplit { get; private set; }
    internal bool HasCustomRatio { get; private set; }

    public bool IsLeaf => _children.Count == 0;

    public bool IsSplit => _children.Count == 2;

    private ContainerNode()
    {
    }

    public static ContainerNode CreateRoot()
    {
        return new ContainerNode();
    }

    public static ContainerNode CreateWindow(nint handle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(handle, nint.Zero);

        return new ContainerNode
        {
            WindowHandle = handle
        };
    }

    public bool ContainsWindow(nint handle)
    {
        if (WindowHandle == handle)
            return true;

        return _children.Any(child => child.ContainsWindow(handle));
    }

    public ContainerNode? FindWindow(nint handle)
    {
        if (WindowHandle == handle)
            return this;

        foreach (var child in _children)
        {
            var result = child.FindWindow(handle);

            if (result is not null)
                return result;
        }

        return null;
    }

    public IEnumerable<ContainerNode> GetLeaves()
    {
        if (IsLeaf)
        {
            if (WindowHandle is not null)
                yield return this;

            yield break;
        }

        foreach (var child in _children)
        {
            foreach (var leaf in child.GetLeaves())
                yield return leaf;
        }
    }

    public void SetRatio(double ratio)
    {
        if (!IsSplit)
        {
            throw new InvalidOperationException(
                "Only split containers can have their ratio changed.");
        }

        ValidateRatio(ratio);
        Ratio = ratio;
        HasCustomRatio = true;
    }

    internal void SetPendingSplitOrientation(
    ContainerOrientation orientation)
    {
        if (!Enum.IsDefined(orientation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(orientation));
        }

        PendingSplitOrientation = orientation;
    }

    internal void SetWindow(nint handle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(handle, nint.Zero);

        if (!IsLeaf || WindowHandle is not null)
        {
            throw new InvalidOperationException(
                "Node is not an empty leaf.");
        }

        WindowHandle = handle;
    }

    internal void ClearWindow()
    {
        WindowHandle = null;
        PendingSplitOrientation = null;
    }

    internal void SetGroupLayout(LayoutType layout)
    {
        GroupLayout = layout;
        PendingSplitOrientation = null;
    }

    internal void RotateSplit(ContainerOrientation orientation)
    {
        var previous = Orientation;
        Orientation = orientation;
        SetGroupLayout(LayoutType.ContainerTree);
        // Implicit binary links are the continuation of this row or column.
        foreach (var child in _children)
        {
            if (child.IsImplicitSplit && child.GroupLayout == LayoutType.ContainerTree &&
                child.Orientation == previous)
                child.RotateSplit(orientation);
        }
    }

    internal void SplitOccupiedLeaf(
        ContainerOrientation orientation,
        ContainerNode second,
        double ratio = 0.5,
        bool secondFirst = false,
        bool automatic = false
    )
    {
        ArgumentNullException.ThrowIfNull(second);

        if (!IsLeaf || WindowHandle is null)
        {
            throw new InvalidOperationException(
                "Only an occupied leaf can be split.");
        }

        if (ReferenceEquals(this, second) || second.Parent is not null)
        {
            throw new InvalidOperationException(
                "The new child must be an independent node.");
        }

        ValidateRatio(ratio);

        var existing = CreateWindow(WindowHandle.Value);

        WindowHandle = null;
        Orientation = orientation;
        Ratio = ratio;
        IsImplicitSplit = automatic;
        HasCustomRatio = ratio != 0.5;
        PendingSplitOrientation = null;

        if (secondFirst)
        {
            AddChild(second);
            AddChild(existing);
        }
        else
        {
            AddChild(existing);
            AddChild(second);
        }
    }

    internal void SplitContainer(
        ContainerOrientation orientation,
        ContainerNode second,
        double ratio = 0.5,
        bool secondFirst = false,
        bool automatic = false
    )
    {
        ArgumentNullException.ThrowIfNull(second);

        if (second.Parent is not null ||
            ReferenceEquals(this, second))
        {
            throw new InvalidOperationException(
                "The new child must be an independent node.");
        }

        ValidateRatio(ratio);

        var existing = CreateRoot();

        existing.Orientation = Orientation;
        existing.GroupLayout = GroupLayout;
        existing.LastFocusedWindow = LastFocusedWindow;
        existing.Ratio = Ratio;
        existing.HasCustomRatio = HasCustomRatio;
        PendingSplitOrientation = null;

        foreach (var child in _children.ToArray())
        {
            _children.Remove(child);
            child.Parent = null;
            existing.AddChild(child);
        }

        existing.WindowHandle = WindowHandle;

        WindowHandle = null;
        Orientation = orientation;
        GroupLayout = LayoutType.ContainerTree;
        Ratio = ratio;
        IsImplicitSplit = automatic;
        HasCustomRatio = ratio != 0.5;

        if (secondFirst)
        {
            AddChild(second);
            AddChild(existing);
        }
        else
        {
            AddChild(existing);
            AddChild(second);
        }
    }

    internal void CollapseTo(ContainerNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (_children.Count == 0 || !_children.Contains(child))
        {
            throw new InvalidOperationException(
                "The supplied node must be a child of this split.");
        }

        foreach (var existingChild in _children)
            existingChild.Parent = null;

        _children.Clear();

        WindowHandle = child.WindowHandle;
        Orientation = child.Orientation;
        if (!child.IsLeaf || child.GroupLayout != LayoutType.ContainerTree)
            GroupLayout = child.GroupLayout;
        LastFocusedWindow = child.LastFocusedWindow;
        Ratio = child.Ratio;
        IsImplicitSplit = child.IsImplicitSplit;
        HasCustomRatio = child.HasCustomRatio;
        PendingSplitOrientation = child.PendingSplitOrientation ?? PendingSplitOrientation;

        child.WindowHandle = null;
        child.Orientation = null;
        child.Ratio = 0.5;
        child.IsImplicitSplit = false;
        child.HasCustomRatio = false;
        child.PendingSplitOrientation = null;

        foreach (var grandchild in child._children.ToArray())
        {
            child._children.Remove(grandchild);
            grandchild.Parent = null;

            AddChild(grandchild);
        }
    }

    private void AddChild(ContainerNode child)
    {
        if (child.Parent is not null)
        {
            throw new InvalidOperationException(
                "Node already has a parent.");
        }

        child.Parent = this;
        _children.Add(child);
    }

    private static void ValidateRatio(double ratio)
    {
        if (double.IsNaN(ratio) ||
            double.IsInfinity(ratio) ||
            ratio <= 0 ||
            ratio >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ratio),
                "Split ratio must be greater than zero and less than one.");
        }
    }

    internal void SwapWindowHandles(ContainerNode other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!IsLeaf || !other.IsLeaf ||
            WindowHandle is null || other.WindowHandle is null)
        {
            throw new InvalidOperationException(
                "Both nodes must be occupied leaves.");
        }

        (WindowHandle, other.WindowHandle) =
            (other.WindowHandle, WindowHandle);
    }

    internal void DetachChild(ContainerNode child)
    {
        ArgumentNullException.ThrowIfNull(child);

        if (!_children.Remove(child))
        {
            throw new InvalidOperationException(
                "The supplied node is not a child of this container.");
        }

        child.Parent = null;
    }

    internal void ReverseChildren() => _children.Reverse();

    internal static void SwapChildPositions(ContainerNode first, ContainerNode second)
    {
        var firstParent = first.Parent!;
        var secondParent = second.Parent!;
        int firstIndex = firstParent._children.IndexOf(first);
        int secondIndex = secondParent._children.IndexOf(second);
        firstParent._children[firstIndex] = second;
        secondParent._children[secondIndex] = first;
        first.Parent = secondParent;
        second.Parent = firstParent;
    }
}
