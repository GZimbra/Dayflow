# Dayflow

Gerenciador de rotina de trabalho para **Android (tablet primeiro)** em **.NET MAUI 10 / C#**, MVVM, SQLite local.
**100% offline**: sem backend, login, sincronização, analytics ou crash reporting. O APK não declara nenhuma permissão de rede.

## Estrutura

```
src/Dayflow.Core/          net10.0, sem MAUI (testável em qualquer SO)
  Domain/                  Tarefa, Projeto, Rotina, RegistroDiario, DivisorDeTempo, RelogioCivil, AgendaLembretes
  Data/                    BancoDayflow (conexão serializada + transação), Migracoes/ (PRAGMA user_version), Linhas/
  Services/                ServicoTarefas, ServicoCronometro, ServicoRegistros, ServicoRotinas,
                           ServicoRelatorios (todas as agregações em SQL), ServicoLembretes, ExportadorRelatorio
  Infra/LogArquivo.cs      Microsoft.Extensions.Logging -> arquivo local rotativo (exportável pelo usuário)
src/Dayflow.App/           net10.0-android (produção) + net10.0 (compilação de XAML/VM em CI)
  ViewModels/  Views/  Controles/ (gráficos)  Infra/  Platforms/Android/ (manifesto, AlarmManager)
  Resources/Styles/Cores.xaml   tokens de cor (ver "Identidade")
tests/Dayflow.Core.Tests/  xUnit v3, SQLite real em arquivo temporário, relógio/fuso controlados
docs/schema.sql            esquema v1 legível (fonte da verdade: M001_Inicial.cs)
scripts/                   build-android.sh / .ps1, verificar-offline.sh
```

## Modelo de dados

Esquema completo em [`docs/schema.sql`](docs/schema.sql). Resumo:

| Tabela | Observações |
|---|---|
| `Projeto` | `Nome` único (NOCASE), `Cor` `#RRGGBB`, `Ativo` (arquivar). Exclusão só sem tarefas. |
| `Tarefa` | `ProjetoId?`, `RotinaId?`, `DataInicio`/`DataFimPrevista`/`DataConclusao` = dia civil (`DateOnly.DayNumber`), `Status` 0..3, `CriadoEm`/`AtualizadoEm` = ticks UTC. CHECKs garantem `Status final ⇔ DataConclusao`. |
| `RegistroDiario` | UNIQUE `(Data, TarefaId)`: **um registro por dia trabalhado**, a tarefa nunca é duplicada. `SegundosResiduais` (0..59) evita perder sessões curtas. |
| `Rotina` | Modelo recorrente (bitmask de dias). Gera uma `Tarefa` por dia (UNIQUE `(RotinaId, DataInicio)`). |
| `OrdemDia` | Reordenação **por dia** `(Data, TarefaId, Posicao)`. Dia sem ordem herda a do último dia ordenado. |
| `CronometroAtivo` | No máximo 1 linha (`CHECK Id = 1`). Persiste o cronômetro: sobrevive a kill/reboot. |

Índices pedidos: `Tarefa(ProjetoId)`, `Tarefa(DataInicio)`, `RegistroDiario(Data, TarefaId)`. Extras: `Tarefa(DataConclusao)` parcial, `RegistroDiario(TarefaId, Data)`, `OrdemDia(TarefaId, Data)`.

Toda escrita passa por `BancoDayflow.EscreverAsync` → `RunInTransaction`. Rollback total em falha (teste `Escrita_que_falha_faz_rollback_completo`).

## Regras de negócio (decisões tomadas)

As dúvidas levantadas no início foram fechadas com os padrões abaixo. Altere se discordar.

