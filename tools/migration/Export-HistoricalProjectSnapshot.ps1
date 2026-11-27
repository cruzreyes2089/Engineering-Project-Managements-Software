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
    $driveRoot = [IO.Path]::GetPathRoot($full)
    Assert-Condition (-not $full.StartsWith('\\') -and [IO.DriveInfo]::new($driveRoot).DriveType -eq [IO.DriveType]::Fixed -and -not $full.Substring($driveRoot.Length).Contains(':')) 'Use a private local fixed drive without alternate data streams.'
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
        { $_ -in @(10,12) } { Assert-Condition ($Value -is [string]) 'Unexpected text representation.'; return $Value }
        { $_ -in @(2,3,4,16) } {
            Assert-Condition ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64]) 'Unexpected integer representation.'
            return [int64]$Value
        }
        default { throw 'Unsupported DAO scalar.' }
    }
}
function Read-Projection($Database,$Choice,[bool]$IdentityOnly,[int]$Limit) {
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
                Assert-Condition ($field.Name -ceq $fieldName -and [int]$field.Type -eq $dao -and $dao -in @(2,3,4,10,12,16)) 'Column metadata mismatch.'
                $types.Add($fieldName,$dao)
            } finally { if ($null -ne $field) { $null = [Runtime.InteropServices.Marshal]::ReleaseComObject($field) } }
        }
        $id = [string]$Choice.identityColumns.legacyId
        $parent = [string]$Choice.identityColumns.legacyClientId
        $number = [string]$Choice.identityColumns.legacyProjectNumber
        Assert-Condition ($types.ContainsKey($id) -and $types[$id] -in @(2,3,4,10,12,16)) 'Invalid project identity column.'
        Assert-Condition ($types.ContainsKey($parent) -and $types[$parent] -in @(2,3,4,10,12,16)) 'Invalid client identity column.'
        Assert-Condition ($types.ContainsKey($number) -and $types[$number] -in @(10,12)) 'Invalid project number column.'
        Assert-Condition (@(@($id,$parent,$number) | Select-Object -Unique).Count -eq 3) 'Distinct identity columns required.'
        $selected = @($types.Keys | Sort-Object)
        if ($IdentityOnly) { $selected = @($id,$parent,$number) }
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
            $result.Add($row)
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
    Assert-Condition ($plan.format -ceq 'CRC-Access-Historical-Project-Plan-v1' -and $plan.sourceRead -is [bool] -and -not $plan.sourceRead -and $plan.importAuthorized -is [bool] -and -not $plan.importAuthorized) 'Invalid extraction plan.'
    $targets = @('legacyId','legacyClientId','legacyProjectNumber','title','site','legacyStatus','legacyAuthorizationStatus','authorizationEvidenceRef')
    Assert-Condition (@($plan.rules.PSObject.Properties).Count -eq 8 -and @($plan.identityColumns.PSObject.Properties).Count -eq 3) 'Invalid plan field scope.'
    $mapped = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($field in $targets) {
        $rule = $plan.rules.$field
        Assert-Condition ($null -ne $rule -and @($rule.PSObject.Properties).Count -eq 2) 'Invalid field rule.'
        if ($rule.transform -ceq 'blank') {
            Assert-Condition ($field -cin @('site','authorizationEvidenceRef') -and $null -eq $rule.column) 'Required field cannot be blank.'
        } else {
            Assert-Condition ($rule.column -is [string] -and $mapped.Add($rule.column)) 'Duplicate or invalid mapped column.'
            if ($field -cin @('legacyId','legacyClientId')) {
                Assert-Condition ($rule.transform -cin @('text-id','integer-id')) 'Invalid ID conversion.'
            } else { Assert-Condition ($rule.transform -ceq 'text') 'Text mapping required.' }
            if ($field -cin @('legacyId','legacyClientId','legacyProjectNumber')) {
                Assert-Condition ($plan.identityColumns.$field -ceq $rule.column) 'Identity rule mismatch.'
            }
        }
    }
    Assert-Condition (@($plan.columns).Count -eq $mapped.Count) 'Invalid projection size.'
    $projected = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    foreach ($column in @($plan.columns)) {
        Assert-Condition ($column.name -is [string] -and $mapped.Contains($column.name) -and
            @($column.PSObject.Properties).Count -eq 2 -and ($column.daoType -is [int] -or $column.daoType -is [long])) 'Unexpected projection column.'
        $projected.Add($column.name,[int]$column.daoType)
    }
    foreach ($field in $targets) {
        $rule = $plan.rules.$field
        if ($rule.transform -ceq 'blank') { continue }
        Assert-Condition ($projected.ContainsKey($rule.column)) 'Exact mapped column spelling required.'
        $dao = $projected[$rule.column]
        if ($rule.transform -ceq 'integer-id') {
            Assert-Condition ($dao -in @(2,3,4,16)) 'Integer identity metadata mismatch.'
        } else {
            Assert-Condition ($dao -in @(10,12)) 'Text metadata mismatch.'
        }
    }
    foreach ($key in @('sourceSha256','inventorySha256','crmMappingSha256','projectMappingSha256')) {
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
        $rows = Read-Projection $db $plan ($Mode -eq 'Identity') 10000
        Assert-Condition ($rows.Count -ge 1) 'Empty historical source is unsupported.'
    } finally {
        if ($null -ne $db) { try { $db.Close() } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) } }
        if ($null -ne $engine) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) }
    }
    $after = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Condition ($after -ceq $before) 'Source changed; no output.'
    $format = if ($Mode -eq 'Rows') { 'CRC-Access-Historical-Project-Projection-v1' } else { 'CRC-Access-Historical-Project-Identity-Census-v1' }
    $report = [ordered]@{format=$format;sourceSha256=$before;planSha256=$ExpectedPlanSha256;rows=$rows}
    $output = [Text.UTF8Encoding]::new($false,$true).GetBytes((ConvertTo-Json -InputObject $report -Depth 12 -Compress))
    Assert-Condition ($output.Length -le 10MB) 'Output exceeds size limit.'
    $stream = $null
    $created = $false
    try {
        $stream = [IO.FileStream]::new($target,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $created = $true
        $stream.Write($output,0,$output.Length); $stream.Flush($true)
    } catch {
        if ($null -ne $stream) { $stream.Dispose(); $stream=$null }
        if ($created) { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue }
        throw
    } finally { if ($null -ne $stream) { $stream.Dispose() } }
    Write-Host "Private synthetic historical projection created. Rows: $($rows.Count). No import authorized."
} catch {
    # Do not expose DAO exceptions, paths, table names or actual data.
    throw 'Extraction rejected. Review private inputs, DAO availability and source conditions locally.'
}
