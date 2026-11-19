# Tessel

Tessel is a native tiling window manager for Windows 11. It arranges windows automatically and gives you keyboard and mouse controls for navigating, moving, and resizing them. Its nested split containers and workspaces are inspired by i3 and Sway.

- Organize windows in nested splits, tabs, and stacked groups.
- Show different workspaces across multiple monitors, or limit tiling to selected displays.
- Drag windows into new positions with a live layout preview, or resize them along a shared split.
- Customize shortcuts, gaps, focus borders, and selection indicators. The native workspace bar, layout headers, and tray menu follow Windows light, dark, and high-contrast settings.

## Getting started

When a release is available, download the Windows x64 ZIP from the repository’s **Releases** page, extract it, and run `Tessel.exe`. The portable Native AOT build includes the .NET runtime. Windows x64 covers 64-bit Intel and AMD PCs. An Arm64 build is possible but has yet to be verified on an Arm64 desktop.

Tessel starts in the Windows notification area. Open its tray menu, under **Show hidden icons** if needed, to open the config, reload it, reset the layout, or quit.

To update a portable copy, quit Tessel, extract the new version, and run it. Your configuration stays in your user profile.

### Default shortcuts

| Shortcut | Action |
| --- | --- |
| `Alt+1`–`Alt+9` | Switch workspace |
| `Alt+Shift+1`–`Alt+Shift+9` | Move the focused window to a workspace |
| `Alt` + arrows or `H/J/K/L` | Move focus |
| `Alt+Shift` + arrows or `H/J/K/L` | Move the focused window or reorder it in a group |
| `Alt+Ctrl` + arrows | Resize a tile |
| `Alt+V` | Toggle horizontal or vertical split |
| `Alt+A` / `Alt+D` | Focus the parent or child container |
| `Alt+W` / `Alt+S` / `Alt+T` | Use a tabbed, stacked, or split layout |
| `Alt+Shift+Space` | Toggle floating |
| `Alt+F` | Toggle fullscreen |
| `Ctrl+Alt+R` | Reload configuration |
| `Ctrl+Alt+Shift+R` | Rescan windows and displays and reapply the layout |

## Configuration

Tessel creates `%USERPROFILE%\.tessel\config.json` on first launch. Use **Open configuration** in the tray menu to edit it. JSON comments and trailing commas are supported. Set the `TESSEL_CONFIG` environment variable before launching Tessel if you want to use a different file. If you have an older config at `%LOCALAPPDATA%\Tessel\config.json`, Tessel copies it to the default location if the new file does not exist.

```jsonc
{
  "gaps": {
    "inner": 8,
    "outer": 8,
    "topBar": 0,
    "layoutHeader": 8,
    "taskbar": 0
  },
  "focusedWindowBorder": { "enabled": true, "color": "#609BE7", "thickness": 1 },
  "parentBorder": { "enabled": true, "color": "#EE9A45", "thickness": 2 },
  "workspaceIndicator": { "light": "#005FB8", "dark": "#60CDFF" },
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

- **Spacing.** Gaps and border widths use device-independent pixels. All gaps default to zero. The outer gap adds to the top bar and taskbar gaps where they meet.
- **Colors.** Use `#RRGGBB` for custom colors. An omitted or `null` indicator color uses the Windows accent for that theme. High contrast uses the system palette.
- **Monitors.** Choose `All`, `PrimaryOnly`, or `Selected`. List the Windows device names under `selectedDeviceNames` for selected mode. A workspace assignment is a preferred display. Run `Tessel.exe list-monitors` to find the names. Dragging a window onto an unmanaged display releases it from its Tessel workspace. Drag it back onto a managed display to tile it again.
- **Keybindings.** Omitted actions keep their defaults. An array replaces an action’s shortcuts, and `[]` disables it. The generated config lists the available actions. Older action IDs such as `focus.left` still work.
- **Layout.** `defaultLayout` applies on startup. Reloading preserves existing workspace layouts. `resizeStep` controls keyboard split resizing, and `liveDragPreview` controls whether tiles rearrange during a drag.

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

Check existing reports before opening a GitHub issue. Include your Tessel version or commit, Windows 11 build and CPU architecture, and steps to reproduce. For layout or focus bugs, include the monitor arrangement and scaling, which workspace and layout you were using, and what you expected to happen. Share only the config settings relevant to the report.

## Building and contributing

Install the .NET 10 SDK. For Native AOT builds, install Visual Studio 2022 or later with the **Desktop development with C++** workload. Run these commands from the repo root.

```powershell
dotnet build Tessel.sln -c Release
dotnet test Tessel.sln -c Release
dotnet publish src/Tessel.App -c Release -r win-x64
```

The executable is at `src/Tessel.App/bin/Release/net10.0-windows/win-x64/publish/Tessel.exe`. See the [maintainer release guide](RELEASING.md) for Windows smoke tests, packaging, and releases.

Pull requests are welcome. Describe the behavior you’re changing and how you tested it. Add or update Core tests when changing layout behavior. For Windows integration changes, include the manual or smoke checks you ran. See [AGENTS.md](AGENTS.md) for the project structure and implementation guidelines.

## License

Tessel is available under the [MIT License](LICENSE).
