# BlueLockExternal V2 — relatório de implementação

Data: 18/10/2026. Plataforma de validação: Windows 10 Home x64, Intel i5-7400, quatro processadores lógicos, aproximadamente 16 GB de RAM, .NET Framework 4.8.1. Dados completos em `reports/environment.json`.

## Entrega

O MVP foi implementado e compilado em `C:/Users/luiz/Documents/BlueLockExternal/V2`. O executável é `bin/BlueLockExternalV2.exe`.

O ciclo completo de percepção, tracking, previsão, decisão, ação e feedback foi demonstrado no campo sintético. Imagens reais e o vídeo fornecido foram processados. A integração autônoma completa com uma partida real não está demonstrada. O adaptador de Studio envia movimento/bloqueio, mas não executa mira, passe ou chute físicos automáticos sem uma calibração de mira implementada.

## 1. Inspeção e linha de base

Foram encontrados o macro original `BlueLockExternal.cs/.exe` e o piloto visual 1.3 em `Pilot/`. O piloto já continha reconhecimento visual simplificado; portanto, a base real incluía mais que macros temporizadas.

Problemas identificados:

- Responsabilidades de interface, timers, captura, seleção de teclas e decisões concentradas no formulário.
- O macro original representava somente uma entrada mantida por vez.
- Dependência do ciclo da interface para parte das interrupções e verificações de foco.
- Ausência de IDs temporais, estimativa matemática de velocidade, previsões avaliadas e estado central de partida.
- Falta de um pipeline com ownership explícito dos frames, descarte limitado e instrumentação por módulo.

A evolução foi adicionada em `V2`, reutilizando a implementação de interop/SendInput do piloto e importando a configuração das macros. Isso mantém uma versão de retorno executável e evita substituir o programa em uso antes da validação.

Os hashes finais dos quatro fontes/binários legados conferem com os capturados na inspeção:

| Arquivo legado | SHA-256 |
|---|---|
| BlueLockExternal.cs | F142A473ECA526313514470EC5282217CC05F07C06DCFDB05AFB6BEE438BEBAF |
| BlueLockExternal.exe | EE20C5E2292B5F908D293E4AB78F4462C061605586F2EA33AAB6CFB63C0EB364 |
| Pilot/Vision.cs | E9CBF20F43A3CD4B2821C592AB74A667A31D12BBFFA0F47C5371906B77B769CA |
| Pilot/PilotForm.cs | 62D8B81EC72C65612136D6A8B9849C8BD2A01351146B075F2AA634170295AF0A |

O verificador legado foi executado isoladamente, sem entradas reais: três positivos e dois negativos passaram. Tempos nesta execução: 154 ms para a primeira imagem, 19 ms e 23 ms nas seguintes. A primeira inclui inicialização/JIT. Isso não constitui comparação equivalente com um benchmark aquecido do V2. Evidência em `reports/legacy-baseline.txt`.

## 2. Arquivos criados e integração

| Área | Arquivos principais criados |
|---|---|
| Core | Models.cs, Pipeline.cs, WindowsInterop.cs |
| Capture | Frames.cs, Simulation.cs, WindowSource.cs |
| Vision | VisionEngine.cs, CameraEstimator.cs |
| Tracking | TemporalTracker.cs |
| Prediction | PredictionEngine.cs |
| Decision | DecisionEngine.cs |
| Input | InputScheduler.cs, NativePlatform.cs, HotkeyService.cs |
| UI | MainForm.cs, CalibrationForm.cs |
| Configuration | Settings.cs |
| Diagnostics | Metrics.cs |
| Tests | TestMain.cs e quatro imagens em Fixtures |
| Construção | build.ps1, format.ps1, dependencies.lock.json |
| Documentação | README.md, ARCHITECTURE.md, NOTICE.md, este relatório e licenses/ |
| Evidências | reports/tests.txt, benchmark.csv, benchmark-image.csv, video.txt, environment.json, hashes e imagens anotadas |

Nenhum fonte ou executável legado foi modificado. Os arquivos criados dentro de V2 foram evoluídos durante a implementação. Configuração e logs novos usam `%LOCALAPPDATA%/BlueLockExternal/V2/`.

## 3. Arquitetura efetivamente implementada

`IFrameSource → LatestSlot<Frame> → IVisionEngine → ITracker → GameState → IPredictor → IDecisionEngine → ActionIntent → IActionExecutor → nova observação`.

Há threads independentes para captura, percepção, decisão, watchdog de entradas e atalhos. O timer da interface lê snapshots e prévias; não executa OpenCV. A fila mantém um frame pendente. Mats são reutilizados e descartados por ownership; históricos, previsões pendentes e medições de latência possuem limites.

