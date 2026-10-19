// JavaScript | Diagnostics/VisualE2E/Report.mjs | Node.js | Generate a candid report and quality gate from recorded metrics only.
import fs from 'node:fs/promises';
import path from 'node:path';
const avg=a=>a.reduce((s,v)=>s+v,0)/a.length;
const f=(v,n=3)=>v===null||v===undefined?'indeterminado':v.toFixed(n).replace('.',',');
export async function report(root){
 const read=async name=>JSON.parse(await fs.readFile(path.join(root,name),'utf8'));
 const base=await read('campaign50-metrics.json'),active=await read('candidate-metrics.json'),conservative=await read('conservative-metrics.json');
 const unverified=base.visualDeclarations-base.complete-base.falseVisualSuccess;
 const gate={schemaVersion:1,recordedLocalDate:'2026-10-19',timeZone:'America/Sao_Paulo',passed:false,continuousCodeEditingEnabled:false,prolongedOperationEnabled:false,codexCliInvoked:false,boundedPhysicalBatchHarness:true,requiredVerifiedCompletionRate:.8,observedVerifiedCompletionRate:base.complete/base.n,maximumUnverifiedDeclarations:0,observedUnverifiedDeclarations:unverified,allKeysReleased:base.allKeysReleased,correctionCycles:2,bothControlCandidatesReverted:true,reasons:['Verified completion below 80%','Missing timely ground truth for three visual declarations','Localization and terminal occlusion failures remain'],nextHypotheses:[{id:'H01',problem:'Magenta contours can merge unrelated objects, especially at reduced resolution',experiment:'Cluster marker contours by expected physical diameter and prior measured position; compare stored frames and matched physical cases'},{id:'H02',problem:'Ball identity changes or disappears near the avatar',experiment:'Bound candidate size using the measured fiducial scale; evaluate partial circle fits and identity confidence without relaxing arrival tolerance'},{id:'H03',problem:'Reframing loses fiducial calibration',experiment:'Validate quadrilateral layout under tilted camera before enabling any camera action'},{id:'H04',problem:'Server truth is not exactly synchronized to rendered Client frames',experiment:'Add a render-frame identity for the evaluator only; never expose coordinates to navigation'}]};
 await fs.writeFile(path.join(root,'quality-gate.json'),JSON.stringify(gate,null,2));
 const history=[{step:1,event:'OpenCV native capture integration',result:'Pixel fiducials, visible actor marker and actual white sphere'},{step:2,event:'Native physical baseline',runs:50,result:'Evidence recorded per run with seed 20261018'},{step:3,event:'Metric integrity correction',result:'Reject wrong-run and duplicate truth; require a <=150ms match at declaration; persistent relay added'},{step:4,event:'Camera reframe plus conservative radius',runs:5,result:'Rejected: calibration lost in three cases; sources checkpointed and reverted'},{step:5,event:'Conservative radius alone',runs:5,result:'Rejected: worse mean final distance; sources checkpointed and reverted'},{step:6,event:'Ground truth disabled',result:'Empty truth file; native vision moved from 8 to 0.93359375 studs; visual confirmation timed out'},{step:7,event:'Emergency STOP verification',result:'Native MOVE observed, emergency status, heldAfter=0'},{step:8,event:'Batch resume verification',result:'50 completed / 0 pending; no additional Studio actions'},{step:9,event:'Final state',result:'Studio Edit; live scripts unchanged; original DLL unchanged; no Runner or Relay left running'}];
 await fs.writeFile(path.join(root,'history.json'),JSON.stringify(history,null,2));
 const detailed=base.trials.map(t=>`| ${t.run} | ${t.kind} | ${t.width} | ${Math.round(t.yaw*180/Math.PI)}° | ${f(t.initialDistance)} | ${f(t.finalDistance)} | ${t.status} | ${t.complete?'sim':'não'} |`).join('\n');
 const comparison=[['Baseline, cinco casos iniciais',base.trials.slice(0,5)],['Reenquadramento rejeitado',active.trials],['Limiar conservador rejeitado',conservative.trials]].map(([label,r])=>`| ${label} | ${r.filter(t=>t.complete).length}/${r.length} | ${f(avg(r.map(t=>t.finalDistance)))} |`).join('\n');
 const text=`# Integração visual e campanha nativa — relatório de implementação
Data de consolidação: 19/10/2026, America/Sao_Paulo. Diretório/identificador: visual-e2e-20261018. Semente: 20261018.

**O caminho visual com entrada C# real foi implementado e executado. A validação para autonomia prolongada não passou.** O orquestrador de alteração automática de código e a pesquisa contínua não foram habilitados, conforme a condição de validá-los somente após o ciclo completo.

## Implementação real
WindowSource captura pixels da área cliente do Studio em primeiro plano. StudioLabVision usa HSV, contornos, momentos, círculo envolvente e homografia. Quatro fiduciais coloridos de geometria fixa calibram o plano; um disco magenta visível sob o avatar fornece sua posição medida. A bola é a esfera física branca existente. Essa é uma cena instrumentada, não reconhecimento geral de personagens ou de partidas públicas.

O caminho executado é: captura → OpenCV → TemporalTracker → GameState → PredictionEngine → StudioVisualDecision (IDecisionEngine) → InputScheduler/NativeTransport → Studio → nova captura. O módulo de decisão usa o plano calibrado e o controlador de navegação existente. PredictionEngine fornece previsões visuais de curto prazo; nenhuma coordenada dinâmica do Studio é passada à decisão.

O mapa fixo dos fiduciais, raio da bola e velocidade de teste são parâmetros de calibração do laboratório. Não são telemetria dinâmica. As posições internas são lidas por um remetente separado que escreve ground truth em arquivos; o controlador não lê esses arquivos nem o receptor. Nenhuma entrada de movimento desta campanha foi emitida por user_keyboard_input ou character_navigation. MCP preparou condições iniciais e consultou resultados.

Foram acrescentados StudioLabVision.cs, StudioVisualDecision.cs e os programas em Diagnostics/VisualE2E. VisionEngine, Pipeline e o aplicativo existente não foram substituídos. O build é separado em reports/visual-e2e-20261018/bin; a DLL original em V2/bin mantém o SHA-256 do checkpoint.

## Execuções e critérios
A bateria principal contém 50 execuções reais, com ${base.frames} frames e ${base.inputEvents} emissões de keyDown nativas. KeyDowns não equivalem a pulsos: movimentos diagonais envolvem mais de uma tecla. Cada caso usa um processo novo e limite de 18 s, com foco obrigatório, parada e liberação de teclas. Há ainda dez regressões comparativas, um teste sem ground truth e um teste de interrupção; os pilotos anteriores estão em pilot/.

As posições aleatórias concretas, orientações e configurações estão em cases.json. Foram incluídos quatro yaws, esfera em movimento, oclusão temporária, blackout para perda de rastreamento, alterações bruscas de direção, mudança temporária de cor e imagens processadas com larguras 480/640/960. A resolução de processamento mudou; não foi validada mudança da resolução física do monitor ou do viewport.

Tolerância física: distância X/Z ≤1,2 stud. Chegada registrada significa que o ground truth observou essa distância em algum instante. Permanência exige pelo menos 0,4 s dentro dela; não exige avatar imóvel. Distância final vem de uma leitura MCP após a parada. Um ciclo completo exige visual-reached, ground truth do mesmo run a até 150 ms da decisão e heldAfter=0. Resultados sem evidência temporal não são aprovados.

## Resultado consolidado
| Medida | Resultado |
|---|---:|
| Chegada registrada durante execução | ${base.arrivals}/${base.n} (${f(100*base.arrivals/base.n,1)}%) |
| Permanência por pelo menos 0,4 s | ${base.stableArrivals}/${base.n} |
| Distância final dentro de 1,2 stud | ${base.finalWithinTolerance}/${base.n} |
| Confirmação visual verificada | ${base.complete}/${base.n} |
| Declarações visuais totais | ${base.visualDeclarations} |
| Declarações sem ground truth temporal suficiente | ${unverified} |
| Falsos sucessos confirmados entre os casos verificáveis | ${base.falseVisualSuccess} |
| Casos com teclas liberadas | ${base.allKeysReleased?'50/50':'falha'} |

As chegadas registradas são um limite inferior: algumas execuções têm ground truth incompleto. A distância final por MCP existe para os 50 casos. A bola pode continuar se movendo após a liberação das teclas; a distância no instante da decisão e a distância final são mantidas separadas.

## Precisão, confiança, latência e recursos
Comparação espacial em studs contra o Server, com associação temporal de até 150 ms:
| Estimativa | Amostras associadas | MAE | P95 |
|---|---:|---:|---:|
| Bola | ${base.ballMatchedSamples} | ${f(base.ballMae)} | ${f(base.ballP95)} |
| Personagem/marcador | ${base.actorMatchedSamples} | ${f(base.actorMae)} | ${f(base.actorP95)} |

Esses erros incluem falhas de identidade/localização e diferenças de replicação/tempo entre o Server e o Client renderizado. Não constituem uma avaliação perfeitamente sincronizada ao frame. Os valores elevados não são ocultados nem atribuídos exclusivamente à replicação. A sincronização deve ser melhorada antes de otimizar algoritmos por diferenças pequenas de erro.

Os escores 0,85/0,95 são heurísticos. scoreBins no JSON compara esses escores com a frequência de erro ≤0,5 stud, sem chamá-los de probabilidades calibradas. Cada caso registra cobertura visual, maior intervalo sem bola e recuperações; indisponibilidade deliberada por oclusão/blackout está incluída, não removida do denominador.

OpenCV: média ${f(base.visionMs.mean,2)} ms/frame, P95 ${f(base.visionMs.p95,2)} ms. Idade captura→decisão: média ${f(base.frameAgeMs.mean,2)} ms e P95 ${f(base.frameAgeMs.p95,2)} ms. Duração média: ${f(base.meanDurationSeconds,2)} s por execução.

CPU acumulada dos processos nativos: ${f(base.cpuSeconds,2)} s; média aproximada de ${f(100*base.cpuSeconds/(base.meanDurationSeconds*base.n*4),2)}% normalizada pelos quatro processadores lógicos, incluindo inicialização. Maior working set final: ${f(base.maxFinalWorkingSet/1048576,2)} MiB. Não é pico contínuo de RAM; não inclui Studio, relay nem o aplicativo original. OpenCV usa uma thread e nenhum modelo neural foi adicionado.

## Diagnóstico, correções e rollback
O pré-teste encontrou a borda azul do viewport ocultando um fiducial quando se usava RetrievalModes.External; a busca passou a List. Cursores/UI coloridos exigiram contexto de campo e forma dos fiduciais. A esfera perto do marcador magenta exigiu contexto que incluísse esse marcador e rastreamento de candidatos. Os registros mostram que ainda há falsos candidatos e perda da bola perto do avatar, sobretudo na conclusão da chegada.

O receptor inicial podia receber um POST atrasado de outro ensaio. A análise foi corrigida para filtrar run, eliminar relógios duplicados e exigir proximidade temporal. Um relay persistente passou a drenar o endpoint entre processos. Por isso, as contagens finais substituem as preliminares de 41 chegadas/5 confirmações; a conclusão corrigida é ${base.arrivals} chegadas/${base.complete} confirmações verificadas. Três declarações ficaram indeterminadas.

Após o baseline foram testadas duas alterações limitadas, mantendo a tolerância física de 1,2 stud:
| Comparação de cinco casos | Confirmações verificadas | Distância final média |
|---|---:|---:|
${comparison}

O reenquadramento derrubou a cobertura de calibração em três casos. O limiar conservador isolado também piorou a distância final média. Ambas as alterações foram revertidas; os snapshots estão em checkpoint/rejected-active e checkpoint/rejected-conservative. StudioVisualDecision e Fixture.client foram comparados por hash com baseline50 após o rollback. Os cinco casos por versão não estimam significância estatística nem isolam todos os efeitos de UI/tempo; a evidência de regressão foi suficiente para não promover as mudanças.

## Independência, foco e emergência
Em independence/truth-off-001, o emissor de ground truth foi desligado e o arquivo de verdade ficou com zero bytes. OpenCV e C# emitiram três keyDowns e levaram o avatar de 8,00 para 0,93359375 stud. A confirmação visual terminou em timeout porque a esfera foi perdida; o resultado prova independência do canal, não aprovação da conclusão visual.

O teste emergency-001 observou MOVE nativo, criou STOP e obteve status emergency, heldAfter=0 e encerramento do processo. F12 e interrupção por controle manual continuam conectados pelo HotkeyService existente. O pressionamento físico de F12 não foi automatizado, pois o hook ignora entradas injetadas. Pausas/bloqueios de foco são preservados; capturas ou entradas não são forçadas em segundo plano.

Os testes automatizados existentes passaram no build isolado: 48 gerais, 105 de navegação e 40 de bola, totalizando 193. São regressões de software, não substitutos para as execuções físicas. Fixtures ausentes no primeiro build isolado foram copiadas para o local esperado antes da execução final. A validação da CLI rejeitou um run-id inválido antes de emitir entrada.

## Infraestrutura entregue e etapas bloqueadas
Campaign.exec.js é o executor finito do ambiente de ferramentas do Codex: checkpoint, plano de até 50 casos, preparação MCP, execução nativa, arquivos por caso, stop e retomada. Sua retomada foi testada no conjunto concluído: 50 completos, zero pendentes, nenhuma ação adicional no Studio. Duas retomadas reais ocorreram durante a bateria: após limite de tamanho de comando Windows e após o guard de foco. A persistência passou a ser por caso, eliminando o comando acumulado grande.

Esse executor não é um agente que altera o próprio código. quality-gate.json mantém edição contínua e operação prolongada desligadas. O gate exige pelo menos 80% de ciclos confirmados e nenhuma declaração indeterminada; o resultado observado é ${f(100*base.complete/base.n,1)}% e ${unverified} declarações indeterminadas. Codex CLI, agendamento prolongado e pesquisas sucessivas autônomas não foram iniciados.

As próximas hipóteses, ligadas às falhas medidas, estão no quality gate: agrupar candidatos magenta por dimensão e histórico, limitar o tamanho aparente da esfera, validar a geometria dos fiduciais antes de mudar câmera e melhorar sincronização da avaliação. A pesquisa desta implementação usou documentação primária de [processamento de imagens OpenCV](https://docs.opencv.org/4.10.0/d7/da8/tutorial_table_of_content_imgproc.html), [momentos](https://docs.opencv.org/3.4.0/d0/d49/tutorial_moments.html) e [transformações de perspectiva](https://docs.opencv.org/4.12.0/da/d6e/tutorial_py_geometric_transformations.html). Mecânicas públicas e vídeos de Blue Lock: Rivals não foram usados como prova de física equivalente.

## Preservação e evidências
O Studio terminou em Edit, com somente Camera e Terrain no Workspace. Os dois scripts próprios foram comparados literalmente com o checkpoint e estavam iguais. A DLL original em V2/bin permaneceu idêntica. Nenhuma memória do cliente foi acessada e nenhum mecanismo de evasão foi implementado. Os processos Runner/Relay da campanha foram encerrados.

Os arquivos principais são campaign50-metrics.json, campaign50-trials.csv, campaign-results.json, cases.json, history.json, quality-gate.json e os JSONL/PNG por caso. candidate-metrics.json e conservative-metrics.json preservam as regressões rejeitadas. studio-preservation.json, emergency/ e independence/ contêm verificações adicionais. O manifesto SHA-256 identifica os artefatos finais.

## Distâncias por caso da bateria principal
Inicial: primeira verdade disponível daquele run, podendo faltar. Final: MCP após parada. Confirmação: critério temporal completo descrito acima.
| Caso | Tipo | Largura | Yaw | Inicial | Final | Estado do Runner | Confirmação verificada |
|---|---|---:|---:|---:|---:|---|---|
${detailed}
`;
 await fs.writeFile(path.join(root,'RELATORIO.md'),text);
 return {report:path.join(root,'RELATORIO.md'),gate};
}
