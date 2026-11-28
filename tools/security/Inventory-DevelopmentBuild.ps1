# CI-only published file evidence; never executes, uploads, signs or modifies a binary.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Join-Path $PSScriptRoot '../..')
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
$manifest = [ordered]@{format='CRC-Development-Build-Inventory-v1';architecture='win-x64';fileCount=$files.Count;bytes=$total;files=$sorted}
$json = ConvertTo-Json -InputObject $manifest -Depth 5 -Compress
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
# Capture exact checked-out source identity (PR checkout may be a merge commit).
$commit = (& git rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40} -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
# Only the inventory JSON is passed to the independent static validator.
# Exclusive temporary file; no executable copy or distribution.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('crc-layout-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
  try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  & python tools/release/validate_install_payload.py $temp --expected-sha256 $digest
  if ($LASTEXITCODE -ne 0) { throw 'Static installer layout validation failed.' }
} finally {
  Remove-Item -LiteralPath $temp -Force
}
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
) { throw 'Source commit identity unavailable.' }
$tree = (& git rev-parse 'HEAD^{tree}' | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $tree -notmatch '^[0-9a-f]{40} -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
# Only the inventory JSON is passed to the independent static validator.
# Exclusive temporary file; no executable copy or distribution.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('crc-layout-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
  try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  & python tools/release/validate_install_payload.py $temp --expected-sha256 $digest
  if ($LASTEXITCODE -ne 0) { throw 'Static installer layout validation failed.' }
} finally {
  Remove-Item -LiteralPath $temp -Force
}
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
) { throw 'Source tree identity unavailable.' }
$sdk = (& dotnet --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.0\.\d+ -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
# Only the inventory JSON is passed to the independent static validator.
# Exclusive temporary file; no executable copy or distribution.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('crc-layout-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
  try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  & python tools/release/validate_install_payload.py $temp --expected-sha256 $digest
  if ($LASTEXITCODE -ne 0) { throw 'Static installer layout validation failed.' }
} finally {
  Remove-Item -LiteralPath $temp -Force
}
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
) { throw 'Expected stable SDK identity unavailable.' }
$deps = Get-Content -LiteralPath (Join-Path $root 'CRC.PracticeManager.deps.json') -Raw | ConvertFrom-Json -ErrorAction Stop
$target = $deps.runtimeTarget.name
if ($target -notmatch '/win-x64 -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
# Only the inventory JSON is passed to the independent static validator.
# Exclusive temporary file; no executable copy or distribution.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('crc-layout-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
  try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  & python tools/release/validate_install_payload.py $temp --expected-sha256 $digest
  if ($LASTEXITCODE -ne 0) { throw 'Static installer layout validation failed.' }
} finally {
  Remove-Item -LiteralPath $temp -Force
}
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
) { throw 'Unexpected published runtime target.' }
$runtimeLibraries = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^(runtimepack\.|Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.)' } | Sort-Object)
if ($runtimeLibraries.Count -lt 1) { throw 'Published runtime library identities unavailable.' }
$entryHash = ($rows | Where-Object { $_.path -eq 'CRC.PracticeManager.exe' }).sha256
$provenance = [ordered]@{format='CRC-Development-Provenance-v1';sourceCommit=$commit;sourceTree=$tree;sdk=$sdk;runtimeTarget=$target;runtimeLibraries=$runtimeLibraries;inventorySha256=$digest;entryPointSha256=$entryHash;releaseAuthorized=$false}
Write-Host ('Development provenance: ' + (ConvertTo-Json -InputObject $provenance -Depth 5 -Compress))
$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $root 'CRC.PracticeManager.exe')
Write-Host "Build file count: $($files.Count)"
Write-Host "Build total bytes: $total"
Write-Host "Application Authenticode status: $($signature.Status)"
Write-Host "Complete manifest SHA-256 (UTF-8 compact JSON, no newline): $digest"
# Only the inventory JSON is passed to the independent static validator.
# Exclusive temporary file; no executable copy or distribution.
$temp = Join-Path ([IO.Path]::GetTempPath()) ('crc-layout-' + [guid]::NewGuid().ToString('N') + '.json')
$stream = [IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
  try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
  & python tools/release/validate_install_payload.py $temp --expected-sha256 $digest
  if ($LASTEXITCODE -ne 0) { throw 'Static installer layout validation failed.' }
} finally {
  Remove-Item -LiteralPath $temp -Force
}
Write-Host "BEGIN CRC DEVELOPMENT BUILD INVENTORY"
Write-Host $json
Write-Host "END CRC DEVELOPMENT BUILD INVENTORY"
Write-Host 'Development build evidence only. Executable distribution suspended pending security review. No artifact upload.'
