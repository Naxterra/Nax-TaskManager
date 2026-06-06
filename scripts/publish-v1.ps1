$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\BetterTaskManager\BetterTaskManager.csproj"
$output = Join-Path $root "artifacts\BetterTaskManager-v1.0"

dotnet publish $project -c Release -r win-x64 --self-contained false -o $output
Write-Host "Published Better Task Manager v1.0 to $output"

