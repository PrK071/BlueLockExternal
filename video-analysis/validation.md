# Conferência offline do detector 1.3

Entradas: screenshot e gravação fornecidos pelo operador. Referência da bola extraída do quadro de 24 segundos. Execução do mesmo `Vision.AnalyzeBitmap` usado pelo aplicativo, sem enviar comandos ao jogo.

| Caso | Resultado | Centro encontrado | Pontuação |
| --- | --- | --- | --- |
| Screenshot, câmera afastada | Bola localizada | 1097,2 / 526,0 | 0,881 |
| Vídeo, 6 segundos | Bola localizada | 622 / 264 | 0,974 |
| Vídeo, 24 segundos | Bola localizada | 625 / 308 | 0,979 |
| Textura de grama sem bola | Nenhuma detecção | — | — |
| Screenshot com bola removida no cenário de teste | Nenhuma detecção | — | — |

Limiar: 0,85. Erro permitido de centro: menos de 20 pixels por eixo em cada imagem original. Todos os três casos positivos ficaram dentro desse limite. As imagens negativas foram derivadas da imagem fornecida; o original foi preservado. Tempo observado de processamento: aproximadamente 18–45 ms nestes três casos, sem captura de tela nem controle do jogo. Não são medições de taxa de acerto ou latência em partida.

Verificador: `CheckVision.cs`. Imagem anotada: `codex-clipboard-c12eeec8-5b01-4fea-a315-8bff59dcc246-detected.png`.
