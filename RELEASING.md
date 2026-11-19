# Releasing Tessel

Push an annotated version tag on a commit that includes the [release workflow](.github/workflows/release.yml).

```powershell
git tag -a v0.1.0 -m "Tessel v0.1.0"
git push origin v0.1.0
```

GitHub Actions tests and builds Windows x64, then attaches a portable ZIP containing `Tessel.exe` to a draft release with generated notes. Tags such as `v0.2.0-beta.1` produce prereleases. Review the notes and download before publishing the draft.

For desktop smoke checks, quit any running Tessel instance and use a clean, unlocked Windows desktop. Run this from the repository root after publishing locally.

```powershell
$exe = (Resolve-Path 'src/Tessel.App/bin/Release/net10.0-windows/win-x64/publish/Tessel.exe').Path
dotnet run --project tests/Tessel.Windows.Smoke -c Release -- $exe --desktop
```

Use `--mixed-dpi` for the two-display test. Verify an Arm64 build on native Windows Arm64 before adding it to the workflow. Use the same version tag for both architectures.
