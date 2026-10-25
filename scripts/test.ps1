<#
.SYNOPSIS
    Runs EndHud's tests: the fast lane while working, the full suites before a push.

.DESCRIPTION
    -Fast (default): both test projects without the tests marked [Trait("Category", "Slow")]: operator
      recognition with the model, recorded videos, the on-screen-display sweeps and the screen-size
      sweep, which are most of the vision suite's half hour. For agents and merges.
    -Full: everything, in Release, the way CI runs it (CI also leaves out "Desktop" tests). Once per
      batch of merges before a push, not after every merge.
    Output goes to a log file per project (named in the summary), never cut short by a pipe.
#>
param([switch]$Full, [switch]$Fast, [string]$Filter, [string]$LogDir = (Join-Path ([IO.Path]::GetTempPath()) 'endhud-tests'))
$ErrorActionPreference = 'Stop'
if ($Full -and $Fast) { throw "Pick -Fast or -Full." }

$root = (git rev-parse --show-toplevel).Trim()
$config = if ($Full) { 'Release' } else { 'Debug' }
$lane = if ($Full) { $null } else { 'Category!=Slow' }
$where = @($lane, $Filter) | Where-Object { $_ }
New-Item -ItemType Directory -Force $LogDir | Out-Null

dotnet build (Join-Path $root 'EndHud.sln') -c $config -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$failed = $false
foreach ($project in 'EndHud.Tests', 'EndHud.Vision.Tests') {
    $log = Join-Path $LogDir "$project-$config-$(Get-Date -Format yyyyMMdd-HHmmss).log"
    $args = @('test', (Join-Path $root "tests/$project"), '-c', $config, '--no-build', '--logger', 'console;verbosity=normal')
    if ($where) { $args += @('--filter', ($where -join '&')) }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @args *> $log
    if ($LASTEXITCODE -ne 0) { $failed = $true }
    $counts = Select-String -Path $log -Pattern '^\s*(Total tests|Passed|Failed|Skipped):|^(Passed|Failed)!|aborted' |
        ForEach-Object { $_.Line.Trim() }
    $summary = if ($counts) { $counts -join ', ' } else { 'no summary: see the log' }
    "{0,-20} {1:mm\:ss}  {2}  ({3})" -f $project, $watch.Elapsed, $summary, $log
    Select-String -Path $log -Pattern '^\s+Failed ' | ForEach-Object { "    $($_.Line.Trim())" }
}
if ($failed) { exit 1 }
