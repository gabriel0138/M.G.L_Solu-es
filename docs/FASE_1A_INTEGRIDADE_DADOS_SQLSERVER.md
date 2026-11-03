# M.G.L Connect — Fase 1A: integridade de dados e esquema SQL Server

Data: 27/10/2026 (America/Sao_Paulo). Branch `main`, commit-base `d8a54d3`.

## 1. Resultado executivo e abrangência

**Fase 1A APROVADA no escopo verificado de SQL Server e SQLite.** Restore e build passaram; build final com **0 warnings e 0 errors**. Suíte completa com SQL Server habilitado: **102 aprovados, 0 falhos, 0 ignorados**. Sem configuração externa: **98 aprovados, 0 falhos, 4 ignorados explicitamente**. Os quatro ignorados são integração SQL Server, não falhas nem aprovações simuladas.

Foram corrigidos o backfill de atribuição, a geração de atribuições incompletas nas ações administrativas e as relações operacionais ausentes no modelo/atualizador. Uma regressão adicional reproduziu a criação indevida de `ChaveIdempotencia` obrigatória com default `0`; sua criação agora corresponde à nulabilidade do modelo. Não houve saneamento automático de dados já inconsistentes.

Esta aprovação não é homologação do SaaS, de todas as bases legadas possíveis, de PostgreSQL, de concorrência multi-instância ou do offline. Nenhuma dessas funcionalidades foi implementada. PostgreSQL continua configurado no código, mas **não foi executado contra servidor real** nesta fase.

Desenvolvimento e aplicação continuam diretamente em .NET 10. Não foram adicionados Testcontainers, comandos Docker ao produto, novos pacotes ou dependências obrigatórias de containers.

## 2. Proteção do repositório

Antes da primeira alteração, executei `git status --short`, `git diff`, `git diff --cached`, `git branch --show-current` e `git log -1 --oneline`. Também consultei `--untracked-files=all`.

Estado inicial:

```text
MD README.md
AD render.yaml
?? docs/ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md
```

O README estava modificado no índice e removido da árvore de trabalho; o arquivo de implantação estava adicionado no índice e removido na árvore de trabalho. Esses estados foram preservados, sem restauração ou alteração do índice. O diff staged continha 29 linhas adicionadas no README e 22 no arquivo de implantação; o unstaged mostrava as remoções de 204 e 22 linhas, respectivamente.

Provas de preservação, SHA-256 iguais antes/depois:

| Material | SHA-256 |
|---|---|
| `git diff --cached --binary` | `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b` |
| `git diff --binary -- README.md render.yaml` | `553b61c482d4a7421111ea9cfa1686752664ceea94cad6f37c494d488c1ced6b` |
| [Diagnóstico preexistente](./ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md) | `2b46ad4a716125b692ff0a56a6a9a57a5429ce59c43bb6e8e7d3016249a4b624` |

O diagnóstico foi lido integralmente e preservado como registro histórico; as retificações estão neste relatório. Nenhum commit, push, reset, restore, limpeza automática, troca de branch ou alteração no staging foi realizado.

## 3. Confronto da Fase 0 com o código

Fontes principais: [inicialização](../src/MglConnect.Web/Data/DbInitializer.cs), [configuração](../src/MglConnect.Web/Program.cs), [atualizador](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs), [contexto EF](../src/MglConnect.Web/Data/MglDbContext.cs), [entidade de entrega](../src/MglConnect.Web/Models/Entrega.cs), [despacho/atribuição](../src/MglConnect.Web/Controllers/EntregasController.cs), [turnos](../src/MglConnect.Web/Services/DriverShiftService.cs), [conclusão/POD](../src/MglConnect.Web/Services/TrackingService.cs) e [cancelamento API](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs).

### Problemas e causas raiz

