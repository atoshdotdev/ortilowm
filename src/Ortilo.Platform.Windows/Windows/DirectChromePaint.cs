using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.Gdi32;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

// The overlay windows share a render target on their UI thread. Coordinates
// are already in physical pixels, so the target itself stays at 96 DPI.
internal sealed unsafe partial class DirectChromePaint : IDisposable
{
    [ThreadStatic]
    private static DirectChromePaint? _renderer;
    [ThreadStatic]
    internal static DirectChromePaint? Current;

    private nint _factory;
    private nint _writeFactory;
    private nint _target;
    private nint _brush;
    private readonly Dictionary<(uint Dpi, int SymbolSize), nint> _formats = [];
    private NativeWindowRect _bounds;
    private nint _dc;
    private uint _dpi;
    private bool _failed;

    internal static DirectChromePaint? TryBegin(nint dc, NativeWindowRect bounds, uint dpi, uint? background)
    {
        if (Current is not null)
            return null;

        var renderer = _renderer ??= new DirectChromePaint();
        if (!renderer.EnsureTarget() ||
            ((delegate* unmanaged[Stdcall]<nint, nint, NativeWindowRect*, int>)Slot(renderer._target, 57))
                (renderer._target, dc, &bounds) < 0) // ID2D1DCRenderTarget::BindDC
            return null;

        renderer._dc = dc;
        renderer._bounds = bounds;
        renderer._dpi = dpi;
        renderer._failed = false;
        ((delegate* unmanaged[Stdcall]<nint, void>)Slot(renderer._target, 48))(renderer._target); // BeginDraw
        var clear = background is uint color ? Color.FromColorRef(color) : default;
        ((delegate* unmanaged[Stdcall]<nint, Color*, void>)Slot(renderer._target, 47))(renderer._target, &clear); // Clear
        Current = renderer;
        return renderer;
    }

