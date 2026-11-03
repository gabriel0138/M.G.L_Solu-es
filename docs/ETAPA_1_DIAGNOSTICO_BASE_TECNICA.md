# M.G.L Connect — Etapa 1: Diagnóstico reproduzível e validação da base técnica

**Data:** 27/10/2026
**Escopo:** diagnóstico somente leitura da aplicação; SDK .NET 10 instalado em diretório isolado após checksum; restore/build/test; inspeção do caminho SQL Server.
**Resultado geral atual:** baseline .NET **aprovada** (restore/build e 75 testes aprovados); SQL Server **foi executado e validado parcialmente** em container Microsoft versionado. A Fase 0 permanece **PARCIALMENTE CONCLUÍDA**: o caminho de esquema vazio passou, mas a atualização legada cria tabelas operacionais sem FKs e o backfill alterou o estado de uma entrega não atribuída. Nada foi corrigido nesta fase.

> **Atualizações cronológicas:** a primeira tentativa .NET foi bloqueada por SDK 6.0.400; depois o SDK 10 local liberou restore/build/test. O primeiro diagnóstico de SQL Server identificou socket sem permissão; após autorização explícita, a imagem e o container de teste foram executados. As seções 8.8 e 14 registram os resultados atuais e prevalecem sobre os bloqueios históricos.

## 1. Resumo executivo

- A solução e os dois projetos apontam para `net10.0`. O SDK 10.0.403/runtime 10.0.14 foi instalado isoladamente em `$HOME/.local/share/mglconnect-dotnet10`; o SDK global 6.0.400 não foi removido nem alterado.
- Com o SDK local: restore concluído, build concluído com 0 warnings/0 errors, 75 testes aprovados, 0 falhos e 0 ignorados. A suíte inclui InMemory e testes SQLite, mas não valida SQL Server.
- O provedor SQL Server está configurado e foi executado via Docker em SQL Server 2022 CU27 GDR1. O schema vazio criado pelo EF tem 6 FKs e índices filtrados; o atualizador repetido em schema legado criou suas tabelas/índices sem FKs. O teste de inicialização real também reproduziu o backfill que ativa entrega pendente sem atribuição.
- O startup chama `EnsureCreatedAsync()` e depois atualizadores SQL próprios. Não há migrations EF no inventário. Isso merece validação com SQL Server: `EnsureCreated` não inicializa o modelo se já houver tabelas e não é uma estratégia intercambiável com migrations [3].
- O atualizador tem uma transformação que define `AtribuicaoAtiva = 1` para toda entrega em `Pendente`/`Em transporte` com valor `0`, sem verificar motorista/veículo. O efeito foi reproduzido no SQL Server real: uma entrega pendente sem atribuição foi marcada ativa após startup.
- A capacidade offline não está conectada ponta a ponta: há módulo IndexedDB, Service Worker e métodos de fila, mas não encontrei consumidores para `addGPS`/`addPODSignature`, o tracker usa uma fila em memória nunca preenchida e o fluxo POD envia diretamente ao servidor.

## 2. Escopo, método e limitações

Inspecionei os arquivos versionados da solução, projetos, configuração de runtime/provedores, `Program.cs`, `DbInitializer`, `DatabaseSchemaUpdater`, `MglDbContext`, script SQL acadêmico, testes existentes e os componentes web de GPS/POD/offline. Não li valores de segredos: o inventário não revelou configuração de conexão SQL Server e os nomes de variáveis ambientais foram consultados sem imprimir seus valores.

Instalei somente o SDK/runtime .NET 10 em diretório local dedicado, autorizado pela Fase 0, após validar o arquivo oficial por SHA-512. Após autorização explícita do usuário, usei `sudo docker` pontualmente; não alterei grupos, daemon, permissões do socket, instalação global, PATH, perfis de shell ou configuração do sistema. O container foi limitado, local e sem volume. Não substituí SQL Server por SQLite/InMemory: os ensaios SQL Server da seção 14 foram executados no container real. Nenhum dado real foi usado.

O relatório trata o estado do código como evidência de implementação, não como prova de funcionamento. Para comportamentos que requerem execução real, o estado é explicitamente pendente.

## 3. Estado do Git

### Antes da auditoria

Este bloco registra a primeira execução da Etapa 1, antes da criação inicial do relatório. Na execução complementar SQL Server, o relatório já existia como arquivo não rastreado; o estado dessa execução está registrado logo abaixo.

Saída de `git status --short --untracked-files=all`:

```text
MD README.md
AD render.yaml
```

Interpretação dos estados em duas colunas:

- `README.md`: modificação preparada no índice; remoção não preparada na cópia de trabalho.
- `render.yaml`: adição preparada no índice; remoção não preparada na cópia de trabalho.

`git diff` mostrou a remoção de ambos na árvore de trabalho. `git diff --cached` mostrou a versão modificada do README (adição de instruções Render/Neon) e a adição de `render.yaml`. Não restaurei nem alterei esses arquivos.

- Branch: `main`.
- Último commit: `d8a54d3 Initial M.G.L Connect backup`.
- Na primeira execução da Etapa 1, a pasta `docs/` e o relatório solicitado não existiam antes da criação.

### Antes do complemento SQL Server

Estado registrado antes da auditoria adicional:

```text
MD README.md
AD render.yaml
?? docs/ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md
```

O relatório era trabalho já existente e foi atualizado, não substituído. README e render mantiveram exatamente seus estados index/worktree.

### Depois da auditoria

O estado final foi verificado após os comandos de .NET e a atualização deste relatório. As duas alterações preexistentes continuam intactas; o único arquivo do repositório criado/atualizado nesta etapa é este relatório. A instalação do SDK está fora do repositório, em `$HOME/.local/share/mglconnect-dotnet10`:

```text
MD README.md
AD render.yaml
?? docs/ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md
```

Não alterei o staging area, branch ou histórico.

## 4. Inventário técnico

| Área | Evidência no repositório | Observação |
|---|---|---|
| Solução | `MglConnect.sln` | Inclui `MglConnect.Web` e `MglConnect.Tests`. |
| Web | `src/MglConnect.Web/MglConnect.Web.csproj` | ASP.NET Core MVC/Razor, `net10.0`, nullable e implicit usings. |
| Testes | `src/MglConnect.Tests/MglConnect.Tests.csproj` | `net10.0`, xUnit 2.9.3, Microsoft.NET.Test.Sdk 18.11.0, Moq, FluentAssertions e EF Core InMemory. |
| EF Core | referências versão 10.0.14 no Web | SQLite, SQL Server e Npgsql 10.0.3 estão referenciados. |
| Entrada | `src/MglConnect.Web/Program.cs` | Seleção de provedor, Data Protection, DI, autenticação, MVC, Swagger e SignalR. |
| Inicialização | `src/MglConnect.Web/Data/DbInitializer.cs` | `EnsureCreatedAsync`, três atualizadores aditivos e seed condicional de demonstração. |
| Atualizador | `src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs` | SQL específico para SQLite, PostgreSQL e SQL Server. |
| Modelo/contexto | `src/MglConnect.Web/Data/MglDbContext.cs` e `Models/` | Filtros tenant, índices únicos/filtrados, concorrência e relações EF. |
| SQL acadêmico | `sql/banco_pim_sqlserver.sql` | DDL, stored procedure e trigger do PIM; não é chamado pelo startup da aplicação. |
| Offline | `wwwroot/js/pwa-offline.js`, `wwwroot/sw.js`, `wwwroot/js/tracker.js` | Componentes existem, mas não formam um fluxo de persistência e sincronização funcional. |
| Documentação | Markdown da raiz, README no índice Git, PDF e manuais DOCX | Há documentos históricos que afirmam recursos além do que o código comprova. |
| Configuração adicional | `.dockerignore`, `.gitignore`, `docker-compose.yml`, `Dockerfile`, `.vscode/settings.json` | Sem `global.json`, `Directory.Build.props`, `Directory.Packages.props` ou `NuGet.config` encontrados. |

Os projetos e o Dockerfile direcionam para .NET 10. O `docker-compose.yml` usa SQLite persistido em volume, não SQL Server. O script `sql/banco_pim_sqlserver.sql` usa convenções acadêmicas como `ID_Pedido`/`ID_Entrega`; a aplicação EF utiliza entidades e mapeamento próprios. Não há evidência de que esse script seja o esquema da aplicação.

## 5. Ambiente e pré-requisitos

Comandos executados: `dotnet --info`, `dotnet --list-sdks`, `dotnet --list-runtimes`.

```text
SDK global preexistente: 6.0.400 (inalterado)
Runtime global:          .NETCore.App 6.0.8; AspNetCore.App 6.0.8 (inalterados)
SDK local isolado:       10.0.403
Runtime local isolado:   .NETCore.App 10.0.14; AspNetCore.App 10.0.14
TargetFramework Web:     net10.0
TargetFramework Test:    net10.0
global.json:             ausente
```

O tarball oficial `dotnet-sdk-10.0.403-linux-x64.tar.gz` foi baixado de `builds.dotnet.microsoft.com`. SHA-512 verificado contra `10.0.14-sha.txt`: `b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5` — resultado `OK`. Diretório temporário do download: `[artefato temporário local omitido]`; permaneceu fora do repositório porque a remoção automática foi recusada pelo controle de comandos. O SDK fica disponível chamando seu executável pelo caminho completo ou definindo `DOTNET_ROOT` somente no processo.

A documentação oficial explica que cada SDK suporta um conjunto limitado de TFMs; para construir `net10.0`, é necessário SDK que suporte esse TFM [1]. O SDK 6 local explica diretamente o `NETSDK1045`; o runtime 6 explica a falha ao iniciar o testhost .NET 10.

Configuração do provedor observada em `Program.cs`:

- `POSTGRES_URL`, `DATABASE_URL` ou `POSTGRES_PRISMA_URL` selecionam PostgreSQL; URLs PostgreSQL passam por `PostgresConnectionStringParser` com SSL requerido.
- Sem URL PostgreSQL, `DatabaseProvider=SqlServer` chama `UseSqlServer(connectionString)`.
- `ConnectionStrings:DefaultConnection` vem da configuração; se ausente, o fallback literal do programa é `Data Source=mglconnect.db`, que é uma connection string SQLite e não serve para SQL Server. Para a decisão de banco de referência, SQL Server precisa de connection string própria provisionada fora do repositório.
- Sem seleção explícita, o provedor padrão é SQLite.
- Docker Compose força `DatabaseProvider=Sqlite` e persiste arquivo/chaves em volume.
- No caminho SQL Server, Data Protection não é automaticamente persistido no SQL Server: o código persiste no filesystem se `DataProtection:KeysPath` estiver definido e no DbContext apenas quando detecta PostgreSQL. É necessário definir estratégia persistente antes de produção com reinícios/múltiplas instâncias.

O provedor EF Core SQL Server está referenciado e a configuração `UseSqlServer` segue o caminho de configuração recomendado pelo provedor [2]. Isso prova que a integração está prevista, não que conectividade, DDL ou migrations estejam validados.

## 6. Comandos e resultados

Executados na raiz do repositório. Códigos de saída registrados pelo executor:

