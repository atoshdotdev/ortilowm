using Ortilo.App.Application;
using Ortilo.Platform.Windows.Interop.Kernel32;
using Ortilo.Platform.Windows.Windows;

if (args.Length == 0)
{
    using var instance = new Mutex(initiallyOwned: false, @"Local\Ortilo.WindowManager");
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
        Console.Error.WriteLine("Ortilo is already running in this Windows session.");
        return 1;
    }

    try
    {
        WindowVisibility.RestoreHiddenWindows();
        using var application = new OrtiloApplication();
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
            Console.WriteLine(OrtiloConfig.FilePath);
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
                    OrtiloConfig.FormatKeybindings();
                OrtiloConfig.Load(createIfMissing: args[0].Equals("init-config", StringComparison.OrdinalIgnoreCase)).Validate();
                Console.WriteLine($"Configuration valid: {OrtiloConfig.FilePath}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
    }
}

Console.Error.WriteLine("Usage: Ortilo.App [reload | reset | config-path | list-monitors | check-config | init-config | format-config]");
return 2;

static int SendCommand(bool sent)
{
    if (sent)
    {
        Console.WriteLine("Request sent to the running Ortilo instance.");
        return 0;
    }

    Console.Error.WriteLine("Could not send the command. Check that Ortilo is running at the same privilege level.");
    return 1;
}