1. **Carry-over**: tarefa avulsa aparece de `DataInicio` até `DataConclusao` (inclusive), ou indefinidamente se aberta. Mesma regra em C# (`Tarefa.VisivelEm`) e SQL (`ConsultasSql.FiltroVisivelNoDia`), com teste de equivalência sobre dados aleatórios.
2. **Cancelada** usa `DataConclusao` como data de encerramento.
3. **Sem `DataFimPrevista`**: continua aparecendo. Com prazo vencido: badge "Atrasada"; o status não muda sozinho.
4. **Fuso**: dia civil = fuso do aparelho no momento do evento, gravado como `DateOnly`. Viagens não recalculam o passado. `TimeProvider` injetado.
5. **Cronômetro**: um ativo por vez (iniciar outro pausa e grava o atual). Ao pausar, o intervalo UTC é **fatiado por dia civil** (`DivisorDeTempo`): 23:30→00:40 = 30 min no dia D + 40 min no D+1. Meia-noite inexistente/ambígua (horário de verão) tratada. Limite de 24 h por tarefa/dia. Relógio voltando no tempo descarta a sessão.
6. **Concluir/cancelar** com cronômetro rodando pausa e grava antes.
7. **Lançamento manual**: soma ao registro do dia; dia dentro da vida da tarefa e não futuro.
8. **Iniciar/concluir tarefa agendada no futuro** puxa `DataInicio` para hoje.
9. **Rotinas**: instância vale só para o próprio dia (não acumula). Criada ao abrir o dia (hoje/passado); dias futuros mostram "rotinas previstas" sem gravar. Instância não pode ser excluída (seria recriada): cancele ou pause a rotina.
10. **Lembretes de hora em hora**: janela configurável (padrão 8h–18h, seg–sex). Conteúdo: atividade em andamento + tempo, ou "N pendentes, M rotinas por fazer". Sem nada pendente, não notifica. Ações: *Iniciar próxima* e *Pausar lembretes hoje*.
11. **Taxa de conclusão** = concluídas ÷ (concluídas + abertas ao fim do período). Canceladas fora do denominador.
12. **Relatórios** classificam pela situação **naquele período** (uma tarefa concluída depois aparece "em aberto" no relatório do dia anterior).
13. **CSV**: `;`, UTF-8 com BOM (Excel pt-BR), RFC 4180, neutralização de fórmula (`=`, `+`, `-`, `@`).
14. **Semana** ISO (segunda a domingo).

## Relatórios e gráficos (SQL)

