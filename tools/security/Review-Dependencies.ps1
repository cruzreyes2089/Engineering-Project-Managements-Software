# CI-only dependency inventory, with no binaries or records distributed.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
$projects = @(
  'src/PracticeManager.Desktop/PracticeManager.Desktop.csproj',
  'tests/PracticeManager.SmokeTests/PracticeManager.SmokeTests.csproj'
)
foreach ($project in $projects) {
  Write-Host "Dependency inventory: $project"
  foreach ($mode in @('inventory','vulnerabilities')) {
    $argsList = @('package','list','--project',$project,'--include-transitive','--format','json','--output-version','1','--no-restore')
    if ($mode -eq 'vulnerabilities') { $argsList += '--vulnerable' }
    $raw = (& dotnet @argsList | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Dependency review command failed: $mode" }
    $report = $raw | ConvertFrom-Json -ErrorAction Stop
    if ($report.version -ne 1 -or $null -eq $report.projects -or @($report.projects).Count -ne 1) {
      throw "Unexpected dependency report schema: $mode"
    }
    Write-Host $raw
    if ($mode -eq 'vulnerabilities') {
      foreach ($p in $report.projects) {
        foreach ($framework in $p.frameworks) {
          $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
          if (@($packages | Where-Object { $null -ne $_ }).Count -gt 0) {
            throw 'Known vulnerable package reported; review required.'
          }
        }
      }
    }
  }
}
Write-Host 'Dependency reports complete. Not a malware verdict or distribution authorization.'
