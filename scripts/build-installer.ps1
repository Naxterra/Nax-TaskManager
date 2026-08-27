[CmdletBinding()]
param(
    [string]$IsccPath,
    [string]$AppIdValue = "{{9B62E509-9DBE-4C73-88EC-DF93F70835A1}",
    [string]$AppNameValue = "Better Task Manager",
    [string]$InstallerBaseNameOverride,
    [switch]$DisableCloseApplications,
    [switch]$SkipReleaseChecksums
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager\BetterTaskManager.csproj"
$installerScript = Join-Path $root "installer\BetterTaskManager.iss"
$iconPath = Join-Path $root "src\BetterTaskManager\assets\BetterTaskManager.ico"
$artifacts = Join-Path $root "artifacts"
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project Version property is missing." }

$sourceFolderName = "BetterTaskManager-v$version-portable-win-x64"
$sourceFolder = Join-Path $artifacts $sourceFolderName
$sourceExecutable = Join-Path $sourceFolder "BetterTaskManager.exe"
if (-not (Test-Path -LiteralPath $sourceExecutable)) {
    throw "Publish the portable package before building the installer: $sourceExecutable"
}
if (-not (Test-Path -LiteralPath $iconPath)) { throw "Application icon not found: $iconPath" }

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $knownPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $knownPaths | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $IsccPath = & (Join-Path $PSScriptRoot "bootstrap-inno.ps1") | Select-Object -Last 1
}
if (-not (Test-Path -LiteralPath $IsccPath)) { throw "Inno Setup compiler not found: $IsccPath" }

$previewNumber = 0
if ($version -match 'preview\.(\d+)$') { $previewNumber = [int]$Matches[1] }
$numericVersion = "1.1.0.$previewNumber"
$installerBaseName = if ([string]::IsNullOrWhiteSpace($InstallerBaseNameOverride)) { "BetterTaskManager-v$version-setup-win-x64" } else { $InstallerBaseNameOverride }
$installerPath = Join-Path $artifacts ($installerBaseName + ".exe")
$closeApplicationsValue = if ($DisableCloseApplications) { "no" } else { "yes" }

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $IsccPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($argument in @(
    "/Qp",
    "/DAppVersion=$version",
    "/DNumericVersion=$numericVersion",
    "/DSourceDir=$sourceFolder",
    "/DOutputDir=$artifacts",
    "/DInstallerBaseName=$installerBaseName",
    "/DIconPath=$iconPath",
    "/DAppIdValue=$AppIdValue",
    "/DAppNameValue=$AppNameValue",
    "/DCloseApplicationsValue=$closeApplicationsValue",
    $installerScript
)) {
    [void]$startInfo.ArgumentList.Add($argument)
}
$process = [System.Diagnostics.Process]::Start($startInfo)
$standardOutput = $process.StandardOutput.ReadToEndAsync()
$standardError = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
$compilerOutput = ($standardOutput.GetAwaiter().GetResult() + [Environment]::NewLine + $standardError.GetAwaiter().GetResult()).Trim()
if ($process.ExitCode -ne 0) { throw "Inno Setup compilation failed with exit code $($process.ExitCode).`n$compilerOutput" }
if (-not (Test-Path -LiteralPath $installerPath)) { throw "Installer output not found: $installerPath" }

Write-Host "Built installer: $installerPath"
if (-not $SkipReleaseChecksums) {
    $zipPath = Join-Path $artifacts ($sourceFolderName + ".zip")
    $checksumPath = Join-Path $artifacts "SHA256SUMS-v$version.txt"
    $checksumLines = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($zipPath, $installerPath, $sourceExecutable)) {
        if (-not (Test-Path -LiteralPath $path)) { throw "Release checksum input not found: $path" }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $relativeName = if ($path -eq $sourceExecutable) { "$sourceFolderName/BetterTaskManager.exe" } else { Split-Path $path -Leaf }
        $checksumLines.Add($hash + " *" + $relativeName)
    }
    [System.IO.File]::WriteAllText($checksumPath, ($checksumLines -join [Environment]::NewLine) + [Environment]::NewLine, [System.Text.Encoding]::ASCII)
    Write-Host "Wrote release checksums: $checksumPath"
}
