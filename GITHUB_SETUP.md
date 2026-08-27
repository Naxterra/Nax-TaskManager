# GitHub and Preview Release Guide

This repository is already initialized and connected to:

```text
https://github.com/Naxterra/Better-Task-Manager
```

The checked-in `release-assets\BetterTaskManager-v1.0-win-x64.zip` file is the stable v1.0 artifact and should remain unchanged while preview work is being validated.

## Build and Verify

```powershell
dotnet build .\BetterTaskManager.slnx -c Release
dotnet .\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.dll --self-test
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-smoke-test
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-smoke-test --language=de
.\src\BetterTaskManager\bin\Release\net11.0-windows\BetterTaskManager.exe --ui-soak-test
```

The same sequence runs in `.github\workflows\windows-ci.yml` on pushes and pull requests. It also publishes and uploads the complete portable folder, including `TESTING.md`, as `BetterTaskManager-portable-win-x64`. This workflow has been validated locally but will not run on GitHub until the branch containing it is pushed.

## Publish the Current Preview

```powershell
.\scripts\publish-v1.ps1
```

Use `-SkipLatest` to stage only the numbered folder and ZIP while a tester is running the stable latest executable. Run the command without that switch after the old window closes to refresh both stable latest artifacts.

The self-contained single-file Windows x64 preview is placed in both a versioned folder and a stable latest-build folder:

```text
artifacts\BetterTaskManager-v1.1.0-preview.54-portable-win-x64
artifacts\BetterTaskManager-latest-portable-win-x64
```

The publish script also creates both ZIP packages automatically:

```text
artifacts\BetterTaskManager-v1.1.0-preview.54-portable-win-x64.zip
artifacts\BetterTaskManager-latest-portable-win-x64.zip
```

Build and verify the normal Windows installer with:

```powershell
.\scripts\build-installer.ps1
.\scripts\test-installer.ps1
```

This produces `artifacts\BetterTaskManager-v1.1.0-preview.54-setup-win-x64.exe` and `artifacts\SHA256SUMS-v1.1.0-preview.54.txt`. CI uploads the installer, portable folder/ZIP, and release checksum manifest together.

Do not create or push a stable tag until the preview has been approved and the version, changelog date, release notes, and packaged artifact all agree.

