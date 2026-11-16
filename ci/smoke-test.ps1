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
    [string]$Output = 'smoke',
    [string]$Settings = (Join-Path $PSScriptRoot 'smoke-settings.json')
)

$ErrorActionPreference = 'Stop'

$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Output | Out-Null
$Output = (Resolve-Path $Output).Path

$log = Join-Path $Output 'smoke.log'
$settings = Join-Path $Output 'settings.json'
Remove-Item $log -ErrorAction SilentlyContinue
Copy-Item $Settings $settings -Force

$started = Get-Date

# A colourful wallpaper, so the screenshots show the frosted glass (the CI desktop is black).
try {
    Add-Type -AssemblyName System.Drawing
    Add-Type -Namespace Native -Name Desktop -MemberDefinition '[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(int action, int param, string value, int flags);'
    $wallpaper = Join-Path $Output 'wallpaper.png'
    $bitmap = [System.Drawing.Bitmap]::new(1920, 1080)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $rect = [System.Drawing.Rectangle]::new(0, 0, 1920, 1080)
    $blend = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, [System.Drawing.Color]::FromArgb(255, 255, 94, 58), [System.Drawing.Color]::FromArgb(255, 58, 80, 255), 20.0)
    $graphics.FillRectangle($blend, $rect)
    foreach ($spot in @(@(250, 700, 'FFD23F'), @(900, 820, '3BCEAC'), @(1500, 650, 'EE4266'), @(600, 300, '540D6E'))) {
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#' + $spot[2]))
        $graphics.FillEllipse($brush, $spot[0] - 220, $spot[1] - 220, 440, 440)
    }
    $graphics.Dispose()
    $bitmap.Save($wallpaper, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    [Native.Desktop]::SystemParametersInfo(0x0014, 0, $wallpaper, 3) | Out-Null
    Start-Sleep -Seconds 1
}
catch {
    Write-Host "Could not set a wallpaper: $($_.Exception.Message)"
}

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
