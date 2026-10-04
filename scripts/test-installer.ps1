[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $root "src\BetterTaskManager.Fluent\BetterTaskManager.Fluent.csproj")
$version = [string]$projectXml.Project.PropertyGroup.Version
$testAppId = "{{7A0B9BC3-3768-472D-819D-8C977CDA49DA}"
$testRegistryAppId = "{7A0B9BC3-3768-472D-819D-8C977CDA49DA}"
$testAppName = "Nax-TaskManager Installer Test"
$testInstallerBaseName = "NaxTaskManager-v$version-installer-test-win-x64"
$InstallerPath = Join-Path $root "artifacts\$testInstallerBaseName.exe"
& (Join-Path $PSScriptRoot "build-installer.ps1") -AppIdValue $testAppId -AppNameValue $testAppName `
    -UninstallRegistryId $testRegistryAppId -InstallerBaseNameOverride $testInstallerBaseName `
    -DisableCloseApplications -SkipReleaseChecksums
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }
$sourceIconPath = Join-Path $root "src\BetterTaskManager.Fluent\Assets\AppIcon.ico"
if (-not (Test-Path -LiteralPath $sourceIconPath)) { throw "Source icon not found: $sourceIconPath" }

Add-Type -AssemblyName System.Drawing.Common
function Get-IconPixelHash([string]$Path, [bool]$ExtractAssociated) {
    $icon = if ($ExtractAssociated) {
        [System.Drawing.Icon]::ExtractAssociatedIcon($Path)
    } else {
        [System.Drawing.Icon]::new($Path, 32, 32)
    }
    if ($null -eq $icon) { throw "No icon could be extracted from: $Path" }
    try {
        $bitmap = $icon.ToBitmap()
        try {
            $pixels = [System.Collections.Generic.List[byte]]::new($bitmap.Width * $bitmap.Height * 4)
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $bitmap.Width; $x++) {
                    $pixels.AddRange([BitConverter]::GetBytes($bitmap.GetPixel($x, $y).ToArgb()))
                }
            }
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try { return [Convert]::ToHexString($sha.ComputeHash($pixels.ToArray())) }
            finally { $sha.Dispose() }
        } finally { $bitmap.Dispose() }
    } finally { $icon.Dispose() }
}

$expectedIconHash = Get-IconPixelHash $sourceIconPath $false
$productionInstallerPath = Join-Path $root "artifacts\NaxTaskManager-v$version-setup-win-x64.exe"
if (-not (Test-Path -LiteralPath $productionInstallerPath)) { throw "Production installer not found: $productionInstallerPath" }
$productionInstallerIconHash = Get-IconPixelHash $productionInstallerPath $true
if ($productionInstallerIconHash -ne $expectedIconHash) { throw "Production installer icon does not match the application icon." }
$installerIconHash = Get-IconPixelHash $InstallerPath $true
if ($installerIconHash -ne $expectedIconHash) { throw "Installer icon does not match the application icon." }

function Invoke-WaitedProcess([string]$FileName, [string[]]$Arguments) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    return $process.ExitCode
}

function Get-RegisteredUninstaller([string]$RegistryPath) {
    $metadata = Get-ItemProperty -LiteralPath $RegistryPath
    $command = [string]$metadata.UninstallString
    if ($command -match '^"([^"]+)"') { return $Matches[1] }
    if ($command -match '^(\S+)') { return $Matches[1] }
    throw "UninstallString is missing or invalid at $RegistryPath"
}

$testRoot = Join-Path $root "artifacts\installer-test"
$installDirectory = Join-Path $testRoot $testAppName
$logPath = Join-Path $testRoot "install.log"
$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$($testRegistryAppId)_is1"
$testRootFull = [System.IO.Path]::GetFullPath($testRoot)
$artifactsFull = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts")).TrimEnd('\') + '\'
if (-not $testRootFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $testRootFull -Leaf) -ne "installer-test") {
    throw "Refusing to use an unverified installer test directory: $testRootFull"
}
if (Test-Path -LiteralPath $uninstallRegistryPath) {
    $staleUninstaller = Get-RegisteredUninstaller $uninstallRegistryPath
    if (Test-Path -LiteralPath $staleUninstaller) {
        $staleExit = Invoke-WaitedProcess $staleUninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
        if ($staleExit -ne 0) { throw "Stale test installation cleanup failed with exit code $staleExit." }
    }
}
if (Test-Path -LiteralPath $testRootFull) { Remove-Item -LiteralPath $testRootFull -Recurse -Force }
New-Item -ItemType Directory -Path $testRootFull -Force | Out-Null

$installArguments = @(
    "/VERYSILENT",
    "/SUPPRESSMSGBOXES",
    "/NORESTART",
    "/CLOSEAPPLICATIONS",
    "/CURRENTUSER",
    "/DIR=$installDirectory",
    "/LOG=$logPath"
)
$firstInstallExit = Invoke-WaitedProcess $InstallerPath $installArguments
if ($firstInstallExit -ne 0) { throw "Installer failed with exit code $firstInstallExit. See $logPath" }

