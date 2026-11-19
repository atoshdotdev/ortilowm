using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

internal static class Program
{
    private const string InstanceMutex = @"Local\Tessel.WindowManager";
    private const string ListenerClass = "TesselDisplayChangeListener";
    private const string TopBarClass = "TesselNativeTopBar";
    private const int TimeoutMs = 10_000;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 ||
            (args.Length == 2 && args[1] is not ("--desktop" or "--mixed-dpi")) ||
            !File.Exists(args[0]))
        {
            Console.Error.WriteLine("Usage: Tessel.Windows.Smoke <path to Tessel.exe> [--desktop | --mixed-dpi]");
            return 2;
        }

        string app = Path.GetFullPath(args[0]);
        var directory = Directory.CreateTempSubdirectory("Tessel-smoke-");
        string config = Path.Combine(directory.FullName, "config.json");
        try
        {
            CheckCommands(app, config);
            if (args.Length == 2)
                CheckDesktop(app, config, mixedDpi: args[1] == "--mixed-dpi");
            else
                Console.WriteLine("Desktop checks skipped. Pass --desktop on a clean, interactive Windows session.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void CheckCommands(string app, string config)
    {
        using (var image = new PEReader(File.OpenRead(app)))
            Check(image.PEHeaders.PEHeader?.Subsystem == Subsystem.WindowsGui, "Tessel must launch without a console.");

        string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tessel", "config.json");
        CheckConfigPath(app, null, defaultPath);
        CheckConfigPath(app, config, config);

        RunCommand(app, config, "init-config").RequireSuccess();
        Check(File.Exists(config), "init-config did not create the isolated configuration.");
        RunCommand(app, config, "check-config").RequireSuccess();
        string validConfig = File.ReadAllText(config);
        var custom = JsonNode.Parse(validConfig)!.AsObject();
        custom["workspaceIndicator"] = new JsonObject { ["light"] = "#005FB8", ["dark"] = "#60CDFF" };
        custom["tabIndicator"] = new JsonObject { ["light"] = "#000000" };
        File.WriteAllText(config, custom.ToJsonString());
        RunCommand(app, config, "check-config").RequireSuccess();
        custom["tabIndicator"]!["dark"] = "red";
        File.WriteAllText(config, custom.ToJsonString());
        Check(RunCommand(app, config, "check-config").ExitCode == 1, "check-config accepted an invalid indicator color.");
        File.WriteAllText(config, "{");
        Check(RunCommand(app, config, "check-config").ExitCode == 1, "check-config accepted invalid JSON.");
        File.WriteAllText(config, validConfig);

        // Hold the same session mutex as a running app. This exercises the
        // real launch guard without allowing the smoke runner to tile windows.
        using var instance = new Mutex(false, InstanceMutex);
        bool ownsMutex;
        try
        {
            ownsMutex = instance.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        Check(ownsMutex, "Quit any running Tessel instance before running the smoke suite.");
        try
        {
            var duplicate = RunCommand(app, config);
            Check(duplicate.ExitCode == 1 && duplicate.Output.Contains("already running", StringComparison.Ordinal),
                $"Duplicate launch was not rejected. Output: {duplicate.Output}");
        }
        finally
        {
            instance.ReleaseMutex();
        }
        Console.WriteLine("PASS configuration commands and duplicate-launch guard");
    }

    private static void CheckConfigPath(string app, string? config, string expected)
    {
        var result = RunCommand(app, config, "config-path");
        result.RequireSuccess();
        Check(string.Equals(result.Output.Trim(), expected, StringComparison.OrdinalIgnoreCase),
            $"config-path returned '{result.Output.Trim()}' instead of '{expected}'.");
    }

    private static void CheckDesktop(string app, string config, bool mixedDpi)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Check(!DesktopApi.HasVisibleAppWindows(),
            "Close other desktop apps before running the desktop checks. Use a clean Windows test session.");

        var primary = Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary display.");
        Screen? secondary = null;
        if (mixedDpi)
        {
            var displays = Screen.AllScreens;
            Check(displays.Length == 2, "The mixed-DPI check requires exactly two connected displays.");
            secondary = displays.Single(display => !display.Primary);
        }

        ConfigureBindings(config, mixedDpi);
        using var first = MakeWindow("Tessel smoke window A", primary.WorkingArea);
        using var second = MakeWindow("Tessel smoke window B", primary.WorkingArea);
        first.Show();
        second.Show();
        first.Activate();
        WaitFor(() => DesktopApi.GetForegroundWindow() == first.Handle, "Focus the first fixture window");
        uint startingDpi = DesktopApi.GetDpiForWindow(first.Handle);

        using var tessel = Start(app, config);
        var output = tessel.StandardOutput.ReadToEndAsync();
        var errors = tessel.StandardError.ReadToEndAsync();
        uint thread = 0;
        try
        {
            WaitFor(() =>
            {
                uint candidateThread = DesktopApi.GetWindowThreadProcessId(
                    DesktopApi.FindWindow(ListenerClass, null), out uint processId);
                if (candidateThread == 0 || processId != tessel.Id)
                    return false;
                thread = candidateThread;
                return HorizontallyTiled(first, second);
            }, "Start Tessel and tile both fixture windows");

            var left = first.Left < second.Left ? first : second;
            var right = left == first ? second : first;
            left.Activate();
            WaitFor(() => DesktopApi.GetForegroundWindow() == left.Handle, "Focus the left fixture window");
            SendKeys.SendWait("^%+{F9}");
            WaitFor(() => DesktopApi.GetForegroundWindow() == right.Handle, "Focus right through a registered hotkey");

            int visibleBars = CountVisibleTopBars();
            Check(visibleBars == Screen.AllScreens.Length, "Tessel should show a bar on each managed display.");
            SendKeys.SendWait("^%+{F5}");
            WaitFor(() => right.Bounds == primary.Bounds && CountVisibleTopBars() == visibleBars - 1,
                "Fullscreen hides only the bar on the fullscreen display");
            SendKeys.SendWait("^%+{F5}");
            WaitFor(() => HorizontallyTiled(first, second) && CountVisibleTopBars() == visibleBars,
                "Leaving fullscreen restores tiling and the top bar");

            SendKeys.SendWait("^%+{F10}");
            WaitFor(() => DesktopApi.IsIconic(right.Handle), "Minimize the focused window through a registered hotkey");
            SendKeys.SendWait("^%+{F11}");
            WaitFor(() => !DesktopApi.IsWindowVisible(left.Handle), "Hide the old workspace");
            SendKeys.SendWait("^%+{F7}");
            WaitFor(() => DesktopApi.IsWindowVisible(left.Handle) && DesktopApi.IsIconic(right.Handle),
                "Return to the workspace without restoring the minimized window");
            right.WindowState = FormWindowState.Normal;
            WaitFor(() => HorizontallyTiled(first, second) && !DesktopApi.IsIconic(right.Handle),
                "Restore the minimized window into its tile");

            CheckFloatingMove(first, second);
            CheckReloadAndReset(app, config, first.Left < second.Left ? first : second);

            if (secondary is not null)
            {
                int tiledWidth = first.Width;
                using (var launched = MakeWindow("Tessel smoke window launched on the other display", secondary.WorkingArea))
                {
                    launched.Show();
                    WaitFor(() => primary.Bounds.Contains(Center(launched)) && first.Width < tiledWidth,
                        "Tile a new window in the focused workspace even when it opens on the other display");
                }
                WaitFor(() => first.Width == tiledWidth, "Retile after closing the new window");

                SendKeys.SendWait("^%+{F8}");
                WaitFor(() => Screen.FromHandle(first.Handle).DeviceName == secondary.DeviceName &&
                    Screen.FromHandle(second.Handle).DeviceName == secondary.DeviceName &&
                    HorizontallyTiled(first, second), "Move the workspace to the other monitor");
                Check(DesktopApi.GetDpiForWindow(first.Handle) != startingDpi,
                    "The monitors must have different DPI scaling for --mixed-dpi.");
                Check(secondary.Bounds.Contains(Center(first)) && secondary.Bounds.Contains(Center(second)),
                    "Both fixture windows must land on the destination monitor after the DPI change.");
                // Return the fixtures to the primary monitor so the configured
                // workspace 3 and workspace 1 shortcuts exercise a hide again.
                SendKeys.SendWait("^%+{F8}");
                WaitFor(() => Screen.FromHandle(first.Handle).Primary && Screen.FromHandle(second.Handle).Primary &&
                    HorizontallyTiled(first, second), "Return the workspace to the primary monitor");
                Console.WriteLine("PASS mixed-DPI workspace move");
            }

            SendKeys.SendWait("^%+{F11}");
            WaitFor(() => !DesktopApi.IsWindowVisible(first.Handle) && !DesktopApi.IsWindowVisible(second.Handle),
                "Hide both fixtures before quit");
            Check(DesktopApi.PostThreadMessage(thread, DesktopApi.QuitMessage, 0, 0), "Could not request a clean quit.");
            WaitFor(() => tessel.HasExited, "Wait for Tessel to quit");
            Check(tessel.ExitCode == 0, $"Tessel exited with code {tessel.ExitCode}.");
            Check(DesktopApi.IsWindowVisible(first.Handle) && DesktopApi.IsWindowVisible(second.Handle),
                "Clean quit did not restore both hidden windows.");
            Console.WriteLine("PASS tiling, hotkey focus, minimize/restore, workspace visibility, and clean quit");
        }
        catch
        {
            if (!tessel.HasExited && thread != 0)
                DesktopApi.PostThreadMessage(thread, DesktopApi.QuitMessage, 0, 0);
            try
            {
                WaitFor(() => tessel.HasExited, "Stop Tessel after a failed smoke check");
            }
            catch (TimeoutException)
            {
                if (!tessel.HasExited)
                {
                    tessel.Kill();
                    tessel.WaitForExit(TimeoutMs);
                }
            }
            if (output.IsCompletedSuccessfully && errors.IsCompletedSuccessfully)
                Console.Error.WriteLine(output.Result + errors.Result);
            throw;
        }
    }

    private static void CheckFloatingMove(Form floating, Form tiled)
    {
        floating.Activate();
        WaitFor(() => DesktopApi.GetForegroundWindow() == floating.Handle, "Focus the window to float");
        int tiledWidth = tiled.Width;
        SendKeys.SendWait("^%+{F6}");
        WaitFor(() => tiled.Width > tiledWidth + 100, "Float the first window and give its tile to the other window");

        tiled.Left += 48;
        int displacedTile = tiled.Left;
        DesktopApi.NotifyWinEvent(0x000A, floating.Handle, 0, 0); // EVENT_SYSTEM_MOVESIZESTART
        floating.Left += 32;
        int dropX = floating.Left;
        DesktopApi.NotifyWinEvent(0x000B, floating.Handle, 0, 0); // EVENT_SYSTEM_MOVESIZEEND

        // Give both asynchronous move events time to arrive and detect
        // any layout pass by watching the deliberately displaced tile.
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 300)
        {
            Application.DoEvents();
            Check(floating.Left == dropX && tiled.Left == displacedTile, "Moving the floating window triggered repositioning.");
            Thread.Sleep(15);
        }

        SendKeys.SendWait("^%+{F6}");
        WaitFor(() => HorizontallyTiled(floating, tiled), "Return the floating fixture to the tiled layout");
        Console.WriteLine("PASS moving a floating window leaves tiled windows in place");
    }

    private static void CheckReloadAndReset(string app, string path, Form left)
    {
        int originalLeft = left.Left;
        var config = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        config["gaps"]!["outer"] = 24;
        File.WriteAllText(path, config.ToJsonString());
        RunCommand(app, path, "reload").RequireSuccess();
        WaitFor(() => left.Left >= originalLeft + 12, "Apply the new outer gap through config reload");

        Rectangle tile = left.Bounds;
        left.Left += 48;
        RunCommand(app, path, "reset").RequireSuccess();
        WaitFor(() => left.Bounds == tile, "Reset reapplies the running layout");
        Console.WriteLine("PASS live config reload and reset");
    }

    private static void ConfigureBindings(string path, bool mixedDpi)
    {
        var config = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var bindings = config["keybindings"]!.AsObject();
        foreach (string action in bindings.Select(binding => binding.Key).ToArray())
            bindings[action] = new JsonArray();
        bindings["Focus right"] = new JsonArray("Ctrl+Alt+Shift+F9");
        bindings["Toggle floating"] = new JsonArray("Ctrl+Alt+Shift+F6");
        bindings["Toggle fullscreen"] = new JsonArray("Ctrl+Alt+Shift+F5");
        bindings["Minimize window"] = new JsonArray("Ctrl+Alt+Shift+F10");
        bindings["Switch to workspace 3"] = new JsonArray("Ctrl+Alt+Shift+F11");
        bindings["Switch to workspace 1"] = new JsonArray("Ctrl+Alt+Shift+F7");
        if (mixedDpi)
            bindings["Move workspace to next monitor"] = new JsonArray("Ctrl+Alt+Shift+F8");
        config["showTopBar"] = true;
        config["focusedWindowBorder"]!["enabled"] = false;
        config["parentBorder"]!["enabled"] = false;
        File.WriteAllText(path, config.ToJsonString());
    }

    private static int CountVisibleTopBars()
    {
        int count = 0;
        nint window = 0;
        while ((window = DesktopApi.FindWindowEx(0, window, TopBarClass, null)) != 0)
        {
            if (DesktopApi.IsWindowVisible(window))
                count++;
        }
        return count;
    }

    private static Form MakeWindow(string title, Rectangle area) => new()
    {
        Text = title,
        StartPosition = FormStartPosition.Manual,
        Bounds = new Rectangle(area.Left + 40, area.Top + 80, 280, 180)
    };

    private static bool HorizontallyTiled(Form first, Form second) =>
        DesktopApi.IsWindowVisible(first.Handle) && DesktopApi.IsWindowVisible(second.Handle) &&
        Math.Abs(first.Top - second.Top) <= 2 &&
        Math.Abs(first.Left - second.Left) > 100 &&
        Math.Abs(first.Width - second.Width) <= 16;

    private static Point Center(Form window) => new(window.Left + window.Width / 2, window.Top + window.Height / 2);

    private static void WaitFor(Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (condition())
                return;
            Thread.Sleep(15);
        } while (timer.ElapsedMilliseconds < TimeoutMs);

        throw new TimeoutException($"Timed out: {description}.");
    }

    private static Process Start(string app, string? config, params string[] args)
    {
        var start = new ProcessStartInfo(app)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in args)
            start.ArgumentList.Add(arg);
        if (config is null)
            start.Environment.Remove("TESSEL_CONFIG");
        else
            start.Environment["TESSEL_CONFIG"] = config;
        return Process.Start(start) ?? throw new InvalidOperationException("Could not start the Tessel executable.");
    }

    private static CommandResult RunCommand(string app, string? config, params string[] args)
    {
        using var process = Start(app, config, args);
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeoutMs))
        {
            process.Kill();
            process.WaitForExit(TimeoutMs);
            throw new TimeoutException($"Tessel command timed out: {string.Join(' ', args)}.");
        }
        return new(process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record CommandResult(int ExitCode, string Output)
    {
        internal void RequireSuccess() => Check(ExitCode == 0, $"Tessel command exited with code {ExitCode}. {Output}");
    }
}
