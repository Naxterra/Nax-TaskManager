$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager\BetterTaskManager.csproj"
$artifacts = Join-Path $root "artifacts"
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project Version property is missing." }

$folderName = "BetterTaskManager-v$version-portable-win-x64"
$output = Join-Path $artifacts $folderName
$artifactsFull = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
$outputFull = [System.IO.Path]::GetFullPath($output)
if (-not $outputFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $outputFull -Leaf) -ne $folderName) {
    throw "Refusing to clean an unverified publish directory: $outputFull"
}

if (Test-Path -LiteralPath $outputFull) { Remove-Item -LiteralPath $outputFull -Recurse -Force }

dotnet publish $project -c Release -p:PublishProfile=win-x64-portable -o $outputFull

$executable = Join-Path $outputFull "BetterTaskManager.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable not found: $executable" }
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $outputFull "README.md")
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $outputFull "CHANGELOG.md")
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $outputFull "LICENSE")

$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
$manifest = $hash.Hash.ToLowerInvariant() + " *BetterTaskManager.exe" + [Environment]::NewLine
[System.IO.File]::WriteAllText((Join-Path $outputFull "SHA256SUMS.txt"), $manifest, [System.Text.Encoding]::ASCII)

Write-Host "Published portable Better Task Manager v$version to $outputFull"

