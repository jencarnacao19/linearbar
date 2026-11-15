# LinearBar plugin: ping latency with a rolling graph.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$target = if ($env:LINEARBAR_OPT_HOST) { $env:LINEARBAR_OPT_HOST } else { '1.1.1.1' }
$historyFile = Join-Path $env:TEMP 'linearbar-ping-history.txt'

$ms = $null
try {
    $reply = (New-Object System.Net.NetworkInformation.Ping).Send($target, 3000)
    if ($reply.Status -eq 'Success') { $ms = [int]$reply.RoundtripTime }
}
catch { }

$history = @()
if (Test-Path $historyFile) { $history = @(Get-Content $historyFile | ForEach-Object { [double]$_ }) }
$history = @($history + @(if ($null -ne $ms) { $ms } else { 0 })) | Select-Object -Last 30
$history | Set-Content $historyFile

$avg = ($history | Measure-Object -Average).Average
@{
    icon    = 'ping'
    color   = if ($null -eq $ms) { '#FF5F57' } elseif ($ms -gt 120) { '#FFB454' } else { '#4FD1A5' }
    text    = if ($null -ne $ms) { "$ms ms" } else { 'offline' }
    graph   = $history
    tooltip = "Latência até $target`nMédia: {0:0} ms" -f $avg
    panel   = @{
        title    = 'Latência'
        subtitle = $target
        rows     = @(
            @{ icon = 'ping'; title = 'Agora'; value = if ($null -ne $ms) { "$ms ms" } else { 'sem resposta' } },
            @{ icon = 'gauge'; title = 'Média (últimas 30)'; value = ('{0:0} ms' -f $avg) }
        )
        buttons  = @(@{ title = 'Atualizar'; icon = 'refresh'; action = 'refresh' })
    }
} | ConvertTo-Json -Depth 5 -Compress
