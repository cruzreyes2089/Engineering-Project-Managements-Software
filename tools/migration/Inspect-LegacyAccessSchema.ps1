# Requires Windows PowerShell matching the installed Microsoft Access Database Engine bitness.
# Run only on a trusted, closed, local COPY. This is metadata inspection, not database repair or migration.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DatabaseCopy,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [switch]$ConfirmTrustedCopy
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows with the installed Access DAO engine is required.' }
if (-not $ConfirmTrustedCopy) { throw 'Confirm that this is a trusted, closed local database copy, not the original or a live database.' }
$source = Get-Item -LiteralPath $DatabaseCopy
if ($source.PSIsContainer -or $source.Extension -notin @('.accdb','.mdb')) { throw 'Select an Access database copy.' }
if ($source.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point input is not supported.' }
if ($source.FullName.StartsWith('\\') -or [IO.DriveInfo]::new([IO.Path]::GetPathRoot($source.FullName)).DriveType -ne [IO.DriveType]::Fixed) { throw 'Use a trusted copy on a local fixed drive.' }
$target = [IO.Path]::GetFullPath($OutputFile)
if ([IO.Path]::GetExtension($target) -ne '.json' -or (Test-Path -LiteralPath $target)) { throw 'Choose a new .json report path.' }
$folder = [IO.Path]::GetDirectoryName($target)
if (-not (Test-Path -LiteralPath $folder -PathType Container)) { throw 'Output directory must already exist.' }
foreach ($lockExtension in @('.laccdb','.ldb')) {
    if (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($source.FullName,$lockExtension))) { throw 'Database lock file exists. Close the copy before inspection.' }
}
$before = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
$engine = $null
$db = $null
$tables = [Collections.Generic.List[object]]::new()
$relations = [Collections.Generic.List[object]]::new()
$localNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$linkedCount = 0
$skippedCount = 0
try {
    try { $engine = New-Object -ComObject DAO.DBEngine.120 }
    catch { throw 'Access DAO engine unavailable. Use Windows PowerShell matching installed Access bitness; do not install unknown providers.' }
    # Options=False: shared opening. ReadOnly=True: no database changes.
    $db = $engine.OpenDatabase($source.FullName,$false,$true)
    if ($db.Updatable) { throw 'Database did not open read-only.' }
    if ($db.TableDefs.Count -gt 2000 -or $db.Relations.Count -gt 4000) { throw 'Schema exceeds inventory limits.' }
    for ($i=0; $i -lt $db.TableDefs.Count; $i++) {
        $table = $db.TableDefs.Item($i)
        $name = [string]$table.Name
        $attributes = [long]$table.Attributes
        # Do not inspect system/hidden tables. Do not read Connect or open linked sources.
        if ($name.StartsWith('MSys',[StringComparison]::OrdinalIgnoreCase) -or ($attributes -band 1) -ne 0 -or $attributes -lt 0) { $skippedCount++; continue }
        if (($attributes -band 536870912) -ne 0 -or ($attributes -band 1073741824) -ne 0) { $linkedCount++; continue }
        if ($table.Fields.Count -gt 500 -or $table.Indexes.Count -gt 500) { throw 'Table schema exceeds inventory limits.' }
        $fields = [Collections.Generic.List[object]]::new()
        $indexes = [Collections.Generic.List[object]]::new()
        for ($j=0; $j -lt $table.Fields.Count; $j++) {
            $field = $table.Fields.Item($j)
            $fields.Add([ordered]@{ name=[string]$field.Name; daoType=[int]$field.Type; size=[int]$field.Size; required=[bool]$field.Required; attributes=[long]$field.Attributes })
        }
        for ($j=0; $j -lt $table.Indexes.Count; $j++) {
            $index = $table.Indexes.Item($j)
            $parts = [Collections.Generic.List[object]]::new()
            for ($k=0; $k -lt $index.Fields.Count; $k++) { $parts.Add([string]$index.Fields.Item($k).Name) }
            $indexes.Add([ordered]@{ name=[string]$index.Name; primary=[bool]$index.Primary; unique=[bool]$index.Unique; fields=$parts.ToArray() })
        }
        $null = $localNames.Add($name)
        $tables.Add([ordered]@{ name=$name; fields=$fields.ToArray(); indexes=$indexes.ToArray() })
    }
    for ($i=0; $i -lt $db.Relations.Count; $i++) {
        $relation = $db.Relations.Item($i)
        if (-not $localNames.Contains([string]$relation.Table) -or -not $localNames.Contains([string]$relation.ForeignTable)) { continue }
        $pairs = [Collections.Generic.List[object]]::new()
        for ($j=0; $j -lt $relation.Fields.Count; $j++) {
            $field = $relation.Fields.Item($j)
            $pairs.Add([ordered]@{ primaryField=[string]$field.Name; foreignField=[string]$field.ForeignName })
        }
        $relations.Add([ordered]@{ name=[string]$relation.Name; primaryTable=[string]$relation.Table; foreignTable=[string]$relation.ForeignTable; attributes=[long]$relation.Attributes; fields=$pairs.ToArray() })
    }
} finally {
    if ($null -ne $db) {
        try { $db.Close() } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) }
    }
    if ($null -ne $engine) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) }
}
$after = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash
if ($before -ne $after) { throw 'Source copy hash changed. No report created.' }
$report = [ordered]@{
    format='CRC-Access-Schema-Inventory-v1'
    sourceSha256=$before
    sourceExtension=$source.Extension.ToLowerInvariant()
    localTables=$tables.ToArray()
    localRelations=$relations.ToArray()
    skippedLinkedTableCount=$linkedCount
    skippedSystemOrHiddenTableCount=$skippedCount
    rowDataIncluded=$false
    linkedSourcesOpened=$false
}
# No connection strings, passwords, SQL, field defaults, VBA, recordsets, or row data.
$json = ConvertTo-Json -InputObject $report -Depth 12
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
if ($bytes.Length -gt 10MB) { throw 'Schema report exceeds size limit.' }
$stream = $null
$created = $false
try {
    $stream = [IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $created = $true
    $stream.Write($bytes,0,$bytes.Length)
    $stream.Flush($true)
} catch {
    if ($null -ne $stream) { $stream.Dispose(); $stream=$null }
    if ($created) { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue }
    throw
} finally { if ($null -ne $stream) { $stream.Dispose() } }
Write-Host "Schema inventory saved locally. Local tables: $($tables.Count); relations: $($relations.Count); linked tables skipped: $linkedCount."
Write-Host 'Review the report for sensitive object names before sharing. No records exported or imported.'
