<#
  Runs EndHud's self-test (src/EndHud/SelfTest.cs) and fails if it does. Used by the release
  workflow on the installed MSI and on the portable exe; works the same on a developer machine,
  and never touches the user's own EndHud data.

    ./installer/self-test.ps1 -Exe "$env:LOCALAPPDATA\Programs\EndHud\EndHud.exe" -Folder "$env:TEMP\endhud-self-test" `
        -Screenshot tests/data/Fixtures/exploration-levels-visible.png -Squad Laevatain,Camille,Arcane,Ardelia
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Folder,
    [string] $Screenshot,
    [string[]] $Squad, # operator ids in slot order, as one comma-separated string or a list
    [int] $TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'

$arguments = @('--self-test', "`"$Folder`"")
if ($Screenshot) { $arguments += @('--screenshot', "`"$(Resolve-Path $Screenshot)`"") }
if ($Squad) { $arguments += @('--expect', ($Squad -join ',')) }

$process = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw "The self-test didn't finish within $TimeoutSeconds s"
}

$log = Join-Path $Folder 'self-test.log'
if (Test-Path $log) { Get-Content $log } else { "(no log at $log)" }
if ($process.ExitCode -ne 0) { throw "The self-test failed (exit code $($process.ExitCode))" }