O tracker usa associação por tipo/distância e filtro alpha-beta com timestamps reais. Há expiração por oclusão, indicação de ambiguidade e mudança brusca de trajetória. A previsão utiliza velocidade constante, horizontes configuráveis, confiança e interceptação quadrática. O erro é comparado com observações posteriores interpoladas.

A FSM implementa os estados e intenções previstos no pedido. Observações insuficientes ou antigas geram espera/recuperação. Aproximação observada e movimento da bola após chute fornecem feedback. A simulação renderiza pixels: o motor não recebe suas coordenadas internas como detecções.

F6/F7/F8, F9 e F12 permanecem disponíveis. O controlador suporta teclas simultâneas, mouse, WAIT, cancelamento, expiração, liberação em erro e verificação de foco. A entrada autônoma de Studio também exige foco na janela usada pela captura. F12 bloqueia execução até uma nova instância do pipeline; o reconhecimento pode continuar para diagnóstico.

Mais detalhes e o diagrama estão em `ARCHITECTURE.md`.

## 4. Funcionalidades operacionais

- Modos MANUAL, ASSISTIDO e AUTONOMO_TESTE.
- Macros configuráveis, XML, importação dos valores antigos e transporte SendInput.
- Captura de pixels da área cliente e ROI, com suspensão fora de foco.
- Reprodução de imagens, incluindo `timeline.csv`, e vídeo local com timestamps do arquivo.
- BALL por HSV, morfologia, contornos, forma, contraste, entorno verde, template opcional e supressão de duplicatas.
- Entidades de jogadores, equipes, gol e goleiro em cenário com marcadores HSV previamente diferenciados.
- Referência fixa do personagem e referências de campo explicitamente marcadas como não medidas.
- Tracking, velocidade, previsões, interceptações e métricas de erro.
- FSM independente da fonte visual, recomendações assistidas e ciclo autônomo na simulação.
- Sobreposição, IDs, centros, pontuações, taxa de processamento, CPU, RAM, latências e logs JSONL com rotação.
- Perfis, frequências independentes configuráveis, redução adaptativa, buffers reutilizados e descarte de frames atrasados.

## 5. Validação automatizada

**48 testes aprovados, zero falhas.** A lista individual está em `reports/tests.txt`. Os testes não enviam comandos físicos ao Roblox.

Cobertura: fila e descarte, pool, reconhecimento real/sintético, negativos, bola sobre linha, ROI, oclusão, perda/reassociação, timestamps irregulares e fora de ordem, câmera, identidade ambígua, limites de histórico, previsão, erro futuro, interceptação, confiança, FSM, multikey, cancelamento, perda de foco, timeout, falha parcial de transporte, atalhos, XML, timeline, homografia, módulos desligados, modo assistido e emergência com pipeline em threads. Dois testes adicionais exercitam cancelamento durante uma ação deliberadamente bloqueada e cancelamento anterior ao Start. O despacho foi serializado com a liberação para impedir reativação após a conclusão da emergência.

### Imagens reais

Centros esperados foram conferidos visualmente nas imagens fornecidas. O teste exige que o candidato de maior pontuação fique próximo desse centro; não basta retornar qualquer região branca.

| Imagem | Centro esperado aproximado | Centro V2 a 480 px | Pontuação |
|---|---:|---:|---:|
| Screenshot 1916×1007 | 1097 / 525 | 1097,7 / 525,5 | 0,920 |
| Vídeo, 6 s | 622 / 262 | 622,7 / 262,7 | 0,920 |
| Vídeo, 24 s | 625 / 308 | 625,3 / 306,7 | 0,920 |

Os mesmos três casos passaram a 640 px. Grama, linha branca isolada e uma imagem real com a bola removida também foram verificadas. São poucos exemplos do mesmo cenário, sem estimativa de precisão/recall populacional. A pontuação 0,920 é heurística, não “92% de acerto”.

![Detecção no frame de 24 segundos](C:/Users/luiz/Documents/BlueLockExternal/V2/reports/detected-480-24s.png)

### Ciclo fechado determinístico

| Intervalo das observações | Deslocamento acumulado | Chutes simulados | Feedback visual pós-chute | RMSE agregado / amostras |
|---|---:|---:|---|---:|
| 0,1 s | 1755 px | 5 | observado | 18,74 px / 833 |
| 0,2 s | 3540 px | 9 | observado | 19,43 px / 829 |

São 150 observações por execução, com tempo virtual. Deslocamento é distância acumulada, não deslocamento líquido. Há resets da bola e mudanças de trajetória. Esses erros são sobre estados estimados observados posteriormente, não ground truth 3D. O teste linear separado produz erro praticamente zero no modelo compatível.

### Vídeo fornecido

O MP4 de aproximadamente 38,4 s foi amostrado em 192 frames, com compensação de câmera ligada. Houve candidatos BALL em 86 frames. Esse número não é taxa de acerto: faltam rótulos completos e há oclusão, HUD, efeitos e movimentos de câmera.

