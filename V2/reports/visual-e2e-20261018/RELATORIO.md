# Integração visual e campanha nativa — relatório de implementação
Data de consolidação: 19/10/2026, America/Sao_Paulo. Diretório/identificador: visual-e2e-20261018. Semente: 20261018.

**O caminho visual com entrada C# real foi implementado e executado. A validação para autonomia prolongada não passou.** O orquestrador de alteração automática de código e a pesquisa contínua não foram habilitados, conforme a condição de validá-los somente após o ciclo completo.

## Implementação real
WindowSource captura pixels da área cliente do Studio em primeiro plano. StudioLabVision usa HSV, contornos, momentos, círculo envolvente e homografia. Quatro fiduciais coloridos de geometria fixa calibram o plano; um disco magenta visível sob o avatar fornece sua posição medida. A bola é a esfera física branca existente. Essa é uma cena instrumentada, não reconhecimento geral de personagens ou de partidas públicas.

O caminho executado é: captura → OpenCV → TemporalTracker → GameState → PredictionEngine → StudioVisualDecision (IDecisionEngine) → InputScheduler/NativeTransport → Studio → nova captura. O módulo de decisão usa o plano calibrado e o controlador de navegação existente. PredictionEngine fornece previsões visuais de curto prazo; nenhuma coordenada dinâmica do Studio é passada à decisão.

O mapa fixo dos fiduciais, raio da bola e velocidade de teste são parâmetros de calibração do laboratório. Não são telemetria dinâmica. As posições internas são lidas por um remetente separado que escreve ground truth em arquivos; o controlador não lê esses arquivos nem o receptor. Nenhuma entrada de movimento desta campanha foi emitida por user_keyboard_input ou character_navigation. MCP preparou condições iniciais e consultou resultados.

Foram acrescentados StudioLabVision.cs, StudioVisualDecision.cs e os programas em Diagnostics/VisualE2E. VisionEngine, Pipeline e o aplicativo existente não foram substituídos. O build é separado em reports/visual-e2e-20261018/bin; a DLL original em V2/bin mantém o SHA-256 do checkpoint.

## Execuções e critérios
A bateria principal contém 50 execuções reais, com 7414 frames e 355 emissões de keyDown nativas. KeyDowns não equivalem a pulsos: movimentos diagonais envolvem mais de uma tecla. Cada caso usa um processo novo e limite de 18 s, com foco obrigatório, parada e liberação de teclas. Há ainda dez regressões comparativas, um teste sem ground truth e um teste de interrupção; os pilotos anteriores estão em pilot/.

As posições aleatórias concretas, orientações e configurações estão em cases.json. Foram incluídos quatro yaws, esfera em movimento, oclusão temporária, blackout para perda de rastreamento, alterações bruscas de direção, mudança temporária de cor e imagens processadas com larguras 480/640/960. A resolução de processamento mudou; não foi validada mudança da resolução física do monitor ou do viewport.

Tolerância física: distância X/Z ≤1,2 stud. Chegada registrada significa que o ground truth observou essa distância em algum instante. Permanência exige pelo menos 0,4 s dentro dela; não exige avatar imóvel. Distância final vem de uma leitura MCP após a parada. Um ciclo completo exige visual-reached, ground truth do mesmo run a até 150 ms da decisão e heldAfter=0. Resultados sem evidência temporal não são aprovados.

## Resultado consolidado
| Medida | Resultado |
|---|---:|
| Chegada registrada durante execução | 38/50 (76,0%) |
| Permanência por pelo menos 0,4 s | 37/50 |
| Distância final dentro de 1,2 stud | 34/50 |
| Confirmação visual verificada | 4/50 |
| Declarações visuais totais | 7 |
| Declarações sem ground truth temporal suficiente | 3 |
| Falsos sucessos confirmados entre os casos verificáveis | 0 |
| Casos com teclas liberadas | 50/50 |

As chegadas registradas são um limite inferior: algumas execuções têm ground truth incompleto. A distância final por MCP existe para os 50 casos. A bola pode continuar se movendo após a liberação das teclas; a distância no instante da decisão e a distância final são mantidas separadas.

