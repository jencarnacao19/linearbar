# LinearBar — plano

Taskbar alternativa para Windows: ícones que aumentam ao passar o rato (como o dock do
macOS), gráficos ao vivo na barra, e funcionalidades que a tornem mais útil do que a
taskbar original.

Este documento é o ponto de partida para quem continuar o trabalho (pessoa ou agente).

## 1. Avaliação

**É viável e vale a pena**, com uma ressalva: o difícil não é o que se vê (ícones a
crescer, gráficos), é a integração com o Windows — reservar espaço no ecrã, esconder a
taskbar original e repô-la sempre, listar janelas, ícones de tray, apps da Store. Escrever
isso de raiz custa meses e parte-se a cada versão do Windows.

**Recomendação: não escrever essa parte.** Usar o
[ManagedShell](https://github.com/cairoshell/ManagedShell) (Apache-2.0), a biblioteca por
trás do RetroBar, que já resolve tudo isso e é usada por muita gente em Windows 10 e 11.
O esforço fica para aquilo que diferencia o LinearBar.

| Opção | Veredicto |
| --- | --- |
| **C# + WPF + .NET 10 + ManagedShell** | **Escolhida.** Nativa, leve, janelas transparentes com clique a atravessar, e a parte Win32 já feita. |
| Electron | Demasiado pesado (150 MB+ de RAM) para algo que está sempre aberto. |
| Tauri / WebView2 | Interface fácil, mas clique a atravessar zonas transparentes é um remendo e não há equivalente ao ManagedShell. |
| WinUI 3 | Mais moderno, mas pior para janelas transparentes e AppBars; não compila fora do Windows. |
| C++ / Rust nativo | O mais leve, mas o mais lento de desenvolver e sem biblioteca de shell pronta. |

Limites a ter presentes:

- A taskbar do Windows é **escondida, não removida**. Tem de voltar sempre que o LinearBar
  fecha ou falha — é o requisito de segurança n.º 1.
- O menu Iniciar, a pesquisa e o centro de notificações continuam a ser os do Windows; o
  LinearBar só os abre.
- Sem assinatura de código, o SmartScreen vai avisar ao instalar noutros PCs.

## 2. Funcionalidades

### Base (o mínimo para substituir a taskbar no dia a dia)

- Barra em baixo que reserva espaço (as janelas maximizadas param por cima dela).
- Dock ao centro: apps fixadas + apps abertas, **um ícone por aplicação**, com ampliação.
- Indicadores: aberta, em primeiro plano, a pedir atenção, progresso (downloads, cópias).
- Clique: abrir / trazer para a frente / minimizar / alternar entre janelas da mesma app.
- Gráficos ao vivo: CPU, RAM, rede (descarga e envio).
- Ícones de tray, relógio com data, botão Iniciar.
- Definições num ficheiro JSON, aplicadas sem reiniciar.

### O que podemos acrescentar

Por ordem de valor para o utilizador. Esforço: P (horas), M (1–3 dias), G (uma semana ou mais).

| # | Funcionalidade | Porque é útil | Esforço |
| --- | --- | --- | --- |
| 1 | Pré-visualização das janelas ao passar o rato (miniaturas ao vivo), com fechar | É o que mais falta face à taskbar do Windows | M |
| 2 | Painel de processos ao clicar num gráfico: top 5 por CPU/RAM, botão «terminar» | Transforma os gráficos em algo acionável | M |
| 3 | Arrastar para reordenar e fixar; largar ficheiros num ícone para abrir com essa app | Uso diário | M |
| 4 | Controlo de média: faixa atual, play/pausa/seguinte | Evita ir à app para mudar de música | M |
| 5 | Volume por aplicação com a roda do rato sobre o ícone | Não existe na taskbar do Windows | M |
| 6 | Mais gráficos: GPU, disco, temperaturas | Para quem joga, renderiza ou compila | P–M |
| 7 | Relógio com calendário, próxima reunião, fusos horários, temporizador | Substitui várias apps pequenas | M |
| 8 | Animações: salto ao abrir, minimizar «para dentro» do ícone | Dá o toque macOS | P |
| 9 | Vários monitores: uma barra por ecrã com as janelas desse ecrã | Essencial para quem tem 2+ ecrãs | M |
| 10 | Aparência: desfoque/acrílico, tema claro/escuro, cor de destaque do Windows, modo «ilha flutuante» | Aspeto | M |
| 11 | Ocultar automaticamente; barra no topo | Preferência comum | M |
| 12 | Janela de definições (em vez de editar JSON) | Usável por quem não é técnico | M |
| 13 | Áreas de trabalho virtuais: indicador e troca | Para quem as usa | M |
| 14 | Silenciar microfone / indicador «em reunião» | Útil em chamadas | P–M |
| 15 | Rede: latência, IP, estado da VPN | Diagnóstico rápido | P |
| 16 | Reciclagem e «stacks» de pastas (Transferências) como no macOS | Toque macOS | M |
| 17 | Lançador por teclado (tipo Spotlight) e Win+número | Para quem vive no teclado | M–G |
| 18 | Arranque com o Windows, instalador, atualizações automáticas | Para distribuir a outras pessoas | M |

## 3. Fases

Cada fase termina com algo utilizável. Não avançar sem cumprir o «feito quando».

### Fase 0 — Fundações

Estrutura do projeto, barra a reservar espaço, dock com ampliação, gráficos de CPU/RAM/rede,
tray, relógio, definições em JSON, CI.

*Feito quando:* compila, os testes passam e a barra arranca em Windows sem erros.
**Já existe neste ramo** — ver secção 6.

### Fase 1 — Validação no PC real (obrigatória antes de qualquer outra coisa)

O que existe só foi executado numa máquina de CI sem rato. No PC:

- [ ] Arrancar e confirmar: a barra aparece, a taskbar do Windows desaparece e volta ao sair.
- [ ] Passar o rato pelo dock: ampliação fluida, sem tremer nem «piscar» entre ícones.
- [ ] Clicar por cima da barra (zona transparente) atinge a janela que está por baixo.
- [ ] Clique num ícone: abre / traz para a frente / minimiza / alterna janelas.
- [ ] Ícones de tray respondem a clique esquerdo e direito.
- [ ] Escala do ecrã diferente de 100 % (125 %, 150 %): tamanhos e posição corretos.
- [ ] Matar o processo no Gestor de Tarefas e voltar a abrir: a taskbar é recuperada.
- [ ] Jogo ou vídeo em ecrã inteiro: a barra sai da frente.

*Feito quando:* a lista está toda verificada e os problemas encontrados estão corrigidos.

### Fase 2 — Dock completo

Pré-visualização de janelas (1), arrastar/reordenar/largar ficheiros (3), animações (8),
menus de contexto completos.

*Feito quando:* dá para trabalhar um dia inteiro sem sentir falta da taskbar do Windows.

### Fase 3 — Barra útil

Painel de processos (2), GPU/disco/temperaturas (6), controlo de média (4), volume por
aplicação (5), calendário (7).

*Feito quando:* cada gráfico responde a um clique com informação ou ação úteis.

### Fase 4 — Aparência e conforto

Janela de definições (12), temas e acrílico (10), vários monitores (9), ocultar
automaticamente e barra no topo (11).

### Fase 5 — Distribuição

Arranque com o Windows, instalador, assinatura de código, atualizações (18).

## 4. Decisões técnicas

Valem mesmo que o código atual seja refeito.

- **Dois projetos.** `LinearBar.Core` (.NET puro: matemática do dock, geometria, métricas,
  definições) testável em qualquer sistema; `LinearBar` (WPF) só com interface e Win32.
- **Ampliação = lente «fisheye».** Cada ícone é escalado por uma função cosseno centrada no
  rato; as posições vêm do *integral* dessa função. Resultado: os ícones nunca se
  sobrepõem, o ponto sob o rato fica sob o rato, e as pontas do dock não tremem.
  Implementado em `Core/Dock/Lens.cs` e `DockLayout.cs`.
- **Ícones a crescer acima da barra.** Só a altura da barra é reservada no ecrã, mas a
  janela é mais alta: a faixa de cima é transparente e os cliques atravessam-na (o Windows
  ignora píxeis com alfa 0 em janelas *layered*). Por baixo dos ícones ampliados
  desenha-se uma «placa» quase invisível (alfa 1/255) para o rato não cair nos intervalos.
  A janela é esticada ao interceptar `WM_WINDOWPOSCHANGING` (`BarWindow.xaml.cs`).
- **Consequência:** o desfoque/acrílico não pode ser aplicado a essa janela (desfocaria
  também a faixa transparente). Para o ter, usar uma segunda janela só de fundo, por baixo.
- **Animar com transformações, não com layout.** Os ícones são dispostos uma vez; a lente
  só altera `RenderTransform` (`MagnifyPanel.cs`).
- **Ícones grandes.** Os ícones das janelas têm 32 px; pedem-se os de 256 px ao shell e
  recortam-se os que vêm com margens vazias (`AppIconLoader.cs`).
- **Métricas baratas.** `GetSystemTimes`, `GlobalMemoryStatusEx` e contadores de rede; sem
  WMI nem contadores de desempenho. Amostragem fora da thread da interface.
- **Recuperação da taskbar.** Antes de esconder, grava-se o estado original num ficheiro;
  se o processo morrer, o arranque seguinte repõe-no. `LinearBar.exe --restore-taskbar`
  repõe à mão.

## 5. Riscos

| Risco | Mitigação |
| --- | --- |
| Utilizador fica sem taskbar após uma falha | Ficheiro de recuperação + `--restore-taskbar` + repor antes de qualquer outra limpeza ao sair |
| Atualizações do Windows 11 mudam o tray ou a taskbar | Ficar em cima das versões do ManagedShell; teste de fumo no CI em cada alteração |
| Desempenho da janela transparente em 4K | Medir na fase 1; a janela só é redesenhada durante a animação |
| ManagedShell deixa de ser mantido | Licença permite fork; a dependência está isolada em `Shell/` e `BarWindow` |
| Ícones do sistema no tray (rede, volume) em falta no Windows 11 | Verificar na fase 1; alternativa: widgets próprios de volume e rede |

## 6. Estado deste ramo

O ramo contém uma primeira implementação da fase 0 (cerca de 7 000 linhas, testes incluídos).

**Verificado automaticamente** no commit `bdc9176` (GitHub Actions, Windows Server 2025,
ecrã 1024×768, 100 %):

- Compila em Linux e em Windows; os 95 testes unitários de então passam.
- A barra arranca, reserva exatamente a sua altura e a taskbar do Windows é escondida e reposta.
- Dock com 8 apps (fixadas e abertas, agrupadas), ícones de 256 px e nomes corretos.
- Gráficos com leituras plausíveis de CPU, memória e rede; 4 ícones de tray.
- Lente: escalas 1,00 · 1,09 · 1,48 · 1,73 · 1,49 · 1,09 · 1,00 à volta do ícone alvo; etiqueta correta.
- Menus e tooltips abrem; saída limpa com código 0.

**Não verificado:**

- Rato real — no CI a ampliação foi simulada, porque a sessão não entrega movimentos do
  rato. O clique a atravessar a faixa transparente e a «placa» nunca foram testados.
- Escalas de ecrã diferentes de 100 %, vários monitores, Windows 11 de secretária.
- Cliques nos ícones de tray, clique/minimizar/alternar nos ícones do dock.

**Corrigido depois dessa verificação, ainda sem correr em Windows:** em ecrãs estreitos o
dock encolhia demasiado (no ecrã de 1024 px do CI os ícones ficaram a 40 % do tamanho),
porque era centrado no ecrã com a mesma margem dos dois lados — a do grupo mais largo, o
da direita. O `MagnifyPanel` passou a ter margens esquerda e direita separadas e
`DockLayout.PlaceOrigin` centra o dock no ecrã quando cabe e desliza-o para o lado livre
quando não cabe. Compila e os 102 testes passam em Linux; o CI não correu para este
commit, por isso confirmar na fase 1 (ou fazer um push, que volta a correr o CI).

**Por fazer na fase 0:** README e ícone da aplicação.

## 7. Continuar no PC

Requisitos: Windows 10 ou 11 e o [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet test
```

```bash
dotnet run --project src/LinearBar
```

Para experimentar sem esconder a taskbar do Windows:

```bash
dotnet run --project src/LinearBar -- --keep-taskbar
```

Teste de fumo (o mesmo que o CI corre), com relatório e capturas de ecrã em `smoke/`:

```bash
dotnet publish src/LinearBar -c Release -r win-x64 --self-contained false -o publish
```

```bash
pwsh ./ci/smoke-test.ps1 -Exe publish/LinearBar.exe
```

Se a taskbar do Windows não voltar:

```bash
publish\LinearBar.exe --restore-taskbar
```

- Definições: `%APPDATA%\LinearBar\settings.json` (editar e gravar aplica de imediato).
- Registo: `%LOCALAPPDATA%\LinearBar\logs\linearbar.log`.
- Sair: clique direito na barra → «Sair do LinearBar».

Começar pela **fase 1**.
