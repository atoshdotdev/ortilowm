using System.Numerics;
using System.Runtime.InteropServices;
using Ortilo.Platform.Windows.Interop.User32;

namespace Ortilo.Platform.Windows.Interop.DwmApi;

// Desktop Windows.UI.Composition ABI. These interfaces are supplied by Windows,
// and the calls work in both managed and Native AOT builds.
internal sealed unsafe partial class HostBackdropVisual : IDisposable
{
    private const uint UseHostBackdropBrush = 17;
    private static nint _dispatcher;
    private nint _target;
    private nint _visual;
    private Vector2 _size;
    private float _opacity = 1;

    internal static HostBackdropVisual Create(nint window)
    {
        EnsureDispatcher();
        var backdrop = new HostBackdropVisual();
        const string compositorClass = "Windows.UI.Composition.Compositor";
        Check(WindowsCreateString(compositorClass, compositorClass.Length, out nint name));
        nint instance = 0, compositor = 0, desktop = 0, desktopTarget = 0, compositor3 = 0, sprite = 0, hostBrush = 0, brush = 0;
        try
        {
            Check(RoActivateInstance(name, out instance));
            compositor = Query(instance, "b403ca50-7f8c-4e83-985f-cc45060036d8"); // ICompositor
            int enabled = 1;
            Check(DwmWindowApi.DwmSetWindowAttribute(window, UseHostBackdropBrush, in enabled, sizeof(int)));

            // ICompositorDesktopInterop::CreateDesktopWindowTarget. Place the
            // composition layer below the child HWND that paints the controls.
            desktop = Query(compositor, "29e691fa-4567-4dca-b319-d0f207eb6807");
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)Slot(desktop, 3))
                (desktop, window, 0, &desktopTarget));
            backdrop._target = Query(desktopTarget, "a1bea8ba-d726-4663-8129-6b5e7927ffa6"); // ICompositionTarget

            compositor3 = Query(compositor, "c9dd8ef0-6eb1-4e3c-a658-675d9c64d4ab");
            hostBrush = CreateObject(compositor3, 6); // ICompositor3::CreateHostBackdropBrush
            brush = Query(hostBrush, "ab0d7608-30c0-40e9-b568-b60a6bd1fb46"); // ICompositionBrush
            sprite = CreateObject(compositor, 22); // ICompositor::CreateSpriteVisual
            SetObject(sprite, 7, brush); // ISpriteVisual::put_Brush
            backdrop._visual = Query(sprite, "117e202d-a859-4c89-873b-c2aa566788e3"); // IVisual
            SetObject(backdrop._target, 7, backdrop._visual); // ICompositionTarget::put_Root
            return backdrop;
        }
        catch
        {
            backdrop.Dispose();
            throw;
        }
        finally
        {
            Release(brush);
            Release(hostBrush);
            Release(sprite);
            Release(compositor3);
            Release(desktopTarget);
            Release(desktop);
            Release(compositor);
            Release(instance);
            WindowsDeleteString(name);
        }
    }

    internal void Update(int width, int height, float opacity)
    {
        var size = new Vector2(width, height);
        if (size != _size)
        {
            Check(((delegate* unmanaged[Stdcall]<nint, Vector2, int>)Slot(_visual, 36))(_visual, size)); // IVisual::put_Size
            _size = size;
        }

        if (opacity != _opacity)
        {
            Check(((delegate* unmanaged[Stdcall]<nint, float, int>)Slot(_visual, 23))(_visual, opacity)); // IVisual::put_Opacity
            _opacity = opacity;
        }
    }

    public void Dispose()
    {
        if (_target != 0)
            ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(_target, 7))(_target, 0);
        Release(_visual);
        Release(_target);
        _visual = _target = 0;
    }

    private static void EnsureDispatcher()
    {
        if (_dispatcher != 0)
            return;

        const uint multiThreaded = 1;
        Check(RoInitialize(multiThreaded));
        try
        {
            // The Ortilo window-event thread already pumps Win32 messages.
            const int currentThread = 2;
            const int existingApartment = 0;
            Check(CreateDispatcherQueueController(new((uint)sizeof(QueueOptions), currentThread, existingApartment),
                out _dispatcher));
        }
        catch
        {
            RoUninitialize();
            throw;
        }
    }

    internal static void ShutdownDispatcher()
    {
        if (_dispatcher == 0)
            return;

        nint action = 0, info = 0;
        try
        {
            action = CreateObject(_dispatcher, 7); // IDispatcherQueueController::ShutdownQueueAsync
            info = Query(action, "00000036-0000-0000-c000-000000000046"); // IAsyncInfo
            int status = 0;
            // A current-thread queue finishes shutdown through its own window messages.
            while (((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(info, 7))(info, &status) >= 0 && status == 0)
            {
                const uint allInput = 0x04FF;
                const uint inputAvailable = 4;
                if (MsgWaitForMultipleObjectsEx(0, 0, uint.MaxValue, allInput, inputAvailable) != 0)
                    break;
                while (WindowMessageApi.PeekMessage(out var message, 0, 0, 0, 1))
                {
                    WindowMessageApi.TranslateMessage(in message);
                    WindowMessageApi.DispatchMessage(in message);
                }
            }
        }
        catch (COMException error)
        {
            Console.Error.WriteLine($"Could not shut down the composition dispatcher: {error.Message}");
        }
        finally
        {
            Release(info);
            Release(action);
            Release(_dispatcher);
            _dispatcher = 0;
            RoUninitialize();
        }
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    private static nint Query(nint instance, string interfaceId)
    {
        var iid = new Guid(interfaceId);
        Check(Marshal.QueryInterface(instance, in iid, out nint result));
        return result;
    }

    private static nint CreateObject(nint instance, int slot)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, &result));
        return result;
    }

    private static void SetObject(nint instance, int slot, nint value) =>
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(instance, slot))(instance, value));

    private static void Check(int result)
    {
        if (result < 0)
            throw new COMException($"Windows Composition returned 0x{result:X8}.", result);
    }

    private static void Release(nint instance)
    {
        if (instance != 0)
            Marshal.Release(instance);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct QueueOptions(uint Size, int ThreadType, int ApartmentType);

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(QueueOptions options, out nint controller);

    [LibraryImport("combase.dll")]
    private static partial int RoInitialize(uint type);

    [LibraryImport("combase.dll")]
    private static partial void RoUninitialize();

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint name);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint name);

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint name, out nint instance);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint handles, uint timeout, uint wakeMask, uint flags);
}