| ID | Evidência confirmada | Causa e impacto | Correção |
|---|---|---|---|
| I1 | SQL original ativava toda entrega pendente/em transporte com flag falsa | Status foi tratado como prova de atribuição; faltavam os dois vínculos e a distinção entre migração e startup recorrente | Backfill somente quando a coluna é introduzida, exigindo motorista e veículo e status operacional |
| I2 | Criação e alterações administrativas atribuíam `true` mesmo ao remover motorista/veículo | O defeito não se limitava ao backfill e podia gerar novas inconsistências | Três pontos de escrita agora exigem os dois IDs; alteração de veículo também verifica status operacional |
| I3 | Modelo original tinha seis FKs, mas nenhuma para códigos, turnos e alertas | A Fase 0 atribuiu o problema apenas ao legado; na realidade o esquema novo também carecia das relações operacionais | Onze relacionamentos adicionados ao modelo; criação nova e upgrade SQL Server passam a ter 17 FKs |
| I4 | SQL próprio do legado não adicionava as FKs do modelo | IDs órfãos podiam ser aceitos e o resultado dependia do histórico da base | Preflight de órfãos e instalação/revalidação aditiva das relações, sem apagar registros |
| I5 | Dois pedidos sem chave impediam upgrade SQLite por `UNIQUE constraint failed: Pedidos.ChaveIdempotencia` | Coluna de string opcional era criada `NOT NULL DEFAULT 0`; o índice único colidia nos registros antigos | Nova coluna criada `NULL`, sem chave artificial. Legado SQL Server com nove pedidos sem a coluna também foi testado |

Retificação expressa: a tabela de auditoria é um histórico com identificador de entidade polimórfico e não recebeu FK para esse identificador. Não é correto dizer que as quatro tabelas operacionais precisavam das mesmas FKs. As onze novas relações pertencem a códigos, turnos e alertas. A auditoria e os campos `TenantId` não receberam novas relações nesta fase.

## 4. Regra de atribuição e preservação semântica

Os serviços de turno exigem flag ativa, motorista, veículo e entrega não finalizada. Despacho valida motorista/veículo disponíveis; POD e cancelamento desativam a atribuição. Encerrar apenas um turno não é cancelar a atribuição: o vínculo pode continuar reservado aguardando nova operação.

| Estado/vínculos ao introduzir a coluna | Backfill esperado e testado |
|---|---|
| Pendente sem ambos | Inativa |
| Pendente só com motorista | Inativa |
| Pendente só com veículo | Inativa |
| Pendente com os dois vínculos válidos | Ativa |
| Em transporte com os dois vínculos válidos | Ativa |
| Em transporte sem motorista | Inativa |
| Entregue ou cancelada, mesmo com os dois vínculos | Inativa |
| Status desconhecido | Inativa; não inferir operação |

Depois de existir a coluna, o startup **preserva o valor explícito**, inclusive uma atribuição completa desativada. Trocas administrativas mantêm ativa somente a atribuição completa, retornando ao estado pendente conforme o fluxo existente. A simples retirada de um componente a desativa.

Referências órfãs impedem a conclusão do upgrade e toda a transação operacional é revertida. Conflitos de reserva/índices também impedem o upgrade sem escolher arbitrariamente qual atribuição manter. Não se reclassifica automaticamente motorista inativo, veículo inativo, status desconhecido ou dados com divergência organizacional.

## 5. Esquema e mecanismo de atualização

O caminho continua: `EnsureCreatedAsync` → colunas de pedido → perfil do motorista → esquema operacional → seed opcional. Não foram introduzidas migrations nem substituída a estratégia de inicialização.

