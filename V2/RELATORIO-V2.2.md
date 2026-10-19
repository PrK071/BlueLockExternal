# BlueLock External V2.2 — posse, condução e finalização

Data local: 18/10/2026. **MVP demonstrado no laboratório próprio do Roblox Studio: 193 testes automatizados e dez cenários físicos de corrida aprovados.** Nove gols e uma falha deliberada de chute corretamente identificada. Uma execução física por cenário; isso não estima confiabilidade estatística nem desempenho em outros computadores.

## Auditoria e preservação da V2.1

A navegação operacional usa referência X/Z e base Right/Look enviados pelo cenário próprio do Studio. Não depende de detecção OpenCV nessa execução. O filtro de tracking/predição visual permanece separado. O `REACHED` da V2.1 significa distância X/Z menor ou igual a 1,2 unidade; não prova colisão nem posse. A bola antiga era ancorada e sem colisão.

Os 16 pulsos físicos WASD foram aprovados nas quatro câmeras anteriormente. Na auditoria adicional por controle remoto, o CSV registrou REACHED nos alvos (0;20), (-20;0), (20;0), além da chegada anterior em (0;-20). O F12 enviado ao painel produziu STOPPED e nenhuma tecla mantida. Evidências: `reports/ball-v22/navigation-audit-live.csv` e `navigation-audit-results.json`. Os vários registros REACHED de cada alvo são frames de uma chegada, não ensaios independentes.

`NavigationLab.rbxlx` e seus dois scripts foram preservados. A V2.2 gera outro arquivo: `StudioLab/Generated/BallControlLab.rbxlx`. A validação automática verifica que gerar o segundo não altera o primeiro. O formato continua UTF-8 sem BOM e com contrato Roblox validado.

## Componentes implementados

| Componente | Responsabilidade |
|---|---|
| `BallControl/Contracts.cs` | Estados, frame autoritativo do laboratório, comandos e relatório por tentativa |
| `PossessionDetector.cs` | Posse com dono e vínculo intacto em amostras distintas; evidências OpenCV separadas |
| `ShotPlanner.cs` | Ponto de finalização, direção, velocidade/potência e trajetória balística; detector de resultado com prova geométrica |
| `BallCycleController.cs` | Máquina de estados com aquisição, condução, chute, recuperação e reinício |
| `BallProtocol.cs` | Parser tipado e limitado da referência do laboratório |
| `BallLabSession.cs` | Worker em segundo plano, lease de entradas, comandos locais, CPU/RAM e JSONL |
| `BallLabProject.cs` | Geração de projeto separado com scripts próprios |
| `UI/BallLabForm.cs` | Cenário, potência, bateria de dez casos, calibração, controle, mapa, OpenCV opcional e exportação |
| `StudioLab/BallServer.server.luau` | Mecânica física, vínculo de posse, lançamento e sensor do gol |
| `StudioLab/BallClient.client.luau` | Câmera conhecida, foco, controles de cenário e HUD |
| `Diagnostics/AnalyzeBallLab.ps1` | Análise reproduzível de logs físicos, provas por cenário e recursos |

Também foram alterados `Navigation/StudioTelemetry.cs`, `Navigation/StudioLabProject.cs`, `UI/MainForm.cs`, `UI/StudioNavigationForm.cs`, `README.md` e `build.ps1` para integração e compilação. Os testes estão em `Tests/BallControl/TestBallLab.cs` e `BallProtocolTests.cs`. Nenhum novo pacote NuGet ou modelo neural foi adicionado. Mantido .NET Framework 4.8 x64.

## Fluxo de controle

```
SEARCH_BALL → APPROACH_BALL → VERIFY_POSSESSION → CARRY_BALL
     ↑                                              ↓
RECOVERING ← perda/falha                   PREPARE_SHOT → SHOOT
                                                    ↓
                                  RESET_SCENE ← VERIFY_GOAL
```

O NavigationController existente executa WASD com resposta estabilizada entre pulsos. A calibração agora espera até 20 segundos pelo foco do Studio depois da contagem regressiva, em vez de falhar imediatamente. O TemporalTracker existente acompanha a bola em um plano de análise de dez unidades por stud, explicitamente convertido para compatibilidade com seus limiares. O PredictionEngine existente fornece extrapolação e medição de erro; sua função de interceptação auxilia a aproximação da bola móvel. As coordenadas de tela não são tratadas como coordenadas de campo.

