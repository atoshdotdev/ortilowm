using Tessel.App.Application;
using Tessel.Platform.Windows.Interop.Kernel32;
using Tessel.Platform.Windows.Windows;

if (args.Length == 0)
{
    using var instance = new Mutex(initiallyOwned: false, @"Local\Tessel.WindowManager");
    bool ownsInstance;
    try
    {
        ownsInstance = instance.WaitOne(0);
    }
    catch (AbandonedMutexException)
    {
        ownsInstance = true;
    }

    if (!ownsInstance)
    {
        Console.Error.WriteLine("Tessel is already running in this Windows session.");
        return 1;
    }

    try
    {
        WindowVisibility.RestoreHiddenWindows();
        using var application = new TesselApplication();
        application.Run();
        return 0;
    }
    finally
    {
        instance.ReleaseMutex();
    }
}

CommandConsole.AttachToParent();

if (args.Length == 1)
{
    switch (args[0].ToLowerInvariant())
    {
        case "reload":
            return SendCommand(NativeDisplayChangeListener.RequestReloadConfig());
        case "reset":
            return SendCommand(NativeDisplayChangeListener.RequestReset());
        case "config-path":
            Console.WriteLine(TesselConfig.FilePath);
            return 0;
        case "list-monitors":
            foreach (var monitor in MonitorEnumerator.GetMonitors())
                Console.WriteLine($"{monitor.DeviceName}{(monitor.IsPrimary ? " (primary)" : "")}");
            return 0;
        case "check-config":
        case "init-config":
        case "format-config":
            try
            {
                if (args[0].Equals("format-config", StringComparison.OrdinalIgnoreCase))
                    TesselConfig.FormatKeybindings();
                TesselConfig.Load(createIfMissing: args[0].Equals("init-config", StringComparison.OrdinalIgnoreCase)).Validate();
                Console.WriteLine($"Configuration valid: {TesselConfig.FilePath}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
    }
}

Console.Error.WriteLine("Usage: Tessel.App [reload | reset | config-path | list-monitors | check-config | init-config | format-config]");
return 2;

static int SendCommand(bool sent)
{
    if (sent)
    {
        Console.WriteLine("Request sent to the running Tessel instance.");
        return 0;
    }

    Console.Error.WriteLine("Could not send the command. Check that Tessel is running at the same privilege level.");
    return 1;
}