- [Program](../src/MglConnect.Web/Program.cs) continua selecionando PostgreSQL por URL ambiental prioritária, SQL Server por `DatabaseProvider=SqlServer`, PostgreSQL por seleção explícita ou SQLite como padrão. A conexão vem de configuração/ambiente; os testes não alteraram configurações persistentes.
- [Modelo](../src/MglConnect.Web/Data/MglDbContext.cs) mantém as seis relações originais: entrega/pedido `Restrict`, entrega/motorista e entrega/veículo `SetNull`, evento/entrega `Cascade`, usuário/cliente `SetNull`, pedido/cliente `Cascade` por convenção.
- Onze relações operacionais usam `Restrict`: código → entrega/motorista/emissor; turno → entrega/motorista/veículo/código; alerta → entrega/motorista/veículo/reconhecedor. Impede exclusão silenciosa do histórico operacional e evita novas cascatas destrutivas.
- [RelationalIntegrityUpdater](../src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs) deriva tabelas, colunas e ações do modelo, em vez de manter uma segunda lista de relacionamentos independente.
- O bloco operacional usa transação `Serializable`: tabelas, colunas, backfill, índices e integridade são confirmados juntos ou revertidos. Os dois atualizadores anteriores e `EnsureCreated` continuam fora desse bloco; **não se afirma atomicidade de toda a inicialização**.
- Todas as relações passam por prévia de órfãos antes de instalar FKs/guardas. O erro informa relação e quantidade, não conteúdo sensível das linhas.
- SQL Server: constraints adicionadas com validação dos dados; constraints existentes de mesmo nome têm alvo, colunas e ações comparados. Uma definição divergente bloqueia o upgrade e não é substituída. FK equivalente com nome legado é reutilizada, revalidada e não duplicada. `WITH CHECK CHECK CONSTRAINT` garante revalidação; os testes verificam `is_disabled=0` e `is_not_trusted=0` [2].
- Mantidos os índices filtrados de atribuições, turnos, códigos e idempotência; incluída a garantia aditiva de entrega única por pedido no legado. A rejeição de uma segunda reserva ativa foi testada em SQLite e SQL Server.

### SQLite legado: diferença explícita

Bancos novos recebem FKs nativas pelo modelo. Para tabelas legadas sem FK, adicionar uma FK física exige reconstrução da tabela no SQLite [1]. Como esta fase não autoriza descartar/recriar tabelas existentes, o upgrade utiliza guardas aditivas por triggers, com validações de INSERT/UPDATE, alteração da chave principal e regras de exclusão correspondentes ao modelo [3]. **Triggers não aparecem em `pragma_foreign_key_list` como FKs nativas.**

Os guardas também recusam substituição de registro referenciado por `INSERT OR REPLACE` e `UPDATE OR REPLACE` através de índices únicos conhecidos do modelo, sem depender de `recursive_triggers=ON`. Há teste com essa opção explicitamente desligada. Isso é uma proteção conservadora: pode recusar uma substituição que uma FK com cascata trataria apagando dependentes; preservar os dados é a escolha desta atualização. Não há promessa de equivalência para DDL externo, índices/colações customizados ou todos os usos possíveis de SQL manual. Alterações manuais de schema exigem avaliação própria.

Campos obrigatórios já existentes não foram tornados opcionais ou reescritos em massa. A correção de nulabilidade afeta somente a **criação** da chave de idempotência ausente. Uma coluna antiga já criada incorretamente não é alterada automaticamente.

## 6. Testes adicionados e resultados

| Arquivo / grupo | Casos novos | O que realmente verifica |
|---|---:|---|
| [AssignmentBackfillTests](../src/MglConnect.Tests/AssignmentBackfillTests.cs) | 11 SQLite | Nove combinações de status/vínculos, flag explicitamente desativada preservada e inconsistência anterior não saneada silenciosamente |
| [RelationalIntegrityTests](../src/MglConnect.Tests/RelationalIntegrityTests.cs) | 6 SQLite | 17 referências inválidas rejeitadas em banco novo; serviços de turno/alerta reais; reserva concorrente rejeitada; repetição; legado com guardas; órfãos e conflitos com rollback; REPLACE; pedidos sem chave |
| [DispatchValidationTests](../src/MglConnect.Tests/DispatchValidationTests.cs) | 6 InMemory | Três despachos incompletos, remoção de motorista/veículo e troca válida; testes de regra do controller, não evidência de FK |
| [SqlServerIntegrityTests](../src/MglConnect.Tests/SqlServerIntegrityTests.cs) | 4 SQL Server | Esquema novo e serviços reais; 17 FKs/6 índices filtrados; definição renomeada/desabilitada revalidada; legado com nove entregas; órfãos; constraint incompatível; repetição; preservação e CHECKDB |

