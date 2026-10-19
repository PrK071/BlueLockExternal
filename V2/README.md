# BlueLockExternal V2

MVP externo para Windows x64, .NET Framework 4.8 e OpenCvSharp. A demonstração autônoma funciona no campo sintético integrado. A análise também aceita imagens, vídeos e pixels da janela em primeiro plano.

## Executar

Abra `bin/BlueLockExternalV2.exe`, mantendo as DLLs e o arquivo `.exe.config` ao lado dele.

### Botão do piloto e tecla configurável

**Piloto Roblox / F10** prepara a fonte `RobloxWindow`, liga a visão e inicia `PILOTO_ROBLOX`. Na V2.1, só produz movimento com posição local medida, quatro referências medidas do chão e uma base de movimento calibrada. Marcar pés sozinho não satisfaz esses requisitos. O botão/atalho desliga; F12 interrompe imediatamente.

O campo **Tecla do piloto** permite escolher F1–F5, F10, F11, Insert, Home, End, PageUp, PageDown, Pause ou Scroll Lock. A mudança vale imediatamente e é salva automaticamente no XML como `PilotHotkey` (código virtual Windows); na próxima abertura ela é restaurada. F6–F9 e F12 continuam reservados às funções existentes. Fora do painel e das janelas do jogo, a tecla escolhida mantém seu funcionamento normal.

O início rápido reaproveita a referência dos pés da V2 ou importa `Pilot/settings.xml` do perfil local. Essa referência continua útil para a visualização; ela não é uma medição do avatar. O detector atual não fornece quatro referências reais do campo e posição local medida em partidas gerais. Portanto, a navegação puramente visual nesse cenário permanece aguardando calibração, com o motivo no painel. Movimento manual e F12 interrompem a execução.

O controlador novo trabalha em X/Z de mundo no laboratório, ou em um plano calibrado por homografia quando as referências medidas permitem. Resolve a base direita/frente da câmera, envia pulsos curtos e compara o deslocamento posterior. Nunca converte diretamente o Y da imagem em profundidade no campo.

**Marcar pés (4 s)** para o piloto e inicia uma contagem: volte à janela Roblox com o ponteiro livre e aponte entre os pés antes do fim. A posição é salva; mudar o enquadramento exige marcar novamente. F12 cancela. Uma referência fixa continua sendo uma aproximação, sem identificar a posição real do avatar.

**Q por proximidade** permanece armazenado por compatibilidade, mas a navegação V2.1 não emite Q nem acrescenta habilidades. As macros manuais F6/F7/F8 continuam disponíveis.

### Laboratório V2.1 no Studio

1. Abra **Navegação Studio** (Alt+S), depois **Gerar cenário .rbxlx** (Alt+G).
2. Abra `StudioLab/Generated/NavigationLab.rbxlx` no Studio, dê Play e clique no viewport. O chão e o alvo são criados ao iniciar o teste.
3. Clique **Testar WASD × 4 câmeras** e volte ao viewport em até quatro segundos. São 16 pulsos independentes, com espera por observações novas e pelo menos 600 ms de estabilidade antes/depois de cada tecla; resultados ficam em `StudioLab/Generated/studio-wasd.csv`. Não movimente o personagem nem gire a câmera manualmente durante o diagnóstico. O teste demora mais por esperar a resposta física.
4. Após aprovação, use **Navegar ao alvo** e volte ao campo. O modo Studio espera a resposta do avatar estabilizar entre pulsos; `OBSERVING` indica teclas liberadas durante essa espera. Z gira a câmera 90°, T troca entre oito casos e Y reinicia a posição. F12 cancela diagnóstico e navegação. `REACHED` significa distância X/Z dentro da tolerância de 1,2 unidade; não é uma medição de contato físico.
5. **Exportar CSV** copia o registro de navegação atual; antes de navegar, copia o diagnóstico WASD. Os registros de navegação ficam em `%LOCALAPPDATA%/BlueLockExternal/V2/navigation/`.

O cenário envia posições e base da câmera por HTTP somente ao receptor localhost da aplicação. Não lê memória do Roblox. Ao reiniciar o aplicativo, gere e reabra o cenário para atualizar a porta e a rota efêmeras. A abertura no Studio foi confirmada após remover o BOM; uma exportação real está em `reports/navigation-v21/studio-exported-reference.rbxlx`.

Resultados e limites: `RELATORIO-V2.1.md`. O laboratório físico precisa concluir a calibração real; os testes sintéticos aprovados não comprovam desempenho em partidas.

### Antena e medidas da bola

