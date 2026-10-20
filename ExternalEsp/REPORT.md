# Implementação do ESP externo — 20/10/2026

Foi construído `bin/BlueLockEsp.exe`, um executável separado de 65.024 bytes, em C#/.NET Framework 4.8 x64. Implementa leitura de instâncias, jogadores, posição física, vida e matriz de câmera, projeção sobre uma janela externa, marcações de jogadores/candidatos a bola, filtro de distância, pausa F9 e encerramento F12.

## Resultado real

**O funcionamento em partida de Blue Lock: Rivals ainda não foi comprovado.** O processo Roblox encontrado correspondeu à build da tabela, mas o ponteiro inicial retornou nulo durante todas as tentativas da sonda. O Windows retornou `MainWindowHandle = 0` para esse processo. Não foi possível obter posições ou validar o alinhamento visual no jogo. A causa de os ponteiros estarem nulos não foi determinada.

Não se apresenta compilação, teste controlado ou igualdade de build como comprovação de funcionamento em partida. O programa informa indisponibilidade e não desenha dados antigos ou posições inventadas.

## Evidência executada

1. **Teste inicial direto no processo encontrado.** OpenProcess com acesso de leitura abriu com sucesso. ReadProcessMemory leu o cabeçalho MZ corretamente e transferiu oito bytes em cada RVA global fornecido; ambos continham zero. Nenhuma escrita foi realizada.
2. **20 verificações aprovadas, zero falhas, em processo controlado.** O próprio executável criou um processo filho e construiu nele um conjunto conhecido de estruturas. O processo pai abriu o filho com os mesmos direitos de leitura e percorreu efetivamente sua memória. Os testes validaram árvore, strings UTF-8 curtas/longas, exclusão do jogador local, saúde, posições e distâncias, matriz, objetos fora da tela/atrás da câmera, filtros e renderização. Corrupção controlada de raiz/matriz foi produzida dentro do próprio fixture, sem escrever no Roblox.
3. **Imagem renderizada do fixture.** `reports/20261020/fixture-overlay.png` contém a caixa e o rótulo do jogador controlado, além do marcador da bola. É renderização fora de tela do fixture; não é screenshot de uma partida.
4. **Sonda no cliente real, limitada a três segundos.** PID observado: 7504; versão `version-9ac8595712b447a7`; 27 tentativas, zero snapshots válidos. Primeiro erro: ponteiro nulo/inválido no RVA FakeDataModel.Pointer. Nenhum overlay ou comando de entrada foi executado por essa sonda.

A sonda consumiu 0,046875 segundo de CPU em 3,0143329 segundos de parede e teve pico de working set de 19.816.448 bytes, aproximadamente 18,90 MiB. Esses números descrevem somente a sonda falhando em obter a raiz, não o custo do ESP em partida. O tamanho/CPU do fixture não foi apresentado como desempenho no jogo.

F12 físico, F9 físico, comportamento efetivo de clique através da janela e alinhamento com uma janela real **não foram exercitados**. Seus handlers e estilos estão implementados; não se afirma validação física dessas funções.

## Tabela importada

Fonte: HPP anexado pelo operador, referente ao site de offsets informado. Versão declarada: `version-9ac8595712b447a7`, dump datado de 20/10/2026 às 20:17 GMT.

- SHA-256 do HPP: `E8522D40B313541D2EDCA8441B42F66A7737685E3CB4BDB1C5A6ADBF728DB275`.
- Cabeçalho declara 392 offsets, mas existem **393 declarações de constantes** no arquivo.
- **18 constantes são zero**. Não foram automaticamente consideradas válidas ou inválidas; o ESP somente utiliza os campos necessários e recusa raízes/endereços inválidos.
- Importador transforma identificadores e números em constantes C#; não executa o código remoto.

O código público do dumper foi consultado para conferir o vetor de filhos com passos de oito bytes e a cadeia de nome. A disposição de strings foi comparada com sua rotina de leitura. Essa conferência não valida todos os offsets no cliente real. Valores e código completo do dumper não foram executados no Roblox.

## Comportamento implementado

O leitor abre apenas `RobloxPlayerBeta` da versão esperada. Quando há vários processos, exige PID explícito. Não solicita escrita ou operação de memória, não ajusta privilégios e não implementa evasão. Não existem driver, injeção, patch de código, escrita de propriedades ou geração de movimentos/chutes.

Cada operação confere o tamanho realmente transferido, limites de endereço, números finitos e limites das estruturas. Um snapshot precisa de raiz, Workspace, Players, referências físicas, dimensões e matriz estruturalmente válidas. Uma leitura válida não prova correspondência com os pixels; essa comparação permanece pendente.

A árvore de filhos é limitada a 16.384 slots por vetor. A descoberta da bola limita-se a 3.000 nós e até seis níveis, atualizada a cada segundo. São processadas até 100 entradas de jogadores e 50 candidatos a bola por frame, com orçamento de 75 ms. Excesso provoca erro explícito e remoção das marcações antigas.

O alvo da bola é selecionado por nome exato configurável, com padrões Ball/Football/SoccerBall. O objeto precisa disponibilizar a estrutura física de BasePart. **O nome e o caminho da bola real do Blue Lock: Rivals ainda não foram observados**, portanto essa identificação não está validada. A implementação não promete marcar uma bola modelada ou renomeada sem um perfil correspondente.

A sobreposição usa a área cliente e coordenadas de tela, ignora cliques e evita ativação de foco. Só desenha com a janela alvo em primeiro plano e snapshot de até 250 ms. Ler falhas elimina o snapshot atual. F9 pausa; F12 solicita encerramento do painel, oculta a sobreposição e encerra o leitor.

## Preservação

Fontes e executável novos estão em `ExternalEsp/`. O build e os testes escrevem somente nessa pasta. Os binários e datasets V2 não foram usados como destinos de saída. O DLL principal original continua com SHA-256:

`E19194F3DE39EEF49AE23118DB11157077F2E3094F1BE4FDD45443074EB34804`.

Os gates das campanhas de percepção continuam independentes. Nenhuma observação de memória foi inserida no controlador visual ou contabilizada como melhora de percepção. A integração posterior no repositório mantém os resultados e as limitações registrados aqui.

## Entrega e reprodução

- [README.md](README.md): comandos de build, execução e configuração.
- [validation.json](validation.json): versão, testes, sonda, recursos, hashes e limitações estruturadas.
- `reports/20261020/tests.json`: verificações do processo controlado.
- `reports/20261020/live-probe.json`: tentativas reais de leitura.
- `reports/20261020/fixture-overlay.png`: desenho observado do fixture.
- `profile/provenance.json`: hash e inconsistências de contagem do dump.

Os próximos requisitos para afirmar funcionamento no jogo são obter uma raiz válida de um cliente com sessão ativa, observar a hierarquia real dos jogadores/bola e comparar o desenho com a imagem da janela. A execução atual ficou bloqueada no primeiro requisito. Essa limitação está registrada e não foi ocultada com dados de fixture.