O teste operacional antigo em [DatabaseSchemaUpdaterTests](../src/MglConnect.Tests/DatabaseSchemaUpdaterTests.cs) passou a usar pais e relacionamentos representativos: sua fixture anterior tinha somente duas tabelas e IDs sem registros correspondentes. As expectativas de ativação de uma entrega corretamente atribuída foram mantidas. A fixture não foi alterada para aceitar órfãos como válidos.

Classificação da execução completa: **27 casos SQLite, 4 SQL Server e 71 casos de unidade/mocks/InMemory e regras sem integração SQL Server**. Total 102. Os 75 casos existentes continuam incluídos; foram acrescentados 27 casos executáveis. Nenhum teste EF InMemory foi tratado como substituto de teste relacional.

### Histórico relevante de execução

| Verificação | Resultado real |
|---|---|
| Regressões iniciais de backfill, antes da correção | 10 casos: 5 falharam, 5 passaram; confirmou quatro ativações incompletas e uma reativação indevida |
| Primeiro ensaio SQL Server | 2 passaram, 1 falhou na nova fixture: `DBNull` sem type mapping no insert de preparação; corrigido o uso de parâmetros tipados no teste |
| Regressão específica de dois pedidos legados | Falha reproduzida no índice `Pedidos.ChaveIdempotencia`; também revelou que o teste de conflito podia falhar pela restrição errada. Corrigida a criação da coluna, e o teste agora exige erro em motorista/veículo da entrega |
| Restore final | Exit 0, projetos atualizados |
| Build final | Exit 0; 0 warnings, 0 errors |
| Suíte com conexão SQL Server de testes | Exit 0; 102 aprovados, 0 falhos, 0 ignorados |
| Suíte sem conexão SQL Server | Exit 0; 98 aprovados, 0 falhos, 4 ignorados |
| Subconjunto relacional SQLite, executado separadamente | Exit 0; 27 aprovados, 0 falhos, 0 ignorados |
| Aplicação SQL Server, primeiro startup e reinício | Página de login HTTP 200 em ambos; schema e dados verificados |
| Aplicação SQLite direta | Página de login HTTP 200; nenhum mecanismo Docker chamado pela aplicação |
| `git diff --check` | Exit 0, sem erro de whitespace |

Houve um warning EF1002 transitório no código novo do teste para SQL gerado a partir de metadados; o insert final separa identificadores internos e parâmetros tipados. O build final não contém warnings. Não houve atualização de dependências para obter esses resultados.

## 7. Como reproduzir

SDK local utilizado: `$HOME/.local/share/mglconnect-dotnet10`, versão **10.0.403**, runtime **10.0.14**. O SDK global não foi alterado. Na raiz da solução:

```bash
export DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10
"$DOTNET_ROOT/dotnet" restore MglConnect.sln --verbosity minimal
"$DOTNET_ROOT/dotnet" build MglConnect.sln --no-restore --verbosity minimal
"$DOTNET_ROOT/dotnet" test MglConnect.sln --no-build --no-restore --verbosity minimal
```

Sem outra configuração, o último comando ignora explicitamente a integração externa. Para SQL Server, provisionar por canal seguro `MGLCONNECT_SQLSERVER_TEST_CONNECTION`, com `Initial Catalog=master`, em instância **exclusiva e autorizada de desenvolvimento/testes**. O usuário dessa conexão precisa criar bancos e esquemas. O valor não deve ser exibido ou versionado. O harness rejeita catálogo inicial diferente de `master`, gera nomes únicos e nunca reutiliza um banco existente:

