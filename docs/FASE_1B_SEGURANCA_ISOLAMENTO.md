# M.G.L Connect — Fase 1B: implementação corretiva de B01

Data: 28/10/2026. Branch `main`; commit-base `d8a54d3`.

## 1. Resultado e limite da entrega

**Primeiro ciclo corretivo de B01 e cenários independentes adicionais implementados e validados em HTTP/WebSocket com SQLite real.** Foram acrescentados **32 casos de integração**, executando a aplicação em Kestrel, com autenticação de produção cookie/bearer, autorização MVC/API, antiforgery e protocolo SignalR. Não foi usado handler de autenticação falso nem EF InMemory nesses casos.

Resultado final da solução: **130 aprovados, 0 falhos, 4 ignorados, total 134**. Restore/build aprovados; build final com **0 warnings e 0 errors**. Os quatro ignorados são os testes SQL Server existentes, sem conexão de teste disponível nesta execução. **Não se afirma validação SQL Server de B01.**

O trabalho ficou no item B01 do [backlog aprovado](./FASE_2_BACKLOG_PRIORIZADO.md). Não foi reiniciada a auditoria geral nem executado B02. Não foram alterados esquema, entidades, FKs, significado de `ClienteId`/`TenantId`, dados existentes, infraestrutura, Docker, deploy ou staging. Os relatórios anteriores permaneceram históricos, sem reescrita.

Esta entrega não aprova o produto para comercialização e não encerra toda a auditoria de segurança da Fase 1B. Há limites e próximos cenários B01 nas seções 8 e 9.

## 2. Proteção do Git e da Fase 1A

