# CI-only, read-only source identity. No distribution or signing.
[CmdletBinding()]
param([ValidateSet('BeforeBuild','AfterBuild')][string]$Stage)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Join-Path $PSScriptRoot '../..')
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows') { throw 'This evidence is restricted to the Windows CI runner.' }
function Read-Git([string[]]$Arguments) {
    $result = @(& git @Arguments)
    if ($LASTEXITCODE -ne 0) { throw 'Git source identity check failed.' }
    return $result
}
$head = [string](Read-Git @('rev-parse','HEAD'))
$tree = [string](Read-Git @('rev-parse','HEAD^{tree}'))
if ($head -cnotmatch '^[0-9a-f]{40}$' -or $tree -cnotmatch '^[0-9a-f]{40}$' -or $head -cne $env:GITHUB_SHA) { throw 'Checkout does not match exact workflow commit.' }
$changes = @(Read-Git @('status','--porcelain=v1','--untracked-files=no'))
if ($changes.Count -ne 0) { throw 'Tracked checkout content changed.' }
if ($env:GITHUB_RUN_ID -notmatch '^[1-9][0-9]*$' -or $env:GITHUB_RUN_ATTEMPT -notmatch '^[1-9][0-9]*$' -or [string]::IsNullOrWhiteSpace($env:GITHUB_REPOSITORY)) { throw 'Workflow run identity unavailable.' }
$sdk = [string](& dotnet --version)
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.[0-9]+\.[0-9]+$') { throw 'Unexpected SDK identity.' }
$workflow = (Get-FileHash -LiteralPath '.github/workflows/windows-build.yml' -Algorithm SHA256).Hash.ToLowerInvariant()
$global = (Get-FileHash -LiteralPath 'global.json' -Algorithm SHA256).Hash.ToLowerInvariant()
$identity = [ordered]@{
    format='CRC-Development-Source-Identity-v1'
    repository=$env:GITHUB_REPOSITORY
    sourceCommit=$head
    sourceTree=$tree
    workflowSha256=$workflow
    sdkConfigurationSha256=$global
    sdkVersion=$sdk
    runnerOS=$env:RUNNER_OS
    runnerArchitecture=$env:RUNNER_ARCH
    runnerImageVersion=$env:ImageVersion
    runId=$env:GITHUB_RUN_ID
    runAttempt=$env:GITHUB_RUN_ATTEMPT
}
$json = ConvertTo-Json -InputObject $identity -Compress
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
if ($Stage -eq 'BeforeBuild') {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_ENV)) { throw 'Workflow environment unavailable.' }
    Add-Content -LiteralPath $env:GITHUB_ENV -Value "CRC_SOURCE_IDENTITY_SHA256=$hash" -Encoding utf8
} elseif ($hash -cne $env:CRC_SOURCE_IDENTITY_SHA256) { throw 'Source or build environment identity changed between checks.' }
Write-Host "Source identity SHA-256 ($Stage): $hash"
Write-Host $json
return $identity