```bash
# A variável com segredo já deve estar definida apenas no processo autorizado.
"$DOTNET_ROOT/dotnet" test MglConnect.sln --no-build --no-restore \
  --filter 'Provider=SqlServer' --verbosity minimal
# Ou executar todos os 102 casos com a mesma variável configurada.
```

Os testes criam `MGLConnect_Fase1A_<cenário>_<guid>`, sem `EnsureDeleted`, `DROP DATABASE`, remoção automática ou dependência da API Docker. **Cada reexecução deixa bancos adicionais**; planejar espaço e autorizar limpeza separadamente. Não apontar a variável para uma instância de produção, mesmo com catálogo `master`.

O startup real utilizou variáveis somente de processo: `DatabaseProvider=SqlServer`, conexão temporária, `DemoData__Enabled=false`, `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://127.0.0.1:15655`, chaves em diretório temporário e URLs PostgreSQL/`PORT` removidas apenas do ambiente filho para não sobrepor o provedor. Comando:

```bash
"$DOTNET_ROOT/dotnet" run --no-launch-profile --no-build --no-restore \
  --project src/MglConnect.Web/MglConnect.Web.csproj
curl --silent --output /dev/null --write-out 'HTTP %{http_code}\n' \
  http://127.0.0.1:15655/Account/Login
```

A configuração SQLite correspondente usou `DatabaseProvider=Sqlite`, conexão para arquivo temporário isolado, porta `15656` e chaves temporárias, sem alterar configuração versionada. Chaves de teste emitiram aviso de armazenamento XML sem criptografia; não constitui configuração recomendada de produção.

## 8. SQL Server real, idempotência e preservação

Reutilizado somente o container autorizado `mglconnect-etapa0-sqlserver`: imagem `mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04`, SQL Server 2022 Developer `16.0.4305.5`, limite **4 GiB / 4 CPUs**, publicação **127.0.0.1:11433 → 1433**, sem volumes compartilhados. Estado, nomes e porta foram conferidos antes do start. Nenhuma configuração global, grupo ou permissão do socket foi alterada. A senha temporária foi obtida em memória, não impressa nem gravada no repositório; SQLCMD e .NET receberam credenciais pelo ambiente do processo.

Nos testes válidos: **17 FKs**, **zero desabilitadas ou não confiáveis**, **6 índices filtrados**, rejeição das referências inválidas, índices únicos exercitados e repetição sem aumento do número de índices ou linhas. `DBCC CHECKDB ... WITH NO_INFOMSGS` completou sem erros nos testes novo e legado e no banco de startup real.

No ensaio real da aplicação, o banco novo `MGLConnect_Fase1A_App_a173cfe7` foi criado pelo mecanismo da aplicação. Depois, uma transação copiou somente fixture sintética de cinco tabelas de um banco novo da própria Fase 1A, preservando IDs e ordem pais/filhos. Não se usaram os bancos da Fase 0 como fonte. Antes/depois do reinício:

```text
Clientes=1; Usuarios=2; Pedidos=1; Entregas=1
Entrega: Id=1, PedidoId=1, MotoristaId=2, VeiculoId=1
StatusEntrega=Em transporte; AtribuicaoAtiva=1; Versao=1
FKs=17; Untrusted=0
HTTP /Account/Login=200
```

Os dois bancos da Fase 0 foram somente consultados; o atualizador novo **não foi executado contra eles**. Ambos conservaram sua única entrega e a flag incorreta anterior. Em `MGLConnect_Etapa1_Test`, motorista/veículo continuam nulos e a flag 1: isso comprova que não houve saneamento disfarçado desses registros.

