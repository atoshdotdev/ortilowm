# <img src="assets/tessel-logo.png" alt="" width="48" height="48" align="top"> Tessel

Tessel is a native tiling window manager for Windows 11. It arranges windows automatically and gives you keyboard and mouse controls for navigating, moving, and resizing them. Its nested split containers and workspaces are inspired by i3 and Sway.

- Organize windows in nested splits, tabs, and stacked groups.
- Show different workspaces across multiple monitors, or limit tiling to selected displays.
- Drag windows into new positions with a live layout preview, or resize them along a shared split.
- Customize shortcuts, gaps, focus borders, and selection indicators. The native workspace bar, layout headers, and tray menu follow Windows light, dark, and high-contrast settings.

## Table of contents

- [Getting started](#getting-started)
  - [Install on Windows 11](#install-on-windows-11)
  - [Default shortcuts](#default-shortcuts)
- [Configuration](#configuration)
- [Troubleshooting and reporting issues](#troubleshooting-and-reporting-issues)
- [Building and contributing](#building-and-contributing)
- [License](#license)

## Getting started

### Install on Windows 11

Download and run the **Windows x64 installer** ending in `-win-x64-setup.exe`. Setup requests administrator approval and installs Tessel for all users under `C:\Program Files\Tessel` by default. Setup creates the default config if needed, adds **Tessel** to the Start menu, and offers to launch it when finished. [GitHub Releases](https://github.com/atoshdotdev/tessel/releases)

The installer includes the .NET runtime and supports Windows 11 PCs with 64-bit Intel or AMD processors. An Arm64 build is possible but has yet to be verified on an Arm64 desktop.

Tessel starts in the Windows notification area. Open its tray menu, under **Show hidden icons** if needed, to open the config, reload it, reset the layout, or quit.

To update, quit Tessel and run the newer installer. To uninstall the installed copy, use **Settings → Apps → Installed apps → Tessel**. Your config stays in your profile through updates and uninstall.

### Default shortcuts

| Shortcut | Action |
| --- | --- |
| `Alt+1`–`Alt+9` | Switch workspace |
| `Alt+Shift+1`–`Alt+Shift+9` | Move the focused window to a workspace |
| `Alt+H` / `Alt+Left` | Focus left |
| `Alt+J` / `Alt+Down` | Focus down |
| `Alt+K` / `Alt+Up` | Focus up |
| `Alt+L` / `Alt+Right` | Focus right |
| `Alt+Shift+H` / `Alt+Shift+Left` | Move window left |
| `Alt+Shift+J` / `Alt+Shift+Down` | Move window down |
| `Alt+Shift+K` / `Alt+Shift+Up` | Move window up |
| `Alt+Shift+L` / `Alt+Shift+Right` | Move window right |
| `Ctrl+Alt+Shift+Left` | Move workspace to previous monitor |
| `Ctrl+Alt+Shift+Right` | Move workspace to next monitor |
| `Ctrl+Alt+Left` | Resize left |
| `Ctrl+Alt+Down` | Resize down |
| `Ctrl+Alt+Up` | Resize up |
| `Ctrl+Alt+Right` | Resize right |
| `Alt+V` | Toggle split direction |
| `Alt+A` | Focus parent container |
| `Alt+D` | Focus child container |
| `Alt+T` | Use split layout |
| `Alt+W` | Use tabbed layout |
| `Alt+S` | Use stacked layout |
| `Alt+E` | Use master stack layout |
| `Alt+Shift+Space` | Toggle floating |
| `Alt+F` | Toggle fullscreen |
| `Alt+M` | Minimize window |
| `Alt+Shift+0` | Reapply layout |
| `Ctrl+Alt+R` | Reload configuration |
| `Ctrl+Alt+Shift+R` | Rescan windows and displays and reapply the layout |

## Configuration

Tessel creates `%USERPROFILE%\.tessel\config.json` during setup for the user who launched it, or on first launch for each additional user. Use **Open configuration** in the tray menu to edit it. JSON comments and trailing commas are supported. Set the `TESSEL_CONFIG` environment variable before running setup or launching Tessel if you want to use a different file. If you have an older config at `%LOCALAPPDATA%\Tessel\config.json`, Tessel copies it to the default location if the new file does not exist.

This example customizes spacing and colors, prefers specific displays for workspaces 1 and 2, and disables the minimize shortcut.

```jsonc
{
  "gaps": {
    "inner": 8,
    "outer": 8,
    "topBar": 0,
    "layoutHeader": 8,
    "taskbar": 0
  },
  "focusedWindowBorder": { "enabled": true, "color": null, "thickness": 1 },
  "parentBorder": { "enabled": true, "color": null, "thickness": 2 },
  "workspaceIndicator": { "light": null, "dark": null },
  "tabIndicator": { "light": null, "dark": null },
  "showTopBar": true,
  "defaultLayout": "ContainerTree",
  "resizeStep": 0.05,
  "liveDragPreview": true,
  "monitors": {
    "mode": "All",
    "selectedDeviceNames": [],
    "workspaceDeviceNames": {
      "1": "\\\\.\\DISPLAY1",
      "2": "\\\\.\\DISPLAY2"
    }
  },
  "keybindings": {
    "Focus left": ["Alt+H", "Alt+Left"],
    "Minimize window": [],
    "Reload configuration": ["Ctrl+Alt+R"]
  }
}
```

- **Spacing.** Gaps are 0–256 device-independent pixels and default to zero. `inner` separates neighboring tiles, `outer` insets the layout, `topBar` adds space under Tessel’s workspace bar, `layoutHeader` separates a tabbed or stacked header from its windows, and `taskbar` adds space along edges reserved by the Windows taskbar. The outer gap adds to the top bar and taskbar gaps where they meet. Border thickness is 1–16 device-independent pixels. Use `enabled: false` in either border section to hide that border.
- **Colors.** Use `#RRGGBB` for custom colors. Omitted or `null` border and indicator colors follow the Windows accent. Set a `#RRGGBB` color to override it. Existing configs keep their explicit colors until you change them to `null`. High contrast uses the system palette.
- **Monitors.** Choose `All`, `PrimaryOnly`, or `Selected`. In `Selected` mode, `selectedDeviceNames` lists the displays Tessel manages. `workspaceDeviceNames` assigns workspace numbers to preferred managed displays. Multiple workspaces can prefer the same display, which shows one workspace at a time. Dragging a window onto an unmanaged display releases it from its Tessel workspace. Drag it back onto a managed display to tile it again.
- **Keybindings.** Omitted actions keep their defaults. An array replaces an action’s shortcuts, and `[]` disables it. Each shortcut must be unique, including shortcuts inherited from the defaults. The generated config lists the action names. Numbered actions use names such as `Switch to workspace 3`. Older action IDs such as `focus.left` still work.
- **Layout.** `defaultLayout` sets the starting layout. Reloading preserves existing workspace layouts. `resizeStep` is the fraction used for keyboard split resizing and must be greater than 0 and at most 0.5. `liveDragPreview` controls whether tiles rearrange during a drag. Set `showTopBar` to `false` to hide Tessel’s workspace bar.

To find display names after a default installation, open PowerShell from any folder and run the following. The `&` operator runs the executable at the quoted path.

```powershell
& "$env:ProgramFiles\Tessel\Tessel.exe" list-monitors | Out-Host
```

A display reported as `\\.\DISPLAY1` is written as `"\\\\.\\DISPLAY1"` in JSON because every backslash must be escaped. Use that same JSON string in both monitor settings when selecting a display and assigning workspaces to it.

For a custom installation folder, replace the executable path with the location you chose during setup. You can also open PowerShell in that folder and run `.\Tessel.exe list-monitors | Out-Host`. The command only lists connected displays and marks the primary display. It exits without starting the window manager or changing settings.

From the folder containing the executable, run the following in PowerShell to validate the config and ask the running instance to reload it.

```powershell
.\Tessel.exe check-config | Out-Host
if ($LASTEXITCODE -eq 0) { .\Tessel.exe reload | Out-Host }
```

Other commands include `init-config` to create the default file, `config-path` to print its location, `format-config` to update older keybinding names and notation, and `reset` to rescan displays and windows. Run `reload` and `reset` at the same privilege level as the running Tessel instance.

## Troubleshooting and reporting issues

- **Missing windows after an unexpected exit.** Start Tessel again to restore windows it hid. Minimized windows remain minimized.
- **An elevated window stays untiled.** Windows running at a higher privilege level than Tessel stay visible and occupy no tile space. To tile apps such as elevated Task Manager, quit Tessel and reopen it with **Run as administrator**.
- **The bars look solid.** Tessel uses solid theme colors when Windows transparency is disabled, high contrast is enabled, or the backdrop is unavailable.

Check existing reports before opening an issue. Include your Tessel version or commit, Windows 11 build and CPU architecture, steps to reproduce, and both the expected and actual behavior. For layout or focus bugs, include the monitor arrangement and scaling and the affected workspace and layout. Share only the config settings relevant to the report. [GitHub Issues](https://github.com/atoshdotdev/tessel/issues)

## Building and contributing

Install the .NET 10 SDK. For Native AOT builds, install Visual Studio 2022 or later with the **Desktop development with C++** workload. Run these commands from the repo root.

```powershell
dotnet build Tessel.sln -c Release
dotnet test Tessel.sln -c Release
dotnet publish src/Tessel.App -c Release -r win-x64
```

The executable is at `src/Tessel.App/bin/Release/net10.0-windows/win-x64/publish/Tessel.exe`. See the [maintainer release guide](RELEASING.md) for Windows smoke tests, packaging, and releases.

Pull requests are welcome. Fork the repo, work on a branch, and describe what changed and how you tested it. Link an existing issue if there is one.

- `src/Tessel.Core` contains the layout and workspace logic. Add or update tests in `tests/Tessel.Core.Tests` when changing that behavior.
- `src/Tessel.Platform.Windows` handles Win32 integration. For changes here, include the Windows smoke checks or manual verification you ran.
- `src/Tessel.App` wires the app together and handles configuration and commands.

## License

Tessel is available under the [MIT License](LICENSE).
