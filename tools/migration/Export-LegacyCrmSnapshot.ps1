# Windows PowerShell matching installed Access DAO bitness. Synthetic closed copies only until qualified.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$DatabaseCopy,
    [Parameter(Mandatory=$true)][string]$PlanFile,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{64}$')][string]$ExpectedPlanSha256,
    [Parameter(Mandatory=$true)][ValidateSet('Rows','Identity')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$OutputFile,
    [switch]$ConfirmTrustedSyntheticCopy
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Condition([bool]$Condition,[string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Quote-Name([string]$Name) {
    Assert-Condition ($Name.Length -ge 1 -and $Name.Length -le 64 -and $Name -cnotmatch '[\[\]\x00-\x1f\x7f]') 'Unsupported identifier.'
    return '[' + $Name + ']'
}
function Check-LocalPath([string]$Path,[bool]$Existing) {
    $full = [IO.Path]::GetFullPath($Path)
    Assert-Condition (-not $full.StartsWith('\\') -and [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full)).DriveType -eq [IO.DriveType]::Fixed) 'Use a private local fixed drive.'
    $cursor = if ($Existing) { $full } else { [IO.Path]::GetDirectoryName($full) }
    while ($cursor) {
        $item = Get-Item -LiteralPath $cursor -Force
        Assert-Condition (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) 'Reparse-point path denied.'
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $full
}
function Hash-Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Read-Scalar($Value,[int]$DaoType) {
    # Preserve actual nulls for explicit downstream rejection; never synthesize values.
    if ($null -eq $Value -or $Value -is [DBNull]) { return $null }
    switch ($DaoType) {
        1 { Assert-Condition ($Value -is [bool]) 'Unexpected boolean representation.'; return $Value }
        { $_ -in @(10,12) } { Assert-Condition ($Value -is [string]) 'Unexpected text representation.'; return $Value }
        { $_ -in @(2,3,4,16) } {
            Assert-Condition ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64]) 'Unexpected integer representation.'
            return [int64]$Value
        }
        default { throw 'Unsupported DAO scalar.' }
    }
}
function Read-Projection($Database,$Choice,[string]$Entity,[bool]$IdentityOnly,[int]$Limit) {
    $name = [string]$Choice.table
    $null = Quote-Name $name
    Assert-Condition (-not $name.StartsWith('MSys',[StringComparison]::OrdinalIgnoreCase)) 'System table denied.'
    $table = $null
    $rs = $null
    $result = [Collections.Generic.List[object]]::new()
    try {
        $table = $Database.TableDefs.Item($name)
        $attrs = [long]$table.Attributes
        Assert-Condition ($table.Name -ceq $name -and $attrs -ge 0 -and ($attrs -band 1) -eq 0 -and ($attrs -band 536870912) -eq 0 -and ($attrs -band 1073741824) -eq 0) 'Nonlocal or hidden table denied.'
        $types = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
        $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($column in @($Choice.columns)) {
            $fieldName = [string]$column.name
            $null = Quote-Name $fieldName
            Assert-Condition ($unique.Add($fieldName)) 'Duplicate column.'
            $field = $null
            try {
                $field = $table.Fields.Item($fieldName)
                $dao = [int]$column.daoType
                Assert-Condition ($field.Name -ceq $fieldName -and [int]$field.Type -eq $dao -and $dao -in @(1,2,3,4,10,12,16)) 'Column metadata mismatch.'
                $types.Add($fieldName,$dao)
            } finally { if ($null -ne $field) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($field) } }
        }
        $id = [string]$Choice.identities.legacyId
        Assert-Condition ($types.ContainsKey($id) -and $types[$id] -in @(2,3,4,10,12,16)) 'Invalid identity column.'
        $parent = $null
        if ($Entity -eq 'contacts') {
            $parent = [string]$Choice.identities.legacyClientId
            Assert-Condition ($types.ContainsKey($parent) -and $types[$parent] -in @(2,3,4,10,12,16) -and $parent -cne $id) 'Invalid parent column.'
        }
        $selected = @($types.Keys | Sort-Object)
        if ($IdentityOnly) { $selected = @($id); if ($null -ne $parent) { $selected += $parent } }
        $projection = ($selected | ForEach-Object { Quote-Name $_ }) -join ','
        $sql = 'SELECT ' + $projection + ' FROM ' + (Quote-Name $name)
        # dbOpenSnapshot=4, dbReadOnly=4. Only validated local table and projected columns.
        $rs = $Database.OpenRecordset($sql,4,4)
        Assert-Condition (-not $rs.Updatable) 'Recordset is not read-only.'
        $budget = 0L
        while (-not $rs.EOF) {
            Assert-Condition ($result.Count -lt $Limit) 'Row limit exceeded.'
            $row = [ordered]@{}
            foreach ($fieldName in $selected) {
                $field = $null
                try {
                    $field = $rs.Fields.Item($fieldName)
                    $value = Read-Scalar $field.Value $types[$fieldName]
                    $row[$fieldName] = $value
                    if ($value -is [string]) { $budget += 6L * $value.Length }
                    $budget += 256
                    Assert-Condition ($budget -le 10MB) 'Snapshot size limit exceeded.'
                } finally { if ($null -ne $field) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($field) } }
            }
            if ($IdentityOnly) {
                $item = [ordered]@{legacyId=$row[$id]}
                if ($null -ne $parent) { $item.legacyClientId = $row[$parent] }
                $result.Add($item)
            } else { $result.Add($row) }
            $rs.MoveNext()
        }
    } finally {
        if ($null -ne $rs) { try { $rs.Close() } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($rs) } }
        if ($null -ne $table) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($table) }
    }
    return ,$result.ToArray()
}
try {
    Assert-Condition ($env:OS -eq 'Windows_NT' -and $ConfirmTrustedSyntheticCopy) 'Trusted synthetic Windows copy confirmation required.'
    $source = Check-LocalPath $DatabaseCopy $true
    $planPath = Check-LocalPath $PlanFile $true
    $target = Check-LocalPath $OutputFile $false
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\') + '\'
    foreach ($p in @($source,$planPath,$target)) {
        Assert-Condition (-not $p.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) 'Private files must remain outside checkout.'
    }
    Assert-Condition ([IO.Path]::GetExtension($source) -in @('.accdb','.mdb') -and [IO.Path]::GetExtension($target) -eq '.json' -and -not (Test-Path -LiteralPath $target)) 'Invalid source or new output path.'
    Assert-Condition ($source -ine $planPath -and $source -ine $target -and $planPath -ine $target) 'Distinct paths required.'
    foreach ($ext in @('.ldb','.laccdb')) { Assert-Condition (-not (Test-Path -LiteralPath ([IO.Path]::ChangeExtension($source,$ext)))) 'Close the source copy first.' }
    $planInfo = Get-Item -LiteralPath $planPath
    Assert-Condition (-not $planInfo.PSIsContainer -and $planInfo.Length -le 1MB) 'Invalid plan size.'
    $bytes = [IO.File]::ReadAllBytes($planPath)
    Assert-Condition ($bytes.Length -le 1MB -and (Hash-Bytes $bytes) -ceq $ExpectedPlanSha256) 'Plan hash mismatch.'
    $plan = [Text.UTF8Encoding]::new($false,$true).GetString($bytes) | ConvertFrom-Json
    Assert-Condition ($plan.format -ceq 'CRC-Access-CRM-Extraction-Plan-v1' -and $plan.clients.table -ine $plan.contacts.table) 'Invalid extraction plan.'
    foreach ($key in @('sourceSha256','inventorySha256','mappingSha256')) {
        Assert-Condition ([string]$plan.$key -cmatch '^[0-9a-f]{64}$') 'Invalid plan binding.'
    }
    $before = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($before -ceq $plan.sourceSha256) 'Source digest mismatch.'
    $engine = $null
    $db = $null
    try {
        $engine = New-Object -ComObject DAO.DBEngine.120
        # Exclusive read-only DAO opening. No application macros, linked records or write queries.
        $db = $engine.OpenDatabase($source,$true,$true)
        Assert-Condition (-not $db.Updatable) 'Database is not read-only.'
        $clients = Read-Projection $db $plan.clients 'clients' ($Mode -eq 'Identity') 5000
        $contacts = Read-Projection $db $plan.contacts 'contacts' ($Mode -eq 'Identity') 10000
        Assert-Condition ($clients.Count -ge 1) 'Empty client source is unsupported.'
    } finally {
        if ($null -ne $db) { try { $db.Close() } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) } }
        if ($null -ne $engine) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) }
    }
    $after = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($after -ceq $before) 'Source changed; no output.'
    $format = if ($Mode -eq 'Rows') { 'CRC-Access-CRM-Rows-v1' } else { 'CRC-Access-CRM-Raw-Identity-v1' }
    $report = [ordered]@{format=$format;sourceSha256=$before;inventorySha256=$plan.inventorySha256;mappingSha256=$plan.mappingSha256;clients=$clients;contacts=$contacts}
    $output = [Text.UTF8Encoding]::new($false,$true).GetBytes((ConvertTo-Json -InputObject $report -Depth 12 -Compress))
    Assert-Condition ($output.Length -le 10MB) 'Output exceeds size limit.'
    $stream = [IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try { $stream.Write($output,0,$output.Length); $stream.Flush($true) } finally { $stream.Dispose() }
    Write-Host "Private synthetic snapshot created. Clients: $($clients.Count); contacts: $($contacts.Count). No import authorized."
} catch {
    # Do not expose DAO exceptions, paths, table names or actual data.
    throw 'Extraction rejected. Review private inputs, DAO availability and source conditions locally.'
}