### Posse

Estados: BALL_FREE, BALL_APPROACHING, BALL_CONTROLLED, BALL_LOST e BALL_UNKNOWN. Proximidade só permite solicitar aquisição à mecânica do cenário. O servidor verifica distância/altura e cria `AlignPosition` ligando a bola física a um attachment do personagem. Só então publica o dono. O detector C# exige dono local, constraint intacto e duas observações distintas, separadas por pelo menos 150 ms. Uma perda confirmada libera movimento e inicia recuperação.

As evidências OpenCV são estimativas identificadas separadamente. Podem indicar aproximação ou bola aparentemente livre entre jogadores visíveis, com confiança limitada. Não concedem BALL_CONTROLLED nem confirmam gol. A caixa de OpenCV é opcional para preservar recursos; desligá-la não elimina a referência autoritativa dos testes.

### Condução e chute

O ponto inicial de chute fica a aproximadamente 12 studs do centro do gol, no lado do personagem. A condução verifica posse continuamente. Antes de preparar o chute, aguarda estabilidade medida. O planejador usa posição real da bola, gravidade do cenário, direção, potência configurável e trajetória `y(t)=y0+vy*t−g*t²/2`. A mecânica deste laboratório usa velocidade horizontal `35+35*potência`; isso não representa parâmetros de Blue Lock: Rivals.

Há duas fases confirmadas pelo servidor: preparar um plano e executar esse plano por seu identificador. O lançamento remove o vínculo, atribui a bola ao servidor e aplica impulso físico. Comandos são identificados por sessão, ensaio e ID monotônico, com deduplicação. O contador parte acima do ack observado, inclusive depois de reiniciar o painel; a parada também usa ID maior que o último comando. O endpoint localhost é preservado em studio-endpoint.txt, com validação de host, porta e token. Respostas HTTP antigas são descartadas pelo cenário. O transporte é uma API localhost escrita especificamente para esse laboratório, não manipulação de tráfego do jogo público.

### Resultado

Estados: SHOT_PREPARED, SHOT_EXECUTED, GOAL_CONFIRMED, SHOT_FAILED e RESULT_UNKNOWN. O servidor verifica o segmento entre posições físicas sucessivas da bola. O plano fica depois da linha do gol por um raio, de modo que a bola inteira deve atravessá-lo. O cruzamento também deve caber em largura e altura descontadas do raio. O C# verifica sessão/ensaio/chute esperado, incremento do contador e posição de cruzamento. Desaparecimento visual não confirma gol.

Chute que cruza fora da abertura ou expira segundo a mecânica do laboratório recebe MISS. Ausência de evidência esperada no controlador produz RESULT_UNKNOWN. Cada tentativa tem relatório próprio e reinício automático confirmado por novo identificador de ensaio. A bateria pode parar após dez casos ou repetir um cenário até a contagem escolhida.

## Cenários implementados

1. Bola parada próxima.
2. Bola parada distante.
3. Bola móvel física antes da aquisição.
4. Avatar começando de costas para o gol.
5. Câmera a 90°.
6. Remoção deliberada e única do vínculo durante a condução.
7. Finalização dirigida ao gol.
8. Perturbação lateral física depois do lançamento para produzir erro verificável.
9. Oclusão visual periódica preservando a referência física.
10. Bloqueio temporário de movimento e recuperação após ausência de progresso.

O caso 8 tem resultado esperado SHOT_FAILED: identificar a falha corretamente conta como aprovação do teste de resultado, não como gol.

## Testes automatizados

Compilação atual em `bin` concluída. **193 testes automatizados passaram: 48 originais + 105 de navegação + 40 da bola.** A suíte da bola inclui os dez ciclos em uma planta independente a 16 studs/s e outros dez a 24 studs/s, posse, perda/recuperação, bloqueio, reinício automático, F12, pausa por foco, trajetória e resultados com prova. Os testes de transporte usam HTTP localhost real, além de verificar tipos, intervalos, IDs fracionários, isolamento de ensaios/sessões, frames repetidos, endpoint persistente e continuidade de IDs depois de reiniciar o painel. Esses testes não enviam teclas ao Studio.