Com os critérios conservadores de câmera/continuidade, nenhuma previsão amadureceu como amostra válida de erro nessa execução. O resultado de RMSE é **n/d**, não zero. O replay terminou sem erro; duração de processamento registrada em `reports/video.txt`. A limitação de continuidade/previsão nesse vídeo permanece aberta.

### Interface

A interface foi aberta e operada por automação de acessibilidade. F9 iniciou a simulação, os estados/posições foram atualizados e F12 interrompeu a execução. O processo respondeu durante o processamento. Uma amostra com interface e sobreposição indicou 113,8 MB privados e 65,8 MB de working set. Não é benchmark de longa duração.

Na versão final, o diagnóstico econômico e sua prévia atualizam a 1 Hz; os controles permanecem orientados a eventos e o processamento visual tem frequência independente. Uma amostra externa de cinco segundos, com percepção/sobreposição ativas e F12 bloqueando ações, registrou CPU média de 6,84%, memória privada de 116,43 MB e working set de 67,71 MB; `Responding=True`. Não houve leitura UI Automation durante essa amostra. Evidências: `reports/gui-resource-sample.json`, `reports/gui-thread-sample.csv` e `reports/ui-validation-final.txt`. Essa medição inclui o custo da interface e não deve ser confundida com o benchmark de console abaixo. Amostras anteriores variaram entre 4,15% e 7,78% de CPU; a alteração de frequência não constitui, sozinha, uma comparação controlada de desempenho.

A captura remota de janelas usada nas sessões anteriores apresentou timeout; a validação da interface foi feita por estado de acessibilidade e interação, sem afirmar inspeção visual pixel a pixel de todo o layout. Atalhos físicos globais e envio real de movimento/chute no Studio ainda precisam de uma sessão operacional dedicada.

## 6. Desempenho medido

Benchmark final em processo de console, cerca de 15 s por perfil, simulação 640×360, sem sobreposição. Compensação de câmera não é aplicada à simulação. CPU é percentual do processo normalizado pelos quatro processadores lógicos. Não houve isolamento dos demais aplicativos do computador. As metas configuradas não são apresentadas como frequências garantidas.

| Perfil | FPS / Hz configurados | FPS / Hz observados | Pico privado MB | CPU média | Visão p95 ms | Latência p95 ms |
|---|---:|---:|---:|---:|---:|---:|
| ECONOMICO | 5 / 15 | 4,74 / 12,67 | 92,04 | 0,97% | 14,513 | 25,771 |
| EQUILIBRADO | 10 / 20 | 9,06 / 15,79 | 93,79 | 1,06% | 13,563 | 16,358 |
| AVANCADO | 15 / 30 | 12,78 / 20,93 | 93,64 | 1,46% | 14,126 | 14,678 |

O perfil avançado não amplia a fonte sintética de 640 px para 960 px. Todos os frames capturados nesse benchmark foram processados, sem descarte por fila e sem falhas. O teste de sobrecarga da fila substitui 100 frames e confirma 99 descartes e liberação dos anteriores.

O delta privado do perfil econômico após a criação da telemetria foi 5,96 MB; parte do runtime e da fonte já estava carregada nesse ponto. Por isso, o total privado e o working set também são publicados. No econômico, working set foi 31,67 MB. Não se deve interpretar o delta aquecido como custo total de iniciar o aplicativo.

Uma execução anterior, preservada em `reports/benchmark-run1.csv`, ativou redução adaptativa nos perfis maiores e obteve frequências inferiores. Os resultados variam com escalonamento, resolução dos timers e carga concorrente. Não há busy-wait ou alteração global da resolução dos timers para forçar as metas.

O benchmark adicional com a screenshot real 1916×1007 em memória, processamento econômico e câmera ligada está em `reports/benchmark-image.csv`. Ele mede redução/percepção/tracking de uma imagem estática; não inclui GDI de captura real ou decodificação PNG a cada frame.

Nesse ensaio final foram processados 74 frames em 15,48 s: 4,82 FPS publicados pelo contador do pipeline, pico privado de 97,25 MB, working set de 37,04 MB, CPU média de 2,18%, visão p95 de 21,067 ms e latência p95 de 54,116 ms, sem falhas. O contador de FPS usa o instante de criação do pipeline; o tempo total inclui a carga inicial da fonte. As medições também usam snapshots com threads ativas. Nas medições de memória, “MB” representa 1024² bytes.

Nos cenários medidos, a memória privada ficou abaixo de 150 MB. Isso não comprova o limite para qualquer resolução, duração ou computador. Não foi realizado soak test de horas nem validação em outras máquinas. O alvo de CPU é adaptativo, não um limite rígido do sistema operacional.

## 7. Dependências e implantação

