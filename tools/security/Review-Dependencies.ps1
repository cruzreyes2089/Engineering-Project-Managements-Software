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
    $stderr = [IO.Path]::GetTempFileName()
    try {
      $raw = (& dotnet @argsList 2> $stderr | Out-String)
      $exit = $LASTEXITCODE
      if ($exit -ne 0) { throw "Dependency review command failed: $mode" }
      if (-not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $stderr -Raw))) {
        throw "Dependency review emitted diagnostic errors or warnings: $mode; review required."
      }
    } finally {
      Remove-Item -LiteralPath $stderr -Force
    }
    $report = $raw | ConvertFrom-Json -ErrorAction Stop
    if ($report.version -ne 1 -or $null -eq $report.projects -or @($report.projects).Count -ne 1) {
      throw "Unexpected dependency report schema: $mode"
    }
    $entry = @($report.projects)[0]
    if ([string]::IsNullOrWhiteSpace($entry.path) -or -not [string]::Equals([IO.Path]::GetFullPath($entry.path),(Resolve-Path -LiteralPath $project).Path,[StringComparison]::OrdinalIgnoreCase)) {
      throw "Dependency report project identity mismatch: $mode"
    }
    if ($mode -eq 'inventory') {
      if ($report.parameters -notmatch '(?:^|\s)--include-transitive(?:$|\s)' -or @($entry.frameworks | Where-Object { $null -ne $_ }).Count -eq 0) { throw 'Missing inventory framework or transitive scope.' }
      foreach ($framework in $entry.frameworks) {
        if ([string]::IsNullOrWhiteSpace($framework.framework)) { throw 'Missing framework identity.' }
        $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
        foreach ($package in $packages | Where-Object { $null -ne $_ }) {
          if ([string]::IsNullOrWhiteSpace($package.id) -or [string]::IsNullOrWhiteSpace($package.resolvedVersion)) { throw 'Unresolved package identity.' }
        }
      }
    } else {
      if ($report.parameters -notmatch '(?:^|\s)--vulnerable(?:$|\s)' -or $report.parameters -notmatch '(?:^|\s)--include-transitive(?:$|\s)' -or @($report.sources) -notcontains 'https://api.nuget.org/v3/index.json') { throw 'Missing advisory scan scope or expected NuGet source.' }
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
