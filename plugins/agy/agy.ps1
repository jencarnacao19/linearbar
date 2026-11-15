# LinearBar plugin: Antigravity (agy) usage.
# Prints LinearBar plugin JSON. Prints nothing (widget hidden) when agy is not installed.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$command = if ($env:LINEARBAR_OPT_COMMAND) { $env:LINEARBAR_OPT_COMMAND } else { 'agy' }
if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { exit 0 }

try {
    $raw = & $command -p '/usage' 2>&1 | Out-String
}
catch {
    $raw = $_.Exception.Message
}

# Strip ANSI colour codes and box-drawing characters from the TUI output.
$esc = [char]27
$text = $raw -replace "$esc\[[0-9;?]*[A-Za-z]", '' -replace '[\u2500-\u257F\u2580-\u259F]', ' '
$lines = $text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }

$rows = @()
$bars = @()
$five = $null
$week = $null
foreach ($line in $lines) {
    if ($line -notmatch '(\d+(?:[.,]\d+)?)\s*%') { continue }
    $value = [double]($Matches[1] -replace ',', '.')
    # Lines that report what is left are turned into "used".
    $used = if ($line -match '(?i)remain|left|restante|dispon') { 100 - $value } else { $value }
    $label = ($line -replace '(\d+(?:[.,]\d+)?)\s*%', '').Trim(' ', ':', '-', '|')
    if (-not $label) { $label = 'Quota' }
    $rows += @{ icon = 'gauge'; title = $label; value = ('{0:0} %' -f $used); progress = [math]::Round($used / 100, 3) }
    if ($null -eq $five -and $line -match '(?i)5\s*-?\s*h|five') { $five = $used }
    elseif ($null -eq $week -and $line -match '(?i)week|seman') { $week = $used }
}

if ($null -ne $five) { $bars += @{ label = '5 h'; value = [math]::Round($five / 100, 3); detail = ('{0:0} %' -f $five) } }
if ($null -ne $week) { $bars += @{ label = 'semana'; value = [math]::Round($week / 100, 3); detail = ('{0:0} %' -f $week) } }

$headline = if ($null -ne $five) { $five } elseif ($null -ne $week) { $week } elseif ($rows.Count) { [double]($rows[0].value -replace '[^\d.]', '') } else { $null }

$output = @{
    icon    = 'agy'
    color   = '#4F8DF7'
    text    = if ($null -ne $headline) { '{0:0} %' -f $headline } else { 'agy' }
    bars    = $bars
    tooltip = "Antigravity`n" + (($lines | Select-Object -First 12) -join "`n")
    panel   = @{
        title    = 'Antigravity'
        subtitle = 'Quota segundo «agy /usage» (experimental)'
        rows     = $rows
        buttons  = @(@{ title = 'Atualizar'; icon = 'refresh'; action = 'refresh' })
    }
}

if (-not $rows.Count) {
    $output.panel.rows = @(@{ icon = 'warning'; title = 'Sem percentagens na saída do agy'; detail = (($lines | Select-Object -First 3) -join ' ') })
}

$output | ConvertTo-Json -Depth 6 -Compress