Os ciclos sintéticos de caminhada duraram aproximadamente 21,82–35,02 segundos simulados. A execução da suíte atual levou 1,712 s de parede, 0,344 s de CPU e usou amostras finais de 23,320 MiB privados / 31,789 MiB de working set. São medidas do processo de teste, sem captura; não representam consumo nem sucesso da execução física. Na planta, o RMSE de observações pós-chute foi aproximadamente zero nos casos ideais, 2,415 studs no caso distante e 15 studs no chute com perturbação deliberada. Esses números não medem a precisão no Roblox. Quando não há amostras, a interface mostra “sem amostras”.

Evidências: `reports/ball-v22/synthetic-tests.txt`, `synthetic-cycles.csv`, `synthetic-resources.txt`, `regression-original.txt` e `regression-navigation.txt`. O arquivo de teste original não foi editado. A compilação mantém o aviso CS1702 de System.Memory; os executáveis usam binding redirects e as três suítes foram executadas. O painel aberto usa a compilação atual de `bin`.

## Histórico de validação e causas demonstradas

O operador abriu BallControlLab no Studio e deu Play. A referência V2.2 foi recebida. Foram executados **16 pulsos físicos WASD em quatro câmeras**, com erro angular máximo de **0,057153°**, registrados em `reports/ball-v22/physical-wasd.csv`. A bateria autônoma foi iniciada pelo painel, com movimento exclusivamente pelas entradas do programa. No primeiro cenário, o jogador se aproximou de (0;0) até aproximadamente (0;-4,0234), com bola em (0;-5): a distância caiu de 5 para aproximadamente 0,9766 stud. O servidor confirmou o reinício inicial por novo ensaio.

