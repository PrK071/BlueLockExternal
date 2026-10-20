# BlueLockExternal

Ferramentas locais para controlar sequências de entrada no Windows e pesquisar navegação visual em um laboratório isolado no Roblox Studio. O projeto contém o aplicativo original, a implementação V2 em C# com OpenCV e a infraestrutura de campanhas do Studio.

## Componentes

- `BlueLockExternal.cs`: aplicativo Windows Forms original, com atalhos globais, pausa por perda de foco e parada de emergência.
- `ExternalEsp/`: ESP externo em C# com build independente, diagnóstico limitado e testes em processo controlado.
- `V2/`: captura da janela, percepção OpenCV, tracking, previsão, decisão, entrada nativa e testes.
- `V2/Diagnostics/VisualE2E/`: detector e controlador visual de laboratório, preparação de cenários, relay MCP, análise e execução de campanhas limitadas.
- `V2/StudioLab/`: scripts do laboratório controlado Roblox Studio.
- `video-analysis/`: análise local de gravações e extração de quadros.
- `V2/reports/visual-e2e-20261018/`: relatório e métricas resumidas da campanha visual de 50 casos. Capturas e logs por quadro permanecem como artefatos locais.

## Compilação V2

No Windows com PowerShell e .NET Framework 4.8:

```powershell
& .\V2\build.ps1
```

O script baixa pacotes NuGet fixados em `V2/dependencies.lock.json` e confere seus hashes SHA-256. As dependências e os binários gerados ficam fora do controle de versão.

## Campanha visual

Leia [`V2/Diagnostics/VisualE2E/README.md`](V2/Diagnostics/VisualE2E/README.md) para executar sondas, campanhas limitadas e a análise das evidências. Os resultados resumidos estão em [`RELATORIO.md`](V2/reports/visual-e2e-20261018/RELATORIO.md) e [`campaign50-metrics.json`](V2/reports/visual-e2e-20261018/campaign50-metrics.json).

A campanha física de 50 casos registrou 38 chegadas observadas e 4 ciclos visuais completos verificáveis; a qualidade não atingiu o limite definido para habilitar alterações autônomas do código. Esses números descrevem o laboratório e a execução registrados, sem alegar equivalência à física ou às mecânicas do Blue Lock: Rivals.

## Testes

Os testes C# são compilados pelo script de build. O relatório de implementação registra 48 testes gerais, 105 testes de navegação e 40 testes de controle da bola aprovados na execução isolada descrita ali. Eles não substituem a validação física no Studio.

## Arquivos locais

Capturas, executáveis, pacotes baixados, checkpoints e logs volumosos de campanhas são ignorados pelo Git. O relatório resumido, CSV por tentativa e JSON agregado ficam versionados para comparação entre campanhas.

## ESP externo

[`ExternalEsp/`](ExternalEsp/README.md) contém um executável separado com leitor de memória, ESP de jogadores/candidatos a bola, filtro de distância, F9 e F12. O transporte e o desenho passaram por 20 verificações em processo controlado. A sonda no cliente real retornou ponteiro inicial nulo; o funcionamento em partida ainda não foi demonstrado. Consulte o [relatório](ExternalEsp/REPORT.md).

```powershell
& .\ExternalEsp\build.ps1
& .\ExternalEsp\bin\BlueLockEsp.exe
```
