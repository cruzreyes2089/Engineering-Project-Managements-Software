# Static deployment qualification only. Never launches or distributes the quarantined application.
param([string]$Root = (Join-Path $PSScriptRoot '../../artifacts/win-x64'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$rootPath = (Resolve-Path -LiteralPath $Root).Path
if ((Get-Item -LiteralPath $rootPath).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected publish root.' }
$required = @('CRC.PracticeManager.exe','CRC.PracticeManager.dll','CRC.PracticeManager.deps.json','CRC.PracticeManager.runtimeconfig.json','hostfxr.dll','hostpolicy.dll','coreclr.dll','PresentationFramework.dll','WindowsBase.dll','Microsoft.Data.Sqlite.dll','SQLitePCLRaw.core.dll','e_sqlite3.dll','PracticeManager.Core.dll','PracticeManager.Persistence.Sqlite.dll')
foreach ($name in $required) {
  $path = Join-Path $rootPath $name
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required deployment component: $name" }
  $item = Get-Item -LiteralPath $path
  if ($item.Length -eq 0 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Invalid deployment component: $name" }
}
function Assert-X64Pe([string]$name) {
  $stream = [IO.File]::Open((Join-Path $rootPath $name),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
  try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid executable header: $name" }
    $stream.Position = 60
    $offset = $reader.ReadInt32()
    if ($offset -lt 64 -or $offset -gt ($stream.Length - 6)) { throw "Invalid PE offset: $name" }
    $stream.Position = $offset
    if ($reader.ReadUInt32() -ne 0x00004550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected x64 PE image: $name" }
  } finally { $stream.Dispose() }
}
foreach ($name in @('CRC.PracticeManager.exe','hostfxr.dll','hostpolicy.dll','coreclr.dll','e_sqlite3.dll')) { Assert-X64Pe $name }
$config = Get-Content -LiteralPath (Join-Path $rootPath 'CRC.PracticeManager.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
$options = $config['runtimeOptions']
if ($null -eq $options -or $options['tfm'] -ne 'net10.0' -or $options.Contains('framework') -or $options.Contains('frameworks')) { throw 'Expected .NET 10 self-contained runtime configuration.' }
$included = @($options['includedFrameworks'] | ForEach-Object { $_['name'] })
foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) { if ($included -notcontains $framework) { throw "Missing included framework: $framework" } }
$deps = Get-Content -LiteralPath (Join-Path $rootPath 'CRC.PracticeManager.deps.json') -Raw | ConvertFrom-Json -AsHashtable
if ($deps['runtimeTarget']['name'] -notmatch '/win-x64$') { throw 'Unexpected dependency runtime target.' }
$unexpected = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Force | Where-Object { $_.Name -match '\.(accdb|mdb|db|sqlite|sqlite3|pfx|p12|pem|key)$' -or $_.Name -match '\.db-(wal|shm|journal)$' })
if ($unexpected.Count -gt 0) { throw 'Unexpected database or credential file in deployment.' }
Write-Host 'PASS: required self-contained Windows x64 deployment files, native architecture, runtime configuration and database/credential extension exclusion.'
Write-Host 'NOT QUALIFIED: application launch, clean-machine install, signing, security clearance or distribution.'