Órfãos e definição incompatível foram criados exclusivamente em novas fixtures descartáveis. Os testes provaram rollback das novas estruturas/colunas operacionais e conservação dos registros ou da constraint original. Bancos com essas fixtures inválidas **devem continuar bloqueados**; não são candidatos a inicialização normal.

## 9. Saneamento proposto — não executado

O código evita novos efeitos do backfill, mas **não corrige automaticamente flags antigas** nem colunas já criadas com definição incorreta. Antes de qualquer saneamento, exigir backup, janela sem gravações concorrentes e aprovação humana por banco/tenant.

Prévia SQL Server sugerida, somente leitura, **não executada em dados reais**:

```sql
SELECT e.Id, e.TenantId, e.StatusEntrega, e.MotoristaId, e.VeiculoId,
       e.AtribuicaoAtiva, e.Versao,
       (SELECT COUNT(*) FROM TurnosMotorista t
        WHERE t.EntregaId = e.Id AND t.Ativo = 1) AS TurnosAtivos
FROM Entregas e
LEFT JOIN Usuarios m ON m.Id = e.MotoristaId
LEFT JOIN Veiculos v ON v.Id = e.VeiculoId
WHERE e.AtribuicaoAtiva = 1
  AND (e.MotoristaId IS NULL OR e.VeiculoId IS NULL
       OR m.Id IS NULL OR v.Id IS NULL
       OR e.StatusEntrega NOT IN (N'Pendente', N'Em transporte')
       OR m.TenantId <> e.TenantId OR v.TenantId <> e.TenantId);
```

Procedimento futuro proposto, idempotente e sem exclusões:

1. Revisar a prévia e a existência de turnos/códigos pendentes, identificando quais flags foram inferidas incorretamente. Não inferir que uma entrega em transporte realmente deixou de estar em operação.
2. Separar candidatos inequívocos sem atribuição completa e sem turno ativo dos conflitos que exigem decisão operacional. Preservar vínculos válidos, POD e histórico.
3. Somente após autorização, em transação e com pré-condição de ID/tenant/versão/flag/status inalterados, desativar as flags aprovadas e incrementar versão; registrar auditoria e contagem esperada. Reexecutar não deve alterar nada, pois a condição exige flag 1.
4. Se aparecer turno ativo, órfão, vínculo de outro tenant ou status desconhecido, interromper a correção automática desse registro e encaminhar à operação. Nunca inventar pais para satisfazer FK, atribuir motorista arbitrário ou apagar histórico.
5. Repetir a prévia, validar contagens e vínculos, e só então instalar/revalidar constraints. Definições antigas incorretas de `ChaveIdempotencia` exigem plano separado, verificando se `0` era chave real; não converter valores existentes em massa.

Nenhuma rotina de saneamento foi instalada ou executada nos bancos preservados.

## 10. Limites e riscos residuais

| Prioridade | Limitação / evidência | Encaminhamento, sem execução nesta fase |
|---|---|---|
| Alta antes de implantação | Flags corrompidas e colunas já criadas incorretamente não são reescritas | Aplicar o processo de prévia/aprovação acima em base representativa |
| Alta antes de SaaS real | FKs são por ID, não compostas por tenant; `Guid.Empty` continua válido no modo atual; não há FK de `TenantId` para organização | Testar autorização e relações cruzadas; decidir migração de tenancy. A aprovação desta fase não comprova isolamento estrutural completo por organização |
| Média | Bases parciais sem tabelas/colunas essenciais não viram automaticamente bases completas | Inventariar versão legada e preparar atualização específica; startup pode falhar de modo conservador |
| Média | Guardas SQLite não são FKs físicas e cobrem schema/índices conhecidos do modelo | Planejar eventual migração versionada, se autorizada; avaliar SQL/índices customizados e colações antes de uso |
| Média | PostgreSQL não teve servidor real disponível/testado; novo caminho conserva a sintaxe específica, mas execução permanece pendente | Validar em instância isolada antes de anunciar suporte de integração atualizado |
| Média | Unicidade/backfill podem bloquear bases com atribuições duplicadas; scans e DDL no startup podem bloquear gravações | Fazer preflight em manutenção; não tratar erro como motivo para remover índice ou escolher uma linha automaticamente |
| Média | Não foi validado startup concorrente em várias instâncias, todos os schemas SQL Server customizados ou todas as definições antigas de índices | Testes de integração direcionados antes de produção; o teste atual cobre os esquemas descritos |
| Operacional | Novas FKs `Restrict` impedem apagar usuários/veículos/entregas com histórico operacional | Usar os fluxos de inativação existentes; não apagar histórico para liberar a exclusão |
| Operacional | Testes preservam bancos e chaves temporárias por solicitação; exigem espaço e têm segredos de teste no container | Limpeza somente após autorização, por nomes explícitos |

