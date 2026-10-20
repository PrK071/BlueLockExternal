# BlueLock External ESP

ESP externo em C#/.NET Framework 4.8 x64. Desenha jogadores, vida, distância e candidatos a bola sobre a janela do Roblox, usando a tabela fornecida para `version-9ac8595712b447a7`.

O leitor usa `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION)` e `ReadProcessMemory`, conferindo a quantidade efetivamente lida. As leituras são locais. Não há injeção, driver, alteração de memória, tráfego de jogo, ativação de privilégios ou geração de entradas. O executável e a infraestrutura V2 continuam separados.

## Compilar e executar

Na raiz do repositório:

```powershell
& .\ExternalEsp\build.ps1
& .\ExternalEsp\bin\BlueLockEsp.exe
```

O painel possui controles de jogadores, bola e distância. **F9 pausa/retoma; F12 encerra**. A sobreposição ignora cliques, acompanha a área cliente e aparece somente com a janela alvo em primeiro plano. Snapshots com mais de 250 ms ou leitura inválida são descartados.

Para escolher entre vários clientes ou configurar nomes exatos de partes da bola:

```powershell
& .\ExternalEsp\bin\BlueLockEsp.exe --pid 7504 --ball-name Ball --ball-name Football
```

PID muda entre execuções; omita `--pid` quando existir apenas um cliente. Nomes padrão: `Ball`, `Football`, `SoccerBall`, comparados sem diferenciar maiúsculas. Objetos com esses nomes precisam expor a estrutura física de uma BasePart. O caminho do candidato faz parte do snapshot interno. A hierarquia real da bola no Blue Lock: Rivals ainda não foi observada/confirmada nesta execução; não se afirma identidade apenas pelo nome.

## Estado verificado em 20/10/2026

- Build do processo encontrado corresponde à versão da tabela.
- Leitura de cabeçalho do módulo funcionou; os ponteiros globais FakeDataModel e VisualEngine retornaram zero no teste inicial.
- Sonda do executável: **0 frames válidos / 27 tentativas**, janela principal retornada pelo Windows = 0.
- **20 verificações aprovadas** em processo filho controlado: leitura nativa entre processos, árvore de instâncias, exclusão do jogador local, strings UTF-8, vida, posições, distâncias, projeção, filtros, renderização e rejeição/recuperação de dados inválidos.
- Esses resultados comprovam o transporte e a implementação no fixture. **Não comprovam ESP funcionando em uma partida real**, compatibilidade de todas as estruturas com Roblox ou identificação da bola do Blue Lock: Rivals.
- A razão de o processo real ter retornado ponteiros nulos não foi estabelecida. Não se concluiu que a versão está errada, que existe bloqueio de leitura ou que a partida estava carregada.

Consulte [REPORT.md](REPORT.md) e [validation.json](validation.json). Os registros detalhados estão em `reports/20261020/`.

A integração Git também foi compilada a partir de uma exportação isolada dos arquivos preparados para commit; os mesmos 20 testes passaram. O registro está em [integration-validation.json](integration-validation.json).

## Diagnóstico reproduzível

```powershell
# Usa um processo filho criado pelo próprio executável; não abre o Roblox.
& .\ExternalEsp\bin\BlueLockEsp.exe --self-test --out .\ExternalEsp\reports\new-tests\tests.json

# Apenas leituras, duração limitada a 10 s; não desenha nem envia entradas.
& .\ExternalEsp\bin\BlueLockEsp.exe --probe --seconds 3 --out .\ExternalEsp\reports\new-probe\probe.json
```

A sonda retorna código 0 quando pelo menos um snapshot foi válido, 3 quando os dados não ficaram disponíveis e 2 em erro de argumentos/inicialização. Contagens de frames válidos são validação estrutural de leitura, não validação visual independente da cena. F12 físico, alinhamento da sobreposição com uma janela real e comparação das posições com pixels do jogo **não foram exercitados** nesta execução.

## Offsets e proveniência

`Offsets.cs` contém somente constantes geradas a partir do HPP anexado, cuja cópia e hash estão em `profile/`. O cabeçalho declara 392 offsets; o parser encontrou **393 constantes**, das quais **18 são zero**. Campos não utilizados pelo ESP não são validados. Zero pode ser deslocamento legítimo ou entrada não resolvida; não se atribuiu significado sem evidência.

O leitor exige a versão da pasta do executável e valida raízes, strings, vetores, posições e matriz. A igualdade da versão é necessária neste perfil e não comprova que o dump esteja correto. Não há seleção automática de outro offset, fallback para memória indiscriminada ou interpretação de ponteiro nulo como posição.

Para importar outra tabela fornecida e revisada:

```powershell
& .\ExternalEsp\ImportOffsets.ps1 -SourceFile 'C:\caminho\offsets.hpp'
& .\ExternalEsp\build.ps1
```

O importador aceita identificadores e números hexadecimais e gera o código de constantes; não compila código obtido diretamente da internet. O build é local e não baixa pacotes.

Os vetores de filhos e a cadeia de nome foram conferidos no código publicado do dumper: [roblox.cpp](https://git.imtheo.lol/theo/RbxDumperV2/src/branch/main/RbxDumperV2/classes/roblox.cpp). A disposição de strings foi comparada com [mem.hpp](https://git.imtheo.lol/theo/RbxDumperV2/src/branch/main/RbxDumperV2/lib/mem.hpp). O transporte implementa a API documentada pela Microsoft: [ReadProcessMemory](https://learn.microsoft.com/windows/win32/api/memoryapi/nf-memoryapi-readprocessmemory). O código do dumper foi lido como referência, sem executar seu binário ou copiar seus mecanismos de escrita.

## Limites

Busca de candidatos a bola: profundidade até 6 níveis da árvore, 3.000 nós por varredura, atualização de descoberta a cada segundo. Até 100 entradas de jogadores e 50 candidatos a bola por snapshot, com orçamento de 75 ms. Exceder limites produz indisponibilidade, sem completar silenciosamente uma leitura parcial. Nomes alterados, streaming, respawn e layout diferente podem impedir a descoberta. Falhas são registradas e removem as marcações antigas.

A implementação inicial fornece ESP. Não inclui aimbot, aumento de velocidade, chutes automáticos ou integração com o controlador visual. As campanhas V2.3/V2.4 e seus gates permanecem independentes deste executável.
