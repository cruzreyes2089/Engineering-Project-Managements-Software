# Qualification only: create one new synthetic fixture; never open an existing database.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [switch]$EmptyContacts,
    [switch]$ConfirmSyntheticFixture
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT' -or -not $ConfirmSyntheticFixture) { throw 'Synthetic Windows fixture confirmation required.' }
$dir = Get-Item -LiteralPath $OutputDirectory -Force
if (-not $dir.PSIsContainer) { throw 'Use an existing private directory.' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\') + '\'
$full = $dir.FullName.TrimEnd('\') + '\'
if ($full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith('\\') -or [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full)).DriveType -ne [IO.DriveType]::Fixed) { throw 'Use a private local directory outside checkout.' }
$cursor = $dir
while ($null -ne $cursor) {
    if ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse-point directory denied.' }
    $cursor = $cursor.Parent
}
$target = Join-Path $full 'SyntheticCRM.accdb'
if (Test-Path -LiteralPath $target) { throw 'Existing fixture will not be overwritten.' }
$engine = $null
$db = $null
try {
    $engine = New-Object -ComObject DAO.DBEngine.120
    # dbLangGeneral; dbVersion120 = 128 (ACCDB).
    $db = $engine.CreateDatabase($target,';LANGID=0x0409;CP=1252;COUNTRY=0',128)
    $db.Execute('CREATE TABLE [SyntheticClients] ([Id] TEXT(20) NOT NULL, [Name] TEXT(100) NOT NULL, [Kind] LONG NOT NULL, [Status] YESNO NOT NULL, [Postal] TEXT(20) NOT NULL, [Ignored] TEXT(100), CONSTRAINT [PKClients] PRIMARY KEY ([Id]))',128)
    $db.Execute('CREATE TABLE [SyntheticContacts] ([Id] LONG NOT NULL, [Parent] TEXT(20) NOT NULL, [Name] TEXT(100) NOT NULL, [PrimaryFlag] YESNO NOT NULL, [Ignored] TEXT(100), CONSTRAINT [PKContacts] PRIMARY KEY ([Id]), CONSTRAINT [FKParent] FOREIGN KEY ([Parent]) REFERENCES [SyntheticClients] ([Id]))',128)
    $db.Execute('CREATE TABLE [Unrelated] ([Id] LONG NOT NULL)',128)
    $db.Execute("INSERT INTO [SyntheticClients] ([Id],[Name],[Kind],[Status],[Postal],[Ignored]) VALUES ('001','Synthetic Alpha',7,TRUE,'00501','DO NOT EXPORT')",128)
    $db.Execute("INSERT INTO [SyntheticClients] ([Id],[Name],[Kind],[Status],[Postal],[Ignored]) VALUES ('002','Synthetic Beta',9,FALSE,'00999','DO NOT EXPORT')",128)
    if (-not $EmptyContacts) {
        $db.Execute("INSERT INTO [SyntheticContacts] ([Id],[Parent],[Name],[PrimaryFlag],[Ignored]) VALUES (11,'001','Synthetic First',TRUE,'DO NOT EXPORT')",128)
        $db.Execute("INSERT INTO [SyntheticContacts] ([Id],[Parent],[Name],[PrimaryFlag],[Ignored]) VALUES (12,'002','Synthetic Second',FALSE,'DO NOT EXPORT')",128)
    }
    $db.Execute('INSERT INTO [Unrelated] ([Id]) VALUES (99)',128)
} finally {
    if ($null -ne $db) { try { $db.Close() } finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) } }
    if ($null -ne $engine) { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine) }
}
Write-Host 'Synthetic fixture created; no existing database opened.'