Os atualizadores não se tornaram um framework geral de migrations. Nem todo índice não único de suporte a FKs de uma base antiga é criado pelo SQL aditivo; isso pode afetar desempenho, não a existência das FKs verificadas. Offline, roteamento, publicação, HTTPS/domínio e pagamentos permaneceram fora do escopo.

## 11. Critérios de aceite

| Critério | Estado | Evidência e limite |
|---|---|---|
| Backfill não ativa entrega sem atribuição válida | **APROVADO** | Nove combinações em SQLite e SQL Server; órfãos bloqueados com rollback; coluna existente não é reativada |
| Legado preserva integridade referencial necessária | **APROVADO no esquema testado** | 17 FKs SQL Server confiáveis; referências inválidas rejeitadas; preflight, definição incompatível bloqueada e FK renomeada reusada |
| Regressões passam | **APROVADO** | 102/102 com SQL Server; 27 casos novos, incluindo os defeitos reproduzidos |
| Build aprovado | **APROVADO** | SDK local 10.0.403, restore/build exit 0; zero warnings/errors |
| Sem perda de dados | **APROVADO nos ensaios** | Contagens e vínculos preservados; rollback em falhas; bancos Fase 0 não atualizados e sem exclusões |
| SQLite não quebrado / execução local sem Docker obrigatório | **APROVADO** | 27 casos relacionais SQLite; startup HTTP 200; suíte padrão 98 aprovados/4 integrações externas ignoradas |
| Todos os provedores homologados integralmente | **NÃO ALEGADO / PostgreSQL PENDENTE** | Não é parte da aprovação SQL Server/SQLite; nenhum ensaio PostgreSQL real foi executado |

## 12. Arquivos e estado final do Git

Alterados nesta fase:

- [DatabaseSchemaUpdater](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs): backfill, nulabilidade, transação e chamada da integridade.
- [MglDbContext](../src/MglConnect.Web/Data/MglDbContext.cs): onze FKs operacionais.
- [EntregasController](../src/MglConnect.Web/Controllers/EntregasController.cs): três correções de flag.
- [DatabaseSchemaUpdaterTests](../src/MglConnect.Tests/DatabaseSchemaUpdaterTests.cs): fixture legada relacional.
- [DispatchValidationTests](../src/MglConnect.Tests/DispatchValidationTests.cs): regressões de atribuição.

Criados nesta fase: [RelationalIntegrityUpdater](../src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs), [AssignmentBackfillTests](../src/MglConnect.Tests/AssignmentBackfillTests.cs), [RelationalIntegrityTests](../src/MglConnect.Tests/RelationalIntegrityTests.cs), [SqlServerIntegrityTests](../src/MglConnect.Tests/SqlServerIntegrityTests.cs) e este relatório. Não existia arquivo nesse caminho antes da criação.

Estado final, sem staging novo:

```text
MD README.md
AD render.yaml
 M src/MglConnect.Tests/DatabaseSchemaUpdaterTests.cs
 M src/MglConnect.Tests/DispatchValidationTests.cs
 M src/MglConnect.Web/Controllers/EntregasController.cs
 M src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs
 M src/MglConnect.Web/Data/MglDbContext.cs
?? docs/ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md
?? docs/FASE_1A_INTEGRIDADE_DADOS_SQLSERVER.md
?? src/MglConnect.Tests/AssignmentBackfillTests.cs
?? src/MglConnect.Tests/RelationalIntegrityTests.cs
?? src/MglConnect.Tests/SqlServerIntegrityTests.cs
?? src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs
```

### Recursos de teste preservados

O container foi reiniciado para esta validação e recebeu `SHUTDOWN` SQL normal no encerramento; o SQLCMD retornou 1 porque a conexão foi encerrada pelo servidor, com a mensagem explícita de shutdown solicitado pelo login de teste. A inspeção final confirmou **`exited`, exit code 255, `Mounts=[]`**; o log confirmou shutdown solicitado e encerramento do SQL Trace/Service Broker. Container/imagem **não foram removidos**. Nenhum processo de aplicação de teste ficou em execução; `ss` confirmou as portas `11433`, `15655` e `15656` livres.

Onze bancos exclusivos da Fase 1A permaneceram, inclusive os de tentativas intermediárias:

```text
MGLConnect_Fase1A_App_a173cfe7
MGLConnect_Fase1A_Fresh_7f682e2a1185406ca5962f575e326021
MGLConnect_Fase1A_Fresh_fb2267fe2d304c07898d9a983db6deca
MGLConnect_Fase1A_Fresh_fd7b6a404287459ab5e1dd7ef4819d2b
MGLConnect_Fase1A_Incompatible_846e2b1216e34f5a910187702f821b37
MGLConnect_Fase1A_Legacy_0a2f159716b24b29b6cdf8d756cdb34e
MGLConnect_Fase1A_Legacy_516a4d3f3324439eb13c96a86ecfb865
MGLConnect_Fase1A_Legacy_9d296d766c6e46f69b8150b9b7a2322f
MGLConnect_Fase1A_Orphan_6060525fa2d54ca38447f18ffc7cdff7
MGLConnect_Fase1A_Orphan_d2c217b6716946489cb6a05cca3bd9cf
MGLConnect_Fase1A_Orphan_f0459b8cf425446994fe9389905686ae
```

Permaneceram também os dois bancos Fase 0 e seus recursos anteriores. Recursos temporários novos fora do repositório: `[artefato temporário local omitido]` e `[artefato temporário local omitido]` (SQLite de smoke test e chaves de teste). Não se executou limpeza automática. A camada gravável do container contém os bancos; removê-lo apagaria todos eles, inclusive a evidência da Fase 0. Qualquer remoção depende de autorização posterior.

## 13. Referências efetivamente consultadas

[1] Microsoft. **SQLite EF Core Database Provider Limitations**. https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations — acesso em 27/10/2026. Fundamenta a necessidade de rebuild para adicionar FK nativa em tabela SQLite existente, não a correção específica deste projeto.

[2] Microsoft. **Disable foreign key constraints with INSERT and UPDATE statements**. https://learn.microsoft.com/en-us/sql/relational-databases/tables/disable-foreign-key-constraints-with-insert-and-update-statements — acesso em 27/10/2026. Fundamenta a distinção entre habilitação e confiança/verificação de dados de FKs SQL Server.

[3] SQLite. **CREATE TRIGGER**. https://www.sqlite.org/lang_createtrigger.html — acesso em 27/10/2026. Referência de triggers por linha e `RAISE(ABORT)`.

Evidências de funcionamento são os testes e consultas descritos acima, não as fontes externas. Próxima ação: revisão humana deste diff e da prévia de saneamento, seguida de autorização específica se houver correção de dados existentes. **Nenhuma fase posterior foi iniciada.**
