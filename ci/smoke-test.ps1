<#
.SYNOPSIS
    Runs LinearBar's built-in smoke test and prints its report.

.DESCRIPTION
    Starts a few ordinary apps so the dock has something to show, launches LinearBar with
    --smoke-test (it checks itself and exits; see src/LinearBar/SmokeTest.cs), then prints
    the log. Screenshots and the log are left in the output directory.

    Works locally too:  ./ci/smoke-test.ps1 -Exe publish/LinearBar.exe

.PARAMETER Exe
    Path to LinearBar.exe.

.PARAMETER Output
    Directory for the log, the settings used and the screenshots.
#>
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$Output = 'smoke'
)

$ErrorActionPreference = 'Stop'

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Output | Out-Null
$Output = (Resolve-Path $Output).Path

$log = Join-Path $Output 'smoke.log'
$settings = Join-Path $Output 'settings.json'
Remove-Item $log -ErrorAction SilentlyContinue
Copy-Item (Join-Path $PSScriptRoot 'smoke-settings.json') $settings -Force

$started = Get-Date

# Give the dock some running windows.
$apps = @()
foreach ($name in 'notepad.exe', 'mspaint.exe') {
    try {
        $apps += Start-Process $name -PassThru
    }
    catch {
        Write-Host "Could not start ${name}: $($_.Exception.Message)"
    }
}
Start-Sleep -Seconds 4

$startInfo = [System.Diagnostics.ProcessStartInfo]::new($Exe)
$startInfo.UseShellExecute = $false
foreach ($argument in '--smoke-test', '--settings', $settings, '--log', $log, '--screenshot', $Output) {
    $startInfo.ArgumentList.Add($argument)
}

$process = [System.Diagnostics.Process]::Start($startInfo)
if ($process.WaitForExit(120000)) {
    $exitCode = $process.ExitCode
}
else {
    $process.Kill()
    $exitCode = 124
    Write-Host '::error::LinearBar did not finish the smoke test within two minutes.'
}

foreach ($app in $apps) {
    try {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    }
    catch {
    }
}

Write-Host '----- smoke.log -----'
if (Test-Path $log) {
    Get-Content $log
}
else {
    Write-Host '::error::LinearBar wrote no log file.'
}

if ($exitCode -ne 0) {
    # A crash before logging starts only leaves a trace in the event log.
    Write-Host '----- .NET Runtime events -----'
    try {
        Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; StartTime = $started } -ErrorAction Stop |
            Select-Object -First 5 |
            ForEach-Object { Write-Host $_.Message; Write-Host '---' }
    }
    catch {
        Write-Host "No .NET Runtime events ($($_.Exception.Message))"
    }
}

Write-Host "LinearBar exit code: $exitCode"
exit $exitCode
