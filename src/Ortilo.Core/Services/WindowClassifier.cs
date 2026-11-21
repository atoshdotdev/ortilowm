using Ortilo.Core.Models;

namespace Ortilo.Core.Services;

public static class WindowClassifier
{
    public static WindowClassification Classify(WindowMetadata window)
    {
        if (window.HasHigherIntegrity || window.IsCloaked || window.IsToolWindow)
            return WindowClassification.Ignored;

        if (window.HasOwner)
            return WindowClassification.Floating;

        if (window.IsMinimized)
            return WindowClassification.Tiled;

        return WindowClassification.Tiled;
    }
}
