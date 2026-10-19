# BlueLockExternal V2.1 — navegação e formato do laboratório

Data: 18/10/2026. Compilação: Windows x64, .NET Framework 4.8, C# compatível com o compilador do Framework.

## Resultado e escopo da validação

Implementados controlador de navegação com feedback, base explícita de coordenadas, telemetria do cenário próprio, diagnóstico WASD em quatro câmeras, mapa e CSV. Compilação concluída; 48 testes originais e 105 testes adicionais passaram. O arquivo de teste que antes falhava abriu no Roblox Studio após retirar exclusivamente o BOM UTF-8. O próprio Studio salvou uma exportação usada pelos testes de comparação.

A calibração física foi concluída no Studio pelo operador: **16 pulsos em quatro orientações, erro direcional máximo de 0,1091°**. O CSV foi conferido e preservado em `reports/navigation-v21/studio-wasd-validated.csv`, com resumo em `physical-calibration-result.json`. A aprovação de navegação em simulação e a calibração física não comprovam que o avatar real já alcança os oito alvos. Esse é o próximo ensaio.

Após corrigir a espera de feedback, houve a primeira chegada física registrada: **20,0362 → 0,2614 unidade**, com estado **REACHED** no CSV. O operador confirmou que tocou na bola. Evidências: `studio-navigation-contact-reported.csv` e `studio-navigation-contact-result.json`. Isso aprova o ensaio atual com alvo fixo em (0;-20); os demais casos ao vivo continuam pendentes. O contato foi observado pelo operador; não foi adicionado sensor de contato.

A tentativa inicial remota foi interrompida por falta de foco; o computer-use retornou `coordinate input geometry is unavailable` ao tentar focar o viewport. A captura de tela também havia falhado com `FrameArrived timed out`. O procedimento local posterior permitiu completar a calibração.

## 1. Causa demonstrada de Invalid XML

O gerador usava `XmlWriter.Create(path, new XmlWriterSettings { Indent = true })`. A saída começava com os bytes `EF BB BF`, seguidos pela declaração XML e por `<roblox version="4">`.

O parser XML genérico aceitava o arquivo. O Studio instalado registrou `DataModelLoadingFailure:Invalid XML`. Para isolar a causa, preservamos todo o conteúdo e retiramos somente os três bytes `EF BB BF`. Ao usar Tentar novamente, o operador confirmou que abriu. A árvore nativa passou a mostrar a aba NavigationLab.rbxlx. O teste automatizado compara os dois arquivos byte a byte e confirma que nenhuma outra alteração foi feita nesse experimento.

Isso demonstra a incompatibilidade com o BOM nesse carregamento/build do Studio. A declaração XML não foi a causa do experimento: permaneceu no arquivo que abriu. Os namespaces ausentes e o uso de entidades XML no Source também não impediram essa abertura.

### Correção permanente

- UTF-8 estrito sem BOM: `new UTF8Encoding(false, true)`.
- Saída começa diretamente em `<roblox>`, como a exportação nativa; declaração omitida.
- Envelope com namespaces e version=4 alinhado ao arquivo exportado.
- Strings Luau passam por verificação de caracteres XML e escaping do XmlWriter; Unicode, `&`, `<`, `>`, aspas e `]]>` são preservados. CR é entitizado para roundtrip exato.
- Gravação temporária, validação do contrato do laboratório e substituição atômica do arquivo anterior. Erros de caracteres não danificam um projeto existente.
- Endpoint aceito somente em HTTP para 127.0.0.1 e sem caracteres que possam quebrar a string Luau.

### Validação específica do formato

`StudioLabFormat` valida UTF-8, início sem BOM, XML bem formado com DTD desabilitado, raiz Roblox version=4, filhos da raiz reconhecidos, classes/referents únicos, Properties e Name tipado, HttpEnabled booleano e os scripts nos serviços corretos:

```
roblox
├── Workspace
├── HttpService (HttpEnabled=true)
├── ServerScriptService
│   └── Script NavigationLabServer / ProtectedString Source
└── StarterPlayer
    └── StarterPlayerScripts
        └── LocalScript NavigationLabClient / ProtectedString Source
```

É um validador do contrato desse laboratório, não uma implementação completa de todo o esquema Roblox. A evidência adicional de compatibilidade é a abertura real e a exportação pelo Studio.

### Comparação com exportação nativa