| Comando — primeira tentativa usando SDK global | Exit code | Resultado observado | Classificação |
|---|---:|---|---|
| `dotnet restore MglConnect.sln --verbosity minimal` | 1 | `NETSDK1045` nos projetos Web e Tests: SDK 6 não suporta `net10.0`; o restore não chegou à resolução de pacotes. | Bloqueio de SDK; **restore não validado**. |
| `dotnet build MglConnect.sln --no-restore --verbosity minimal` | 1 | `NETSDK1045` em ambos os projetos; 0 warnings, 2 errors de avaliação do target framework. | Bloqueio de SDK; **compilação não validada**. |
| `dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal` | 1 | Encontrou assembly em `bin/Debug/net10.0`, mas o testhost abortou porque `Microsoft.NETCore.App 10.0.0` não está instalado. Nenhum caso de teste rodou. | Bloqueio de runtime; **0 aprovados / 0 falhos executados / 75 casos não executados**. |

Os 75 casos foram contados por atributos `[Fact]`/`[Theory]` nos arquivos atuais. Não são resultados de execução. A CLI oficial documenta `dotnet test` como comando que compila e executa a suíte; `--no-build` evita build e ainda precisa do runtime correspondente para iniciar o testhost [6].

Baseline reproduzível depois de disponibilizar SDK/runtime 10:

```bash
dotnet --info
dotnet restore MglConnect.sln
dotnet build MglConnect.sln --no-restore
dotnet test MglConnect.sln --no-build --no-restore
```

### Reexecução com SDK local .NET 10.0.403

Comandos executados sem alterar arquivos-fonte/configuração:

```bash
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet restore MglConnect.sln --verbosity minimal
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet build MglConnect.sln --no-restore --verbosity minimal
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal
```

| Comando | Exit code | Resultado | Classificação |
|---|---:|---|---|
| `dotnet restore MglConnect.sln --verbosity minimal` | 0 | Web e Tests restaurados; conclusão reportada em 838 ms por projeto. | **Passou**. |
| `dotnet build MglConnect.sln --no-restore --verbosity minimal` | 0 | Build succeeded; 0 warnings, 0 errors; 3.65 s. | **Passou**. |
| `dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal` | 0 | Failed 0, Passed 75, Skipped 0, Total 75; duração ~1 s. | **Passou** para provedores/casos presentes na suíte; SQL Server não coberto. |

A primeira tentativa e a reexecução estão preservadas no registro para demonstrar causa e resolução do bloqueio ambiental. A CLI `dotnet test` realmente executa o testhost; agora o runtime 10 está disponível e a contagem acima é saída real, não inferida [6].

## 7. Auditoria dos testes existentes

Os testes foram abertos por grupo e os cenários foram conferidos; a execução ficou bloqueada pelo runtime. A cobertura é majoritariamente EF InMemory e mocks; o atualizador tem testes SQLite.

| Área | Casos encontrados | Limite da evidência |
|---|---|---|
| Cliente/pedido | cultura pt-BR no ViewModel, associação ao cliente autenticado, cliente inativo, intermediador | Não testa browser/renderização real nem SQL Server. |
| Despacho | pedido duplicado, motorista/veículo inativos, capacidade, atribuições ocupadas, despacho válido | Cenários de concorrência são principalmente verificação de regra; não comprova bloqueios multi-processo SQL Server. |
| Turno/alerta | hash, expiração, cancelamento, limite de tentativas, início concorrente, idempotência de emergência, localização stale, falha de persistência | Parte substancial em SQLite/InMemory/mock; não SQL Server. |
| Autorização/tenant | isolamento tenant, dono do recurso, turno ativo, acesso ao hub, Swagger administrativo | Não constitui matriz exaustiva de todos os endpoints e políticas. |
| Estado/POD | impedir conclusão fora do POD, conclusão, comprovante já existente, transições de pedido | Não há browser offline, PDF visual/regressão, nem banco SQL Server. |
| Engine de rota/telemetria | precisão GPS, três frames, salto implausível, rota genérica, Photon mockado e cache | Testes unitários não validam fornecedor real, cobertura geográfica nem estado entre processos. |
| Atualização de esquema | três testes idempotentes: colunas/pedidos, data nascimento, tabelas operacionais | Todos usam SQLite e não medem preservação semântica para entrega não atribuída. |
| PostgreSQL | parser de URL e SSL requerido | Não existe teste de conexão/migração/consulta contra servidor PostgreSQL. |
| SQL Server | nenhum teste de integração localizado | DDL e comportamento de transações permanecem sem validação real. |

Arquivos relevantes: `ClientePortalOrderTests.cs`, `DispatchValidationTests.cs`, `DriverExperienceTests.cs`, `OperationalSecurityTests.cs`, `TenantIsolationTests.cs`, `TelemetryHubAuthorizationTests.cs`, `DatabaseSchemaUpdaterTests.cs`, `PostgresConnectionStringParserTests.cs` e `DriverRoutingTests.cs`, todos em `src/MglConnect.Tests/`.

## 8. Banco de dados e inicialização

### 8.1 Caminho efetivo

No startup, [Program.cs](../src/MglConnect.Web/Program.cs) cria um scope e chama `DbInitializer.InitializeAsync`. O inicializador [DbInitializer.cs](../src/MglConnect.Web/Data/DbInitializer.cs) executa nesta ordem:

1. `Database.EnsureCreatedAsync()`;
2. `EnsurePedidoRouteColumnsAsync()`;
3. `EnsureDriverProfileColumnsAsync()`;
4. `EnsureOperationalSecuritySchemaAsync()`;
5. seed opcional, apenas quando habilitado e quando as tabelas verificadas estão vazias.