A aquisição falhou com a mensagem real `ForceApplyAtCenterOfMass is not a valid member of AlignPosition "VerifiedPossession"`, na função acquire. A propriedade correta, verificada na [documentação oficial](https://create.roblox.com/docs/reference/engine/classes/AlignPosition), é `ApplyAtCenterOfMass`. A exceção saía do despachante de comandos e encerrava a coroutine que publicava telemetria; o C# recebia frames antigos e liberava entradas. Evidências: `physical-first-failure.jsonl` e `physical-first-error.txt`.

O script foi corrigido e o despachante passou a usar xpcall, liberar a posse, pausar comandos e registrar BALLLAB_COMMAND_ERROR quando um comando gera exceção. A aquisição corrigida foi depois demonstrada fisicamente: os logs registraram BALLLAB_ACQUIRED, dono 1, vínculo intacto, condução, BALLLAB_SHOT e BALLLAB_RESULT com cruzamento do gol.

A versão seguinte fez cinco gols físicos consecutivos, mas falhou na perda deliberada de posse: colisões com o avatar arremessaram a bola para fora do piso. O laboratório passou a separar avatares e bola em grupos de colisão, mantendo colisões com chão/traves, e recebeu limites físicos. A posse continua dependendo da solicitação e do vínculo, sem atribuição por proximidade. A perturbação de perda define velocidade física conhecida de 4 studs/s. Reinícios recriam uma bola removida pelo limite de queda. APIs de grupos de colisão foram conferidas na [referência WorldRoot](https://create.roblox.com/docs/reference/engine/classes/WorldRoot).

A calibração ganhou espera limitada pelo foco. O endpoint persistente permite reiniciar o painel sem invalidar o cenário aberto, e os IDs seguem acima do ack do servidor. O F12 atual muda imediatamente para STOPPED, inclusive fora de foco. Pausas por foco liberam entradas, removem comandos pendentes e não consomem os timers da tentativa; na retomada a navegação é reavaliada com frame atual.

Houve ainda três instâncias do Studio abertas simultaneamente, somando aproximadamente 8,3 GiB de memória privada. Elas foram encerradas; a bateria de corrida utiliza uma instância. Esse consumo é do ambiente de desenvolvimento e não é contabilizado como memória do painel.

## Corrida solicitada

Foi adicionada **Corrida de teste (24 studs/s)**, com Alt+C no painel V2.2; desmarcada mantém 16 studs/s. O reset do laboratório aplica somente uma dessas duas velocidades. A referência de movimento informa a velocidade efetiva ao NavigationController e os logs guardam requested_speed e movement_speed. O caso 10 publica velocidade zero durante o bloqueio e restaura a velocidade configurada na recuperação.

A corrida é uma mecânica deste laboratório. Não pressupõe valores, tecla Shift ou regras do Blue Lock: Rivals. O controle mantém pulsos curtos e pausa entre observações para medir a resposta; não é corrida contínua sem interrupções. O tempo total de uma tentativa inclui aquisição, estabilização, execução e verificação do chute.

A bateria física final usou `StudioLab/Generated/BallControlLab-Running.rbxlx`, potência 70%, velocidade solicitada 24 studs/s, OpenCV desligado e referência do Studio. `AnalyzeBallLab.ps1 -RequireCompleteSuite` terminou sem erro e aprovou **10/10**, exigindo dez cenários distintos, sequência de estados, dono/vínculo, chute, resultado e evidência das perturbações. A velocidade observada foi 24 durante a corrida; também há 16 antes do reset inicial e zero no bloqueio intencional do caso 10.

| Cenário | Resultado verificado | Duração ativa (s) | Recuperações |
|---|---|---:|---:|
| 1 Bola próxima | GOAL_CONFIRMED | 31,83 | 0 |
| 2 Bola distante | GOAL_CONFIRMED | 33,64 | 0 |
| 3 Bola em movimento | GOAL_CONFIRMED | 44,61 | 0 |
| 4 Costas para o gol | GOAL_CONFIRMED | 27,39 | 0 |
| 5 Câmera rotacionada | GOAL_CONFIRMED | 28,71 | 0 |
| 6 Perda de posse | GOAL_CONFIRMED, com perda e nova aquisição | 62,79 | 2 |
| 7 Chute dirigido ao gol | GOAL_CONFIRMED | 33,77 | 0 |
| 8 Desvio deliberado | SHOT_FAILED, cruzamento fora da abertura | 31,09 | 0 |
| 9 Oclusão visual | GOAL_CONFIRMED, com frames visible=false | 34,13 | 0 |
| 10 Bloqueio de navegação | GOAL_CONFIRMED, após recuperação | 37,65 | 1 |

Cada tentativa completa teve aquisição confirmada, condução e lançamento físico. Os nove reinícios entre casos foram confirmados por novos IDs de ensaio. Depois do décimo, o controlador parou no limite configurado. O caso 9 valida a oclusão do objeto renderizado enquanto a referência autoritativa continua disponível; não comprova recuperação de detecção OpenCV em uma partida real.

Evidências finais em `reports/ball-v22/physical-running/`: `cycle.jsonl`, `attempts.jsonl`, `trials.csv`, `summary.json` e `timing-and-aim.json`. Os arquivos anteriores `physical-first-*`, `physical-fix2-*` e `physical-fix3-*` registram falhas/revisões intermediárias e não são incluídos na taxa da bateria final.

## Recursos, tempos e precisão medidos

Condições: Windows x64, quatro processadores lógicos, uma instância do Studio durante a bateria final, captura OpenCV desligada. Valores abaixo são do processo do painel, não do Studio.

| Métrica | Observação |
|---|---:|
| CPU normalizada, mediana | 6,65% |
| CPU normalizada, p95 | 9,81% |
| CPU normalizada, máximo observado | 12,95% |
| Memória privada máxima do painel | 47,26 MiB |
| Working set máximo do painel | 66,41 MiB |
| Mediana da duração ativa por tentativa | 33,64 s |
| Intervalo mediano entre registros | 244 ms |
| Intervalo p95 entre registros | 334 ms |
| Erro de mira no plano, nos nove gols | 0,079–0,487 stud |

CPU é dividida pelos quatro processadores lógicos. O intervalo entre registros inclui mudanças de estado e não é latência sensor→ação. A telemetria é solicitada a cada 200 ms mais o tempo da requisição; a decisão tem espera independente de 67 ms. Uma amostra verificável do painel no ensaio separado de F12 mostrou p95 do loop de controle de **19,58 ms**, em janela móvel de até 128 decisões, incluindo verificações de foco e instrumentação. Ela não é o p95 consolidado da bateria nem a latência ponta a ponta. A evidência está em `emergency-ui-state.txt`.

O erro no plano compara o cruzamento físico registrado pelo servidor com o centro do gol à altura configurada de 3 studs. O RMSE de observações pós-chute permanece no log, mas **não mede precisão balística em voo**: são apenas uma ou duas observações por finalização, podendo ocorrer depois do cruzamento e de colisões/rebotes. Na bateria, esse valor chegou a 54,58 studs em um gol já confirmado. Não o interpretar como erro de mira nem usar esse número como precisão comprovada do modelo de voo. A instrumentação precisa invalidar previsões após colisão/cruzamento ou coletar um breve burst em voo numa próxima revisão.

As durações dos relatórios de tentativa excluem pausas explícitas de foco. Tempos de chegada a estados no CSV usam timestamps de parede e podem incluir essas pausas. O alvo preliminar de menos de 150 MiB foi atendido para o painel nessa configuração sem OpenCV; não é um benchmark do processo do Studio ou de outros PCs.

## Foco e F12

Durante o caso 3, o painel foi colocado em primeiro plano: os registros `focus-paused.jsonl` mostram teclas liberadas e ausência de comandos. Ao retornar ao Studio, o mesmo ensaio retomou a condução e fez gol. Esse teste não foi contabilizado como falha nem consumiu o timeout da tentativa.

Após os dez cenários, foi iniciado um ensaio adicional exclusivo de emergência. Houve movimento e posse antes do F12. A tecla foi enviada ao painel, que mudou para STOPPED. Foram observados 126 registros de parada, 77 já com foco novamente no Studio, **zero teclas/comandos após a parada**, nenhum novo ensaio e contador de gols inalterado. Evidências: `emergency-result.json`, `emergency-cycle.jsonl` e `emergency-ui-state.txt`. Esse ensaio interrompido intencionalmente não entra na bateria de dez. A injeção da ferramenta exerceu ProcessCmdKey do painel; o caminho global de tecla física não foi reexecutado por essa injeção. Os testes automatizados de prioridade/cancelamento e os atalhos anteriores foram preservados.

O seletor interno do Studio e a captura do computer-use falharam por cache/FrameArrived. A abertura foi recuperada pelo Explorador via endereço do arquivo, e Play, calibração e início foram controlados pela ferramenta. A cópia `reports/ball-v22/StudioExport-Fix3.rbxlx` foi salva pelo próprio Studio antes de encerrar aquela revisão, preservando uma exportação nativa para comparação de formato.

## Operação

1. Abra `bin/BlueLockExternalV2.exe` e **Bola V2.2 Studio**.
2. **Gerar BallControlLab**, abrir o arquivo gerado no Studio e dar Play.
3. **Validar WASD** abre a calibração existente, usando a referência do novo cenário; retornar ao Studio durante os 16 pulsos.
4. Escolher cenário, potência, contagem e bateria automática. Marcar **Corrida de teste (24 studs/s)** ou usar Alt+C; desmarcado usa 16. **Iniciar ciclo** (Alt+I) e retornar ao campo.
5. F12 libera entradas e bloqueia novos comandos/reinícios. A bola já em voo continua sujeita à física; isso não autoriza nova tentativa após a parada.
6. Evidências em `%LOCALAPPDATA%/BlueLockExternal/V2/ball-lab/`: ciclos e tentativas JSONL, com estado, dono, constraint, comandos, chute, cruzamento, erro e recursos.

O painel foi deixado com corrida selecionada e ciclo interrompido por F12. Iniciar ciclo rearma explicitamente e reinicia o cenário escolhido. A geração padrão `BallControlLab.rbxlx` e a cópia `BallControlLab-Running.rbxlx` contêm a mecânica atual; a fixture em `reports/ball-v22/generated` usa endpoint fictício e não deve ser aberta para operação.

## Dependências e limites de entrega

Nenhuma dependência nova foi acrescentada nesta evolução. Continuam OpenCvSharp/OpenCV e os pacotes Microsoft fixados no build; o inventário e os textos de licença estão em `NOTICE.md` e `licenses/`. O runtime de vídeo inclui FFmpeg; o NOTICE registra pendências de fontes/instruções de construção e do inventário nativo antes de distribuição comercial. A licença do código próprio continua a definir. Esta validação não encerra essas pendências.

O ciclo operacional demonstrado depende da telemetria e das mecânicas do cenário próprio. A estimativa OpenCV e suas calibrações continuam separadas; reconhecimento de posse, gol ou estratégias de uma partida pública não foi demonstrado por esta bateria. Não foram adicionadas habilidades especiais, dribles complexos, redes neurais, leitura de memória, offsets ou injeção.

Próxima etapa recomendada: corrigir a instrumentação de trajetória em voo, repetir múltiplas baterias a 16/24 studs/s com perturbações variadas e medir novamente com OpenCV habilitado e captura vinculada à janela correta. A taxa 10/10 descreve somente a bateria registrada acima.
