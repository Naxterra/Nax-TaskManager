[CmdletBinding()]
param()

# Publishes the WinUI 3 app (with the bundled history service) as the portable folder and ZIP that
# build-installer.ps1 packages. Framework-dependent: needs the .NET 11 desktop runtime.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager.Fluent\BetterTaskManager.Fluent.csproj"
$artifacts = Join-Path $root "artifacts"
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project Version property is missing." }

$folderName = "NaxTaskManager-v$version-portable-win-x64"
$output = Join-Path $artifacts $folderName
$artifactsFull = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
$outputFull = [System.IO.Path]::GetFullPath($output)
if (-not $outputFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $outputFull -Leaf) -ne $folderName) {
    throw "Refusing to clean an unverified publish directory: $outputFull"
}

if (Test-Path -LiteralPath $outputFull) { Remove-Item -LiteralPath $outputFull -Recurse -Force }

dotnet publish $project -c Release -r win-x64 --self-contained false -o $outputFull
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$executable = Join-Path $outputFull "Nax-TaskManager.exe"
foreach ($required in @(
    $executable,
    (Join-Path $outputFull "Nax-TaskManager.pri"),
    (Join-Path $outputFull "MainWindow.xbf"),
    (Join-Path $outputFull "HistoryService\Nax-TaskManager.HistoryService.exe")
)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Published output is incomplete, missing: $required" }
}
foreach ($document in @("README.md", "SECURITY.md", "LICENSE")) {
    Copy-Item -LiteralPath (Join-Path $root $document) -Destination (Join-Path $outputFull $document)
}

$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
$manifest = $hash.Hash.ToLowerInvariant() + " *Nax-TaskManager.exe" + [Environment]::NewLine
[System.IO.File]::WriteAllText((Join-Path $outputFull "SHA256SUMS.txt"), $manifest, [System.Text.Encoding]::ASCII)

$versionedZip = Join-Path $artifacts ($folderName + ".zip")
if (Test-Path -LiteralPath $versionedZip) { Remove-Item -LiteralPath $versionedZip -Force }
Compress-Archive -LiteralPath $outputFull -DestinationPath $versionedZip -CompressionLevel Optimal

Write-Host "Published portable Nax-TaskManager v$version to $outputFull"
