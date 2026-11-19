using System.Runtime.InteropServices;
using Tessel.Platform.Windows.Interop.Kernel32;
using Tessel.Platform.Windows.Interop.User32;

namespace Tessel.Platform.Windows.Windows;

public sealed class NativeDisplayChangeListener : IDisposable
{
    private const uint WM_DISPLAYCHANGE = 0x007E;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SPI_SETWORKAREA = 0x002F;
    private const uint WM_TIMER = 0x0113;
    private const uint WM_APP_DISPLAY_CONFIGURATION_CHANGED = 0x8002;
    private const uint WM_APP_RELOAD_CONFIG = 0x8004;
    private const uint WM_APP_RESET = 0x8005;
    private const uint WM_APP_WORK_AREA_CHANGED = 0x8007;

    private const uint WS_POPUP = 0x80000000;

    private const uint DisplayRefreshDelayMs = 300;

    private static readonly nuint DisplayRefreshTimerId = 1;

    private const string WindowClassName = "TesselDisplayChangeListener";

    private static bool _classRegistered;
    private static NativeDisplayChangeListener? _instance;

    private static readonly WindowProcedure Procedure = WndProc;

    private readonly int _threadId;

    private nint _windowHandle;
    private bool _workAreaRefreshPending;
    private bool _disposed;

    public event Action? DisplayConfigurationChanged;
    public event Action? WorkAreaChanged;
    public event Action? ReloadConfigRequested;
    public event Action? ResetRequested;

    public static bool RequestReloadConfig() => RequestCommand(WM_APP_RELOAD_CONFIG);

    public static bool RequestReset() => RequestCommand(WM_APP_RESET);

    private static bool RequestCommand(uint message)
    {
        nint window = WindowApi.FindWindow(WindowClassName, null);
        return window != nint.Zero && WindowApi.PostMessage(window, message, 0, nint.Zero);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam
    );

    public NativeDisplayChangeListener()
    {
        if (_instance is not null)
        {
            throw new InvalidOperationException(
                "A display change listener is already active."
            );
        }

        _instance = this;

        try
        {
            EnsureWindowClass();

            _windowHandle = WindowClassApi.CreateWindowEx(
                0,
                WindowClassName,
                "Tessel Display Listener",
                WS_POPUP,
                0,
                0,
                1,
                1,
                nint.Zero,
                nint.Zero,
                ModuleApi.GetModuleHandle(null),
                nint.Zero
            );

            if (_windowHandle == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"Could not create display listener. " +
                    $"Win32 error: {Marshal.GetLastPInvokeError()}"
                );
            }

            _threadId = Environment.CurrentManagedThreadId;
        }
        catch
        {
            _instance = null;
            throw;
        }
    }

    private static void EnsureWindowClass()
    {
        if (_classRegistered)
            return;

        NativeWindowClassRegistrar.Register(
            WindowClassName,
            Marshal.GetFunctionPointerForDelegate(Procedure)
        );

        _classRegistered = true;
    }

    private static nint WndProc(
        nint hwnd,
        uint message,
        nuint wParam,
        nint lParam)
    {
        var instance = _instance;

        if (instance is null)
        {
            return WindowClassApi.DefWindowProc(
                hwnd,
                message,
                wParam,
                lParam
            );
        }

        switch (message)
        {
            case WM_APP_RELOAD_CONFIG:
                instance.ReloadConfigRequested?.Invoke();
                return nint.Zero;

            case WM_APP_RESET:
                instance.ResetRequested?.Invoke();
                return nint.Zero;

            case WM_DISPLAYCHANGE:
                instance.QueueDisplayRefresh();

                return WindowClassApi.DefWindowProc(
                    hwnd,
                    message,
                    wParam,
                    lParam
                );

            case WM_SETTINGCHANGE when wParam is 0 or SPI_SETWORKAREA:
                instance.QueueWorkAreaRefresh();
                break;

            case WM_APP_WORK_AREA_CHANGED:
                instance._workAreaRefreshPending = false;
                instance.WorkAreaChanged?.Invoke();
                return nint.Zero;

            case WM_TIMER:
                if (wParam == DisplayRefreshTimerId)
                {
                    instance.KillDisplayRefreshTimer();
                    instance.NotifyDisplayConfigurationChanged();

                    return nint.Zero;
                }

                break;

            case WM_APP_DISPLAY_CONFIGURATION_CHANGED:
                instance.NotifyDisplayConfigurationChanged();

                return nint.Zero;
        }

        return WindowClassApi.DefWindowProc(
            hwnd,
            message,
            wParam,
            lParam
        );
    }

    private void QueueWorkAreaRefresh()
    {
        if (_workAreaRefreshPending || _disposed || _windowHandle == 0)
            return;

        _workAreaRefreshPending = WindowApi.PostMessage(_windowHandle, WM_APP_WORK_AREA_CHANGED, 0, 0);
        if (!_workAreaRefreshPending)
            Console.Error.WriteLine($"Could not queue work-area refresh. Win32 error: {Marshal.GetLastPInvokeError()}");
    }

    private void QueueDisplayRefresh()
    {
        if (_disposed || _windowHandle == nint.Zero)
            return;

        // Restart the timer so that multiple display changes
        // produce one refresh after the last notification.
        KillDisplayRefreshTimer();


        nuint timer = WindowApi.SetTimer(
            _windowHandle,
            DisplayRefreshTimerId,
            DisplayRefreshDelayMs,
            nint.Zero
        );

        if (timer != 0)
            return;

        Console.Error.WriteLine(
            $"Could not start display refresh timer. " +
            $"Win32 error: {Marshal.GetLastPInvokeError()}"
        );

        // Fallback: queue an immediate refresh.
        if (!WindowApi.PostMessage(
            _windowHandle,
            WM_APP_DISPLAY_CONFIGURATION_CHANGED,
            0,
            nint.Zero))
        {
            Console.Error.WriteLine(
                $"Could not queue display refresh. " +
                $"Win32 error: {Marshal.GetLastPInvokeError()}"
            );
        }
    }

    private void KillDisplayRefreshTimer()
    {
        WindowApi.KillTimer(
            _windowHandle,
            DisplayRefreshTimerId
        );
    }

    private void NotifyDisplayConfigurationChanged()
    {
        if (_disposed)
            return;

        try
        {
            DisplayConfigurationChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Error handling display configuration change: {exception}"
            );
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (Environment.CurrentManagedThreadId != _threadId)
        {
            throw new InvalidOperationException(
                "The display listener must be disposed on its creating thread."
            );
        }

        _disposed = true;

        if (_windowHandle != nint.Zero)
        {
            KillDisplayRefreshTimer();

            if (!WindowApi.DestroyWindow(_windowHandle))
            {
                Console.Error.WriteLine(
                    $"Could not destroy display listener. " +
                    $"Win32 error: {Marshal.GetLastPInvokeError()}"
                );
            }

            _windowHandle = nint.Zero;
        }

        DisplayConfigurationChanged = null;
        WorkAreaChanged = null;
        _instance = null;
    }
}
