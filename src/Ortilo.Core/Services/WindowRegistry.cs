using Ortilo.Core.Models;

namespace Ortilo.Core.Services;

public sealed class WindowRegistry
{
    private readonly Dictionary<nint, WindowMetadata> _windows = [];

    public IReadOnlyCollection<WindowMetadata> TiledWindows =>
        GetWindowsByClassification(WindowClassification.Tiled);

    public IReadOnlyCollection<WindowMetadata> FloatingWindows =>
        GetWindowsByClassification(WindowClassification.Floating);

    public IReadOnlyCollection<WindowMetadata> IgnoredWindows =>
        GetWindowsByClassification(WindowClassification.Ignored);

    public IReadOnlyCollection<WindowMetadata> Windows =>
        _windows.Values;

    public void AddOrUpdate(WindowMetadata window)
    {
        _windows[window.Handle] = window;
    }

    public bool Remove(nint handle)
    {
        return _windows.Remove(handle);
    }

    public bool TryGet(nint handle, out WindowMetadata? window)
    {
        return _windows.TryGetValue(handle, out window);
    }

    public void Clear()
    {
        _windows.Clear();
    }

    public void Synchronize(IEnumerable<WindowMetadata> currentWindows)
    {
        var current = currentWindows.ToDictionary(w => w.Handle);

        foreach (var handle in _windows.Keys.ToArray())
        {
            if (!current.ContainsKey(handle))
                _windows.Remove(handle);
        }

        foreach (var window in current.Values)
        {
            AddOrUpdate(window);
        }
    }

    private IReadOnlyCollection<WindowMetadata> GetWindowsByClassification(
    WindowClassification classification)
    {
        return [.. _windows.Values.Where(window => WindowClassifier.Classify(window) == classification)];
    }
}
