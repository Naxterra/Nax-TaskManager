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
```

## Publish the Current Preview

```powershell
.\scripts\publish-v1.ps1
```

The self-contained single-file Windows x64 preview is placed in:

```text
artifacts\BetterTaskManager-v1.1.0-preview.23-portable-win-x64
```

Create a ZIP for manual testing or a GitHub prerelease with:

```powershell
Compress-Archive `
  -LiteralPath .\artifacts\BetterTaskManager-v1.1.0-preview.23-portable-win-x64 `
  -DestinationPath .\artifacts\BetterTaskManager-v1.1.0-preview.23-portable-win-x64.zip `
  -CompressionLevel Optimal
```

Do not create or push a stable tag until the preview has been approved and the version, changelog date, release notes, and packaged artifact all agree.

