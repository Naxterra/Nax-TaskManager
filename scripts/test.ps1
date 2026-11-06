[CmdletBinding()]
param([ValidateSet("Debug", "Release")][string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $root
try {
    dotnet build BetterTaskManager.slnx -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Solution build failed ($LASTEXITCODE)." }
    dotnet run --project tests/BetterTaskManager.Core.Tests -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Core regression tests failed ($LASTEXITCODE)." }
    # Sequential: these file-based runners share the Core project's build outputs.
    foreach ($suite in @("history", "app-identity", "network-rules", "blocked-log", "memory-policy", "memory-rules")) {
        dotnet run --file "tests/$suite-checks.cs" -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw "$suite checks failed ($LASTEXITCODE)." }
    }
    Write-Host "All seven test runners passed. No installer, service or firewall changes were made."
}
finally { Pop-Location }