Foi mantido .NET Framework 4.8. O ambiente já dispõe do compilador/runtime e a interface existente é WinForms. OpenCvSharp4 oferece o alvo netstandard2.0 utilizado. Migrar agora exigiria SDK/runtime e uma nova matriz de implantação sem benefício necessário para este MVP. [Projeto OpenCvSharp](https://github.com/shimat/opencvsharp).

| Dependência | Versão fixada | Licença declarada |
|---|---|---|
| OpenCvSharp4 e runtime Windows x64 | 4.13.0.20260627 | Apache-2.0 |
| System.Memory | 4.6.3 | MIT |
| System.Buffers | 4.6.1 | MIT |
| System.Numerics.Vectors | 4.6.1 | MIT |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT |
| Plugin FFmpeg fornecido com OpenCV | opencv_videoio_ffmpeg4130_64.dll | LGPLv2.1+ conforme documentação upstream |

As versões e hashes SHA-256 são verificados no build. O runtime completo foi escolhido para suportar vídeo e fluxo óptico. A variante slim avaliada remove módulos necessários a esses recursos; não foi adotada. Não há dependência de modelo neural ou GPU.

As DLLs nativas principais ocupam aproximadamente 97 MB em disco; isso não equivale ao consumo de RAM. A execução foi validada com VC runtime 14.51.36247.0. Requisitos e instalação em Windows limpo ainda não foram ensaiados.

Há aviso CS1702 do compilador antigo referente à versão de assembly System.Memory. O build gera binding redirects para a versão realmente copiada; os testes e o aplicativo carregaram as bibliotecas com esses redirects. O aviso não foi ocultado.

### Considerações comerciais

Apache-2.0 e MIT permitem uso comercial com cumprimento de suas condições de redistribuição. As licenças foram copiadas para `licenses/` e a interface possui “Sobre / licenças”. [Licença OpenCvSharp](https://github.com/shimat/opencvsharp/blob/b161e7e012f5101f6d5dc68a835c59db6cc88b18/LICENSE), [licença OpenCV](https://github.com/opencv/opencv/blob/4.13.0/LICENSE).

FFmpeg exige tratamento próprio dos fontes correspondentes, condições de ligação e avisos; um link genérico não encerra isso. O plugin foi correlacionado com os metadados upstream pelo hash publicado. Os fontes exatos e a documentação de reconstrução ainda precisam ser reunidos para distribuição comercial. [Documentação OpenCV sobre FFmpeg](https://github.com/opencv/opencv/blob/4.13.0/3rdparty/ffmpeg/readme.txt), [termos e orientações FFmpeg](https://ffmpeg.org/legal.html).

O runtime pré-compilado informa `Non-free algorithms: YES`, embora o MVP não chame esses algoritmos. A distribuição comercial deve revisar os componentes nativos incorporados e preferir uma construção mínima sem módulos não utilizados e sem OPENCV_ENABLE_NONFREE. O trabalho entrega um MVP técnico; não declara concluída a auditoria de distribuição comercial. Inventário em `NOTICE.md` e informações nativas em `reports/opencv-build.txt`.

## 8. Funcionalidades parciais e limites

1. Equipes, jogadores, gols e goleiros não são identificados semanticamente em partidas arbitrárias. Exigem marcadores calibrados ou método/dados adicionais.
2. Posse é hipótese por proximidade/co-movimento. Não há leitura interna de estado do jogo.
3. Compensação de câmera é aproximação 2D. O vídeo fornecido ainda não sustenta previsão contínua com os thresholds atuais.
4. Homografia está implementada e testada isoladamente; a decisão do MVP permanece em pixels.
5. Mira, passe e chute automáticos físicos no Studio não foram concluídos. A simulação executa essas intenções; macros manuais usam SendInput.
6. Não houve validação física do novo adaptador em uma sessão Studio, nem benchmark de captura GDI em diferentes monitores/DPI. Perda de foco/cancelamento foram exercitados com transporte simulado.
7. Detecção possui poucos exemplos rotulados; não há garantia de acerto em outras câmeras, efeitos, estilos ou atualizações do jogo.
8. Não há hard cap de CPU, garantia universal de 150 MB ou certificação de uso comercial do conjunto nativo.

## 9. Próxima etapa recomendada

Rotular sequências do vídeo com centro/visibilidade da bola e intervalos de movimento/zoom da câmera. Medir continuidade de IDs e erro de previsão por horizonte; ajustar o estimador de câmera com essas evidências. Perfilar também o custo da interface e da sobreposição, que superou o processamento sem interface nestes ensaios. Em seguida, integrar um cenário Studio com marcadores conhecidos e referência de mira verificável para fechar o ciclo físico. Depois, realizar testes prolongados em outros PCs e produzir o runtime nativo mínimo com inventário comercial completo.