Não encontrei diretório/classes de migrations EF. O processo é `EnsureCreated` mais SQL aditivo customizado, não uma migração versionada. A documentação EF esclarece que `EnsureCreated` cria o esquema se ainda não houver tabelas e não o inicializa se alguma tabela já existir; também adverte que `EnsureCreated` e migrations não devem ser combinados como mecanismos de gestão do mesmo esquema [3]. Migrations são a estratégia documentada pelo EF Core para evolução incremental preservando dados e histórico [4].

### 8.2 SQL Server configurado e validado nos cenários descritos na seção 14

O pacote `Microsoft.EntityFrameworkCore.SqlServer` 10.0.14 está no projeto. `Program.cs` seleciona `UseSqlServer` somente quando `DatabaseProvider=SqlServer`; a connection string é `ConnectionStrings:DefaultConnection` ou variável de configuração equivalente. Não há connection string SQL Server no `appsettings.json` e não foi encontrada variável de ambiente com nome de SQL/DB entre as variáveis consultadas; nenhum valor de ambiente foi exibido.

O atualizador contém ramificações SQL Server para tipos/DDL (`COL_LENGTH`, `OBJECT_ID`, `sys.indexes`, índices filtrados). A inicialização EF em banco vazio e a execução do caminho aditivo legado foram testadas no SQL Server. A suíte xUnit existente continua sem integração SQL Server automatizada; os cenários desta execução foram realizados por startup real e consultas SQLCMD. A documentação do provedor não valida o DDL customizado deste projeto [2].

### 8.3 Idempotência versus preservação de dados

**Idempotência estrutural — demonstrada nos dois bancos de teste SQL Server e nos testes SQLite.** O startup repetido completou no banco recém-criado e no fixture legado; contagens de tabelas/índices e linhas permaneceram constantes, sem duplicação observada.

**Preservação semântica — falhou no teste SQL Server.** Em `EnsureOperationalSecuritySchemaAsync`, o backfill define `AtribuicaoAtiva = 1` para toda entrega com `AtribuicaoAtiva = 0` cujo status seja `Pendente` ou `Em transporte`. O `UPDATE` não exige `MotoristaId IS NOT NULL` nem `VeiculoId IS NOT NULL`. Uma entrega pendente sem atribuição foi inserida no banco de teste com `AtribuicaoAtiva=0`; o startup seguinte marcou-a como ativa, embora motorista e veículo continuassem nulos. O segundo startup não duplicou dados, mas preservou esse estado semanticamente incorreto.

O teste SQLite existente insere entrega atribuída e espera backfill para ativo. O teste SQL Server desta execução incluiu entrega pendente sem motorista/veículo e confirmou a ativação indevida na primeira inicialização repetida. O segundo startup não duplicou dados, mas preservou o estado semanticamente incorreto (`AtribuicaoAtiva=1`). O código não foi alterado.

### 8.4 Resultado SQL Server — atualizado na seção 14

A validação inicial estava pendente por permissão no socket. Depois da autorização explícita, foi testado SQL Server real em dois bancos exclusivamente de teste. A criação limpa, a reinicialização e a consulta de integridade foram executadas; a atualização de schema legado também foi testada e revelou a ausência de FKs nas tabelas operacionais criadas pelo DDL customizado. O backfill indevido foi reproduzido em SQL Server.

### 8.5 Diagnóstico atual de Docker, alternativas e capacidade

| Verificação somente leitura | Resultado |
|---|---|
| Cliente Docker | `/usr/bin/docker`, versão 28.5.2 (Debian/Kali build). |
| Daemon/serviço | `docker.service` e `docker.socket` ativos; daemon rootful. |
| API/socket | `/run/docker.sock` pertence a `root:docker`, modo `0660`; `sudo docker` funcionou após autenticação explícita. |
| Usuário atual | Não pertence ao grupo `docker`; não foi adicionado. Uso privilegiado foi pontual. |
| Docker rootless | Não encontrado: sem `rootlesskit`, `dockerd-rootless.sh`, `dockerd-rootless-setuptool.sh`, `newuidmap` e `newgidmap`; serviço user-level inativo e nenhum socket rootless detectado. |
| Alternativas | `podman`, `nerdctl` e `containerd` client utilizável não encontrados; há `/usr/bin/containerd`, como dependência do daemon Docker. |
| SQL Server nativo | Sem `sqlservr`, `mssql-conf`, `sqlcmd`; `mssql-server.service` ausente e nenhuma porta local 1433 escutando antes do container. O `sqlcmd` incluído no container foi usado para consultas de validação. |
| Arquitetura/capacidade | Linux Kali x86_64; Intel i7-14700HX, 28 CPUs lógicas; 15 GiB RAM, ~7.6 GiB disponíveis; filesystem com ~368 GiB disponíveis. É capacidade suficiente para um container de teste limitado a 4 GiB. |

A Microsoft documenta que o grupo `docker` concede privilégios equivalentes a root; adicionar o usuário permanentemente ao grupo seria uma mudança de segurança persistente, portanto não foi feito [11]. Rootless Docker não está preparado e requer pré-requisitos que não foram encontrados [12]. O host tem arquitetura x86_64 e excede requisitos mínimos de memória/disco para a imagem SQL Server. Kali não é listado para instalação nativa do SQL Server; o ensaio foi feito pelo container oficial Microsoft, sem instalar o motor no host [13][14].

### 8.6 Backfill de atribuição — probe não destrutivo

Para demonstrar a condição do `UPDATE` sem tocar no repositório nem em banco persistente, executei um probe em SQLite `:memory:` com três linhas (uma atribuída, uma pendente sem motorista/veículo e uma em transporte sem motorista). A condição é a mesma regra lógica do backfill, não a execução do updater nem SQL Server:

```sql
UPDATE Entregas
SET AtribuicaoAtiva = 1
WHERE AtribuicaoAtiva = 0
  AND StatusEntrega IN ('Pendente', 'Em transporte');
```

Saída após o `UPDATE`:

```text
Id StatusEntrega MotoristaId VeiculoId AtribuicaoAtiva
1  Pendente      10          3         1
2  Pendente      NULL        NULL      1
3  Em transporte NULL        2         1
```

Este probe foi um diagnóstico preliminar em memória. O cenário equivalente foi depois reproduzido no banco SQL Server real descrito na seção 14. Não corrigi o backfill.

### 8.7 Intervenção Docker autorizada e concluída

O usuário autorizou uma execução pontual de `sudo docker`, download da imagem Microsoft Developer e aceite do EULA. A autenticação foi realizada no prompt interativo; a senha não foi incluída em comandos, arquivos, logs ou relatório. Não adicionei o usuário ao grupo `docker`, não alterei o socket/daemon e não usei volumes. Os comandos e resultados estão na seção 14.

O container permanece parado, não removido e com bancos/credenciais temporários na configuração/camada gravável. Imagem, container, bancos e diretórios de Data Protection continuam preservados; a remoção exige nova autorização.

## 9. Diagnóstico do offline atual

| Requisito de comportamento | Evidência encontrada | Diagnóstico |
|---|---|---|
| Dados persistidos localmente antes da confirmação | `pwa-offline.js` possui `addGPS` e `addPODSignature`, mas `rg` não encontrou invocações desses métodos; tracker usa `gpsOfflineQueue = []` | **Não comprovado / fluxo desconectado** |
| GPS recuperável após reload | fila GPS do `tracker.js` é apenas memória; IndexedDB tem implementação separada não usada | **Não preservado pelo fluxo ativo** |
| POD preservado em interrupção | formulário converte canvas para Base64 e submete via POST online; não chama `addPODSignature` | **Não preservado offline** |
| Sincronização ao voltar rede | worker aguarda evento `sync` e lê store `pendingSync`; não encontrei chamada `registration.sync.register`; tracker reenvia array em memória, mas array não recebe entradas | **Não há sincronização efetiva demonstrada** |
| Falha HTTP preserva pendência | worker remove item depois de qualquer resposta `fetch`, sem verificar `response.ok` | **Risco de perda se houver item enfileirado e o servidor responder erro HTTP** |
| Schema do IndexedDB consistente | página cria `gpsQueue` + `podQueue`; worker cria `pendingSync` + `gpsQueue`; ambos usam versão 1 e criam stores somente em upgrade | **Incompatibilidade de stores conforme quem abre primeiro** |
| Isolamento por conta | records não carregam identificador/escopo de usuário no contrato; limpeza `clearAll` existe, mas não é integrada ao logout no código encontrado | **Não demonstrado** |
| Estados claros para usuário | interface online/offline e mensagens existem parcialmente, mas não há confirmação durável “salvo localmente” nem confirmação de sync implementada | **Parcial/inconclusivo sem browser** |

O próprio comportamento do Background Sync é condicional: a API tem suporte limitado entre navegadores e requer contexto seguro; além do listener no worker, a aplicação precisa registrar tarefa por `ServiceWorkerRegistration.sync.register()` [6]. IndexedDB exige stores incluídos no escopo da transação; store inexistente falha ao iniciar a transação [5].

**Conclusão offline:** requisito essencial do MVP, mas a base atual é scaffolding e não um offline funcional. Não foi implementado nesta etapa.

## 10. Problemas confirmados e riscos potenciais

| ID | Classificação | Descrição/evidência | Impacto e prioridade | Validação recomendada |
|---|---|---|---|---|
| P1 | Resolvido nesta etapa | SDK 6/runtime 6 eram insuficientes; SDK 10.0.403/runtime 10.0.14 instalado isoladamente e restore/build/test passaram | Baseline .NET executável — **Resolvido e validado** | Manter execução via SDK local/CI com .NET 10 |
| P2 | Acesso administrativo resolvido para esta execução | Socket `root:docker` `0660`; `sudo docker` autorizado/uso pontual; usuário não foi adicionado ao grupo | Um comando `sudo docker` tem escopo rootful — **Risco controlado nesta execução** | Não persistir privilégio; solicitar autorização separada antes de remover recursos |
| P3 | Erro confirmado no SQL Server real | Inicialização do updater alterou entrega `Pendente`, sem motorista/veículo, de `AtribuicaoAtiva=0` para `1` | Dado operacional semanticamente alterado — **Alta** | Não corrigido nesta fase; especificar regra e regressão antes da Fase 1 |
| P4 | Limitação estrutural confirmada | `EnsureCreated` não inicializa esquema se já houver qualquer tabela; não há migrations encontradas | Upgrade parcial/legado pode ficar incompleto — **Alta** | Testar banco vazio e bases legadas representativas; decidir estratégia após evidência |
| P5 | Falha confirmada no caminho legado | DDL customizado cria quatro tabelas operacionais sem cláusulas FK; schema vazio criado por EF tem 6 FKs | Integridade relacional diverge conforme estado inicial do banco — **Alta** | Planejar correção/versionamento com cobertura SQL Server; não corrigido nesta fase |
| P6 | Offline incompleto confirmado | Armazenamento, worker, GPS e POD não estão integrados; stores divergentes e sync não registrado | Perda de operações desconectadas — **Alta para MVP** | E2E browser offline/reload/reconnect depois de a etapa ser autorizada |
| P7 | Cobertura de integração parcial | SQL Server foi validado manualmente em banco vazio e fixture legado; não há testes SQL Server automatizados no repositório | Regressões em CI podem passar despercebidas — **Alta antes de piloto** | Criar/autorizar harness de integração em etapa própria; nenhuma alteração de teste foi feita nesta execução |
| P8 | Persistência de chaves potencialmente insuficiente | `Program.cs` persiste Data Protection no banco apenas para PostgreSQL; SQL Server sem `KeysPath` cai no provider padrão | Sessões/tokens protegidos podem invalidar em restart/scale — **Média, depende do deploy** | Decidir/persistir chaves e validar restart/multi-instância |
| P9 | Risco de exposição a reproduzir | Rastreamento público por código retorna destinatário/cidade/eventos; código possui oito dígitos decimais e não há rate limit identificado | Enumeração/privacidade — **Média** | Definir superfície pública; teste de abuso/limites e campos retornados |
| P10 | Cobertura parcial | 75 testes existentes passaram; testes manuais SQL Server passaram para os cenários descritos; sem E2E browser/offline | Riscos de browser e sincronização permanecem — **Alta** | Cobertura automatizada SQL Server/browser é trabalho futuro |

