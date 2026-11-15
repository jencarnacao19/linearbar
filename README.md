# LinearBar

Taskbar alternativa para Windows 10 e 11: dock com ícones que aumentam ao passar o rato
(como no macOS), gráficos ao vivo de CPU, RAM e rede, ícones de tray e relógio.

> Estado: protótipo (fase 0). Arranca e foi verificado automaticamente em Windows, mas
> ainda não foi usado com rato real. Ver [PLANO.md](PLANO.md) para o roteiro e o estado.

## Funcionalidades

- **Dock** ao centro: apps fixadas e abertas, um ícone por aplicação, ícones de 256 px.
- **Ampliação** suave à volta do cursor, com o nome da app por cima.
- **Indicadores**: uma pinta por janela, destaque para a app em primeiro plano, pulsar
  quando uma janela pede atenção, barra de progresso (downloads, cópias).
- **Clique**: abre a app, traz para a frente, minimiza, ou alterna entre janelas da
  mesma app. Clique do meio abre nova instância; clique direito tem janelas, fixar e fechar.
- **Gráficos ao vivo** de CPU, RAM e rede (descarga e envio), e bateria em portáteis.
  Clicar num gráfico abre o Gestor de Tarefas.
- **Tray**, botão Iniciar e relógio com data (abre o centro de notificações).
- Na primeira execução importa as apps fixadas na taskbar do Windows.
- Esconde a taskbar do Windows e repõe-na sempre, mesmo depois de uma falha.

## Requisitos

- Windows 10 ou 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) para compilar.

## Executar

```bash
dotnet run --project src/LinearBar
```

Para experimentar mantendo a taskbar do Windows visível:

```bash
dotnet run --project src/LinearBar -- --keep-taskbar
```

Sair: clique direito na barra → «Sair do LinearBar».

Se a taskbar do Windows não voltar por algum motivo:

```bash
dotnet run --project src/LinearBar -- --restore-taskbar
```

## Definições

Ficam em `%APPDATA%\LinearBar\settings.json` e são aplicadas assim que o ficheiro é
gravado. Também se abrem pelo menu de clique direito na barra. Exemplo:

```json
{
  "barHeight": 52,
  "barOpacity": 0.88,
  "hideWindowsTaskbar": true,
  "showTray": true,
  "dock": { "iconSize": 40, "spacing": 8, "magnify": true, "maxScale": 1.75, "lensRadius": 2.5, "showLabels": true },
  "metrics": { "enabled": true, "intervalMs": 1000, "historyLength": 60, "showCpu": true, "showMemory": true, "showNetwork": true, "showBattery": true },
  "clock": { "show": true, "timeFormat": "HH:mm", "showDate": true, "dateFormat": "ddd, d MMM" },
  "pinned": [ { "path": "C:\\Windows\\explorer.exe" } ]
}
```

O registo fica em `%LOCALAPPDATA%\LinearBar\logs\linearbar.log`.

## Opções da linha de comandos

| Opção | Efeito |
| --- | --- |
| `--keep-taskbar` | Não esconde a taskbar do Windows nesta execução |
| `--restore-taskbar` | Repõe a taskbar do Windows e sai |
| `--settings <ficheiro>` | Usa outro ficheiro de definições |
| `--log <ficheiro>` | Escreve o registo noutro ficheiro |
| `--verbose` | Inclui mensagens de depuração no registo |
| `--smoke-test` | Arranca, verifica-se a si próprio, escreve um relatório no registo e sai |
| `--screenshot <pasta>` | Com `--smoke-test`, guarda capturas de ecrã nessa pasta |

## Desenvolvimento

```bash
dotnet test
```

Teste de fumo completo (o mesmo que o CI corre em Windows), com capturas em `smoke/`:

```bash
dotnet publish src/LinearBar -c Release -r win-x64 --self-contained false -o publish
```

```bash
pwsh ./ci/smoke-test.ps1 -Exe publish/LinearBar.exe
```

Estrutura:

| Pasta | Conteúdo |
| --- | --- |
| `src/LinearBar.Core` | Lógica sem Windows: lente do dock, geometria, agrupamento de apps, métricas, definições. Testável em qualquer sistema. |
| `src/LinearBar` | Aplicação WPF: janela da barra, dock, gráficos, tray, integração com o shell. |
| `tests/LinearBar.Core.Tests` | Testes unitários do núcleo. |
| `ci/` | Script e definições do teste de fumo. |
| `tools/make-icon.py` | Gera o ícone da aplicação. |

A solução também compila em Linux e macOS (`EnableWindowsTargeting`), mas a aplicação só
corre em Windows.

## Créditos

A integração com o shell do Windows (AppBar, lista de janelas, tray, esconder a taskbar)
usa o [ManagedShell](https://github.com/cairoshell/ManagedShell), licença Apache-2.0.
