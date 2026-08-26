$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager\BetterTaskManager.csproj"
$output = Join-Path $root "artifacts\BetterTaskManager-v1.1.0-preview.12-portable-win-x64"

dotnet publish $project -c Release -p:PublishProfile=win-x64-portable -o $output
Write-Host "Published portable Better Task Manager v1.1.0-preview.12 to $output"