## 11. Critérios E1–E9

| Critério | Status | Evidência/pendência |
|---|---|---|
| E1 — Estado inicial preservado | **APROVADO** | Estado inicial do complemento e final registrados; relatório anterior atualizado; README/render preservados. |
| E2 — Ambiente identificado | **APROVADO** | SDK 10.0.403/runtime 10.0.14 instalado localmente; instalação global 6.0 inalterada. |
| E3 — Restauração verificada | **APROVADO** | Restore exit 0 para Web e Tests. |
| E4 — Compilação verificada | **APROVADO** | Build exit 0, 0 warnings e 0 errors. |
| E5 — Testes verificados | **APROVADO, escopo delimitado** | 75 aprovados, 0 falhos, 0 ignorados; integração SQL Server foi manual, sem teste automatizado commitado. |
| E6 — SQL Server avaliado | **FALHOU parcialmente** | Conectividade, criação em banco vazio, FKs e rejeição de FK inválida passaram; caminho legado cria tabelas operacionais sem FKs. |
| E7 — Idempotência/preservação | **FALHOU** | Repetição não duplicou objetos/linhas, porém o backfill alterou `AtribuicaoAtiva` para entrega sem atribuição. |
| E8 — Offline diagnosticado | **APROVADO como diagnóstico** | Componentes e ligações reais inspecionados; offline permanece incompleto e não foi implementado. |
| E9 — Estado Git preservado | **APROVADO** | README/render preservados; somente este relatório foi atualizado no repositório. |

## 12. Plano de ação priorizado para a próxima etapa

1. **Tratar os dois defeitos comprovados antes de avançar:** definir e corrigir com testes o backfill sem atribuição e adicionar FKs ausentes ao caminho legado, com estratégia compatível com bancos existentes. Nenhuma correção foi feita aqui.
2. **Criar regressões automatizadas SQL Server:** converter os cenários manuais desta execução (schema vazio, legado, reinicialização, FK inválida e backfill) em validação repetível sem banco compartilhado.
3. **Baseline .NET:** manter SDK local .NET 10.0.403 ou provisionar major 10 em CI; os 75 testes atuais passaram nesta execução.
4. **Validar fluxos essenciais no provedor de referência:** pedido, despacho, turno, emergência e POD com SQL Server e concorrência controlada.
5. **Formalizar baseline offline sem implementá-lo:** GPS/POD continuam online-only; políticas de confirmação, idempotência e privacidade devem preceder sua implementação.

As validações executadas não alteraram código funcional nem arquivos de teste. A Fase 0 está **PARCIALMENTE CONCLUÍDA**, não aprovada: restore/build/tests e conectividade SQL Server passaram, mas foram reproduzidos defeitos semântico/relacional que precisam de triagem e autorização de correção em etapa própria. Browser/offline continuam fora da execução atual.

## 13. Referências técnicas consultadas