O comando Salvar do Studio foi executado pela interface; a barra de status confirmou o caminho local. O arquivo salvo começa diretamente em `<roblox>`, tem version=4 e os mesmos tipos e hierarquia dos scripts. O teste compara ambas as fontes Luau após normalização CRLF e confirma igualdade. O Studio acrescentou serviços, propriedades padrão, Camera e Terrain; o gerador mínimo não precisa copiar todos esses valores.

Evidências em `reports/navigation-v21/`:

| Arquivo | Conteúdo |
|---|---|
| invalid-original.rbxlx | Arquivo rejeitado, com BOM |
| bom-only-removed-opened.rbxlx | Variante que abriu; só três bytes retirados |
| studio-exported-reference.rbxlx | Exportação real salva pelo Studio |
| studio-format-comparison.txt | Comparação automática de contrato e fontes |
| studio-native-log.txt | Trechos de execução do laboratório no Studio |

SHA-256 da variante aberta: `E3B26D31C744377F58C52E9F3A823150FD1AF415E2435B70EB2DD50E166B9D89`.

SHA-256 da exportação nativa: `86B58FD8B4EE891CCA3132C2FE71460BC7A9D9A9F3A28A2C840214B21CAC10EE`.

O gerador corrigido recriou `StudioLab/Generated/NavigationLab.rbxlx` pelo painel atualizado. Seu endpoint é efêmero: reiniciar a aplicação exige gerar e reabrir o cenário. A reabertura dessa última geração foi solicitada; a abertura já comprovada refere-se à variante isolada sem BOM.

## 2. Auditoria da navegação anterior

### Posições e coordenadas

As detecções visuais são coordenadas da imagem. O objeto LOCAL_PLAYER frequentemente representa uma referência fixa de pés, marcada `Measured=false`, e não uma posição medida do avatar. O registro anterior contém, por exemplo, pés fixos em 960/508. A câmera em terceira pessoa pode mostrar os pés muito abaixo desse ponto. O piloto anterior subtraía essa âncora da bola e usava os sinais de dx/dy para selecionar WASD.

Exemplo da falha: bola em y=600, âncora antiga em y=508 e pés reais em y=785. O erro antigo fica positivo, escolhendo S, embora o alvo esteja acima dos pés reais. Corrigir apenas o sinal do eixo faria outros enquadramentos falharem. Y de imagem mistura perspectiva, profundidade, elevação e câmera; não equivale a Z no campo.

`CameraEstimator` estima translação visual esparsa e descontinuidade. Não fornece yaw completo ou geometria de perspectiva. A simulação anterior usava o mesmo plano de pixels para decisões e movimento, sem revelar a falha de referencial da câmera. A regressão nova separa a planta física, a câmera e as teclas.

### Conversão e liberação de entradas

O contrato existente do InputScheduler é X positivo=D, Y positivo=S. Esse contrato não estava globalmente invertido. O novo controlador define segundo eixo positivo=frente/W e converte explicitamente para `Direction=(right,-forward)` ao entregar a intenção. Testes verificam W/A/S/D e liberação da tecla oposta.

Foram mantidos cancelamento, watchdog, verificação de foco e F12. O scheduler agora serializa sinalização e descarte para evitar corrida com Dispose. O diagnóstico usa uma trava para impedir que um pulso novo seja iniciado depois do cancelamento. A emergência do Pipeline permanece serializada com execução em andamento.

## 3. Arquitetura implementada

```
Studio próprio: posição XZ + Right/Look da câmera
       ↓ localhost, amostras com sequência e tempo de recebimento
NavigationSample → NavigationController → ActionIntent
                                              ↓
                                    InputScheduler / SendInput
                                              ↓
                         nova posição observada → comparação
```

O receptor TCP escuta somente loopback, usa porta e rota aleatórias, limita cabeçalho/corpo, timeout e sequência. Ele recebe dados produzidos pelos scripts do cenário criado para o teste. A flag Studio no JSON é uma condição do protocolo, não uma prova criptográfica da identidade do processo emissor.

### Transformações

- Mundo: usa X/Z; remove altura de RightVector e LookVector; normaliza e ortogonaliza os vetores.
- Resolve uma base 2×2 para direção direita/frente. Base degenerada ou de baixa confiança não produz movimento.
- Visual calibrado: FieldProjector transforma pixels do chão por homografia. Exige posição local medida e quatro referências medidas que continuem próximas dos cantos da pose calibrada.
- O caminho visual geral permanece incompleto: o detector atual não fornece essas quatro referências reais e posição local medida em uma partida comum. Sem elas o piloto aguarda, em vez de dirigir com a antiga âncora fixa.