Todas as agregações estão em `ServicoRelatorios` como constantes SQL (`GROUP BY` no SQLite; C# só mapeia):

| Gráfico | Consulta |
|---|---|
| Horas por projeto (barras e pizza) | `SqlHorasPorProjeto`: `SUM(TempoGastoMin) … GROUP BY t.ProjetoId` |
| Concluídas por dia (linha) | `SqlConcluidasPorDia`: CTE recursivo de dias `LEFT JOIN Tarefa … GROUP BY dia` (dias zerados incluídos) |
| Tempo por tarefa de vários dias (empilhada) | `SqlTempoTarefasMultiDia`: `GROUP BY TarefaId, Data` para tarefas com `COUNT(DISTINCT Data) >= 2` (top 10) |
| Taxa de conclusão por projeto | `SqlTaxaConclusao`: `SUM(CASE …)` por projeto |
| Marcação do calendário | `SqlCalendario`: pendentes ao fim de cada dia do mês |

Microcharts (barras, donut, linha) e um `SKCanvasView` próprio para a barra empilhada, que o Microcharts não tem. Tudo renderizado localmente com SkiaSharp.

## Relatório de conclusão e economia gerada (v1.1)

- Ao concluir uma tarefa **de projeto**, o app pergunta se quer registrar o relatório (também em "•••" → Relatório de conclusão).
- Campos: o que foi feito, quem se beneficia, **minutos poupados × pessoas × frequência** (única vez, dia útil, semana, mês). Nada é estimado pelo app.
- Estimativa mensal com fatores fixos e declarados: dia útil × 21, semana × 52/12, mês × 1. Única vez entra como "pontual".
- Agregação em SQL (`ServicoConclusoes.SqlEconomiaPorProjeto`, `GROUP BY t.ProjetoId`). Reabrir a tarefa tira o relatório da soma (sem apagar); excluir a tarefa apaga em cascata.
- Relatórios → "Economia gerada" (texto/CSV). Gráficos → tabela "Economia por projeto" com min/vez, pessoas, frequência, por ocorrência, por mês, subtotal e total geral.
- Pessoas no total = soma por tarefa (a mesma pessoa pode contar em mais de uma melhoria).

## Exportação dos gráficos (PNG)

Gráficos → **Exportar PNG**: todos numa imagem ou um por vez (inclui a tabela de economia). Renderização SkiaSharp em CPU, tema claro fixo, arquivo no cache privado e envio só pelo menu de compartilhamento do Android.

## Identidade visual

- **Paleta**: preto `#111111`, laranja `#F26A1B`, branco `#FFFFFF`. ⚠ Hex **provisórios** até o guia oficial; troque só os três `Base*` em `Resources/Styles/Cores.xaml`.
- **Tipografia**: Manrope (texto) + IBM Plex Mono (cronômetro), ambas OFL e **embutidas no APK**.
- **Logo autoral**: sol laranja nascendo no horizonte + traço que sobe e termina em check (`Resources/AppIcon/appiconfg.svg`).
- **Tablet**: trilho de navegação fixo ≥ 840 dp; Agenda em 2 painéis (calendário | dia) ≥ 900 dp; em retrato/celular os painéis empilham.

## Build Android (API mínima 26)

Pré-requisitos: .NET SDK 10, `dotnet workload install maui-android`, Android SDK (platform 36 + build-tools) e JDK 21. O Visual Studio 2022/2026 com a carga MAUI instala tudo.

```bash
# Testes do núcleo (qualquer SO)
dotnet test tests/Dayflow.Core.Tests

# APK Release otimizado (arm64, trim completo, R8, AOT por perfil, IL removido após AOT), verificação offline no final
# x64 p/ emulador: -p:DayflowAbis="android-arm64%3Bandroid-x64" · AAB: -p:DayflowFormatos=aab
scripts/build-android.sh            # Linux/macOS
pwsh scripts/build-android.ps1      # Windows

# Assinatura com sua chave (recomendado para distribuir)
keytool -genkeypair -v -keystore dayflow.keystore -alias dayflow -keyalg RSA -keysize 4096 -validity 10000
DAYFLOW_KEYSTORE=$PWD/dayflow.keystore DAYFLOW_KEY_ALIAS=dayflow DAYFLOW_KEY_PASS=*** scripts/build-android.sh
```

Saída: `src/Dayflow.App/bin/Release/net10.0-android/publish/com.dayflow.app-Signed.apk` (~20 MB; o 1.0.0 tinha 38,5 MB).
`minSdkVersion=26`, `targetSdkVersion=36`.

## Checklist de verificação offline

`scripts/verificar-offline.sh <apk>` automatiza os itens e falha o build se algum quebrar.

- [x] `AndroidManifest.xml` (Release) **não declara** `INTERNET`. Também remove via `tools:node="remove"` `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_*`, `AD_ID`, `c2dm`, `SCHEDULE_EXACT_ALARM`, `USE_EXACT_ALARM`, inclusive se vierem de bibliotecas (merger `manifestmerger.jar`).
- [x] APK final (`aapt2 dump permissions`) contém apenas:
  `POST_NOTIFICATIONS`, `RECEIVE_BOOT_COMPLETED` e `com.dayflow.app.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` (assinatura própria do AndroidX, sem rede).
- [x] `android:allowBackup="false"` + `fullBackupContent` excluindo tudo (API ≤ 30) + `dataExtractionRules` excluindo tudo em `cloud-backup` **e** `device-transfer` (API 31+ ignora `allowBackup` para transferência entre aparelhos).
- [x] `usesCleartextTraffic="false"`.
- [x] Sem Firebase, App Center, Sentry, Play Services, OkHttp ou analytics no pacote.
- [x] Dependências: MAUI, CommunityToolkit.Mvvm, sqlite-net-pcl (SQLite nativo empacotado), SkiaSharp, Microcharts. Nenhuma faz I/O de rede.
- [x] Fontes e ícones embutidos; nada é baixado em runtime.
- [x] Dados só saem por ação explícita: Relatórios → Texto/CSV, Economia → Texto/CSV, Gráficos → PNG e Ajustes → Exportar log, ambos pelo menu de compartilhamento do Android (`ShareFileRequest`).
- [x] Logs sem dados pessoais: só IDs, contagens e códigos; exceções gravam tipo + HResult + stack (sem `Message`).
- [ ] **Manual, no aparelho**: Configurações → Apps → Dayflow → Permissões não lista "Rede". Em modo avião, todas as telas funcionam.

## Testes

`dotnet test`: **60 testes** (inclui relatório de conclusão: elegibilidade, validação, upsert, fatores mensais no SQL, cascade e exportação) cobrindo tarefas multi-dia, rotinas, cronômetro (meia-noite, vários dias, DST de São Paulo 2018/2019, fuso +14), agregação de relatórios/gráficos, CSV, lembretes (janela, fim de semana, DST), esquema/CHECK/rollback e plano de consulta usando índice.

## Limitações conhecidas

- **Correção 1.1.2 (app não abria):** os APKs 1.0.0–1.1.1 foram gerados aqui com um `android.jar` substituto sem as classes `java.*`. O R8 (com `-ignorewarnings`) não reconheceu `java.lang.Runnable` etc. como biblioteca e removeu `run()` de 167 classes do AndroidX → `AbstractMethodError` antes do .NET iniciar. A 1.1.2 usa `android.jar` + `java.base` e foi conferida por inspeção do dex (0 implementações faltando). Com o Android SDK oficial esse problema não ocorre.

- Não houve teste em aparelho/emulador neste ambiente. O APK de teste incluído foi gerado contra um android.jar substituto (Robolectric `android-all` 16) e assinado com chave de debug. Para distribuir, gere de novo com `scripts/build-android.*` usando o Android SDK oficial.
- Lembretes usam alarme inexato: atraso de alguns minutos é esperado.
- O ícone de notificação usa o traço do relógio (monocromático, como o Android exige).

## Novidades 1.3.0

- **Número da semana (ISO 8601)** em coluna "SEM" no calendário; tocar no número seleciona a segunda-feira. O cabeçalho do dia mostra "Semana N".
- **Dia hora a hora:** alternância *Lista / Por hora* no painel do dia. Toque numa hora para planejar uma tarefa (início em :00/:15/:30/:45, duração padrão 60 min). O editor da tarefa também permite definir horário e duração. Persistido em `HorarioDia` (migração M003); exclusão da tarefa remove o horário em cascata.
- **Relatórios redesenhados:** KPIs (tempo registrado, média/dia, concluídas, em aberto, taxa de conclusão), barras por projeto, atividades com filtro por situação e selo de status, tabela de economia por projeto.
- **Exportação visual:** botão *Exportar* → Relatório em PDF (A4, 1 página), Imagem PNG, Texto ou CSV. Gerado localmente com SkiaSharp e enviado só pela folha de compartilhamento do Android.
- Taxa de conclusão = concluídas ÷ (concluídas + em aberto); canceladas ficam fora da conta.

## 1.3.1 — em pé e deitado

- Margens menores em celular (16 dp em vez de 28) em todas as telas; editores com cartão mais justo.
- Agenda com altura curta (celular deitado): título do dia, botão "+ Nova" e cronômetro encolhem para a lista do dia continuar visível.
- Relatórios: tabela de economia rola na horizontal quando a tela é estreita, em vez de esmagar a coluna do projeto.
- Seletor de período cabe em celular em pé.

## 1.3.2 — desempenho

- Telas fora de vista não recarregam mais a cada alteração: ficam marcadas e recarregam uma vez ao voltar. Trocar de aba sem mudança de dados não refaz consultas nem gráficos.
- Rajadas de alterações (ex.: concluir = pausar cronômetro + salvar) viram uma única recarga.
- Listas trocadas em lote (uma notificação em vez de limpar + N inserções); a Agenda não recria os cartões quando nada mudou no dia.
- Grade hora a hora montada só quando visível.
- Relatórios: lista de atividades em blocos de 30 ("Mostrar mais") e no máximo 20 detalhes de economia na tela (o export traz tudo).
- Calendário: consulta pré-filtra as tarefas do mês (≈9× mais rápida com 1 ano de histórico).
- Log local gravado em segundo plano.

## 1.4.0 — celular (referência: POCO X7 Pro)

Layout responsivo por classe de tela, sem caminho específico de aparelho. Referência de teste: POCO X7 Pro, 6,67", 1220×2712, densidade 2,75 ≈ **443×986 dp** em pé e **986×443 dp** deitado.

- **Classes de tela** (`Infra/Tela.cs`): largura compacta < 600 dp, altura baixa < 520 dp, celular = menor lado < 600 dp. Fontes de título e alturas de gráfico viraram `DynamicResource` e diminuem no celular (Display 34→28, Título 22→20, gráficos 300→240 dp, cartões 20→16 dp de padding).
- **Navegação**: tablet mantém o trilho fixo de 232 dp. Celular deitado ganha trilho fixo **só com ícones (80 dp)** e sem barra de título (antes: trilho de 232 dp numa tela de ~986 dp ou barra de 56 dp numa altura de ~443 dp). Celular em pé continua com menu lateral.
- **Agenda em pé (largura < 600 dp)**: calendário abre **recolhido na semana do dia** (setas andam por semana; "Ver mês" expande e a escolha fica salva). Células do calendário 54→46 dp (42 dp deitado). Título do dia 26 sp, botão "+ Nova", cartão do cronômetro sem ícone para o nome da tarefa caber.
- **Projetos / Rotinas**: colunas por largura (1 em pé, 2 médio, 3 tablet); cabeçalho de Projetos quebra em duas linhas no estreito; dias da rotina quebram linha.
- **Ajustes**: coluna dos seletores 240→170 dp no estreito.
- **Safe area (Android 15+ edge-to-edge)**: páginas com `ScrollView` raiz declaram `SafeAreaEdges="Container"` (barra de status, barra de gestos e furo da câmera). As de `Grid` raiz já usam o padrão `Container` do MAUI 10.
- **Assinatura nova**: a chave de debug usada até a 1.3.2 não existe mais. A 1.4.0 usa uma keystore própria (`dayflow/assinatura/` nos arquivos do projeto). Aparelho com 1.3.x instalada precisa desinstalar antes (perde os dados locais, já que o backup está desligado).

## 1.4.1 — tudo visível em pé no celular

Revisão do retrato em ~443 dp (POCO X7 Pro em pé), com foco em não cortar informação:

- **Sem rolagem lateral para dados principais**: as tabelas de economia (Relatórios e Gráficos) viram **lista em cartões** quando a largura é < 600 dp (recursos globais `TelaEstreita`/`TelaLarga`, atualizados pelo `AppShell` ao girar). Em tela larga a tabela continua igual.
- **Texto quebra linha em vez de cortar com "…"**: título das tarefas (lista e visão por hora), nome da tarefa no cronômetro (até 3 linhas), nomes de projeto e rotina, atividades e detalhes do relatório, resumo do relatório de conclusão, itens da tabela de economia, resumo "sem horário".
- **Gráficos**: tamanho do texto em dp (12 dp; antes 26 px fixos ≈ 9 dp em densidade 2,75). Barras com pouco espaço por item giram os rótulos na vertical para não sobrepor. Pizza distribui rótulos dos dois lados no estreito. "Tarefas de vários dias": nome da tarefa inteiro numa linha acima da barra (antes invadia a barra) e legenda em linha própria.
