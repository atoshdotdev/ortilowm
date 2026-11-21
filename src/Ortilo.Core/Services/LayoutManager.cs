
using Ortilo.Core.Models;

namespace Ortilo.Core.Services;

public sealed class LayoutManager
{
    public LayoutType CurrentLayout { get; private set; } =
        LayoutType.ContainerTree;

    public void SetLayout(LayoutType layout)
    {
        CurrentLayout = layout;
    }
}