1. Microsoft, **Select which .NET version to use**. https://learn.microsoft.com/en-us/dotnet/core/versions/selection — acesso em 27/10/2026. Fundamenta a relação entre SDK instalado e TFM suportado.
2. Microsoft, **Microsoft SQL Server Database Provider — EF Core**. https://learn.microsoft.com/en-us/ef/core/providers/sql-server/ — acesso em 27/10/2026. Fundamenta o provedor `Microsoft.EntityFrameworkCore.SqlServer` e configuração por `UseSqlServer`.
3. Microsoft, **Create and Drop APIs — EF Core**. https://learn.microsoft.com/en-us/ef/core/managing-schemas/ensure-created — acesso em 27/10/2026. Explica o escopo de `EnsureCreated` e sua incompatibilidade prática com migrations.
4. Microsoft, **Migrations Overview — EF Core**. https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/ — acesso em 27/10/2026. Referência para evolução incremental versionada de esquema.
5. MDN Web Docs, **IndexedDB API**. https://developer.mozilla.org/en-US/docs/Web/API/IndexedDB_API — acesso em 27/10/2026. Referência para database, object stores, transações e escopo de armazenamento.
6. MDN Web Docs, **Background Synchronization API**. https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API — acesso em 27/10/2026. Documenta registro de sync, execução por service worker, HTTPS e disponibilidade limitada.
7. Microsoft, **dotnet test command with VSTest — .NET CLI**. https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest — acesso em 27/10/2026. Referência de `--no-build`, `--no-restore` e execução pelo testhost.
8. Microsoft, **Download .NET 10.0**. https://dotnet.microsoft.com/en-us/download/dotnet/10.0 — acesso em 27/10/2026. Release listou SDK 10.0.403/runtime 10.0.14 como versão vigente consultada.
9. Microsoft, **Install .NET on Linux without using a package manager**. https://learn.microsoft.com/en-us/dotnet/core/install/linux-scripted-manual — acesso em 27/10/2026. Instruções oficiais para instalação local e verificação de checksum.
10. Microsoft, **dotnet-install scripts — .NET CLI**. https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script — acesso em 27/10/2026. Referência para diretório de instalação local não administrativo.
11. Docker Docs, **Linux post-installation steps for Docker Engine**. https://docs.docker.com/engine/install/linux-postinstall/ — acesso em 27/10/2026. Informa propriedade do socket e que grupo `docker` concede privilégios root-level.
12. Docker Docs, **Rootless mode**. https://docs.docker.com/engine/security/rootless/ — acesso em 27/10/2026. Referência para requisitos e configuração do daemon rootless.
13. Microsoft, **Quickstart: Run SQL Server Linux container images with Docker**. https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker — acesso em 27/10/2026. Informa x86-64, dependências Docker, requisitos de memória/disco e variáveis de setup/EULA.
14. Microsoft, **Installation Guidance for SQL Server on Linux**. https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-setup — acesso em 27/10/2026. Lista arquitetura e requisitos mínimos do SQL Server para Linux.
15. Microsoft, **SQL Server on Linux FAQ**. https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-faq — acesso em 27/10/2026. Lista as plataformas Linux de instalação nativa suportadas e distingue execução em container.
16. Microsoft Container Registry, **SQL Server 2022 CU27 GDR1 — Ubuntu 22.04 tag metadata**. https://mcr.microsoft.com/v2/mssql/server/tags/list?n=1000 — acesso em 27/10/2026. A lista retornada continha o tag versionado usado nesta validação.

As afirmações sobre o M.G.L Connect neste relatório derivam da inspeção dos arquivos locais e saídas dos comandos. As fontes externas acima sustentam apenas o comportamento geral de .NET, EF Core e APIs web; não comprovam o funcionamento específico desta aplicação.

## 14. Complemento da Fase 0 — validação SQL Server executada

### 14.1 Inventário, imagem e container

Antes da criação, a inspeção privilegiada read-only confirmou: zero containers, zero imagens, zero volumes nomeados e somente redes padrão; a porta host `11433` estava livre. Nenhum recurso de outros projetos foi listado e nenhum foi alterado.

- Imagem Microsoft: `mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04`.
- Digest reportado pelo Docker: `sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718`.
- Engine: SQL Server 2022 CU27 GDR, build `16.0.4305.5`, Developer Edition, Linux Ubuntu 22.04.5, x64.
- Container: `mglconnect-etapa0-sqlserver`, ID `962b81fd038496eaed5e7da9ddd1d9fcd5fde2cf49ee1706806e256844d32d85`.
- Limites confirmados via inspect: 4 GiB RAM (`4294967296` bytes), 4 CPUs (`4000000000` NanoCPUs), publicação `127.0.0.1:11433 -> 1433`; `Mounts=[]`, sem volumes.
- EULA Developer aceito conforme autorização do usuário. SA password gerada aleatoriamente por processo e nunca incluída em logs/relatório nem em arquivo versionado.
- O primeiro `docker stop` expirou e terminou em exit 137. Reiniciei o mesmo container; o SQL Server recuperou os dois bancos, e ambos passaram `DBCC CHECKDB` com zero erros/zero reparos. Depois o servidor recebeu `SHUTDOWN` por SQLCMD e o log confirmou “Server shut down by request from login sa”; o container ficou `Exited 255` porque a conexão SQLCMD terminou pelo shutdown. Não foi removido. A imagem continua no cache local. A porta 11433 está desocupada.

Para obter SHA do digest, o pull via MCR concluiu com o digest acima. O tag é explícito por SQL Server 2022 CU27 GDR1 e Ubuntu 22.04, não `latest`.

### 14.2 Banco vazio e inicialização da aplicação

O primeiro startup foi executado com `DatabaseProvider=SqlServer`, connection string de processo para `MGLConnect_Etapa1_Test`, `DemoData__Enabled=false` e armazenamento temporário de Data Protection fora do repositório. O `dotnet run` inicial utilizou o launch profile (`Development`), mas o seed permaneceu desativado e as consultas confirmaram zero linhas em Usuarios/Clientes/Pedidos/Entregas. EF Core emitiu `CREATE DATABASE` e criou 12 tabelas, índices e constraints. O app começou a ouvir em localhost; uma requisição a `/` respondeu HTTP 302 para login.

Depois, reiniciei a aplicação com `--no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Production` e o mesmo banco. O startup completou sem erro. As consultas de catálogo confirmaram 12 tabelas de aplicação, 6 foreign keys e 6 índices filtrados (incluindo índices ativos por motorista/veículo, turno, código e idempotência). Uma tentativa controlada de inserir Entrega apontando para Pedido inexistente foi rejeitada com erro SQL Server 547 e a transação de teste foi revertida. `DBCC CHECKDB` reportou zero erros e zero reparos.

Para testar preservação e o backfill, inseri em transação uma empresa/cliente, um pedido e uma entrega sintéticos no banco dedicado. Antes do restart, a entrega estava `Pendente`, sem motorista/veículo e `AtribuicaoAtiva=0`. Após o startup real seguinte, as contagens continuaram em 1 cliente/1 pedido/1 entrega, mas a linha passou para `AtribuicaoAtiva=1` permanecendo sem motorista e veículo. Este é um **defeito reproduzido no ramo SQL Server**; não foi corrigido.

