using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.DwmApi;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

internal sealed class ComposedBackdrop(nint window, bool suppressFrameShadow = false) : IDisposable
{
    private const float BlurOpacity = 0.18f;
    private readonly NativeBackdrop _fallback = new(window, suppressFrameShadow);
    private HostBackdropVisual? _visual;
    private nint _content;
    private bool _compositionFailed;
    private bool _enabled;
    private bool _initialized;

    internal bool IsContentWindow(nint handle) => handle == _content;

    internal bool SetSystemCorners(bool rounded) => _fallback.SetSystemCorners(rounded);

    internal void Refresh()
    {
        _initialized = true;
        var colors = NativeChrome.Colors;
        _enabled = !_compositionFailed && colors.Transparency && !colors.HighContrast;
        if (_enabled && _visual is null)
        {
            try
            {
                const uint layeredTransparent = 0x00080000 | 0x00000020;
                const uint child = 0x40000000;
                _content = WindowClassApi.CreateWindowEx(layeredTransparent, "STATIC", "", child,
                    0, 0, 0, 0, window, 0, 0, 0);
                if (_content == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());

                _visual = HostBackdropVisual.Create(window);
            }
            catch (Exception error) when (error is ExternalException or UnauthorizedAccessException or EntryPointNotFoundException)
            {
                DisableAfterFailure(error.Message);
            }
        }

        if (!_enabled && _visual is not null)
        {
            try
            {
                _visual.Update(0, 0, 0);
                WindowApi.ShowWindow(_content, 0);
            }
            catch (Exception error) when (error is ExternalException or UnauthorizedAccessException)
            {
                DisableAfterFailure(error.Message);
            }
        }

        _fallback.Refresh(suppressSystemBackdrop: true);
    }

    internal void Paint(uint dpi, int width, int height, Action<nint, NativeChrome.Palette> draw)
    {
        if (!_initialized)
            Refresh();

        if (_enabled && _visual is not null)
        {
            try
            {
                var colors = NativeChrome.Colors;
                // Blend the native blur over the theme's solid background.
                _visual.Update(width, height, BlurOpacity);
                if (PaintComposed(dpi, width, height, colors, draw))
                    return;

                DisableAfterFailure("Could not paint the layered controls.");
            }
            catch (Exception error) when (error is ExternalException or UnauthorizedAccessException)
            {
                DisableAfterFailure(error.Message);
            }

            WindowApi.InvalidateRect(window, 0, false);
        }

        _fallback.Paint(dpi, draw);
    }

    private bool PaintComposed(uint dpi, int width, int height, NativeChrome.Palette colors,
        Action<nint, NativeChrome.Palette> draw)
    {
        nint dc = WindowPaintingApi.BeginPaint(window, out var paint);
        try
        {
            if (dc == 0 || width <= 0 || height <= 0)
                return true;

            NativeChrome.Fill(dc, paint.Paint, colors.Background);
            var bounds = NativeChrome.Rect(0, 0, width, height);
            if (!_fallback.RenderToBuffer(dc, bounds, dpi, colors, draw,
                    source => Present(source, width, height)))
                return false;

            if (!WindowApi.IsWindowVisible(_content))
                WindowApi.ShowWindow(_content, 4);
            return true;
        }
        finally
        {
            WindowPaintingApi.EndPaint(window, in paint);
        }
    }

    private void Present(nint source, int width, int height)
    {
        if (!LayeredWindowApi.Present(_content, source, width, height))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    private void DisableAfterFailure(string reason)
    {
        Console.Error.WriteLine($"Host backdrop unavailable for window 0x{window:X}: {reason}");
        _compositionFailed = true;
        _enabled = false;
        ReleaseVisual();
    }

    private void ReleaseVisual()
    {
        _visual?.Dispose();
        _visual = null;
        if (_content != 0)
        {
            if (WindowApi.IsWindow(_content))
                WindowApi.DestroyWindow(_content);
            _content = 0;
        }
    }

    public void Dispose()
    {
        ReleaseVisual();
        _fallback.Dispose();
    }
}