Marque **Antena da bola (jogo)**. Com uma fonte de janela ativa e o jogo em primeiro plano, uma janela transparente desenha a caixa da bola, uma antena vertical e uma linha até os pés. O overlay não recebe cliques nem ativa a janela; acompanha a área cliente e atualiza em até 5 Hz, usando as observações existentes. Não aumenta a frequência da captura. A opção pode ser desligada imediatamente e persistida em Salvar XML.

O rótulo mostra ID, confiança, largura/altura da caixa detectada, distância euclidiana em pixels até a referência local e diferenças `dx/dy`. Os tamanhos vêm da caixa de contorno em resolução original, incluindo o efeito da segmentação e dilatação; não são dimensões físicas da bola. A linha vertical é um marcador visual: sua extensão não mede elevação no campo. A posição dos pés segue sendo uma referência calibrada quando não há detecção real.

Ciano indica uma observação confirmada; laranja identifica incerteza ou confirmação inicial. Ao perder a bola, a antena some e aparece **BOLA NAO DETECTADA**. Fora de foco ou com observação antiga, o overlay é ocultado. As mesmas medidas aparecem em Objetos rastreados e na prévia. O marcador não melhora a detecção nem fornece distância 3D, posse ou visão de objetos ocultos.

O overlay solicita `WDA_EXCLUDEFROMCAPTURE` para evitar que seus próprios desenhos entrem na captura, recurso disponível desde Windows 10 2004 conforme [documentação Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity). Essa solicitação não garante suporte em todo ambiente de captura. O desenho também evita cobrir o interior da bola com branco. A integração foi compilada; funcionamento visual ao vivo, custo adicional e compatibilidade com captura ainda exigem verificação nesta partida.

Para demonstrar o ciclo completo:

1. Selecione `AUTONOMO_TESTE`, perfil `ECONOMICO` e fonte `Simulation`.
2. Pressione F9. O jogador azul se aproxima da bola e o sistema observa o resultado das ações.
3. F12 interrompe a execução e libera entradas. F9 desativa a instância; outro F9 inicia uma nova.

`ASSISTIDO` mostra detecções, previsões e intenções, sem executar as recomendações. `MANUAL` mantém as macros: F6 para chute A, F7 para chute B e F8 para a sequência configurada. Macros físicas exigem Roblox Player ou Studio em primeiro plano. Uma macro manual interrompe a execução autônoma vigente.

F6/F7 usam o botão escolhido e a duração em milissegundos. O combo aceita teclas simultâneas: `W+SHIFT:200; WAIT:80; MOUSELEFT:180`. Uma tecla manual de controle interrompe a execução. Atalhos globais só são capturados no contexto do painel/jogo, exceto F12, que é a parada global.

O aplicativo abre parado, com fonte `Simulation`. A tecla do piloto é salva automaticamente; as demais configurações usam **Salvar XML** em `%LOCALAPPDATA%/BlueLockExternal/V2/settings.xml`. Quando ainda não existe configuração V2, os valores das macros são importados do XML do programa original. A configuração antiga não é sobrescrita. As alterações de módulos e calibração entram no próximo início.

No econômico, o painel e a prévia atualizam uma vez por segundo para reduzir seu custo. Percepção e decisão continuam com frequências próprias; os botões e a interrupção funcionam por eventos.

## Imagens e vídeo

- `Images`: escolha uma pasta com PNG/JPG/BMP. Sem manifesto, os timestamps são os instantes da reprodução, sem afirmar que são os tempos originais da gravação.
- Para preservar o tempo gravado, inclua um `timeline.csv` na pasta:

```csv
file,seconds
06s.png,6
24s.png,24
```

- `Video`: escolha um vídeo local. O decoder usa uma thread, salta frames atrasados e mantém os timestamps do arquivo.
- `RobloxWindow` e `StudioWindow`: a captura lê pixels da área cliente e é suspensa fora de foco. Não utiliza memória interna, injeção, offsets ou tráfego do jogo.

A fonte `RobloxWindow` oferece análise assistida, macros manuais e movimento pelo modo `PILOTO_ROBLOX`. Nessa fonte, mira, passe e chute autônomos não estão validados; os chutes manuais F6/F7 continuam disponíveis. O ciclo completo foi demonstrado na simulação e no BallControlLab próprio do Studio, que usa a referência e a mecânica descritas na seção V2.2 abaixo.

## Calibração

O botão **Calibração** configura a referência fixa dos pés, marcadores HSV do cenário, exclusões do HUD, template opcional, ROI, FPS de captura, frequência de decisão, largura de processamento e alvo de CPU. Valores de frequência/largura iguais a zero usam o perfil.

Uma referência fixa dos pés é marcada como não medida. Ela exige câmera alinhada e não confirma posse. As cores de equipes, gol e goleiro só devem representar marcadores previamente diferenciados no cenário: roupas de jogadores reais não bastam para inferir a equipe. A simulação fornece marcadores distintos que são reconhecidos a partir dos pixels renderizados.