$installedExe = Join-Path $installDirectory "NaxTaskManager.exe"
$uninstaller = Get-RegisteredUninstaller $uninstallRegistryPath
$startMenuGroup = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) $testAppName
$startMenuShortcut = Join-Path $startMenuGroup ($testAppName + ".lnk")
$desktopShortcut = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) ($testAppName + ".lnk")
foreach ($required in @($installedExe, $uninstaller, (Join-Path $installDirectory "README.md"), (Join-Path $installDirectory "LICENSE"),
    (Join-Path $installDirectory "HistoryService\NaxTaskManager.HistoryService.exe"))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Installed file missing: $required" }
}
if (-not (Test-Path -LiteralPath $startMenuShortcut)) { throw "Start Menu shortcut missing: $startMenuShortcut" }
if (Test-Path -LiteralPath $desktopShortcut) { throw "Desktop shortcut should remain optional and unchecked by default: $desktopShortcut" }
if (-not (Test-Path -LiteralPath $uninstallRegistryPath)) { throw "Current-user uninstall registration missing: $uninstallRegistryPath" }
$uninstallMetadata = Get-ItemProperty -LiteralPath $uninstallRegistryPath
if ($uninstallMetadata.DisplayName -notlike "$testAppName*" -or $uninstallMetadata.DisplayVersion -ne $version -or
    $uninstallMetadata.InstallLocation.TrimEnd('\') -ne $installDirectory.TrimEnd('\')) {
    throw "Uninstall metadata does not match the installed version/directory."
}
$installedIconHash = Get-IconPixelHash $installedExe $true
if ($installedIconHash -ne $expectedIconHash) { throw "Installed executable icon does not match the application icon." }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($startMenuShortcut)
if ($shortcut.TargetPath.TrimEnd('\') -ne $installedExe.TrimEnd('\')) { throw "Start Menu shortcut target is incorrect: $($shortcut.TargetPath)" }

# Starts the installed app and waits for its window. Returns $null when another copy is open: the app is single
# instance, so a second launch would only hand over to that copy.
function Start-InstalledApp {
    $other = @(Get-CimInstance Win32_Process -Filter "Name = 'NaxTaskManager.exe'" | Where-Object { $_.ExecutablePath -ne $installedExe })
    if ($other.Count -gt 0) {
        Write-Warning "Skipping app launch checks because another Nax-TaskManager instance is already open."
        return $null
    }
    $app = Start-Process -FilePath $installedExe -ArgumentList @("--page", "Processes") -PassThru
    for ($i = 0; $i -lt 60 -and $app.MainWindowHandle -eq [IntPtr]::Zero; $i++) {
        if ($app.HasExited) { throw "Installed app exited during startup with code $($app.ExitCode)." }
        Start-Sleep -Milliseconds 500
        $app.Refresh()
    }
    if ($app.MainWindowHandle -eq [IntPtr]::Zero) { throw "Installed app did not show its window within 30 seconds." }
    return $app
}

$crashLog = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "NaxTaskManager\crash.log"
$crashLogLength = if (Test-Path -LiteralPath $crashLog) { (Get-Item -LiteralPath $crashLog).Length } else { 0 }
$runningTestApp = Start-InstalledApp

$secondInstallExit = Invoke-WaitedProcess $InstallerPath $installArguments
if ($secondInstallExit -ne 0) { throw "Installer uninstall-before-reinstall pass failed with exit code $secondInstallExit." }
if ($null -ne $runningTestApp -and -not $runningTestApp.WaitForExit(5000)) {
    throw "The previous installed app remained running during uninstall-before-reinstall."
}
$upgradeLog = Get-Content -LiteralPath $logPath -Raw
if ($upgradeLog -notmatch "Existing installation detected" -or
    $upgradeLog -notmatch "Previous installation removed successfully before installing the new version") {
    throw "Upgrade did not run and complete the previous uninstaller before reinstalling."
}
if ((Get-ChildItem -LiteralPath $installDirectory -Filter "unins*.exe" -File).Count -ne 1) {
    throw "Upgrade/repair created multiple uninstallers instead of reusing the stable AppId."
}
$uninstaller = Get-RegisteredUninstaller $uninstallRegistryPath
if ((Split-Path $uninstaller -Leaf) -ne "unins000.exe" -or -not (Test-Path -LiteralPath $uninstaller)) {
    throw "The previous uninstaller was not fully removed before the new version was installed: $uninstaller"
}

# Smoke test of the reinstalled app: it must start, keep running with live refreshes for a while, and log no crash.
$smokeApp = Start-InstalledApp
if ($null -ne $smokeApp) {
    Start-Sleep -Seconds 10
    if ($smokeApp.HasExited) { throw "Installed app exited during the smoke test with code $($smokeApp.ExitCode)." }
    [void]$smokeApp.CloseMainWindow()
    if (-not $smokeApp.WaitForExit(15000)) { $smokeApp.Kill(); throw "Installed app did not close within 15 seconds." }
}
$newCrashLogLength = if (Test-Path -LiteralPath $crashLog) { (Get-Item -LiteralPath $crashLog).Length } else { 0 }
if ($newCrashLogLength -gt $crashLogLength) { throw "The app wrote to its crash log during the installer test:`n$(Get-Content -LiteralPath $crashLog -Tail 30 | Out-String)" }

$uninstallExit = Invoke-WaitedProcess $uninstaller @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART")
if ($uninstallExit -ne 0) { throw "Uninstaller failed with exit code $uninstallExit." }
if (Test-Path -LiteralPath $installedExe) { throw "Installed executable remained after uninstall: $installedExe" }
if (Test-Path -LiteralPath $startMenuShortcut) { throw "Start Menu shortcut remained after uninstall: $startMenuShortcut" }
if (Test-Path -LiteralPath $uninstallRegistryPath) { throw "Uninstall registration remained after uninstall: $uninstallRegistryPath" }

Write-Host "Installer verification passed: install metadata/shortcuts, uninstall-before-reinstall, app smoke test, and final cleanup."
