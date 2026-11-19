using Tessel.Core.Models;

namespace Tessel.Platform.Windows.Windows;

public sealed class FocusedContainerBorder : IDisposable
{
    public bool Enabled { get; set; } = true;
    public uint Color { get; set; } = 0x00459AEE;
    public int Thickness { get; set; } = 2;
    private readonly OutlineOverlay _outline = new();
    private bool _disposed;

    public void SetBounds(LayoutArea area, nint dpiReferenceWindow)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Enabled)
        {
            Clear();
            return;
        }

        uint dpi = NativeChrome.DpiForWindow(dpiReferenceWindow);
        var bounds = NativeChrome.Rect(area.X, area.Y, area.X + area.Width, area.Y + area.Height);
        var colors = NativeChrome.Colors;
        _outline.Show(bounds, NativeChrome.Scale(Thickness, dpi), NativeChrome.Scale(8, dpi),
            colors.HighContrast ? colors.Text : Color, dpiReferenceWindow);
    }

    public void Clear() => _outline.Hide();

    public void Dispose()
    {
        if (_disposed)
            return;

        _outline.Dispose();
        _disposed = true;
    }
}
