# Blue Lock External — piloto visual

Abra `BlueLockExternalPilot.exe` (versão 1.3). Feche o painel antigo `Blue Lock External | Macros` para evitar duas versões abertas. O piloto começa pausado. A atualização preserva a referência dos pés e verifica a validade da amostra anterior.

## Quando o personagem não se move

No Roblox, solte as teclas e aperte **F2**. Esse teste envia W (ou seta para cima, se configurada) por 450 ms, sem rastrear a bola nem chutar. A linha “Entrada enviada ao Windows” conta os eventos aceitos por SendInput; isso não confirma que o jogo os aceitou. O registro informa se o foco interrompeu o teste. Se F2 não movimentar o personagem, uma gravação mostrando o teste e o registro ajuda a diagnosticar a entrada.

A amostra de bola da calibração anterior foi identificada como 100% de pixels verdes de grama. A versão 1.2 rejeita amostras com mais de 85% de pixels desse tipo ou contraste insuficiente. Ela mostra o recorte ampliado no painel. Faça nova captura com F4, contendo a bola inteira com pouca margem; reduza o tamanho da amostra se a bola estiver pequena. Uma amostra rejeitada não substitui uma amostra válida já salva.

## Calibração necessária

1. Entre no campo do Roblox e deixe o ponteiro livre. **F10** envia Ctrl para alternar shift lock se necessário; o programa não lê esse estado.
2. Coloque o ponteiro no ponto do chão entre os pés do seu personagem e pressione **F3**. Isso define a referência do personagem na tela.
3. Coloque o ponteiro no centro da bola visível e pressione **F4**. A amostra deve conter a bola inteira com pouca margem. O tamanho inicial é 28 pixels; ajuste “Amostra da bola” e repita se necessário. Essa amostra é uma pequena captura local.
4. Oriente a câmera para o ataque. Ative o shift lock com Ctrl/F10 se esse for seu modo de jogar. Evite mudar zoom ou inclinação depois de calibrar os pés.
5. Solte as teclas de movimento e pressione **F9** dentro do Roblox.
6. Para parar, use **F12**. F9 também pausa; pressionar fisicamente WASD/setas, Q, R, Space, Ctrl ou Shift pausa o piloto para retomar o controle manual. Abrir chat com Enter ou `/` e pressionar Escape também pausa. Outras formas de abrir chat/menus não são reconhecidas.

## Como o piloto decide

- Captura apenas a região da janela do Roblox, enquanto ela está em primeiro plano. Procura componentes claros compactos, avalia formato, padrão claro/escuro e grama ao redor, e compara a aparência com a amostra real da bola em diferentes tamanhos. A busca usa imagem de trabalho com largura de 640 pixels e ignora zonas da interface para reduzir falsas candidatas.
- Exige duas correspondências sucessivas antes de começar a seguir a posição candidata. Desloca o personagem usando W/A/S/D ou as setas, aproximando a posição da bola à referência dos pés.
- Com a bola próxima da referência, mantém avanço no rumo da câmera, usa Q com intervalo e pode clicar para chutar após proximidade estável. **Proximidade não confirma posse.** Ele pode tentar Q/chute sem estar com a bola.
- Corrida segura Shift somente durante movimento. F8 alterna isso. F10 apenas alterna Ctrl; ele não mantém Ctrl pressionado.
- Pulo automático e pedido de passe com R são opcionais, desmarcados inicialmente. Pulo usa intervalo mínimo de 3 segundos; pedido de passe é feito após perder a bola e respeita intervalo de 8 segundos.
- Ao perder a correspondência visual, interrompe movimento. Se o Roblox sair de foco, libera os comandos e fica em **AGUARDANDO ROBLOX**. Retoma a busca ao retornar à mesma janela/processo do Roblox; não envia comandos a outro aplicativo. F12 desliga essa retomada. Se o processamento exceder 700 ms, descarta o resultado e para o movimento. F12 e liberação de teclas são processados separadamente da visão.

É um seguidor visual com regras de proximidade. Não reconhece o gol, adversários, obstáculos, placar ou posse; não escolhe jogadas táticas. O chute segue a mira/câmera atual. Movimento em tela presume uma câmera usual de terceira pessoa; zoom e inclinação diferentes podem exigir nova calibração.

## Atalhos

