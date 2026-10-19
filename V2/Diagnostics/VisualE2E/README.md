# Validação visual nativa do Studio

Este módulo é experimental e opt-in. Não altera o VisionEngine original, a interface existente nem o binário em V2/bin. A campanha registrada não aprovou autonomia prolongada. O arquivo de quality gate permanece bloqueado.

O laboratório usa quatro fiduciais coloridos de geometria fixa e um marcador magenta sob o avatar. O OpenCV mede esses pixels e a esfera física branca. Isso não é um detector geral de personagens ou de bolas em partidas públicas.

O caminho nativo é WindowSource → StudioLabVision → TemporalTracker → GameState → PredictionEngine → StudioVisualDecision → InputScheduler/NativeTransport → Studio. StudioVisualDecision implementa IDecisionEngine em um plano métrico calibrado. Não recebe StudioTelemetry ou dados do receptor de ground truth.

O receptor escreve o ground truth em arquivos para análise independente. IDs de ensaio e relógios repetidos são rejeitados. Relay mantém o endpoint do Studio disponível entre processos, evitando retries HTTP de casos anteriores. Não encaminha posições ao controlador.

## Compilar

No PowerShell, a partir da raiz do projeto:

```powershell
& V2/Diagnostics/VisualE2E/build.ps1
```

Saída isolada: V2/reports/visual-e2e-20261018/bin. OpenCV usa uma thread. Runner aceita largura 320–1280 e duração 1–60 segundos; o driver limita a campanha a 50 casos de 18 segundos.

## Captura e análise sem movimento

```powershell
& V2/reports/visual-e2e-20261018/bin/Probe.exe 'C:\saida\studio.png'
& V2/reports/visual-e2e-20261018/bin/Probe.exe 'C:\saida\replay.png' 'C:\entrada\studio.png'
```

A primeira forma depende de foco do Studio; a segunda lê apenas o arquivo. Nenhuma emite entradas. Os escores 0,85/0,95 são heurísticos, não probabilidades calibradas.

## Campanha MCP com entradas C#

Campaign.exec.js é um programa completo para o ambiente de ferramentas functions.exec do Codex, não para node.exe. Antes de executá-lo, o agente deve ler a skill computer-use. O driver seleciona o BallControlLab aberto, cria checkpoint dos scripts, inicia Play, instala Fixture.client.luau e Truth.server.luau somente em runtime, prepara os casos via MCP e executa Runner.exe. Não usa user_keyboard_input nem character_navigation. Os movimentos são nativos do InputScheduler C#.

O plano cases.json contém a semente e todos os parâmetros concretos. O driver ignora casos que já possuem arquivos -mcp.json. A retomada no conjunto concluído foi testada: 50 completos, zero pendentes e nenhuma ação no Studio. Para uma nova campanha, use um novo reportDirectory e copie seu plano para cases.json. Não reutilize resultados de casos com parâmetros ou fontes diferentes.

O driver não faz pesquisa, alteração automática de fontes ou execução do Codex CLI. Essas etapas permanecem bloqueadas pelo resultado físico. Ele é somente o executor finito e retomável da bateria.

## Interrupção e isolamento

Runner mantém WindowSource e NativeTransport com suas verificações de foco. HotkeyService mantém F12 e a interrupção por controle manual. Um arquivo STOP no diretório da bateria interrompe o Runner; o stop permanece latched e não é apagado automaticamente. O driver para ao receber status emergency ou heldAfter diferente de zero.

O teste físico de emergência detectou MOVE nativo, criou STOP, confirmou status emergency e heldAfter=0. O pressionamento físico de F12 não foi automatizado: o hook existente ignora entradas injetadas. Os processos nativos são encerrados no fim dos testes. Relay possui limite de 1–1800 segundos e STOP-RELAY.

## Reanalisar

Analyze.mjs é um módulo Node que exporta analyze(reportDirectory, subdir). Ele lê os arquivos sem emitir entradas. A análise rejeita ground truth de outro run, elimina relógios duplicados e exige uma amostra a até 150 ms da decisão para validar uma declaração de chegada. Distância final por MCP é registrada separadamente. Ground truth do Server ainda inclui diferenças de replicação em relação ao Client renderizado; o erro calculado não é uma medição de visão sincronizada exatamente ao frame.

Os detalhes, resultados por caso, regressões rejeitadas e hipóteses seguintes estão no RELATORIO.md e nos JSON/CSV do diretório da campanha. Os checkpoints baseline50, rejected-active e rejected-conservative preservam os fontes das versões comparadas.
