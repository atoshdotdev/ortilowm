# Releasing Tessel

Push an annotated version tag on a commit that includes the [release workflow](.github/workflows/release.yml).

```powershell
git tag -a v0.1.1 -m "Tessel v0.1.1"
git push origin v0.1.1
```

GitHub Actions tests the Windows x64 build, compiles the installer with the runner’s Inno Setup compiler, and checks installation, reinstallation, and uninstall. It attaches the setup EXE and portable ZIP to a draft release with generated notes. Tags such as `v0.2.0-beta.1` produce prereleases. Review the notes and test the downloads on Windows 11 before publishing the draft. [Windows runner software](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)

For desktop smoke checks, quit any running Tessel instance and use a clean, unlocked Windows desktop. Run this from the repository root after publishing locally.

```powershell
$exe = (Resolve-Path 'src/Tessel.App/bin/Release/net10.0-windows/win-x64/publish/Tessel.exe').Path
dotnet run --project tests/Tessel.Windows.Smoke -c Release -- $exe --desktop
```

Use `--mixed-dpi` for the two-display test. Verify an Arm64 build on native Windows Arm64 before adding it to the workflow. Use the same version tag for both architectures.

## Build the installer locally

Publish Windows x64 first. With Inno Setup installed, run the following from the repo root. Use the full path to `ISCC.exe` if it is outside your `PATH`. [Inno Setup compiler documentation](https://jrsoftware.org/inno-setup/documentation/topic_compilercmdline.htm)

```powershell
dotnet publish src/Tessel.App -c Release -r win-x64 -p:Version=0.1.1
ISCC.exe /DAppVersion=0.1.1 installer/Tessel.iss
```

The installer is written to `artifacts/Tessel-v0.1.1-win-x64-setup.exe`. Keep the app ID in [Tessel.iss](installer/Tessel.iss) stable so new versions update the same installation. Before publishing, test a fresh install, an upgrade from the previous public installer when available, and uninstall on Windows 11. Check that the Start menu shortcut works, setup asks you to quit a running Tessel instance, and an existing config survives. Release artifacts are unsigned unless you configure code signing.
