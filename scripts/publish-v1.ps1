$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager\BetterTaskManager.csproj"
$artifacts = Join-Path $root "artifacts"
[xml]$projectXml = Get-Content -LiteralPath $project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw "The project Version property is missing." }

$folderName = "BetterTaskManager-v$version-portable-win-x64"
$output = Join-Path $artifacts $folderName
$latestFolderName = "BetterTaskManager-latest-portable-win-x64"
$latestOutput = Join-Path $artifacts $latestFolderName
$artifactsFull = [System.IO.Path]::GetFullPath($artifacts).TrimEnd('\') + '\'
$outputFull = [System.IO.Path]::GetFullPath($output)
$latestOutputFull = [System.IO.Path]::GetFullPath($latestOutput)
if (-not $outputFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $outputFull -Leaf) -ne $folderName) {
    throw "Refusing to clean an unverified publish directory: $outputFull"
}
if (-not $latestOutputFull.StartsWith($artifactsFull, [System.StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $latestOutputFull -Leaf) -ne $latestFolderName) {
    throw "Refusing to clean an unverified latest directory: $latestOutputFull"
}

if (Test-Path -LiteralPath $outputFull) { Remove-Item -LiteralPath $outputFull -Recurse -Force }

dotnet publish $project -c Release -p:PublishProfile=win-x64-portable -o $outputFull

$executable = Join-Path $outputFull "BetterTaskManager.exe"
if (-not (Test-Path -LiteralPath $executable)) { throw "Published executable not found: $executable" }
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $outputFull "README.md")
Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $outputFull "CHANGELOG.md")
Copy-Item -LiteralPath (Join-Path $root "RELEASE_NOTES-v1.1-preview.md") -Destination (Join-Path $outputFull "RELEASE_NOTES-v1.1-preview.md")
Copy-Item -LiteralPath (Join-Path $root "SECURITY.md") -Destination (Join-Path $outputFull "SECURITY.md")
Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $outputFull "LICENSE")

$hash = Get-FileHash -LiteralPath $executable -Algorithm SHA256
$manifest = $hash.Hash.ToLowerInvariant() + " *BetterTaskManager.exe" + [Environment]::NewLine
[System.IO.File]::WriteAllText((Join-Path $outputFull "SHA256SUMS.txt"), $manifest, [System.Text.Encoding]::ASCII)

if (Test-Path -LiteralPath $latestOutputFull) { Remove-Item -LiteralPath $latestOutputFull -Recurse -Force }
Copy-Item -LiteralPath $outputFull -Destination $latestOutputFull -Recurse

$versionedZip = Join-Path $artifacts ($folderName + ".zip")
$latestZip = Join-Path $artifacts ($latestFolderName + ".zip")
if (Test-Path -LiteralPath $versionedZip) { Remove-Item -LiteralPath $versionedZip -Force }
if (Test-Path -LiteralPath $latestZip) { Remove-Item -LiteralPath $latestZip -Force }
Compress-Archive -LiteralPath $outputFull -DestinationPath $versionedZip -CompressionLevel Optimal
Compress-Archive -LiteralPath $latestOutputFull -DestinationPath $latestZip -CompressionLevel Optimal

Write-Host "Published portable Better Task Manager v$version to $outputFull"
Write-Host "Updated latest executable at $(Join-Path $latestOutputFull 'BetterTaskManager.exe')"
Write-Host "Created versioned and latest ZIP packages."