## Precisão, confiança, latência e recursos
Comparação espacial em studs contra o Server, com associação temporal de até 150 ms:
| Estimativa | Amostras associadas | MAE | P95 |
|---|---:|---:|---:|
| Bola | 1439 | 1,139 | 6,185 |
| Personagem/marcador | 5274 | 0,522 | 5,221 |

Esses erros incluem falhas de identidade/localização e diferenças de replicação/tempo entre o Server e o Client renderizado. Não constituem uma avaliação perfeitamente sincronizada ao frame. Os valores elevados não são ocultados nem atribuídos exclusivamente à replicação. A sincronização deve ser melhorada antes de otimizar algoritmos por diferenças pequenas de erro.

Os escores 0,85/0,95 são heurísticos. scoreBins no JSON compara esses escores com a frequência de erro ≤0,5 stud, sem chamá-los de probabilidades calibradas. Cada caso registra cobertura visual, maior intervalo sem bola e recuperações; indisponibilidade deliberada por oclusão/blackout está incluída, não removida do denominador.

OpenCV: média 7,64 ms/frame, P95 13,66 ms. Idade captura→decisão: média 42,11 ms e P95 54,29 ms. Duração média: 17,12 s por execução.

CPU acumulada dos processos nativos: 327,23 s; média aproximada de 9,56% normalizada pelos quatro processadores lógicos, incluindo inicialização. Maior working set final: 50,66 MiB. Não é pico contínuo de RAM; não inclui Studio, relay nem o aplicativo original. OpenCV usa uma thread e nenhum modelo neural foi adicionado.

## Diagnóstico, correções e rollback
O pré-teste encontrou a borda azul do viewport ocultando um fiducial quando se usava RetrievalModes.External; a busca passou a List. Cursores/UI coloridos exigiram contexto de campo e forma dos fiduciais. A esfera perto do marcador magenta exigiu contexto que incluísse esse marcador e rastreamento de candidatos. Os registros mostram que ainda há falsos candidatos e perda da bola perto do avatar, sobretudo na conclusão da chegada.

O receptor inicial podia receber um POST atrasado de outro ensaio. A análise foi corrigida para filtrar run, eliminar relógios duplicados e exigir proximidade temporal. Um relay persistente passou a drenar o endpoint entre processos. Por isso, as contagens finais substituem as preliminares de 41 chegadas/5 confirmações; a conclusão corrigida é 38 chegadas/4 confirmações verificadas. Três declarações ficaram indeterminadas.

Após o baseline foram testadas duas alterações limitadas, mantendo a tolerância física de 1,2 stud:
| Comparação de cinco casos | Confirmações verificadas | Distância final média |
|---|---:|---:|
| Baseline, cinco casos iniciais | 0/5 | 1,280 |
| Reenquadramento rejeitado | 2/5 | 1,556 |
| Limiar conservador rejeitado | 1/5 | 1,989 |

O reenquadramento derrubou a cobertura de calibração em três casos. O limiar conservador isolado também piorou a distância final média. Ambas as alterações foram revertidas; os snapshots estão em checkpoint/rejected-active e checkpoint/rejected-conservative. StudioVisualDecision e Fixture.client foram comparados por hash com baseline50 após o rollback. Os cinco casos por versão não estimam significância estatística nem isolam todos os efeitos de UI/tempo; a evidência de regressão foi suficiente para não promover as mudanças.

## Independência, foco e emergência
Em independence/truth-off-001, o emissor de ground truth foi desligado e o arquivo de verdade ficou com zero bytes. OpenCV e C# emitiram três keyDowns e levaram o avatar de 8,00 para 0,93359375 stud. A confirmação visual terminou em timeout porque a esfera foi perdida; o resultado prova independência do canal, não aprovação da conclusão visual.

O teste emergency-001 observou MOVE nativo, criou STOP e obteve status emergency, heldAfter=0 e encerramento do processo. F12 e interrupção por controle manual continuam conectados pelo HotkeyService existente. O pressionamento físico de F12 não foi automatizado, pois o hook ignora entradas injetadas. Pausas/bloqueios de foco são preservados; capturas ou entradas não são forçadas em segundo plano.