A primeira tentativa de inserir a fixture via SQLCMD falhou com erro 1934 porque a conexão não tinha `QUOTED_IDENTIFIER`/opções ANSI exigidas pelos índices filtrados. A consulta de verificação confirmou que nenhuma das linhas daquela tentativa permaneceu. Repeti a criação com as opções corretas e transação explícita; as linhas sintéticas foram gravadas com sucesso.

### 14.3 Caminho de atualização de schema legado

Criei o banco isolado `MGLConnect_Etapa1_Legacy_Test` com um fixture mínimo anterior ao schema atual (Usuarios, Pedidos com `TipoCarga=NULL` e Entregas pendente sem atribuição). O startup real passou pelo `EnsureCreatedAsync()` (que detectou tabelas preexistentes) e executou `DatabaseSchemaUpdater` no SQL Server. Resultado: startup completou; criou as quatro tabelas operacionais, adicionou colunas de rota/perfil/turno, criou os índices filtrados e preencheu `TipoCarga` nulo com `Carga Geral Seca`.

O segundo startup no mesmo banco legado também completou. Após a repetição: 7 tabelas de usuário (3 fixture + 4 operacionais), 6 índices filtrados, 1 Pedido e 1 Entrega; não foram duplicadas tabelas/índices/linhas. A entrega sem atribuição permaneceu marcada como ativa após o primeiro backfill. `DBCC CHECKDB` reportou zero erros e zero reparos em ambos os bancos.

**Integridade relacional:** o schema vazio criado pelo EF contém seis FKs e rejeitou Pedido inválido. No caminho legado, o DDL que cria as quatro tabelas operacionais não inclui cláusulas FK. O fixture base foi criado sem FKs, portanto o total do banco legado foi zero; não concluo que o updater remove FKs existentes, mas as novas tabelas não recebem constraints relacionais no SQL de criação. É uma falha de integridade do caminho legado a corrigir após decisão e plano de migração, não nesta etapa.

### 14.4 Comandos/validação e recursos restantes

Comandos principais (a senha SA foi gerada aleatoriamente em variável de ambiente de processo; seu valor foi omitido):

```bash
sudo docker pull mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04
sudo docker run --detach --name mglconnect-etapa0-sqlserver \
  --hostname mglconnect-etapa0-sqlserver --memory=4g --cpus=4 \
  --publish 127.0.0.1:11433:1433 \
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer \
  --env MSSQL_SA_PASSWORD \
  mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04

# A aplicação recebeu a connection string por variável de processo; o valor foi
# omitido por conter a credencial temporária.
ASPNETCORE_ENVIRONMENT=Production DemoData__Enabled=false DatabaseProvider=SqlServer \
  ConnectionStrings__DefaultConnection='<connection string local com segredo omitido>' \
  DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet run --no-launch-profile \
  --no-build --no-restore --project src/MglConnect.Web/MglConnect.Web.csproj

# Consultas executadas dentro do container com sqlcmd e senha no ambiente,
# sem exibir a senha:
sqlcmd -S localhost -U sa -C -Q "SELECT @@VERSION"
sqlcmd -S localhost -U sa -C -Q "DBCC CHECKDB (N'MGLConnect_Etapa1_Test') WITH NO_INFOMSGS"
sqlcmd -S localhost -U sa -C -Q "DBCC CHECKDB (N'MGLConnect_Etapa1_Legacy_Test') WITH NO_INFOMSGS"

DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet restore MglConnect.sln
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet build MglConnect.sln --no-restore
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet test MglConnect.sln --no-build --no-restore
```

Resultado final dos testes existentes: 75 aprovados, 0 falhos, 0 ignorados. São testes unitários, EF InMemory e SQLite conforme cada teste; não foi adicionado teste automatizado SQL Server ao repositório. As validações SQL Server acima foram integração manual reproduzível com SQLCMD e inicializações reais da aplicação. Não houve teste browser, telemetria ao vivo, offline, concorrência de múltiplas instâncias ou fluxo completo com usuários do banco.

As verificações privilegiadas de inventário/estado foram `sudo docker ps/images/volume ls/network ls/info/inspect`, `sudo docker pull/run/start/exec/logs/stop` e a autenticação auxiliar `sudo -v`. Uma checagem `sudo ss -ltnp` também foi executada somente para confirmar que a porta 11433 estava livre no final; é read-only e não alterou estado. A primeira inserção manual de fixture foi rejeitada por opções SET de sessão; a consulta de verificação mostrou que não ficou linha parcial. A repetição foi feita com opções ANSI/`QUOTED_IDENTIFIER` e transação explícita.

Recursos que permanecem deliberadamente sem limpeza automática:

- Container `mglconnect-etapa0-sqlserver`, **parado e não removido**, com os dois bancos e registros sintéticos no filesystem gravável do container. Não há volume Docker montado. Remover o container apagará esses dois bancos.
- Imagem `mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04` no cache local, digest registrado acima.
- Diretórios temporários de Data Protection `[artefato temporário local omitido]` e `[artefato temporário local omitido]` criados pela aplicação para o teste; contêm chaves de teste e não foram removidos.
- SDK local `.NET 10.0.403` em `$HOME/.local/share/mglconnect-dotnet10` e arquivo temporário de download/checksum `[artefato temporário local omitido]` da etapa anterior.

Não parei/removi recursos de outros projetos, não removi imagem/container, não alterei grupo/permissões/daemon e não apaguei arquivos temporários. O container de teste está parado, mas seus dados e a senha aleatória de SA continuam na configuração/container layer do recurso. Qualquer remoção de container, imagem, bancos, chaves temporárias ou artefatos exige autorização posterior.
