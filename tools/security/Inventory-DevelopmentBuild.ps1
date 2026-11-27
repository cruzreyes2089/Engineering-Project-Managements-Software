# CI-only published file evidence; never executes, uploads, signs or modifies a binary.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Join-Path $PSScriptRoot '../..')
$source = & (Join-Path $PSScriptRoot 'Assert-DevelopmentSource.ps1') -Stage AfterBuild
$root = (Resolve-Path -LiteralPath 'artifacts/win-x64').Path
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Unexpected build root reparse point.' }
$all = @(Get-ChildItem -LiteralPath $root -Force -Recurse | Select-Object -First 10001)
if ($all.Count -gt 10000) { throw 'Build inventory entry limit exceeded.' }
if (@($all | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw 'Unexpected reparse point in published output.' }
$files = @($all | Where-Object { -not $_.PSIsContainer })
if ($files.Count -lt 1) { throw 'No build output to inventory.' }
$names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$rows = [Collections.Generic.List[object]]::new()
$total = [long]0
foreach ($file in $files) {
  $relative = [IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
  if ($relative -match '(^|/)\.\.?(/|$)' -or [IO.Path]::IsPathRooted($relative) -or $relative -notmatch '^[A-Za-z0-9_./+-]+$' -or -not $names.Add($relative)) { throw 'Unexpected or duplicate published path.' }
  $before = $file.Length
  $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  if ((Get-Item -LiteralPath $file.FullName).Length -ne $before) { throw 'Published file size changed during inventory.' }
  $rows.Add([ordered]@{path=$relative;bytes=$before;sha256=$hash})
  $total += $before
}
foreach ($required in @('CRC.PracticeManager.exe','CRC.PracticeManager.dll','CRC.PracticeManager.deps.json','CRC.PracticeManager.runtimeconfig.json')) {
  if (-not $names.Contains($required)) { throw "Required output missing: $required" }
}
$sorted = @($rows | Sort-Object -Property { $_.path } -CaseSensitive)
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
$manifest = [ordered]@{format='CRC-Development-Build-Inventory-v2';source=$source;sourceIdentitySha256=$env:CRC_SOURCE_IDENTITY_SHA256;architecture='win-x64';applicationAuthenticodeStatus=[string]$signature.Status;distributionAuthorized=$false;fileCount=$files.Count;bytes=$total;files=$sorted}
$json = ConvertTo-Json -InputObject $manifest -Depth 5 -Compress
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
