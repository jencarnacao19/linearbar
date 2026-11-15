# Plugins do LinearBar

Um plugin é uma pasta com um `plugin.json` e um programa em qualquer linguagem
(PowerShell, Python, Node, um `.exe`…). O LinearBar executa-o e mostra o que ele
imprime num widget de vidro com squircles, igual aos incluídos.

- Plugins incluídos: pasta `plugins` ao lado do `LinearBar.exe` (os desta pasta).
- Os teus plugins: `%APPDATA%\LinearBar\plugins\<nome>\`. Se tiverem o mesmo `id` que
  um incluído, substituem-no.

## `plugin.json`

```json
{
  "id": "meu-plugin",
  "name": "O meu plugin",
  "run": "powershell -NoProfile -ExecutionPolicy Bypass -File meu.ps1",
  "mode": "interval",
  "intervalSeconds": 60,
  "timeoutSeconds": 30,
  "placement": "right",
  "enabledByDefault": true,
  "permissions": ["network"],
  "options": { "cidade": "Lisboa" }
}
```

| Campo | Significado |
| --- | --- |
| `id` | Minúsculas, algarismos, `-` e `.` |
| `run` | Linha de comandos, executada na pasta do plugin |
| `mode` | `interval`: executa de N em N segundos. `stream`: fica a correr e cada linha impressa é uma atualização |
| `placement` | `right` ou `left` (ilha da direita ou da esquerda) |
| `options` | Valores por omissão; o utilizador pode mudá-los nas definições. Chegam ao programa como variáveis de ambiente `LINEARBAR_OPT_<NOME>` |

Quando o utilizador clica num botão com `"action": "x"`, o programa é executado com
`--action x` acrescentado.

## O que o programa imprime

Texto simples (primeira linha no widget, resto na dica), ou JSON:

```json
{
  "text": "42 %",
  "icon": "gauge",
  "color": "#4F8DF7",
  "tooltip": "Texto da dica",
  "bars": [ { "label": "5 h", "value": 0.42 }, { "label": "semana", "value": 0.1 } ],
  "graph": [3, 5, 4, 8, 6],
  "hidden": false,
  "click": { "action": "panel" },
  "menu": [ { "title": "Abrir site", "open": "https://exemplo.pt" } ],
  "panel": {
    "title": "Título",
    "subtitle": "Subtítulo",
    "rows": [ { "icon": "clock", "title": "Linha", "detail": "detalhe", "value": "12", "progress": 0.3 } ],
    "buttons": [ { "title": "Atualizar", "icon": "refresh", "action": "refresh" } ]
  }
}
```

Ações (`menu`, `buttons`, `click`): `open` (URL, ficheiro ou app), `run` (comando na pasta
do plugin), `copy` (copia texto), `action` (`refresh`, `panel`, `menu`, ou uma ação tua).

Ícones disponíveis: `weather-sunny`, `weather-clear-night`, `weather-partly-cloudy`,
`weather-cloudy`, `weather-fog`, `weather-drizzle`, `weather-rain`, `weather-snow`,
`weather-storm`, `music`, `play`, `pause`, `next`, `previous`, `claude`, `codex`, `agy`,
`sparkle`, `gauge`, `clock`, `calendar`, `refresh`, `warning`, `thermometer`, `droplet`,
`wind`, `globe`, `cpu`, `plug`, `terminal`, `bell`, `timer`, `ping`. Também aceita um
emoji, um carácter, ou o caminho de uma imagem.

Scripts PowerShell com acentos devem ser gravados em UTF-8 com BOM (o Windows PowerShell
5.1 lê os outros como ANSI).
