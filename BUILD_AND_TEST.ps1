param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required for this build workflow.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun.' }
function Invoke-Dotnet { param([string[]]$DotnetArgs) & dotnet @DotnetArgs; if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $DotnetArgs" } }
Invoke-Dotnet @('--version')
Invoke-Dotnet @('build','src/PracticeManager.Desktop/PracticeManager.Desktop.csproj','-c','Release')
Invoke-Dotnet @('run','--project','tests/PracticeManager.SmokeTests/PracticeManager.SmokeTests.csproj','-c','Release')
if ($Publish) {
  Invoke-Dotnet @('publish','src/PracticeManager.Desktop/PracticeManager.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/win-x64')
  if (-not (Test-Path 'artifacts/win-x64/CRC.PracticeManager.exe')) { throw 'Expected executable missing.' }
  Write-Host 'Development executable created: artifacts/win-x64/CRC.PracticeManager.exe'
}