### Controle e feedback

- Pulsos de 60–120 ms, seguidos de observação nova e intervalo neutro.
- Amostra mais antiga que 350 ms, alvo perdido, personagem no ar ou baixa confiança: WAIT e liberação das entradas.
- Inversão de direção incompatível (>70°), deslocamento desproporcional ou três pulsos sem resposta: solicita recalibração.
- Histerese de 280 ms evita reversões rápidas. Mudança relevante de câmera interrompe o pulso para reobservação.
- F12 bloqueia execução até reiniciar explicitamente; não há reativação automática pelo próximo frame.
- CSV registra posições, distância, orientação, confiança, intenção, teclas efetivamente mantidas, deslocamento esperado/observado, erros e estado de decisão.

## 4. Cenário preservado

O servidor cria chão plano e alvo, configura velocidade 16 e oferece oito casos. Z gira câmera 90°, T troca caso e Y reinicia posição. O cliente usa câmera Scriptable conhecida e comunica sua base. O diagnóstico pede cada tecla separadamente em quatro orientações antes de permitir navegação. Não foram adicionadas estratégias de chute ou novas habilidades.

O ambiente é criado em Play. Ver o editor inicialmente vazio não significa que o cenário foi perdido.

## 5. Testes reproduzíveis e comparação

### Primeiro diagnóstico físico e correção de isolamento das teclas

O operador confirmou que o avatar se moveu com W e A no cenário. O CSV real (`studio-wasd-before-settling.csv`) contém apenas esses dois pulsos: W com erro de 0,0021° e A com 30,5368°. O diagnóstico interrompeu no A por exceder 15°; S, D e as demais câmeras ainda não foram aprovados.

O deslocamento registrado no A foi aproximadamente (-2,8168; -1,6617): continha movimento para frente além do deslocamento lateral. O método anterior iniciava a tecla seguinte imediatamente depois de uma espera de 450 ms, usando a última amostra disponível de telemetria a 5 Hz. Não verificava se o avatar havia parado. Movimento residual e atraso de observação são uma hipótese coerente com esse registro; só o novo ensaio físico pode confirmar a causa completa.

O diagnóstico agora libera entradas e aguarda uma janela de observações novas com pelo menos três intervalos e 550 ms de estabilidade (normalmente 600 ms a 5 Hz), velocidade observada máxima de 0,18 unidade/s e câmera estável. A espera é aplicada antes e depois de cada pulso. Leituras repetidas do mesmo pacote não contam. Se não estabilizar em seis segundos, perde foco ou for cancelado, interrompe sem aprovar a calibração. O CSV inclui os timestamps das posições antes/depois. As colunas velocity são deslocamento total dividido pelo pulso nominal de 250 ms, incluindo a resposta residual, e não velocidade instantânea do Humanoid.

### Primeiro vídeo de navegação física

Foi inspecionado o vídeo `2026-10-18 16-19-06.mp4` (53,87 s) e preservada uma cópia do CSV da execução em `studio-navigation-first-attempt.csv`. O avatar se movimenta, mas o arquivo não contém estado REACHED. A distância inicial é 34,1577 e a menor distância registrada é 27,2264 unidades. O controlador comparou três pulsos curtos com observações quase estacionárias e entrou em RECALIBRATE; posições posteriores mudaram. O registro também contém interrupção F12. Ainda é necessário separar atraso de resposta e eventual entrada manual antes de atribuir a causa. O relato de aproximação não foi tratado como aprovação de chegada ao alvo. Resumo: `first-navigation-observation.json`.

O operador posteriormente confirmou que também apertou WASD/setinhas nesse primeiro vídeo. Ele não isola a execução autônoma.

### Ensaio posterior: parada antecipada perto do alvo

O registro `studio-navigation-near-target.csv` mostra aproximação de 27,7150 para 4,7315 unidades. Não chegou à margem de 1,2 nem registrou REACHED. Ao mudar para diagonal, a avaliação esperou deslocamento (-1,92;-1,92), mas a primeira amostra apresentou (+0,3203;+0,2487), erro de 172,83°. Entrou em RECALIBRATE em t=3106,3136. Em t=3106,4696, a posição seguinte já reduzia a distância para 4,7399; a resposta ainda estava mudando quando a falha foi decidida.

