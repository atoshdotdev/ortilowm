using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

public sealed class FocusedWindowBorder : IDisposable
{
    public bool Enabled { get; set; } = true;
    public uint? Color { get; set; }
    public int Thickness { get; set; } = 1;

    private readonly OutlineOverlay _outline = new();
    private nint _focusedWindow;
    private bool _disposed;

    public FocusedWindowBorder() => NativeChrome.PaletteChanged += Refresh;

    private void Refresh()
    {
        if (_focusedWindow != 0)
            SetFocusedWindow(_focusedWindow);
    }

    public static bool IsInternalWindow(nint handle) => OutlineOverlay.IsInternalWindow(handle);

    public void SetFocusedWindow(nint handle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Enabled || handle == nint.Zero ||
            !WindowApi.IsWindow(handle) ||
            !WindowApi.IsWindowVisible(handle) ||
            WindowApi.IsIconic(handle) ||
            (!WindowFrame.TryGetVisibleBounds(handle, out var bounds) &&
             !WindowApi.GetWindowRect(handle, out bounds)))
        {
            Clear();
            return;
        }

        uint dpi = NativeChrome.DpiForWindow(handle);
        int radius = WindowApi.IsZoomed(handle) || FullscreenManager.IsFullscreen(handle)
            ? 0
            : NativeChrome.Scale(8, dpi);
        var colors = NativeChrome.Colors;

        _focusedWindow = handle;
        _outline.Show(bounds, NativeChrome.Scale(Thickness, dpi), radius,
            colors.HighContrast ? colors.Text : Color ?? colors.Accent, handle);
    }

    public void Clear(nint handle)
    {
        if (handle != nint.Zero && handle == _focusedWindow)
            Clear();
    }

    public void Clear()
    {
        _focusedWindow = nint.Zero;
        _outline.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        NativeChrome.PaletteChanged -= Refresh;
        _outline.Dispose();
        _focusedWindow = nint.Zero;
        _disposed = true;
    }
}
