using Ortilo.Core.Models;

namespace Ortilo.Platform.Windows.Windows;

public sealed class FocusedContainerBorder : IDisposable
{
    public bool Enabled { get; set; } = true;
    public uint? Color { get; set; }
    public int Thickness { get; set; } = 2;
    private readonly OutlineOverlay _outline = new();
    private bool _disposed;
    private LayoutArea? _area;
    private nint _referenceWindow;

    public FocusedContainerBorder() => NativeChrome.PaletteChanged += Refresh;

    private void Refresh()
    {
        if (_area is { } area)
            SetBounds(area, _referenceWindow);
    }

    public void SetBounds(LayoutArea area, nint dpiReferenceWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Enabled)
        {
            Clear();
            return;
        }

        uint dpi = NativeChrome.DpiForWindow(dpiReferenceWindow);
        _area = area;
        _referenceWindow = dpiReferenceWindow;
        var bounds = NativeChrome.Rect(area.X, area.Y, area.X + area.Width, area.Y + area.Height);
        var colors = NativeChrome.Colors;
        _outline.Show(bounds, NativeChrome.Scale(Thickness, dpi), NativeChrome.Scale(8, dpi),
            colors.HighContrast ? colors.Text : Color ?? colors.Accent, dpiReferenceWindow);
    }

    public void Clear()
    {
        _area = null;
        _referenceWindow = 0;
        _outline.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        NativeChrome.PaletteChanged -= Refresh;
        _outline.Dispose();
        _disposed = true;
    }
}