O caminho Studio agora instancia `NavigationController(true)`: depois do pulso de 60–120 ms libera as teclas e usa a mesma janela de estabilidade da calibração. Nenhum novo pulso é empilhado nessa fase. Sem resposta, aguarda até 1,5 s antes de contar tentativa sem movimento. Se não estabilizar em seis segundos, solicita recalibração. A verificação de inversão é aplicada à posição estabilizada; uma inversão persistente continua bloqueada. O caminho visual não foi automaticamente habilitado por essa mudança.

O cálculo de velocidade esperada das diagonais em WorldXZ agora normaliza o vetor de controle, consistente com o [vetor unitário de direção do Humanoid](https://create.roblox.com/docs/reference/engine/classes/Humanoid/MoveDirection). Os testes físicos iniciais validaram apenas os quatro eixos; o efeito da diagonal corrigida no avatar real ainda exige novo ensaio.

Novos testes reproduzem uma amostra inicial invertida seguida de resposta correta, mantêm bloqueio para inversão estabilizada, limitam ausência de resposta/estabilidade, verificam normalização e repetem os oito casos nas quatro câmeras com o novo modo. Uma planta independente com atraso de observação de 0/0,2/0,6/1,0 s partiu exatamente de (0,8828;-15,3516), o ponto real em que parou, e alcançou a margem em todos os quatro ensaios sintéticos. Resultados: `delayed-feedback-acceptance.csv`; distâncias finais 1,1971/1,1971/1,1971/1,0089. Isso não demonstra contato físico: o alvo do laboratório continua sendo uma peça ancorada sem colisão e sem sensor de contato.

No ensaio posterior no Studio, o CSV registrou REACHED com distância 0,2614 e o operador relatou contato. A avaliação com resposta estabilizada, portanto, também concluiu uma aproximação física, além dos ensaios sintéticos. Ainda não foi demonstrada a bateria completa de oito casos no Studio.

Foram acrescentados quatro testes determinísticos: contaminação W→A, pacotes repetidos/antigos, perda de observação/rotação e rejeição de personagem ainda em movimento. No ensaio físico posterior à correção, os 16 pulsos passaram, com erro máximo de 0,1091°. Isso valida as teclas nas quatro câmeras desse laboratório; a perseguição autônoma ainda precisa ser demonstrada.

Comandos:

```powershell
Set-Location C:\Users\luiz\Documents\BlueLockExternal\V2
.\build.ps1
.\bin\BlueLockV2.Tests.exe
.\bin\BlueLockV2.NavigationTests.exe
```

O build aceita `-OutputDirectory` para verificar uma compilação separada enquanto o painel anterior mantém DLLs abertas. Para atualizar `bin`, feche a aplicação primeiro.

| Suíte | Resultado |
|---|---|
| Original | 48 aprovados / 0 falhas |
| Adicional V2.1 | 105 aprovados / 0 falhas |
| Incluídos na adicional: cenários de navegação | 8 casos × 4 orientações × 2 modos de feedback = 64 aprovados |
| Incluídos na adicional: formato do laboratório | 11 aprovados |

Os demais testes cobrem calibração independente, base inválida, inversão observada, confiança, F12, perda de foco, espaço de coordenadas, entradas opostas, CSV, servidor localhost e integração multithread do Pipeline sem captura. Nenhum teste automatizado envia teclas reais ao Roblox.

SHA-256 de `Tests/TestMain.cs` continua `0981FD4A46CA43CE3C8FBB74E3F8D78CC5E8629DE45470397838C30640D354DF`.

### Comparação controlada de movimento

Planta independente em X/Z, controles normalizados, percepção 5 Hz e passo físico/decisão de 20 ms. Casos: frente, atrás, esquerda, direita, diagonal, alvo móvel, rotação da câmera e oclusão temporária. Distâncias são unidades do cenário, não metros medidos no jogo.

| Implementação | Alvos alcançados | Distância final média | Erro direcional médio |
|---|---:|---:|---:|
| Regra antiga com âncora desatualizada | 4/32 | 21,55 | 95,48° |
| Controlador V2.1 com avaliação imediata | 32/32 | 1,12 | 2,83° |
| Controlador Studio com resposta estabilizada | 32/32 | 1,11 | 2,62° |

Maior intervalo invertido: regra antiga 1,58 s; controlador novo 0 s. O arquivo `acceptance.csv` contém distância inicial/final, caso, câmera, erro e sucesso individuais. Esta é uma regressão sintética que reproduz a falha da âncora, não uma taxa medida em partidas reais.

## 6. Métricas observadas

| Medição | Resultado | Limite da interpretação |
|---|---:|---|
| 100 mil atualizações do controlador | 33,428 ms | Microbenchmark do modo padrão; exclui captura/renderização/Studio |
| Média por atualização | 0,334 µs | Média, não p95 de controle físico |
| Processo da suíte adicional: memória privada | 93,301 MiB | Inclui integração e carga de bibliotecas, não RAM incremental isolada |
| Processo da suíte: working set | 37,590 MiB | Uma amostra ao fim dos testes |
| GUI + laboratório aberto: memória privada | 43,957 MiB | Sem navegação autônoma ativa |
| GUI + laboratório: working set | 59,645 MiB | Mesma amostra |
| CPU do processo em janela de 2,029 s | 2,50% da máquina com 4 CPUs lógicas | Aproximadamente 10% de um núcleo; amostra curta sem controle ativo |

Fontes: `performance.txt` e `gui-resource-sample.csv`. Esses números não validam consumo durante uma partida, latência física de ponta a ponta ou distribuição em PCs diferentes. O Studio e seu consumo são excluídos da medição do processo C#.

## 7. Arquivos principais criados/modificados

Novos:

- `Navigation/NavigationController.cs`: base de movimento, controle, feedback e calibração.
- `Navigation/MotionSettlement.cs`: janela de estabilidade com observações novas para isolar pulsos de calibração.
- `Navigation/StudioTelemetry.cs`: receptor localhost e parser limitado.
- `Navigation/NavigationLog.cs`: CSV.
- `Navigation/StudioLabProject.cs`: gerador corrigido.
- `Navigation/StudioLabFormat.cs`: validação do contrato Roblox do laboratório.
- `StudioLab/Server.server.luau`, `StudioLab/Client.client.luau`: cena e telemetria autorizada.
- `UI/StudioNavigationForm.cs`: diagnóstico, navegação, mapa e exportação.
- `Tests/Navigation/Acceptance.cs`, `PlaceFormatTests.cs`, `PipelineNavigationTests.cs`, `SettlementTests.cs`, `FeedbackTests.cs`.
- `RELATORIO-V2.1.md` e evidências em `reports/navigation-v21/`.

Modificados:

- `Core/Models.cs`, `Core/Pipeline.cs`: modo Studio e integração com execução/emergência.
- `Configuration/Settings.cs`: base de movimento calibrada.
- `Decision/VisualPilotDecision.cs`: remove navegação direta por pixels e exige referencial observável.
- `Input/InputScheduler.cs`, `Input/HotkeyService.cs`: liberação/sinalização e foco vinculado ao Studio.
- `UI/MainForm.cs`: acesso ao laboratório, configuração e status.
- `build.ps1`, `README.md`: compilação e uso.
- `StudioLab/Generated/NavigationLab.rbxlx`, `bin/BlueLockExternalV2.exe`, `bin/BlueLockV2.Core.dll` e testes compilados.

Não alterados: fonte dos 48 testes originais, programa original fora de V2 e piloto 1.3.

## 8. Dependências e pendências

Não houve migração de runtime nem novo pacote NuGet. O receptor usa System.Web.Extensions do Framework. OpenCvSharp/OpenCV e dependências existentes permanecem; a compilação ainda emite CS1702 para a referência System.Memory. Os redirects existentes permitiram executar todas as suítes. Inventário e pendências comerciais de FFmpeg/OpenCV permanecem em `NOTICE.md` e `licenses/`.

Ainda não demonstrado: todos os oito casos com WASD físico no Studio, perseguição visual geral em partida, orientação visual robusta sem telemetria, posse da bola, altura 3D e mudanças de identidade de jogadores. Não foi adicionado estimador que invente esses dados. A próxima etapa é concluir e registrar a calibração e os casos no laboratório real, antes de ampliar o controlador visual.

Referência de implementação do formato, mantida pelo projeto rbx-dom: [Roblox XML format](https://github.com/rojo-rbx/rbx-dom/blob/master/docs/xml.md). A comparação principal deste trabalho usa a exportação nativa local, e não apenas essa documentação ou um parser XML.
