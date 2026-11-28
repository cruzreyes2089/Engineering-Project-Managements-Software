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
  # Do not mix old binaries or unrelated files into a new qualification candidate.
  # Preserve any prior output; this build never deletes or replaces it.
  $publishRoot = Join-Path $PSScriptRoot 'artifacts/win-x64'
  $parent = Join-Path $PSScriptRoot 'artifacts'
  if (Test-Path -LiteralPath $parent) {
    $item = Get-Item -LiteralPath $parent -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid or redirected artifacts directory.' }
  }
  if (Test-Path -LiteralPath $publishRoot) {
    $item = Get-Item -LiteralPath $publishRoot -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Invalid or redirected publish directory.' }
    if (@(Get-ChildItem -LiteralPath $publishRoot -Force).Count -ne 0) { throw 'Publish directory is not empty. Preserve prior evidence and use a fresh trusted checkout.' }
  }
  Invoke-Dotnet @('publish','src/PracticeManager.Desktop/PracticeManager.Desktop.csproj','-c','Release','-r','win-x64','--self-contained','true','-o','artifacts/win-x64')
  if (-not (Test-Path 'artifacts/win-x64/CRC.PracticeManager.exe')) { throw 'Expected executable missing.' }
  Write-Host 'Development executable created: artifacts/win-x64/CRC.PracticeManager.exe'
}
