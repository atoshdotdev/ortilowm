using System.ComponentModel;
using System.Runtime.InteropServices;
using Tessel.Platform.Windows.Interop.User32;
using Tessel.Platform.Windows.Interop.Kernel32;

namespace Tessel.Platform.Windows.Windows;

public sealed partial class WindowEventListener
{
    public event Action<WindowEventType, nint>? EventReceived;
    public event Action<int>? HotkeyPressed;

    public event Action<nint>? WindowMoveSizeStarted;
    public event Action<nint>? WindowMoveSizeEnded;
    public event Action<nint>? WindowLocationChanged;

    public void QuitMessageLoop() => WindowMessageApi.PostQuitMessage(0);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint EVENT_OBJECT_HIDE = 0x8003;
    private const uint EVENT_OBJECT_STATECHANGE = 0x800A;
    private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;

    private const uint WINEVENT_OUTOFCONTEXT = 0;

    private const uint WM_QUIT = 0x0012;
    private const uint WM_HOTKEY = 0x0312;
    private const uint PM_NOREMOVE = 0;

    public WindowEventListener()
    {
        _callback = OnWinEvent;

        _callbackPointer =
            Marshal.GetFunctionPointerForDelegate(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventProc(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime
    );

    private readonly WinEventProc _callback;
    private readonly nint _callbackPointer;

    private void OnWinEvent(
        nint hWinEventHook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        try
        {
            if (hwnd == nint.Zero)
                return;

            if (eventType == EVENT_SYSTEM_FOREGROUND)
            {
                Console.WriteLine(
                    $"[NATIVE FOREGROUND] HWND: 0x{hwnd.ToInt64():X} | " +
                    $"Object: {idObject} | Child: {idChild} | " +
                    $"Thread: {eventThread}"
                );
            }

            if (eventType != EVENT_SYSTEM_FOREGROUND &&
                (idObject != OBJID_WINDOW || idChild != CHILDID_SELF))
            {
                return;
            }

            if (eventType == EVENT_SYSTEM_MOVESIZESTART)
            {
                WindowMoveSizeStarted?.Invoke(hwnd);
                return;
            }

            if (eventType == EVENT_SYSTEM_MOVESIZEEND)
            {
                WindowMoveSizeEnded?.Invoke(hwnd);
                return;
            }

            if (eventType == EVENT_OBJECT_LOCATIONCHANGE)
            {
                WindowLocationChanged?.Invoke(hwnd);
                return;
            }

            WindowEventType? windowEventType = eventType switch
            {
                EVENT_SYSTEM_FOREGROUND => WindowEventType.Foreground,
                EVENT_OBJECT_CREATE => WindowEventType.Created,
                EVENT_OBJECT_DESTROY => WindowEventType.Destroyed,
                EVENT_OBJECT_SHOW => WindowEventType.Shown,
                EVENT_OBJECT_HIDE => WindowEventType.Hidden,
                EVENT_OBJECT_STATECHANGE => WindowEventType.StateChanged,
                _ => null
            };

            if (windowEventType is null)
                return;

            EventReceived?.Invoke(windowEventType.Value, hwnd);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Error processing native window event: {exception}"
            );
        }
    }

    private nint RegisterEventHook(uint eventMin, uint eventMax)
    {
        nint hook = WindowEventApi.SetWinEventHook(
            eventMin,
            eventMax,
            nint.Zero,
            _callbackPointer,
            0,
            0,
            WINEVENT_OUTOFCONTEXT
        );

        if (hook == nint.Zero)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return hook;
    }

    public void Run(Action? initializeMessageThread = null)
    {
        uint threadId = ThreadApi.GetCurrentThreadId();

        _ = WindowMessageApi.PeekMessage(
            out _,
            nint.Zero,
            0,
            0,
            PM_NOREMOVE
        );

        initializeMessageThread?.Invoke();

        nint createShowHook = nint.Zero;
        nint stateChangeHook = nint.Zero;
        nint foregroundHook = nint.Zero;
        nint moveSizeHook = nint.Zero;
        nint locationChangeHook = nint.Zero;

        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            WindowMessageApi.PostThreadMessage(threadId, WM_QUIT, 0, nint.Zero);
        };

        try
        {
            createShowHook = RegisterEventHook(
                EVENT_OBJECT_CREATE,
                EVENT_OBJECT_HIDE
            );

            stateChangeHook = RegisterEventHook(
                EVENT_OBJECT_STATECHANGE,
                EVENT_OBJECT_STATECHANGE
            );

            foregroundHook = RegisterEventHook(
                EVENT_SYSTEM_FOREGROUND,
                EVENT_SYSTEM_FOREGROUND
            );

            Console.WriteLine(
    $"[HOOK] Foreground hook registered: 0x{foregroundHook.ToInt64():X}"
);

            moveSizeHook = RegisterEventHook(
                EVENT_SYSTEM_MOVESIZESTART,
                EVENT_SYSTEM_MOVESIZEEND
            );

            locationChangeHook = RegisterEventHook(
                EVENT_OBJECT_LOCATIONCHANGE,
                EVENT_OBJECT_LOCATIONCHANGE
            );

            bool hasConsole = CommandConsole.IsAttached;
            if (hasConsole)
                Console.CancelKeyPress += cancelHandler;

            try
            {
                Console.WriteLine("Tessel is monitoring Windows events.");
                if (hasConsole)
                    Console.WriteLine("Press Ctrl+C to stop.\n");

                while (true)
                {
                    int result = WindowMessageApi.GetMessage(
                        out NativeMessage message,
                        nint.Zero,
                        0,
                        0
                    );

                    if (result == 0)
                        break;

                    if (result == -1)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastPInvokeError()
                        );
                    }

                    if (message.Message == WM_HOTKEY)
                    {
                        try
                        {
                            HotkeyPressed?.Invoke((int)message.WParam);
                        }
                        catch (Exception exception)
                        {
                            Console.Error.WriteLine(
                                $"Error processing hotkey: {exception}"
                            );
                        }

                        continue;
                    }

                    WindowMessageApi.TranslateMessage(in message);
                    WindowMessageApi.DispatchMessage(in message);
                }
            }
            finally
            {
                if (hasConsole)
                    Console.CancelKeyPress -= cancelHandler;
            }
        }
        finally
        {
            if (moveSizeHook != nint.Zero)
                WindowEventApi.UnhookWinEvent(moveSizeHook);

            if (foregroundHook != nint.Zero)
                WindowEventApi.UnhookWinEvent(foregroundHook);

            if (stateChangeHook != nint.Zero)
                WindowEventApi.UnhookWinEvent(stateChangeHook);

            if (createShowHook != nint.Zero)
                WindowEventApi.UnhookWinEvent(createShowHook);

            if (locationChangeHook != nint.Zero)
                WindowEventApi.UnhookWinEvent(locationChangeHook);

            Console.WriteLine("\nTessel stopped.");
        }
    }
}