| Tecla | Ação |
| --- | --- |
| F2 | Teste de caminhada de 450 ms sem visão |
| F3 | Calibrar referência dos pés sob o ponteiro |
| F4 | Capturar amostra da bola sob o ponteiro |
| F5 | Reiniciar a busca visual |
| F6 | Chute manual com duração configurada |
| F7 | Chute manual com o dobro da duração, até 2 s |
| F8 | Alternar corrida automática com Shift |
| F9 | Iniciar/pausar o piloto dentro do Roblox |
| F10 | Enviar Ctrl para alternar shift lock |
| F11 | Pular com Space |
| F12 | Parar e liberar todos os comandos |

Os atalhos F2–F12 são interceptados enquanto o Roblox ou o painel estão em foco. F12 é global. X não recebe automação. As outras teclas físicas continuam chegando ao jogo. Os comandos injetados pelo próprio programa são ignorados pelo hook de teclado, sem tratar sua própria saída como uma ação manual.

## Ajustes

“Usar setinhas” troca WASD por setas. O chute confirmado é `MOUSELEFT` (esquerdo), 300 ms iniciais; ajuste a duração no painel. Pontuação inicial na versão 1.3: 85%. É uma pontuação de compatibilidade visual, não uma probabilidade de acerto. Reduzir aumenta a chance de falsas detecções de roupas, linhas ou efeitos. O quadro do painel mostra a candidata com caixa verde e a referência dos pés em azul. Esse quadro fica congelado quando o Roblox sai de foco.

Quando a bola girar, mudar de tamanho, ficar oculta ou receber efeitos, a amostra pode deixar de corresponder. Recalibre com F4. Se o programa seguir um objeto errado, pare com F12, refine a amostra e aumente a similaridade. Se a captura ficar preta, o programa pausa e informa o erro; experimente o Roblox em modo janela ou sem bordas.

## Correção de foco na versão 1.1

A janela e o processo do Roblox são vinculados ao iniciar com F9. Durante a execução, as verificações usam o PID dessa janela, aceitam janelas internas do mesmo processo e deixam de consultar o nome do processo a cada tick. Uma captura cancelada de uma execução anterior não pode mais pausar uma execução nova. Mudanças de foco suspendem as saídas sem desarmar o piloto; o registro informa o processo que ficou em primeiro plano ou a indisponibilidade transitória da janela, sem registrar títulos de documentos.

## Entrada e diagnóstico na versão 1.2

Comandos de teclado usam scan codes pelo SendInput normal do Windows. F2 separa o diagnóstico do teclado da visão. O painel mostra eventos de entrada, teclas mantidas e o último evento enviado; o registro mostra a recepção de F9, a decisão de movimento e o motivo de interrupções. O foco também registra a classe da janela que recebeu primeiro plano (por exemplo, barra de tarefas do Explorer), sem registrar títulos de documentos. Esses registros locais são salvos em `pilot.log` na pasta de configuração; não incluem texto digitado no teclado.

## Arquivos locais

Fonte: `PilotForm.cs`, `Platform.cs`, `Vision.cs`, `Settings.cs`.

Configuração: `%LOCALAPPDATA%\BlueLockExternal\Pilot\settings.xml`. Amostra: `ball.png` na mesma pasta. Quadros completos são processados em memória e não são gravados. Não há envio de imagens nem de teclas pela rede.

## Detector da versão 1.3

A busca antiga usava tamanhos derivados da amostra e falhava quando a câmera era muito afastada. Além disso, descartes antecipados de candidatos podiam aparecer como 0% no registro. O detector atual seleciona candidatos pela própria imagem, verifica bolas pequenas e calcula a pontuação completa nas candidatas. Ele foi conferido na imagem enviada (bola localizada perto de 1097/526) e em dois quadros do vídeo, além de duas imagens sem bola. Isso confirma esses casos offline; não mede desempenho em partida nem garante detecção com oclusões, efeitos, linhas sobrepostas ou objetos semelhantes.

## Estado de validação

O executável foi compilado neste computador. O operador confirmou que o teste F2 movimenta o personagem. O detector 1.3 passou pelos casos offline descritos acima. O funcionamento contínuo do piloto na partida ainda precisa de confirmação; nenhuma taxa geral de acerto foi medida. A captura remota do Roblox retornou `FrameArrived timed out`, limitando a inspeção direta.
