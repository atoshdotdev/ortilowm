using Ortilo.Core.Models;
using Ortilo.Platform.Windows.Interop.Gdi32;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Windows;

internal sealed class NativeTrayMenu(nint window) : IDisposable
{
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_ACTIVATE = 0x0006;
    private const uint WM_CANCELMODE = 0x001F;
    private const uint WM_CAPTURECHANGED = 0x0215;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;

    private readonly ComposedBackdrop _backdrop = new(window, suppressFrameShadow: true);
    private readonly TrayMenuShadow _shadow = new();
    private IReadOnlyList<TrayMenuItem> _entries = [];
    private nint _previousForeground;
    private uint _dpi = 96;
    private uint _pendingDpi;
    private int _anchorX;
    private int _anchorY;
    private int _width;
    private int _height;
    private (int Width, int Height, int Radius)? _cornerMask;
    private int _selected = -1;
    private int _pressed = -1;
    private bool _open;
    private nint _iconFont;
    private uint _iconFontDpi;

    private NativeWindowRect Bounds => NativeChrome.Rect(0, 0, _width, _height);

    internal bool IsInternalWindow(nint handle) => _backdrop.IsContentWindow(handle) || _shadow.OwnsWindow(handle);

    internal void Show(int x, int y, IReadOnlyList<TrayMenuItem> entries, bool keyboard)
    {
        if (_open)
            return;

        _anchorX = x;
        _anchorY = y;
        var monitor = FindAnchorMonitor();
        if (monitor is null)
            return;

        _pendingDpi = 0;
        _entries = entries;
        _previousForeground = WindowApi.GetForegroundWindow();
        _selected = -1;
        _pressed = -1;
        if (keyboard)
            MoveSelection(1);

        _backdrop.Refresh();
        _open = true;
        if (!Position(monitor, MonitorEnumerator.GetDpi(monitor.Handle)) ||
            !WindowApi.SetForegroundWindow(window))
        {
            Close(restoreForeground: false);
            return;
        }

        TrayApi.SetCapture(window);
        WindowApi.InvalidateRect(window, nint.Zero, false);
    }

    private DisplayMonitor? FindAnchorMonitor()
    {
        var monitors = MonitorEnumerator.GetMonitors();
        return monitors.FirstOrDefault(display =>
            _anchorX >= display.Bounds.X && _anchorX < display.Bounds.X + display.Bounds.Width &&
            _anchorY >= display.Bounds.Y && _anchorY < display.Bounds.Y + display.Bounds.Height)
            ?? monitors.FirstOrDefault(display => display.IsPrimary);
    }

    private bool Position(DisplayMonitor monitor, uint dpi)
    {
        _dpi = dpi;
        var area = monitor.WorkArea;
        _width = Math.Min(Scale(326), area.Width);
        _height = Scale(6) * 2 + _entries.Sum(entry => RowHeight(entry.Action));
        if (_width <= 0 || _height > area.Height)
            return false;

        int left = Math.Clamp(_anchorX - _width, area.X, area.X + area.Width - _width);
        int top = Math.Clamp(_anchorY - _height - Scale(8), area.Y, area.Y + area.Height - _height);
        const uint showWithoutActivation = 0x0040 | 0x0010;
        if (!WindowApi.SetWindowPos(window, (nint)(-1), left, top, _width, _height, showWithoutActivation))
            return false;

        UpdateCorners();
        _shadow.Show(window, _dpi, _cornerMask?.Radius ?? 0);
        return true;
    }

    private void UpdateCorners()
    {
        int radius = NativeChrome.Colors.HighContrast ? 0 : Math.Min(Scale(8), Math.Min(_width, _height) / 2);
        var mask = (_width, _height, radius);
        if (_cornerMask == mask)
            return;

        if ((_cornerMask is null && radius == 0) || NativeChrome.TryApplyRoundedRegion(window, _width, _height, radius))
            _cornerMask = mask;
        else
            Console.Error.WriteLine("Could not apply rounded corners to the tray menu.");
    }

