# Native qualification of an unactivated candidate, using a disposable exact Git revision.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows required for native archive qualification.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ('crc-archive-source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
try {
    $revision = & git -C $repo rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify exact revision.' }
    $archive = Join-Path $root 'source.tar'
    & git -C $repo archive --format=tar "--output=$archive" $revision
    if ($LASTEXITCODE -ne 0) { throw 'Git archive failed.' }
    $source = Join-Path $root 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    & tar -xf $archive -C $source
    if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
    $candidate = Join-Path $source 'tools/migration/prototypes/HistoricalProjectArchiveCandidate.cs'
    $persistence = Join-Path $source 'src/PracticeManager.Persistence.Sqlite'
    $destination = Join-Path $persistence 'HistoricalProjectArchiveCandidate.cs'
    if (Test-Path $destination) { throw 'Candidate already present in persistence project.' }
    Copy-Item -LiteralPath $candidate -Destination $destination
    $project = Join-Path $source 'tests/HistoricalArchiveCandidateQualification'
    New-Item -ItemType Directory -Path $project | Out-Null
    Copy-Item -LiteralPath (Join-Path $source 'tools/migration/prototypes/HistoricalArchiveCandidateQualification.cs') -Destination (Join-Path $project 'Program.cs')
    $projectFile = Join-Path $project 'Qualification.csproj'
    Set-Content -LiteralPath $projectFile -Encoding utf8 -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><ProjectReference Include="../../src/PracticeManager.Persistence.Sqlite/PracticeManager.Persistence.Sqlite.csproj"/></ItemGroup></Project>'
    $sql = Join-Path $source 'tools/migration/prototypes/historical_project_archive.sql'
    Push-Location $source
    try {
        Write-Host "Qualifying exact revision $revision in isolated source copy; not publishing."
        $fixture = Join-Path $root 'synthetic-v46.db'
        & dotnet run --project $projectFile -c Release -- $sql $fixture 46
        if ($LASTEXITCODE -ne 0) { throw 'Native historical archive candidate qualification failed.' }
        # Apply the staged migration only to the owned temporary source copy.
        & (Join-Path $source 'tools/migration/prototypes/Stage-HistoricalArchiveV47.ps1') -SourceRoot $source -ExpectedTemporaryRoot $root
        & dotnet run --project $projectFile -c Release -- $sql $fixture 47
        if ($LASTEXITCODE -ne 0) { throw 'Staged schema 47 archive/recovery qualification failed.' }
    } finally { Pop-Location }
} finally {
    Remove-Item -LiteralPath $root -Recurse -Force
}