Antes de editar, foram registrados status completo, branch, commit, estatísticas dos diffs staged/unstaged e SHA-256 dos diffs. Estado inicial:

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
?? docs/FASE_2_ARQUITETURA_ALVO_SAAS.md
?? docs/FASE_2_BACKLOG_PRIORIZADO.md
?? docs/FASE_2_INVENTARIO_FUNCIONAL.md
?? docs/FASE_2_MAPA_APIS_E_INTEGRACOES.md
?? src/MglConnect.Tests/AssignmentBackfillTests.cs
?? src/MglConnect.Tests/RelationalIntegrityTests.cs
?? src/MglConnect.Tests/SqlServerIntegrityTests.cs
?? src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs
```

As alterações antigas do índice e as remoções locais não foram restauradas. As correções da Fase 1A não foram sobrescritas. O hash do diff **restrito aos sete caminhos já modificados inicialmente** continua igual ao hash de todo o diff unstaged inicial:

| Prova | SHA-256 inicial/final |
|---|---|
| Diff unstaged preexistente, excluindo alterações novas de B01 | `299b971d410863811d5d88f5f67a75ee0bdadb579ab8d33f544f7fb4328ff105` |
| Diff staged completo | `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b` |
| [Relatório da Fase 0](./ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md) | `2b46ad4a716125b692ff0a56a6a9a57a5429ce59c43bb6e8e7d3016249a4b624` |
| [Relatório da Fase 1A](./FASE_1A_INTEGRIDADE_DADOS_SQLSERVER.md) | `0a4e8097b1950ea900da19cc215fd214430773621ae35ed5aba675816480243b` |

Também foram conferidos os hashes dos três arquivos de regressão e do atualizador relacional não rastreados da Fase 1A; permaneceram iguais. Nenhum commit, push, reset, restore, alteração de branch ou limpeza foi executado.

## 3. Fixture e método de reprodução

[SecurityHttpFixture](../src/MglConnect.Tests/SecurityHttpFixture.cs) cria, por caso:

- Duas organizações A/B com GUIDs diferentes.
- Três clientes: A1/A2 na organização A e B1 na organização B. O terceiro cliente permite distinguir isolamento por cliente de isolamento por organização.
- Três motoristas: dois de A e um de B, administradores A/B, clientes e intermediadores vinculados. Não há conversão de cliente em organização.
- Veículos, pedidos e entregas separados; turno/código persistidos quando o cenário exige. Há variante sem turnos para testar recusa e início autenticado.
- SQLite exclusivo, com FKs habilitadas e modelo atual da Fase 1A. Nada é inserido em banco preexistente.
- Processo Kestrel exclusivo, `Production`, seed demonstrativo desativado, porta dinâmica em `127.0.0.1`, chaves Data Protection em diretório temporário exclusivo e integrações de mapas desabilitadas no processo filho.

O teste faz login verdadeiro pela API ou pelo formulário MVC. Os POSTs MVC positivos obtêm token antiforgery da página renderizada. As APIs recebem bearer protegido pelo mecanismo real. As sessões WebSocket negociam e executam o handshake SignalR JSON de verdade. Os testes observam respostas, persistência e eventos recebidos, não apenas retorno de métodos de controller.

Para expiração, a fixture emite tickets de teste com o mesmo Data Protection e purpose do servidor isolado. Não há token operacional, chave de produção ou segredo de usuário usado no ensaio. Um ticket de quatro segundos permitiu reproduzir a conexão que permanecia autorizada depois da validade.

Cada processo da fixture é encerrado por seu próprio objeto `Process` ao terminar. Os dados/chaves de teste não são limpos pelo teste; ficam em `Path.GetTempPath()/mgl-b01-<guid>`. Nesta execução, a raiz foi `[artefato temporário local omitido]`. O host pode gerir a vida útil desse diretório temporário. Nenhum arquivo de usuário foi apagado; eventual limpeza das fixtures deve identificar esses alvos explicitamente.

## 4. Falhas efetivamente reproduzidas e correções

### B01-01 — sobrescrita de cliente de outra organização

**Gravidade: alta.** Admin de A enviava `POST /Clientes/Edit/{id-de-B}` com antiforgery válido e `TenantId=A`. O cliente de B era atualizado e passava a pertencer a A. A reprodução conferiu no banco que o GUID persistido mudou para o tenant forjado; não foi inferida apenas por HTTP 302.

**Causa raiz:** o POST anexava diretamente a entidade recebida com `_context.Update(cliente)`. O valor “original” rastreado vinha do formulário, não do banco. Filtro global de consulta não autoriza automaticamente esse UPDATE desconectado.

**Correção:** [ClientesController.Edit](../src/MglConnect.Web/Controllers/ClientesController.cs) carrega primeiro o cliente pelo contexto filtrado; ausência resulta em 404. Copia somente campos de cadastro permitidos. Tenant, ID e data original não são substituídos pelo formulário.

**Validação:** tentativa cruzada retorna 404 e conserva tenant/nome do registro B. A edição legítima de A funciona sem enviar TenantId, preservando a data original.

### B01-02 — grafo de entidades não autorizado no cadastro

**Gravidade: alta.** Um POST válido de pedido aceitava `Entrega.*` aninhada, incluindo referências a motorista/veículo de B, sem passar pelo despacho e suas validações. O teste anterior à correção comprovou a criação da entrega aninhada, embora a operação fosse apenas cadastro de pedido.

**Causa raiz:** binding da entidade EF inteira, inclusive propriedades de navegação. A validação do cliente raiz e o preenchimento de TenantId não validavam a finalidade nem a autorização do grafo recebido.

**Correção:** [PedidosController.Create](../src/MglConnect.Web/Controllers/PedidosController.cs) recebe por binding somente os campos do formulário de pedido. ID, tenant, status, chave interna e navegações não entram por esse contrato. O [cadastro de cliente](../src/MglConnect.Web/Controllers/ClientesController.cs) recebeu a mesma proteção contra `Pedidos[]` aninhados e IDs arbitrários.

**Validação:** pedido legítimo é criado em A, com status `Registrado`, sem entrega. Cliente legítimo é criado em A com ID gerado, sem pedidos adicionais. Não se alterou o modelo de dados para resolver overposting.

### B01-03 — cliente inativo ainda autorizado fora do portal

**Gravidade: média.** Cliente/intermediador com conta ativa e cliente comercial desativado recebia HTTP 200 nos detalhes da entrega. O portal já exigia cliente ativo, mas a API e a inscrição de telemetria não aplicavam a mesma condição.

**Correção:** [PodeAcessarEntregaAsync](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs) exige cliente materializado, ativo, do tenant do usuário e vinculado ao pedido. [TelemetryHub](../src/MglConnect.Web/Hubs/TelemetryHub.cs) aplica condição equivalente à inscrição.

**Validação:** ambos os perfis passam a receber 403 na API e erro de inscrição privada, preservando os acessos positivos dos clientes ativos. A proteção também alcança os endpoints que reutilizam o helper, incluindo PDF.

### B01-04 — inscrição privada permanecia válida após revogação

**Gravidade: alta.** Após uma inscrição autorizada, desativar conta, mudar perfil, cliente ou organização, ou encerrar turno não impedia o recebimento de novas posições. Cinco cenários falharam antes da correção: o cliente continuava recebendo `ReceiveVehiclePosition`. O middleware HTTP não roda novamente a cada emissão recebida por uma conexão aberta.

**Correção:** [TelemetrySubscriptions](../src/MglConnect.Web/Hubs/TelemetrySubscriptions.cs) mantém apenas identidade/escopo da inscrição por conexão. Antes de cada emissão privada, [SendAuthorizedAsync](../src/MglConnect.Web/Hubs/TelemetryHub.cs) consulta novamente conta, perfil, tenant e vínculo atuais; motorista exige atribuição e turno ativos, cliente/intermediador exige cliente ativo. Destinatário sem acesso é removido da inscrição e do grupo. Logout de grupo e desconexão retiram o registro local.

Os três eventos privados — posição, rota recalculada e falha de recálculo — usam esse caminho. Não se passou a confiar na lista em memória como autorização: o banco é consultado a cada emissão. A emissão é direcionada aos IDs de conexão aprovados, não ao grupo antigo inteiro.

**Validação:** sete cenários WebSocket com recebimento positivo anterior e ausência de novo evento após revogação: conta, perfil, cliente, organização, turno, motorista removido da atribuição e cliente comercial desativado. A mudança é realizada e confirmada no banco antes da próxima emissão. Mensagens já enviadas/em trânsito não podem ser desfeitas; não se promete revogação retroativa.

### B01-05 — edição legítima falhava com tenant não vazio

**Gravidade: média operacional; não é escalada de privilégio.** O formulário legítimo não enviava TenantId; anexar o objeto resultava em conflito de propriedade e HTTP 500. A busca autorizada e a cópia seletiva de B01-01 corrigiram também esse caminho positivo.

### B01-06 — rejeição de conta inativa mudava de 401 para redirect

**Gravidade: baixa; falha de protocolo, não acesso concedido.** Após desativar a conta, a API rejeitava a identidade, mas `UseStatusCodePagesWithReExecute` chamava `/Home/Error`. O middleware passava novamente e redirecionava ao login, resultando em 302 em vez de 401.

**Correção:** [ActiveAccountMiddleware](../src/MglConnect.Web/Middleware/ActiveAccountMiddleware.cs) desabilita status-page re-execution para essa rejeição. A resposta API/hub permanece 401; navegação MVC continua redirecionando ao login.

**Validação:** bearer previamente emitido é rejeitado depois de desativar a conta; negociação autenticada do hub também recebe 401 e novo login da conta desativada é recusado. Um redirect de desafio anônimo por cookie não foi classificado como bypass.

### B01-07 — intermediador encaminhado à área administrativa

**Gravidade: baixa funcional; a autorização administrativa bloqueava o destino.** Login do intermediador redirecionava para `/Dashboard`, não permitido ao perfil.

**Correção:** [AccountController](../src/MglConnect.Web/Controllers/AccountController.cs) e [HomeController](../src/MglConnect.Web/Controllers/HomeController.cs) incluem Intermediador no encaminhamento para `/Cliente`, sem conceder papel administrativo.

**Validação:** login MVC e acesso à raiz redirecionam ao portal autorizado; a matriz de recurso mantém o cliente associado e bloqueia pedidos de A2/B1.

### B01-08 — WebSocket sobrevivia ao ticket de autenticação expirado

**Gravidade: média.** Um bearer protegido com validade curta permitia conexão e inscrição legítimas; depois de expirar, a mesma conexão continuava invocando comandos. HTTP com bearer expirado já era rejeitado; o problema era a conexão persistente.

**Correção:** [mapeamento de TelemetryHub](../src/MglConnect.Web/Program.cs) configura `CloseOnAuthenticationExpiration=true`, opção do ASP.NET Core para essa finalidade [1]. Não foi alterado o formato nem o tempo padrão de validade dos tokens.

**Validação:** conexão aceita antes de expirar e frame de fechamento do servidor recebido em até cinco segundos após o vencimento. O teste não considera timeout/abort local como prova de revogação. A opção tem agendamento do framework; não se afirma fechamento na precisão de milissegundos.

## 5. Matriz de testes novos

[B01HttpAuthorizationTests](../src/MglConnect.Tests/B01HttpAuthorizationTests.cs): **32 casos executáveis**, alguns com múltiplas requisições e verificações de banco.

| Grupo | Casos | Evidência |
|---|---:|---|
| Matriz API/portal | 8 | Admin A/B; clientes, intermediadores e motoristas; positivo próprio, recusa outro cliente na mesma organização e outra organização |
| Edição de cliente | 2 | Ataque cruzado com tenant forjado; edição legítima sem tenant e sem alteração da data de cadastro |
| Cliente inativo | 2 | Cliente e intermediador: API e inscrição privada recusadas |
| Overposting de pedido | 1 | Pedido criado sem aceitar entrega/navegações e campos internos enviados |
| Login/revogação | 1 | Anônimo, senha inválida, conta desativada, bearer anterior e negociação do hub |
| Recursos do motorista | 1 | Própria lista minimizada, outra lista negada, detalhes/POD sem atribuição/turno recusados |
| Hubs: inscrição/publicação | 1 | Cliente pode observar somente própria entrega; não publica; motorista autorizado publica, não publica em outra viagem nem simula; Admin não assina outra organização |
| Revogação passiva | 7 | Evento real recebido antes da alteração e bloqueado na emissão seguinte, nos sete estados descritos |
| POD/PDF/relatório | 1 | Cliente não conclui POD; Admin conclui; cliente/intermediador legítimos recebem PDF real; outro cliente/Admin de outra organização não recebem; ESG administrativo protegido |
| Redirecionamento intermediador | 1 | Formulário MVC/cookie e raiz encaminham ao portal |
| Área administrativa/CSRF | 1 | Motorista não acessa páginas administrativas; antiforgery inválido rejeita edição e conserva o registro |
| Overposting de cliente | 1 | ID/tenant/navegação de pedidos não são aceitos no cadastro |
| Código e turno HTTP | 1 | Emissão administrativa, no-store, expiração, outro motorista, código incorreto, retry, uso único e encerramento; banco conserva no máximo um turno |
| Bearer expirado/malformado | 1 | Mesmo com cookie válido, header bearer inválido não cai em autenticação por cookie; bearer válido continua funcionando |
| Expiração da conexão | 1 | Bearer válido conecta; após expiração o servidor fecha a conexão privada |
| Comandos administrativos cruzados | 1 | Details MVC, reassociação de motorista/veículo e cancelamento de outra organização recusados; cancelamento legítimo funciona; vínculos estrangeiros preservados |
| Emergência HTTP | 1 | Outro motorista não aciona a viagem nem reutiliza a chave; envio/retry próprios idempotentes; Admin B não reconhece alerta de A; Admin A reconhece com ator/tenant corretos |

A geração PDF foi exercitada e a resposta começou com `%PDF`, com tipo `application/pdf`. Isso valida geração e autorização no host testado, **não certificação jurídica do documento ou revisão visual de seu conteúdo**.

## 6. Execução e resultados reproduzíveis

SDK local `.NET 10.0.403`, runtime `10.0.14`; xUnit v2/VSTest. A skill `run-tests` orientou a detecção e a separação entre execução local e integração externa. Nenhuma dependência nova ficou nos projetos. Foi avaliada uma biblioteca de hosting de testes, mas ela não foi mantida: a fixture final usa o executável/Kestrel real e APIs presentes no framework.

Na raiz, com o SDK local:

```bash
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet restore MglConnect.sln --verbosity minimal

DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet build MglConnect.sln --no-restore --verbosity minimal

env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION \
  DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet test --no-build --no-restore --verbosity minimal

# Somente os casos novos, recompilando quando necessário:
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION \
  DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet test --no-restore \
  --filter 'FullyQualifiedName~B01HttpAuthorizationTests' --verbosity minimal
```

| Verificação | Resultado |
|---|---|
| Restore final | Exit 0, dependências atualizadas/restauradas |
| Build final | Exit 0, 0 warnings, 0 errors |
| Suíte final, após reconstrução dos testes | Exit 0; **130 aprovados, 0 falhos, 4 ignorados, total 134**, aproximadamente 46 s |
| Casos B01 nessa suíte | **32 aprovados**, HTTP/WebSocket com SQLite real |
| Casos preexistentes executados | **98 aprovados**, incluindo regressões locais da Fase 1A |
| Testes SQL Server existentes | **4 ignorados**, nenhum declarado aprovado nesta execução |
| Diagnósticos do editor / `git diff --check` | Sem erros reportados nos arquivos novos/alterados verificados; whitespace check exit 0 |

Histórico red/green preservado na sessão: primeiro lote HTTP teve seis falhas, sendo uma expectativa inicial de status ajustada para investigar corretamente o 401/302; confirmou-se depois a re-execução indevida. A sobrescrita cruzada foi reproduzida novamente verificando o tenant persistido. O lote de cinco revogações WebSocket falhou integralmente antes da correção. O login do intermediador e a conexão além da validade também falharam antes de seus ajustes.

Uma primeira regressão completa teve 127 aprovados, 1 falho e 4 ignorados: o teste de expiração exigia fechamento após apenas 500 ms. O teste foi corrigido para observar efetivamente o frame de fechamento do servidor com limite de cinco segundos, sem aceitar timeout local como sucesso. A suíte completa final passou. Não se alterou código para ignorar falha de autorização nem se desabilitou teste negativo.

### SQL Server e outras validações não executadas

`ss` não mostrou listener na porta local de teste `11433`; conexão SQL Server não foi provisionada neste ciclo. Não se usou sudo ou Docker para iniciar infraestrutura, conforme restrição. Os quatro casos existentes são os de schema/FKs da Fase 1A, e não uma matriz HTTP B01 em SQL Server.

Fixtures B01 novas foram executadas exclusivamente com SQLite. A matriz HTTP/WS equivalente no SQL Server continua **pendente**, assim como PostgreSQL, concorrência multi-instância e browser real. O sucesso SQL Server registrado na Fase 1A é histórico e não foi reutilizado como aprovação desta mudança.

## 7. Arquivos modificados e criados

Alterações desta entrega:

- [ClientesController](../src/MglConnect.Web/Controllers/ClientesController.cs): busca filtrada antes de editar, campos explícitos, preservação de propriedade/data e bloqueio de grafos.
- [PedidosController](../src/MglConnect.Web/Controllers/PedidosController.cs): binding somente dos campos de cadastro.
- [EntregasApiController](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs): vínculo de cliente ativo, materializado e da mesma organização.
- [TelemetryHub](../src/MglConnect.Web/Hubs/TelemetryHub.cs): inscrição atualizada, revalidação por destinatário em três emissões privadas, saída/desconexão e vínculo ativo na consulta.
- [Program](../src/MglConnect.Web/Program.cs): registro do serviço de inscrições e fechamento da conexão na expiração.
- [ActiveAccountMiddleware](../src/MglConnect.Web/Middleware/ActiveAccountMiddleware.cs): preservação da resposta 401 de conta rejeitada.
- [AccountController](../src/MglConnect.Web/Controllers/AccountController.cs) e [HomeController](../src/MglConnect.Web/Controllers/HomeController.cs): destino legítimo do intermediador.

Arquivos novos: [TelemetrySubscriptions](../src/MglConnect.Web/Hubs/TelemetrySubscriptions.cs), [SecurityHttpFixture](../src/MglConnect.Tests/SecurityHttpFixture.cs), [B01HttpAuthorizationTests](../src/MglConnect.Tests/B01HttpAuthorizationTests.cs) e este relatório. O caminho deste relatório não existia antes da criação.

Nenhuma alteração nova foi feita no projeto de testes, versões de pacotes, modelos, esquema ou arquivos da Fase 1A. Não foram alterados [TrackingHub público](../src/MglConnect.Web/Hubs/TrackingHub.cs), API/view públicas de rastreio, worker, IndexedDB ou sync offline.

## 8. Limitações e riscos residuais

1. **B02 não foi executado:** rastreio público, enumeração, projeção de dados e limites de consulta mantêm as pendências anteriores. O fato de um recurso privado estar protegido não torna a superfície pública revisada.
2. **Não houve modelo novo de tenancy:** `Guid.Empty`, FKs por ID, ausência de constraints compostas e delegações múltiplas continuam decisões de B08. Nenhuma migração/saneamento foi feita. A proteção dos endpoints testados não autoriza acesso SQL direto nem substitui futura revisão das relações entre organizações.
3. **Revogação privada é conferida por emissão:** não retira mensagens já enviadas e tem janela de concorrência entre consulta e envio. A inscrição é local à instância, como o realtime existente; escala com múltiplos servidores/backplane exige desenho próprio. As consultas por destinatário têm custo, não foram submetidas a teste de carga e não devem ser substituídas por cache sem política de invalidação.
4. **Eventos testados:** o recebimento e bloqueio de posições foram exercitados por WebSocket real. Recálculo/falha de rota usam o mesmo helper verificado no código, mas não foram acionados com provedor externo real. O teste de expiração usa bearer curto; cookie de oito horas não foi aguardado até vencer.
5. **Transporte de testes:** HTTP loopback, não navegador/HTTPS/proxy de produção. Não certifica flags de cookies em hospedagem, CORS, SameSite, interface JavaScript, reconnect automático ou acessibilidade.
6. **Cobertura não é exaustiva:** existem outras combinações de ações administrativas, alteração concorrente de atribuição, cancelamento e emergência. Os cenários cobertos são os listados; não se declara que todos os endpoints e todos os payloads foram auditados.
7. **Migração/decisão humana:** organização desativada, usuário multi-organização, tratamento de tenants vazios e delegação multicliente precisam de regra aprovada e análise de dados. Não foi acrescentada uma política arbitrária que bloqueasse registros legados ou convertesse ClienteId em TenantId.
8. **Sem homologação comercial:** falta validação no banco-alvo desta matriz, operação/restore/privacidade/provedores e revisão restante. Nenhum resultado de teste é declaração de prontidão comercial ou validade jurídica do POD.

## 9. Próximos cenários implementáveis, ainda dentro de B01

- Executar a mesma matriz HTTP/WS em SQL Server isolado quando conexão e autorização de infraestrutura estiverem disponíveis; não substituir essa evidência por EF InMemory.
- Ampliar os comandos administrativos já testados com despacho concorrente, versão antiga e falha de persistência, mantendo os testes positivos de todos os perfis. Reassociação/cancelamento cruzados já estão cobertos neste ciclo.
- Completar transições de emergência além do reconhecimento e sessões com reconnect após mudança de perfil/tenant. Criação/reenvio/reconhecimento HTTP e revogação das inscrições existentes já foram testados; não modificar regras de monitoramento ou canais externos.
- Acionar os dois eventos privados de recálculo em fixture de provedor controlado, sem chamar demos nem receber endereços reais; verificar a mesma revogação e o comportamento de desconexão.
- Validar cookie expirado, HTTPS e proxy no harness apropriado, sem mudar infraestrutura global. Decisões de dados/organização continuam exigindo aprovação, não “correção” automática.

Essas são pendências de ampliação da evidência, não autorização para B02, novo modelo SaaS, deploy ou outras fases. Este ciclo termina com as correções descritas e aguarda a análise do usuário.

## 10. Referências técnicas consultadas

[1] Microsoft. **Authentication and authorization in ASP.NET Core SignalR**. https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0 — acesso em 28/10/2026. Sustenta a distinção entre autenticação na conexão, alterações de perfil posteriores e fechamento por expiração. As correções foram verificadas por execução local, não apenas pela documentação.

[2] Microsoft. **Integration tests in ASP.NET Core**. https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0 — acesso em 28/10/2026. Consultado para delimitar testes de pipeline e infraestrutura versus testes diretos de métodos; o harness final usa Kestrel real, não WebApplicationFactory.

[3] Microsoft. **Use hub filters in ASP.NET Core SignalR**. https://learn.microsoft.com/en-us/aspnet/core/signalr/hub-filters?view=aspnetcore-10.0 — acesso em 28/10/2026. Alternativa consultada, não adotada: filtrar somente a chamada do remetente não resolve o recebimento passivo de um grupo antigo.

## 11. Estado final

O diff staged e os caminhos modificados antes desta fase mantiveram os hashes da seção 2. Continuam os estados antigos de README/implantação e os arquivos não rastreados da Fase 1A/2. B01 acrescentou **oito arquivos rastreados modificados e quatro arquivos novos**, listados na seção 7; nenhum foi colocado no staging. Branch/commit permanecem iguais.

Os processos Kestrel pertencentes às fixtures foram encerrados; não ficou processo de aplicação de teste executando após a suíte. Nenhum container/banco anterior foi iniciado, parado, removido ou atualizado nesta fase. O desenvolvimento continua independente de Docker.