`ImageCorners` no XML recebe oito números, na ordem superior esquerdo, superior direito, inferior direito, inferior esquerdo. `FieldProjector` calcula uma homografia para o chão plano. O modo visual V2.1 exige quatro referências medidas ainda nessa pose e uma base WASD calibrada. Isso não reconstrói altura nem permite localizar bolas aéreas no chão.

## Compilar e verificar

Feche a V2 antes de recompilar: o Windows mantém as DLLs nativas carregadas.

```powershell
Set-Location C:\Users\luiz\Documents\BlueLockExternal\V2
.\build.ps1
.\bin\BlueLockV2.Tests.exe
.\bin\BlueLockV2.NavigationTests.exe
.\bin\BlueLockV2.BallTests.exe
.\bin\BlueLockV2.Tests.exe --benchmark 15
.\bin\BlueLockV2.Tests.exe --benchmark-image 15
.\bin\BlueLockV2.Tests.exe --video 'C:\Users\luiz\Videos\2026-10-18 03-13-06.mp4'
```

O script restaura versões fixas do NuGet e verifica SHA-256 em `dependencies.lock.json`. Não exige SDK moderno do .NET ou executável NuGet. Usa o compilador x64 do .NET Framework instalado no Windows. Os testes não enviam teclas reais ao jogo.

São necessários .NET Framework 4.8 x64 e runtime Visual C++ compatível com as DLLs OpenCvSharp. O conjunto foi carregado e executado nesta máquina; implantação em máquinas limpas ainda requer validação.

## Evidências

- `RELATORIO.md`: entrega, resultados e limitações.
- `ARCHITECTURE.md`: contratos, concorrência e política de recursos.
- `reports/tests.txt`: resultados individuais dos testes.
- `reports/benchmark.csv`: medições por perfil.
- `reports/video.txt`: replay do vídeo fornecido.
- `reports/opencv-build.txt`: informações da construção nativa.
- `NOTICE.md` e `licenses/`: dependências e pendências de distribuição comercial.

O executável e os fontes originais, assim como o piloto 1.3, permanecem disponíveis fora de `V2`.

## Laboratório físico V2.2

**Bola V2.2 Studio** (Alt+B) abre o ciclo de aquisição, condução e chute do cenário próprio. Gere BallControlLab, abra-o no Studio e dê Play. **Validar WASD** abre os 16 pulsos nas quatro câmeras; o diagnóstico aguarda foco por até 20 segundos. Depois use **Iniciar ciclo** (Alt+I), retorne ao Studio e acompanhe os ensaios. F12 interrompe e libera entradas.

**Corrida de teste (24 studs/s)** (Alt+C) configura a velocidade no reset do laboratório; desmarcada mantém 16 studs/s. A velocidade efetiva é registrada e usada no referencial de navegação. O controle usa pulsos e feedback entre eles. Pausas de foco liberam entradas e suspendem os timers da tentativa.

O endereço localhost e o token do laboratório são preservados em `%LOCALAPPDATA%\BlueLockExternal\V2\studio-endpoint.txt`. Reiniciar o painel não exige regenerar um cenário que já usa esse endereço. Se a porta estiver ocupada ou a configuração for inválida, o painel informa o erro. Uma nova sessão do Studio exige recalibração.

A posse física exige dono e vínculo de aquisição confirmados; proximidade não basta. O chute usa impulso e gravidade do laboratório. O gol exige cruzamento físico verificado, com IDs de ensaio e chute. A estimativa OpenCV é independente e opcional. As mecânicas não representam as regras do jogo público.

Os registros JSONL ficam em `%LOCALAPPDATA%\BlueLockExternal\V2\ball-lab`. Para analisar uma bateria salva:

```powershell
.\Diagnostics\AnalyzeBallLab.ps1 -CycleLog 'C:\caminho\cycle.jsonl' -AttemptsLog 'C:\caminho\attempts.jsonl'
```

O script gera `reports\ball-v22\physical\summary.json` e `trials.csv`. CPU é a porcentagem do processo dividida pelos processadores lógicos; RAM é o total do painel e não inclui o Studio. O caso 8 deve confirmar um chute errado. Os resultados físicos e suas limitações ficam em `RELATORIO-V2.2.md`.

Acrescente `-RequireCompleteSuite` para exigir os dez cenários com provas de estados, dono/vínculo, chute, resultado e perturbações. A bateria física registrada a 24 studs/s aprovou 10/10: nove gols e um erro deliberado corretamente identificado. Os 193 testes automatizados também passaram. Os dados finais estão em `reports\ball-v22\physical-running`.
