# Isolated source transformation for candidate qualification only.
# Not a deployment, installer or production migration.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceRoot,[Parameter(Mandatory)][string]$ExpectedTemporaryRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ExpectedTemporaryRoot).Path
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ((Split-Path -Parent $root) -ne $temp -or (Split-Path -Leaf $root) -notmatch '^crc-archive-source-[0-9a-f]{32}$' -or $source -ne (Join-Path $root 'source') -or (Test-Path (Join-Path $source '.git'))) { throw 'Refusing transformation outside owned isolated source copy.' }
function Replace-Exact([string]$Path,[string]$Old,[string]$New) {
    $value = [IO.File]::ReadAllText($Path)
    if ([regex]::Matches($value,[regex]::Escape($Old)).Count -ne 1) { throw "Unexpected source anchor in $Path" }
    [IO.File]::WriteAllText($Path,$value.Replace($Old,$New),[Text.UTF8Encoding]::new($false))
}
$persistence = Join-Path $source 'src/PracticeManager.Persistence.Sqlite'
$prototypes = Join-Path $source 'tools/migration/prototypes'
Copy-Item -LiteralPath (Join-Path $prototypes 'HistoricalArchiveMigrationV47.cs') -Destination $persistence
Copy-Item -LiteralPath (Join-Path $prototypes 'historical_project_archive.sql') -Destination $persistence
Replace-Exact (Join-Path $persistence 'PracticeManager.Persistence.Sqlite.csproj') '</ItemGroup></Project>' '<EmbeddedResource Include="historical_project_archive.sql" LogicalName="PracticeManager.historical_project_archive.sql"/></ItemGroup></Project>'
Replace-Exact (Join-Path $persistence 'LocalPractice.cs') '"Projects.Closeout","Work.Correct"];' '"Projects.Closeout","Work.Correct","HistoricalProjects.View","Administration.HistoricalProjectImport"];'
Replace-Exact (Join-Path $persistence 'LocalPractice.cs') 'if(version!=46)throw new InvalidOperationException("Unsupported schema version.");' 'if(version==46){UpgradeToV47(c,t);version=47;}if(version!=47)throw new InvalidOperationException("Unsupported schema version.");'
Replace-Exact (Join-Path $persistence 'UpgradeRehearsal.cs') 'const int CurrentSchema=46;' 'const int CurrentSchema=47;'
Replace-Exact (Join-Path $persistence 'RecoveryCopy.cs') 'if(Require(copy,t,"Administration.RecoveryCopy")!=actor)throw new UnauthorizedAccessException();' 'if(Require(copy,t,"Administration.RecoveryCopy")!=actor)throw new UnauthorizedAccessException(); _=ReadArchiveStored(copy,t);'
Replace-Exact (Join-Path $persistence 'IsolatedRestore.cs') 'if(Convert.ToInt64(Scalar(db,null,"PRAGMA user_version"))!=46)throw new ArgumentException("Only current schema 46 recovery copies are supported; older copies require a reviewed migration.");' 'if(Convert.ToInt64(Scalar(db,null,"PRAGMA user_version"))!=47)throw new ArgumentException("Only current schema 47 recovery copies are supported; older copies require a reviewed migration.");'
Replace-Exact (Join-Path $persistence 'IsolatedRestore.cs') 'long Count(string sql)=>Convert.ToInt64(Scalar(db,null,sql));' 'using(var archiveTx=db.BeginTransaction(deferred:true)){_=ReadArchiveStored(db,archiveTx);} long Count(string sql)=>Convert.ToInt64(Scalar(db,null,sql));'
Write-Host 'Staged schema 47 and recovery controls applied to isolated source only.'