    internal bool End()
    {
        Current = null;
        _dc = 0;
        int result = ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(_target, 49))(_target, 0, 0); // EndDraw
        if (result < 0)
            ReleaseTarget();
        else
            GdiApi.GdiFlush();
        return result >= 0 && !_failed;
    }

    internal bool Fill(nint dc, NativeWindowRect rect, uint color, double radius)
    {
        if (dc != _dc)
            return false;

        var bounds = ToTargetRect(rect);
        SetBrushColor(color);
        float corner = (float)Math.Min(radius, Math.Min(rect.Right - rect.Left, rect.Bottom - rect.Top) / 2.0);
        if (corner > 0)
        {
            var rounded = new RoundedRect(bounds, corner, corner);
            ((delegate* unmanaged[Stdcall]<nint, RoundedRect*, nint, void>)Slot(_target, 19))(_target, &rounded, _brush);
        }
        else
        {
            ((delegate* unmanaged[Stdcall]<nint, FloatRect*, nint, void>)Slot(_target, 17))(_target, &bounds, _brush);
        }

        return true;
    }

    internal bool Text(nint dc, string text, NativeWindowRect rect, uint color, uint alignment, int symbolSize)
    {
        if (dc != _dc)
            return false;

        nint format = GetFormat(symbolSize);
        uint textAlignment = alignment switch { NativeChrome.Center => 2u, NativeChrome.Right => 1u, _ => 0u };
        if (format == 0 || SetTextFormatOption(format, 3, textAlignment) < 0) // SetTextAlignment
        {
            _failed = true;
            return true;
        }

        var bounds = ToTargetRect(rect);
        SetBrushColor(color);
        const uint clipAndColorFont = 2 | 4;
        fixed (char* content = text)
        {
            ((delegate* unmanaged[Stdcall]<nint, char*, uint, nint, FloatRect*, nint, uint, uint, void>)Slot(_target, 27))
                (_target, content, (uint)text.Length, format, &bounds, _brush, clipAndColorFont, 0); // DrawText, natural metrics
        }
        return true;
    }

    private bool EnsureTarget()
    {
        if (_target != 0)
            return true;

        var writeId = new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"); // IDWriteFactory
        if (_writeFactory == 0 && DWriteCreateFactory(0, in writeId, out _writeFactory) < 0)
            return false;

        var drawId = new Guid("06152247-6f50-465a-9245-118bfd3b6007"); // ID2D1Factory
        if (_factory == 0 && D2D1CreateFactory(0, in drawId, 0, out _factory) < 0)
            return false;

        var properties = new TargetProperties(0, 87, 1, 96, 96, 0, 0); // BGRA, premultiplied alpha
        nint target = 0;
        if (((delegate* unmanaged[Stdcall]<nint, TargetProperties*, nint*, int>)Slot(_factory, 16))
                (_factory, &properties, &target) < 0) // CreateDCRenderTarget
            return false;

        _target = target;
        var color = Color.FromColorRef(0);
        nint brush = 0;
        if (((delegate* unmanaged[Stdcall]<nint, Color*, nint, nint*, int>)Slot(_target, 8))
                (_target, &color, 0, &brush) < 0) // CreateSolidColorBrush
        {
            ReleaseTarget();
            return false;
        }

        _brush = brush;
        ((delegate* unmanaged[Stdcall]<nint, uint, void>)Slot(_target, 34))(_target, 2); // Grayscale text antialiasing
        return true;
    }

    private nint GetFormat(int symbolSize)
    {
        var key = (_dpi, symbolSize);
        if (_formats.TryGetValue(key, out nint format))
            return format;

        string family = symbolSize == 0 ? NativeChrome.BodyFontFamily : "Segoe Fluent Icons";
        float size = (symbolSize == 0 ? NativeChrome.BodyFontSize : symbolSize) * _dpi / 96f;
        fixed (char* font = family, locale = "")
        {
            if (((delegate* unmanaged[Stdcall]<nint, char*, nint, uint, uint, uint, float, char*, nint*, int>)Slot(_writeFactory, 15))
                    (_writeFactory, font, 0, 400, 0, 5, size, locale, &format) < 0) // CreateTextFormat
                return 0;
        }

        nint ellipsis = 0;
        var trimming = new Trimming(1, 0, 0); // Trim at character boundaries.
        int result = SetTextFormatOption(format, 4, 2); // Center vertically.
        if (result >= 0)
            result = SetTextFormatOption(format, 5, 1); // No wrapping.
        if (result >= 0 && symbolSize == 0)
        {
            result = ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(_writeFactory, 20))
                (_writeFactory, format, &ellipsis); // CreateEllipsisTrimmingSign
            if (result >= 0)
                result = ((delegate* unmanaged[Stdcall]<nint, Trimming*, nint, int>)Slot(format, 9))
                    (format, &trimming, ellipsis); // SetTrimming
            if (ellipsis != 0)
                Marshal.Release(ellipsis);
        }

        if (result < 0)
        {
            Marshal.Release(format);
            return 0;
        }

        _formats.Add(key, format);
        return format;
    }

    private FloatRect ToTargetRect(NativeWindowRect rect) => new(
        rect.Left - _bounds.Left, rect.Top - _bounds.Top,
        rect.Right - _bounds.Left, rect.Bottom - _bounds.Top);

    private void SetBrushColor(uint colorRef)
    {
        var color = Color.FromColorRef(colorRef);
        ((delegate* unmanaged[Stdcall]<nint, Color*, void>)Slot(_brush, 8))(_brush, &color); // SetColor
    }

    private static int SetTextFormatOption(nint format, int slot, uint value) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, int>)Slot(format, slot))(format, value);

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    private void ReleaseTarget()
    {
        if (_brush != 0)
            Marshal.Release(_brush);
        if (_target != 0)
            Marshal.Release(_target);
        _brush = _target = 0;
    }

    internal static void ShutDown()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    public void Dispose()
    {
        ReleaseTarget();
        foreach (nint format in _formats.Values)
            Marshal.Release(format);
        _formats.Clear();
        if (_writeFactory != 0)
            Marshal.Release(_writeFactory);
        if (_factory != 0)
            Marshal.Release(_factory);
        _writeFactory = _factory = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct TargetProperties(uint Type, uint Format, uint Alpha, float DpiX, float DpiY,
        uint Usage, uint MinimumFeatureLevel);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FloatRect(float Left, float Top, float Right, float Bottom);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RoundedRect(FloatRect Rect, float RadiusX, float RadiusY);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Trimming(uint Granularity, uint Delimiter, uint DelimiterCount);
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Color(float R, float G, float B, float A)
    {
        internal static Color FromColorRef(uint value) =>
            new((value & 255) / 255f, (value >> 8 & 255) / 255f, (value >> 16 & 255) / 255f, 1);
    }

    [LibraryImport("d2d1.dll")]
    private static partial int D2D1CreateFactory(uint factoryType, in Guid interfaceId, nint options, out nint factory);

    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(uint factoryType, in Guid interfaceId, out nint factory);
}