    internal bool HandleMessage(uint message, nuint wParam, nint lParam, out nint result)
    {
        result = nint.Zero;
        if (message is NativeChrome.ThemeChanged or NativeChrome.SystemColorsChanged or
            NativeChrome.SettingsChanged or NativeChrome.ColorizationChanged)
        {
            NativeChrome.ThemeChangedFor(window);
            if (_open)
            {
                _backdrop.Refresh();
                UpdateCorners();
                _shadow.Show(window, _dpi, _cornerMask?.Radius ?? 0);
                if (message == NativeChrome.SettingsChanged)
                    NativeChrome.QueueLayoutRefresh(window);
            }
            return true;
        }

        if (!_open)
            return false;

        switch (message)
        {
            case WM_PAINT:
                _backdrop.Paint(_dpi, _width, _height, PaintRows);
                return true;
            case WM_ERASEBKGND:
                result = 1;
                return true;
            case NativeBackdrop.CompositionChanged:
                _backdrop.Refresh();
                WindowApi.InvalidateRect(window, nint.Zero, false);
                return true;
            case NativeChrome.DpiChanged:
                _pendingDpi = NativeChrome.DpiFromMessage(wParam);
                NativeChrome.QueueLayoutRefresh(window);
                return true;
            case NativeChrome.RefreshLayout:
                if (FindAnchorMonitor() is { } monitor)
                {
                    uint dpi = _pendingDpi != 0 ? _pendingDpi : MonitorEnumerator.GetDpi(monitor.Handle);
                    _pendingDpi = 0;
                    if (dpi != _dpi)
                    {
                        _pressed = -1;
                        if (Position(monitor, dpi))
                            WindowApi.InvalidateRect(window, nint.Zero, false);
                        else
                            Close(restoreForeground: true);
                    }
                }
                return true;
            case WM_ACTIVATE when (wParam & 0xFFFF) == 0:
            case WM_CAPTURECHANGED:
            case WM_CANCELMODE:
                Close(restoreForeground: false);
                return true;
            case NativeChrome.MouseMove:
                SetSelection(ItemAt(lParam));
                return true;
            case WM_LBUTTONDOWN:
            case WM_RBUTTONDOWN:
                if (!NativeChrome.Contains(Bounds, lParam))
                    Close(restoreForeground: true);
                else
                    _pressed = message == WM_LBUTTONDOWN ? ItemAt(lParam) : -1;
                return true;
            case WM_LBUTTONUP:
                int pressed = _pressed;
                _pressed = -1;
                if (pressed >= 0 && pressed == ItemAt(lParam))
                    Invoke(pressed);
                return true;
            case WM_KEYDOWN:
                switch (wParam)
                {
                    case 0x1B: Close(restoreForeground: true); break; // Escape
                    case 0x26: MoveSelection(-1); break; // Up
                    case 0x28: MoveSelection(1); break; // Down
                    case 0x0D or 0x20: Invoke(_selected); break; // Enter or Space
                }
                return true;
            default:
                return false;
        }
    }

    private void PaintRows(nint dc, NativeChrome.Palette colors)
    {
        if (_iconFontDpi != _dpi || _iconFont == nint.Zero)
        {
            if (_iconFont != nint.Zero)
                GdiApi.DeleteObject(_iconFont);
            _iconFont = NativeChrome.CreateFont(_dpi, "Segoe Fluent Icons", 16);
            _iconFontDpi = _dpi;
        }

        DrawRows(dc, colors, _iconFont);
    }

