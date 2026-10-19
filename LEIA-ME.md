# Blue Lock External

Programa local para Windows com dois cliques de duração configurável e uma sequência de teclas/mouse. Inicia pausado. Os valores iniciais são exemplos, sem calibração no Blue Lock Rivals.

## Uso

1. Abra `BlueLockExternal.exe`.
2. Configure a duração dos chutes A/B, o botão do chute e a sequência. Salve.
3. Volte ao Roblox e pressione **F9** para ativar.
4. **F6** envia chute A; **F7** envia chute B; **F8** envia a sequência.
5. **F9** pausa. **F12** pausa e interrompe imediatamente a sequência.

Os comandos só começam quando `RobloxPlayerBeta` está em primeiro plano. Ao perder foco, a sequência é cancelada e o comando mantido pressionado é liberado. O programa não detecta chat ou menus do Roblox: pause com F9 antes de abri-los. F6/F7/F8 podem também chegar ao jogo, pois não são interceptadas.

## Sequências

Formato: `TECLA:milissegundos; WAIT:milissegundos`. Cada passo mantém um único botão pressionado e depois o solta. Não há combinação simultânea de teclas.

Exemplo inicial: `Q:45; WAIT:120; MOUSELEFT:300`.

Esse exemplo apenas envia Q, espera 120 ms e mantém o clique esquerdo por 300 ms. Seu efeito depende do mapeamento do jogo e do estado do personagem. Não foi validado como combo.

Teclas: A–Z, 0–9, SPACE, SHIFT, CTRL, ALT, TAB, UP, DOWN, LEFT, RIGHT, MOUSELEFT e MOUSERIGHT. Até 30 passos, 10–3000 ms por passo, duração total máxima de 15 segundos. A execução é única por acionamento; atalhos mantidos não repetem.

Evite manter fisicamente o mesmo botão que a macro vai usar. O programa verifica se ele já está pressionado antes de iniciar o passo.

Configuração em `%LOCALAPPDATA%\BlueLockExternal\settings.xml`. A precisão é a de temporizadores normais do Windows; o servidor e o ping também afetam o resultado.

## Compilação

Código incluído em `BlueLockExternal.cs`. Compilado como aplicativo Windows x64 com o compilador C# do .NET Framework disponível no sistema, usando System.Windows.Forms, System.Drawing e System.Xml.

## Estado da entrega

Não foi possível capturar a janela do Roblox pelo controle do computador: `window capture timed out`. Por isso, os controles, as durações e a aceitação dos comandos pelo jogo ainda precisam de calibração. O programa não recupera informação oculta do jogo nem altera o estado do servidor.
