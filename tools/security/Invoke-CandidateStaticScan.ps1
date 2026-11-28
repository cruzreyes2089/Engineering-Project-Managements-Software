# Owner-led static qualification of a fresh checkout. No app launch, installer,
# certificate installation, security exclusions, policy changes or artifact upload.
param([Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'Windows required.' }
Push-Location (Join-Path $PSScriptRoot '../..')
try {
  $commit = (& git rev-parse HEAD | Out-String).Trim()
  if ($LASTEXITCODE -ne 0 -or $commit -cne $ExpectedCommit) { throw 'Exact source revision mismatch.' }
  $dirty = @(& git status --porcelain --untracked-files=all)
  if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Use a clean dedicated checkout.' }
  if (Test-Path -LiteralPath 'artifacts/win-x64') { throw 'Expected clean output; existing files are not reused or deleted.' }
  $status = Get-MpComputerStatus
  if (-not $status.AntivirusEnabled -or -not $status.AMServiceEnabled -or -not $status.RealTimeProtectionEnabled) {
    throw 'BLOCKED: active Defender unavailable. No candidate scan result.'
  }
  Update-MpSignature -ErrorAction Stop
  $status = Get-MpComputerStatus
  if (-not $status.AntivirusEnabled -or -not $status.AMServiceEnabled -or -not $status.RealTimeProtectionEnabled -or $null -eq $status.AntivirusSignatureLastUpdated) {
    throw 'BLOCKED: active current Defender not verified.'
  }
  $age = ((Get-Date) - $status.AntivirusSignatureLastUpdated).TotalHours
  if ($age -lt 0 -or $age -gt 48) { throw 'BLOCKED: Defender definitions not current.' }
  $status | Select-Object AMProductVersion,AMEngineVersion,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntivirusEnabled,RealTimeProtectionEnabled | Format-List
  dotnet publish src/PracticeManager.Desktop/PracticeManager.Desktop.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
  if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
  ./tools/security/Inventory-DevelopmentBuild.ps1
  $root = (Resolve-Path -LiteralPath 'artifacts/win-x64').Path
  function Snapshot {
    $entries = @(Get-ChildItem -LiteralPath $root -Force -Recurse | Select-Object -First 10001)
    if ($entries.Count -gt 10000 -or @($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -ne 0) { throw 'Unexpected inventory.' }
    @($entries | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName | ForEach-Object {
      (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash + ' ' + [IO.Path]::GetRelativePath($root,$_.FullName)
    })
  }
  $before = @(Snapshot)
  if ($before.Count -lt 1) { throw 'Empty candidate.' }
  $scanner = Join-Path $env:ProgramFiles 'Windows Defender/MpCmdRun.exe'
  $platform = Join-Path $env:ProgramData 'Microsoft/Windows Defender/Platform'
  if (Test-Path -LiteralPath $platform) {
    $latest = Get-ChildItem -LiteralPath $platform -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest -and (Test-Path -LiteralPath (Join-Path $latest.FullName 'MpCmdRun.exe'))) {
      $scanner = Join-Path $latest.FullName 'MpCmdRun.exe'
    }
  }
  if (-not (Test-Path -LiteralPath $scanner -PathType Leaf)) { throw 'Scanner missing.' }
  $signature = Get-AuthenticodeSignature -LiteralPath $scanner
  if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(?i)(^|,\s*)O=Microsoft Corporation(,|$)') {
    throw 'Microsoft scanner publisher not verified.'
  }
  $start = Get-Date
  & $scanner -Scan -ScanType 3 -File $root
  if ($LASTEXITCODE -ne 0) { throw 'Candidate scan failed or reported a detection.' }
  $after = @(Snapshot)
  if ($before.Count -ne $after.Count -or (Compare-Object $before $after)) { throw 'Candidate bytes or census changed.' }
  $events = @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Windows Defender/Operational';StartTime=$start} -ErrorAction Stop)
  if (@($events | Where-Object { $_.Id -in @(1116,1117,1118,1119,1002,1005) }).Count -gt 0) { throw 'Detection, remediation or interrupted scan requires review.' }
  if (@($events | Where-Object { $_.Id -eq 1001 }).Count -lt 1) { throw 'Scan completion not verified.' }
  ./tools/security/Inventory-DevelopmentBuild.ps1
  Write-Host 'Fresh candidate static scan completed; matching detection events absent and bytes unchanged. Not release approval.'
} finally {
  Pop-Location
}