    private void DrawRows(nint dc, NativeChrome.Palette colors, nint icons)
    {
        int inset = Scale(6);
        int top = inset;
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            int bottom = top + RowHeight(entry.Action);
            if (entry.Action is null)
            {
                int line = (top + bottom) / 2;
                NativeChrome.Fill(dc, NativeChrome.Rect(Scale(12), line, _width - Scale(12), line + Scale(1)),
                    colors.Divider);
            }
            else
            {
                bool highlighted = index == _selected;
                if (highlighted)
                    NativeChrome.Fill(dc, NativeChrome.Rect(inset, top, _width - inset, bottom),
                        colors.HighContrast ? colors.Selected : colors.Hover,
                        colors.HighContrast ? 0 : inset);

                int right = _width - Scale(16);
                int labelRight = entry.Shortcut is null ? right : Math.Min(Scale(175), right);
                uint primary = highlighted && colors.HighContrast ? colors.SelectedText : colors.Text;
                uint secondary = highlighted && colors.HighContrast ? colors.SelectedText : colors.SecondaryText;
                DrawIcon(dc, icons, entry.Icon, top, bottom, primary);
                NativeChrome.Text(dc, entry.Label,
                    NativeChrome.Rect(Scale(42), top, labelRight, bottom), primary);
                if (entry.Shortcut is { } shortcut)
                    NativeChrome.Text(dc, shortcut,
                        NativeChrome.Rect(labelRight + Scale(6), top, right, bottom), secondary, NativeChrome.Right);
            }
            top = bottom;
        }
    }

    private void DrawIcon(nint dc, nint font, TrayMenuIcon icon, int top, int bottom, uint color)
    {
        string glyph = icon switch
        {
            TrayMenuIcon.Settings => "\uE713",
            TrayMenuIcon.Refresh => "\uE72C",
            TrayMenuIcon.Repair => "\uE90F",
            TrayMenuIcon.Power => "\uE7E8",
            _ => ""
        };
        if (font == nint.Zero || glyph.Length == 0)
            return;

        nint previous = GdiApi.SelectObject(dc, font);
        if (previous == nint.Zero || previous == (nint)(-1))
            return;

        try
        {
            NativeChrome.Text(dc, glyph, NativeChrome.Rect(Scale(12), top, Scale(34), bottom),
                color, NativeChrome.Center, symbolSize: 16);
        }
        finally
        {
            GdiApi.SelectObject(dc, previous);
        }
    }

    private int RowHeight(Action? action) => Scale(action is null ? 9 : 34);
    private int Scale(int value) => NativeChrome.Scale(value, _dpi);

    private int ItemAt(nint mousePosition)
    {
        int top = Scale(6);
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            int bottom = top + RowHeight(entry.Action);
            if (entry.Action is not null &&
                NativeChrome.Contains(NativeChrome.Rect(Scale(6), top, _width - Scale(6), bottom), mousePosition))
                return index;
            top = bottom;
        }
        return -1;
    }

    private void MoveSelection(int direction)
    {
        int index = _selected < 0 && direction < 0 ? 0 : _selected;
        for (int step = 0; step < _entries.Count; step++)
        {
            index = (index + direction + _entries.Count) % _entries.Count;
            if (_entries[index].Action is not null)
            {
                SetSelection(index);
                return;
            }
        }
    }

    private void SetSelection(int index)
    {
        if (_selected == index)
            return;
        _selected = index;
        WindowApi.InvalidateRect(window, nint.Zero, false);
    }

    private void Invoke(int index)
    {
        if (index < 0 || _entries[index].Action is not { } action)
            return;

        Close(restoreForeground: true);
        action();
    }

    private void Close(bool restoreForeground)
    {
        if (!_open)
            return;

        _open = false;
        if (TrayApi.GetCapture() == window)
            TrayApi.ReleaseCapture();
        _shadow.Hide();
        WindowApi.ShowWindow(window, 0);
        if (restoreForeground && _previousForeground != nint.Zero && _previousForeground != window)
            WindowApi.SetForegroundWindow(_previousForeground);
        _entries = [];
    }

    public void Dispose()
    {
        Close(restoreForeground: false);
        _shadow.Dispose();
        _backdrop.Dispose();
        if (_iconFont != nint.Zero)
            GdiApi.DeleteObject(_iconFont);
    }
}
