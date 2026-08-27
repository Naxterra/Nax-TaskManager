[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$version = "6.7.3"
$expectedHash = "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732"
$downloadUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe"
$toolsRoot = Join-Path $root "artifacts\.tools"
$installRoot = Join-Path $toolsRoot "inno-setup-$version"
$downloadRoot = Join-Path $toolsRoot "downloads"
$downloadPath = Join-Path $downloadRoot "innosetup-$version.exe"
$iscc = Join-Path $installRoot "ISCC.exe"

if (Test-Path -LiteralPath $iscc) {
    Write-Output $iscc
    exit 0
}

New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $downloadPath)) {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $downloadPath
}

$actualHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $expectedHash) {
    throw "Inno Setup bootstrap hash mismatch. Expected $expectedHash but received $actualHash."
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $downloadPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($argument in @("/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CURRENTUSER", "/DIR=$installRoot")) {
    [void]$startInfo.ArgumentList.Add($argument)
}
$process = [System.Diagnostics.Process]::Start($startInfo)
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    throw "Inno Setup bootstrap failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $iscc)) {
    throw "Inno Setup compiler was not found after bootstrap: $iscc"
}

Write-Output $iscc