Os testes automatizados existentes passaram no build isolado: 48 gerais, 105 de navegação e 40 de bola, totalizando 193. São regressões de software, não substitutos para as execuções físicas. Fixtures ausentes no primeiro build isolado foram copiadas para o local esperado antes da execução final. A validação da CLI rejeitou um run-id inválido antes de emitir entrada.

## Infraestrutura entregue e etapas bloqueadas
Campaign.exec.js é o executor finito do ambiente de ferramentas do Codex: checkpoint, plano de até 50 casos, preparação MCP, execução nativa, arquivos por caso, stop e retomada. Sua retomada foi testada no conjunto concluído: 50 completos, zero pendentes, nenhuma ação adicional no Studio. Duas retomadas reais ocorreram durante a bateria: após limite de tamanho de comando Windows e após o guard de foco. A persistência passou a ser por caso, eliminando o comando acumulado grande.

Esse executor não é um agente que altera o próprio código. quality-gate.json mantém edição contínua e operação prolongada desligadas. O gate exige pelo menos 80% de ciclos confirmados e nenhuma declaração indeterminada; o resultado observado é 8,0% e 3 declarações indeterminadas. Codex CLI, agendamento prolongado e pesquisas sucessivas autônomas não foram iniciados.

As próximas hipóteses, ligadas às falhas medidas, estão no quality gate: agrupar candidatos magenta por dimensão e histórico, limitar o tamanho aparente da esfera, validar a geometria dos fiduciais antes de mudar câmera e melhorar sincronização da avaliação. A pesquisa desta implementação usou documentação primária de [processamento de imagens OpenCV](https://docs.opencv.org/4.10.0/d7/da8/tutorial_table_of_content_imgproc.html), [momentos](https://docs.opencv.org/3.4.0/d0/d49/tutorial_moments.html) e [transformações de perspectiva](https://docs.opencv.org/4.12.0/da/d6e/tutorial_py_geometric_transformations.html). Mecânicas públicas e vídeos de Blue Lock: Rivals não foram usados como prova de física equivalente.

## Preservação e evidências
O Studio terminou em Edit, com somente Camera e Terrain no Workspace. Os dois scripts próprios foram comparados literalmente com o checkpoint e estavam iguais. A DLL original em V2/bin permaneceu idêntica. Nenhuma memória do cliente foi acessada e nenhum mecanismo de evasão foi implementado. Os processos Runner/Relay da campanha foram encerrados.

Os arquivos principais são campaign50-metrics.json, campaign50-trials.csv, campaign-results.json, cases.json, history.json, quality-gate.json e os JSONL/PNG por caso. candidate-metrics.json e conservative-metrics.json preservam as regressões rejeitadas. studio-preservation.json, emergency/ e independence/ contêm verificações adicionais. O manifesto SHA-256 identifica os artefatos finais.

## Distâncias por caso da bateria principal
Inicial: primeira verdade disponível daquele run, podendo faltar. Final: MCP após parada. Confirmação: critério temporal completo descrito acima.
| Caso | Tipo | Largura | Yaw | Inicial | Final | Estado do Runner | Confirmação verificada |
|---|---|---:|---:|---:|---:|---|---|
| e2e-001 | static | 960 | 0° | 8,000 | 1,067 | timeout | não |
| e2e-002 | static | 960 | 0° | 8,006 | 2,371 | timeout | não |
| e2e-003 | static | 960 | 0° | 1,717 | 1,717 | timeout | não |
| e2e-004 | static | 960 | 0° | 8,006 | 0,010 | timeout | não |
| e2e-005 | moving | 960 | 0° | 10,765 | 1,234 | visual-reached | não |
| e2e-006 | static | 480 | 90° | 0,606 | 0,606 | timeout | não |
| e2e-007 | moving | 960 | 180° | 10,312 | 8,941 | timeout | não |
| e2e-008 | occlusion | 640 | 270° | 7,216 | 0,925 | timeout | não |
| e2e-009 | tracking-loss | 480 | 0° | 1,854 | 1,087 | timeout | não |
| e2e-010 | abrupt | 960 | 90° | 8,898 | 0,323 | timeout | não |
| e2e-011 | perception | 640 | 180° | 9,715 | 0,349 | timeout | não |
| e2e-012 | resolution | 480 | 270° | 6,188 | 6,188 | timeout | não |
| e2e-013 | static | 960 | 0° | 9,249 | 0,547 | timeout | não |
| e2e-014 | static | 640 | 90° | 8,961 | 0,760 | timeout | não |
| e2e-015 | moving | 480 | 180° | 9,198 | 9,293 | timeout | não |
| e2e-016 | occlusion | 960 | 270° | 10,702 | 13,835 | timeout | não |
| e2e-017 | tracking-loss | 640 | 0° | 9,890 | 2,201 | timeout | não |
| e2e-018 | abrupt | 480 | 90° | 8,806 | 0,760 | timeout | não |
| e2e-019 | perception | 960 | 180° | 0,942 | 0,942 | timeout | não |
| e2e-020 | resolution | 640 | 270° | 8,534 | 0,266 | timeout | não |
| e2e-021 | static | 480 | 0° | 7,261 | 0,714 | timeout | não |
| e2e-022 | static | 960 | 90° | 0,518 | 2,075 | timeout | não |
| e2e-023 | moving | 640 | 180° | 7,877 | 1,608 | timeout | não |
| e2e-024 | occlusion | 480 | 270° | 8,803 | 0,270 | timeout | não |
| e2e-025 | tracking-loss | 960 | 0° | 1,290 | 1,010 | visual-reached | sim |
| e2e-026 | abrupt | 640 | 90° | 8,174 | 0,922 | timeout | não |
| e2e-027 | perception | 480 | 180° | 8,198 | 0,577 | timeout | não |
| e2e-028 | resolution | 960 | 270° | indeterminado | 1,101 | visual-reached | não |
| e2e-029 | static | 640 | 0° | 0,625 | 0,625 | timeout | não |
| e2e-030 | static | 480 | 90° | 9,001 | 0,474 | timeout | não |
| e2e-031 | moving | 960 | 180° | 8,227 | 0,708 | visual-reached | sim |
| e2e-032 | occlusion | 640 | 270° | 8,357 | 0,090 | timeout | não |
| e2e-033 | tracking-loss | 480 | 0° | 9,142 | 0,667 | timeout | não |
| e2e-034 | abrupt | 960 | 90° | 6,514 | 1,828 | timeout | não |
| e2e-035 | perception | 640 | 180° | 6,305 | 0,412 | timeout | não |
| e2e-036 | resolution | 480 | 270° | 9,410 | 1,393 | timeout | não |
| e2e-037 | static | 960 | 0° | 10,092 | 1,477 | timeout | não |
| e2e-038 | static | 640 | 90° | 7,929 | 0,625 | timeout | não |
| e2e-039 | moving | 480 | 180° | indeterminado | 1,701 | visual-reached | não |
| e2e-040 | occlusion | 960 | 270° | 10,805 | 10,805 | timeout | não |
| e2e-041 | tracking-loss | 640 | 0° | 7,474 | 0,555 | timeout | não |
| e2e-042 | abrupt | 480 | 90° | 7,723 | 0,743 | timeout | não |
| e2e-043 | perception | 960 | 180° | 4,307 | 0,678 | visual-reached | sim |
| e2e-044 | resolution | 640 | 270° | 7,271 | 0,664 | timeout | não |
| e2e-045 | static | 480 | 0° | 6,427 | 0,564 | timeout | não |
| e2e-046 | static | 960 | 90° | 0,659 | 0,659 | timeout | não |
| e2e-047 | moving | 640 | 180° | 9,677 | 0,990 | visual-reached | sim |
| e2e-048 | occlusion | 480 | 270° | 6,125 | 6,125 | timeout | não |
| e2e-049 | tracking-loss | 960 | 0° | 0,575 | 0,575 | timeout | não |
| e2e-050 | abrupt | 640 | 90° | 5,232 | 0,568 | timeout | não |
