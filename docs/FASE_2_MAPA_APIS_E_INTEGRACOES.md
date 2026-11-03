# Fase 2 — Mapa de APIs, views e integrações

Inventário estático elaborado em 27/10/2026, consolidado em 28/10/2026. **Conclusão documental da Fase 2 entregue para revisão humana. Fase 1B não executada.** Código e configuração foram preservados. A leitura inicial não executou testes ou aplicação; restore/build/test foram autorizados e reexecutados na retomada, conforme [registro final do inventário](./FASE_2_INVENTARIO_FUNCIONAL.md#13-encerramento-reproduzível-da-fase-2--28102026). Não houve Docker, sudo, deploy, acesso a produção ou nova auditoria ofensiva.

## 1. Escopo, cobertura e contagem

Foram lidos integralmente **12 controllers, com 2.540 linhas, e 32 arquivos Razor, com 4.980 linhas**. Arquivos maiores foram lidos em trechos contíguos até o fim, complementando saídas truncadas. A contagem corresponde a **54 ações públicas e 56 combinações de ação/template/verbos**: 38 convencionais e 18 por atributo, incluindo os aliases. Não é uma contagem de todas as URLs concretas possíveis nem uma enumeração de endpoints obtida em execução.

| Controller — arquivo | Ações | Combinações de rota | Linhas / cobertura |
|---|---:|---:|---|
| [AccountController.cs](../src/MglConnect.Web/Controllers/AccountController.cs#L14) | 4 | 4 convencionais | 123; integral |
| [AuthController.cs](../src/MglConnect.Web/Controllers/Api/AuthController.cs#L16) | 1 | 1 por atributo | 92; integral |
| [EntregasApiController.cs](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L14) | 8 | 8 por atributo | 559; integral |
| [RastreioApiController.cs](../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs#L8) | 1 | 1 por atributo | 56; integral |
| [ClientePortalController.cs](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L14) | 5 | 6 por atributo | 242; integral |
| [ClientesController.cs](../src/MglConnect.Web/Controllers/ClientesController.cs#L12) | 6 | 6 convencionais | 120; integral |
| [DashboardController.cs](../src/MglConnect.Web/Controllers/DashboardController.cs#L13) | 1 | 1 convencional | 85; integral |
| [EntregasController.cs](../src/MglConnect.Web/Controllers/EntregasController.cs#L17) | 18 | 18 convencionais | 938; integral |
| [HomeController.cs](../src/MglConnect.Web/Controllers/HomeController.cs#L7) | 3 | 3 convencionais | 48; integral |
| [PedidosController.cs](../src/MglConnect.Web/Controllers/PedidosController.cs#L13) | 5 | 5 convencionais | 131; integral |
| [RastreioController.cs](../src/MglConnect.Web/Controllers/RastreioController.cs#L8) | 1 | 2 por atributo | 44; integral |
| [RoteamentoController.cs](../src/MglConnect.Web/Controllers/RoteamentoController.cs#L12) | 1 | 1 convencional | 102; integral |
| **Total** | **54** | **56: 38 convencionais + 18 por atributo** | **2.540 linhas; integral** |

As 32 Razor compreendem 27 views de conteúdo, uma view compartilhada de erro, um layout, um partial e dois arquivos de configuração. Não foram encontrados `@page`, Razor Pages, componentes `.razor` ou chamadas de renderização de partial nas views/controllers inventariados.

### Convenções das tabelas

- A rota convencional de [Program.cs, linha 200](../src/MglConnect.Web/Program.cs#L200) é `/{controller=Home}/{action=Index}/{id?}`. O sufixo opcional **`/{id?}` está implícito em todas as rotas convencionais** abaixo, inclusive quando a ação não utiliza esse parâmetro.
- `Index` convencional também aceita omitir a ação: `/Dashboard`, `/Clientes`, `/Pedidos`, `/Entregas` e `/Roteamento`. A inicial aceita `/`, `/Home` e `/Home/Index`.
- `*` significa **sem restrição explícita de verbo**, não somente GET. Há 14 ações assim. Nos overloads com a mesma ação convencional, a versão `[HttpPost]` é a candidata específica para POST.
- Auth: `P` = sem `[Authorize]`; `A` = role `Admin`; `AM` = roles `Admin,Motorista`; `CI` = roles `Cliente,Intermediador`; `U` = `[Authorize]` sem roles. Roles separadas por vírgula são alternativas; atributos de classe e ação acumulam requisitos.
- `AF+` = `[ValidateAntiForgeryToken]`; `AF−` = atributo ausente. Não foi encontrado filtro antiforgery global, `[AllowAnonymous]`, `[IgnoreAntiforgeryToken]` ou renomeação por `[ActionName]` no código pesquisado.
- `V01` a `V32` identificam arquivos Razor na seção 5. Os IDs numéricos identificam ações e permitem relacionar renderização, links e formulários sem repetir rotas.
- `View` normalmente produz HTML 200; redirecionamentos registrados são 302. Os códigos nas tabelas descrevem resultados das ações. Autenticação, antiforgery, binding e middleware podem interromper antes delas ou alterar a forma da resposta.
- São 17 ações com GET explícito, 22 com POST, uma com PUT e 14 sem restrição explícita. Os aliases adicionam duas combinações GET, resultando em 56 combinações de ação/template/verbos. Não há ação declarada com DELETE, PATCH, HEAD ou OPTIONS.

## 2. Ações MVC — 26 ações, incluindo aliases por atributo

| ID | Arquivo / símbolo / linha | HTTP e rota | Auth / AF | Chamadas, respostas e renderização |
|---|---|---|---|---|
| 01 | [HomeController.Index, L20](../src/MglConnect.Web/Controllers/HomeController.cs#L20) | `* /Home/Index`; aliases `/` e `/Home` | P / AF− | Consulta autenticação/roles; redireciona para 08, 20, `/Cliente` ou 04. Não renderiza V21. |
| 02 | [HomeController.Privacy, L38](../src/MglConnect.Web/Controllers/HomeController.cs#L38) | `* /Home/Privacy` | P / AF− | View V22. |
| 03 | [HomeController.Error, L44](../src/MglConnect.Web/Controllers/HomeController.cs#L44) | `* /Home/Error` | P / AF− | Cria modelo com identificador da requisição; V28. `[ResponseCache]`: duração zero, localização None e NoStore. |
| 04 | [AccountController.Login, L26](../src/MglConnect.Web/Controllers/AccountController.cs#L26) | GET `/Account/Login` | P / AF− | Autenticado: redireciona por perfil; demais: V01 com `returnUrl`. |
| 05 | [AccountController.Login, L47](../src/MglConnect.Web/Controllers/AccountController.cs#L47) | POST `/Account/Login` | P / AF+ | Busca usuário ativo por login; verifica hash; cria claims e cookie. Modelo/credenciais inválidos: V01; sucesso: ReturnUrl local validada ou destino por perfil. |
| 06 | [AccountController.Logout, L111](../src/MglConnect.Web/Controllers/AccountController.cs#L111) | POST `/Account/Logout` | P / AF+ | `SignOutAsync` do cookie; redireciona para 01. Não exige `[Authorize]`. |
| 07 | [AccountController.AccessDenied, L118](../src/MglConnect.Web/Controllers/AccountController.cs#L118) | GET `/Account/AccessDenied` | P / AF− | View V02; a ação não define status 403 diretamente. |
| 08 | [DashboardController.Index, L25](../src/MglConnect.Web/Controllers/DashboardController.cs#L25) | `* /Dashboard/Index` | A / AF− | EF: contagens, somatório, cinco entregas recentes e cinco pedidos sem entrega; `ObterResumoCustosFrotaAsync`; estimativa CO₂ baseada em entregas concluídas. V12. |
| 09 | [ClientesController.Index, L22](../src/MglConnect.Web/Controllers/ClientesController.cs#L22) | `* /Clientes/Index` | A / AF− | EF: busca `termoBusca` por razão social, fantasia, CNPJ/cidade; ordena por cadastro. V11. |
| 10 | [ClientesController.Create, L40](../src/MglConnect.Web/Controllers/ClientesController.cs#L40) | `* /Clientes/Create` | A / AF− | Novo modelo de cliente; V08. |
| 11 | [ClientesController.Create, L47](../src/MglConnect.Web/Controllers/ClientesController.cs#L47) | POST `/Clientes/Create` | A / AF+ | Recebe entidade; valida modelo/CNPJ duplicado; adiciona e salva. Erro: V08; sucesso: redireciona 09 com TempData. |
| 12 | [ClientesController.Edit, L66](../src/MglConnect.Web/Controllers/ClientesController.cs#L66) | `* /Clientes/Edit` | A / AF− | `FindAsync(id)`; ID ausente ou entidade não encontrada: 404; encontrado: V10. |
| 13 | [ClientesController.Edit, L78](../src/MglConnect.Web/Controllers/ClientesController.cs#L78) | POST `/Clientes/Edit` | A / AF+ | Compara IDs, verifica CNPJ e chama `Update(cliente)` sobre entidade recebida. 404 por divergência ou ausência após concorrência; V10 inválido; redireciona 09 no sucesso; outras exceções propagam. |
| 14 | [ClientesController.Details, L106](../src/MglConnect.Web/Controllers/ClientesController.cs#L106) | `* /Clientes/Details` | A / AF− | EF: cliente, pedidos e entregas; 404 ou V09. |
| 15 | [PedidosController.Index, L23](../src/MglConnect.Web/Controllers/PedidosController.cs#L23) | `* /Pedidos/Index` | A / AF− | EF: pedidos com cliente/entrega, filtro `status`, ordenação por data; V25. |
| 16 | [PedidosController.Create, L40](../src/MglConnect.Web/Controllers/PedidosController.cs#L40) | `* /Pedidos/Create` | A / AF− | Lista clientes ativos; número preliminar baseado em contagem; V23. |
| 17 | [PedidosController.Create, L49](../src/MglConnect.Web/Controllers/PedidosController.cs#L49) | POST `/Pedidos/Create` | A / AF+ | Valida cliente ativo; remove validação do número informado; gera número definitivo, data e status Registrado; salva. V23 inválido ou redireciona 15. |
| 18 | [PedidosController.Details, L75](../src/MglConnect.Web/Controllers/PedidosController.cs#L75) | `* /Pedidos/Details` | A / AF− | EF: cliente, entrega, motorista e eventos; 404 ou V24. |
| 19 | [PedidosController.AtualizarStatus, L94](../src/MglConnect.Web/Controllers/PedidosController.cs#L94) | POST `/Pedidos/AtualizarStatus` | A / AF+ | EF por `id`; permite Registrado→EmProcessamento/Cancelado e EmProcessamento→Cancelado; incrementa versão e salva. 404 ou redireciona 18 com mensagem, inclusive transição inválida/concorrência. |
| 38 | [RoteamentoController.Index, L30](../src/MglConnect.Web/Controllers/RoteamentoController.cs#L30) | GET `/Roteamento/Index` | A / AF− | EF: entregas/veículos; calcula rota por `entregaId` ou origem/destino, altura, peso, largura, eixos, tipoCarga/perfil; calcula custos com preço de combustível/consumo opcionais. V27. |
| 39 | [RastreioController.Index, L20](../src/MglConnect.Web/Controllers/RastreioController.cs#L20) | GET `/Rastreio`; GET `/Rastreio/{codigo}` — por atributo | P / AF− | `GetTrackingDetailsAsync(codigo)`; V26 inicial, encontrado ou mensagem de ausência. Pacote ausente não retorna 404 nesta ação. |
| 40 | [ClientePortalController.NovoPedido, L50](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L50) | GET `/Cliente/Pedido/Novo` — por atributo | CI / AF− | Resolve cliente atual ativo; V04 ou V07 sem vínculo. |
| 41 | [ClientePortalController.NovoPedido, L62](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L62) | POST `/Cliente/Pedido/Novo` — por atributo | CI / AF+ | Resolve cliente; valida modelo/GUID; consulta idempotência; cria pedido com cliente obtido no servidor. 403 sem vínculo ou chave de outro cliente; V04 inválido; redireciona 44 em sucesso/repetição; erro de banco não recuperável propaga. |
| 42 | [ClientePortalController.Index, L146](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L146) | GET `/Cliente`; GET `/Cliente/Dashboard` — por atributo | CI / AF− | EF: pedidos do cliente atual, entrega/eventos e indicadores; V03 ou V07. |
| 43 | [ClientePortalController.Pedidos, L173](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L173) | GET `/Cliente/Pedidos` — por atributo | CI / AF− | Pedidos próprios; filtros `status`/`termo`, incluindo código de rastreio; V06 ou V07. |
| 44 | [ClientePortalController.PedidoDetalhes, L214](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L214) | GET `/Cliente/Pedido/{id:int}` — por atributo | CI / AF− | Consulta conjunta ID + cliente atual; inclui cliente, entrega, motorista, veículo e eventos. V07 sem vínculo; 404 não localizado; V05 encontrado. |

## 3. Entregas MVC — 18 ações convencionais

Todas herdam `[Authorize(Roles = "Admin,Motorista")]` da classe. `A` abaixo indica atributo adicional da ação, restringindo-a a Admin. O sufixo opcional convencional `/{id?}` também se aplica a todas as rotas desta tabela; parâmetros como `entregaId` são nomes de binding distintos de `id`.

| ID | Arquivo / símbolo / linha | HTTP e rota | Auth / AF | Chamadas, respostas e renderização |
|---|---|---|---|---|
| 20 | [EntregasController.Index, L46](../src/MglConnect.Web/Controllers/EntregasController.cs#L46) | `* /Entregas/Index` | AM / AF− | Motorista sem Admin: entregas próprias com atribuição ativa, não Entregue/Cancelada, projeção reduzida e indicador de turno → V18; claim inválida: 403. Admin: lista/filtros `status`/`termo` e relacionamentos → V17. |
| 21 | [EntregasController.IniciarTurno, L118](../src/MglConnect.Web/Controllers/EntregasController.cs#L118) | POST `/Entregas/IniciarTurno` | AM / AF+ | Claim atual + `entregaId`/`codigo` → `StartAsync`; 403 sem claim; redireciona 33 iniciado/já ativo, ou 20 com mensagem conforme resultado. |
| 22 | [EntregasController.EncerrarTurno, L143](../src/MglConnect.Web/Controllers/EntregasController.cs#L143) | POST `/Entregas/EncerrarTurno` | AM / AF+ | `EndAsync` encerra turno do usuário atual; 403 sem claim; redireciona 20 com resultado. |
| 23 | [EntregasController.EmitirCodigoTurno, L160](../src/MglConnect.Web/Controllers/EntregasController.cs#L160) | POST `/Entregas/EmitirCodigoTurno` | A / AF+ | `IssueCodeAsync`; 403 sem claim; falha redireciona 32; sucesso V14 com headers Cache-Control `no-store, no-cache` e Pragma `no-cache`. |
| 24 | [EntregasController.CancelarCodigoTurno, L183](../src/MglConnect.Web/Controllers/EntregasController.cs#L183) | POST `/Entregas/CancelarCodigoTurno` | A / AF+ | `CancelCodeAsync` cancela código pendente; 403 sem claim; redireciona 32 com resultado. |
| 25 | [EntregasController.AlertasEmergencia, L195](../src/MglConnect.Web/Controllers/EntregasController.cs#L195) | GET `/Entregas/AlertasEmergencia` | A / AF− | EF: últimos 100 alertas e até 250 registros associados de auditoria; V13. |
| 26 | [EntregasController.CriarAlertaEmergencia, L213](../src/MglConnect.Web/Controllers/EntregasController.cs#L213) | POST `/Entregas/CriarAlertaEmergencia` | AM / AF+ | `CreateAsync` recebe chave de idempotência, entrega, latitude/longitude, precisão e data da posição. JSON 200 registrado/duplicado; 409 recusado; 503 falha de persistência; 403 sem claim. |
| 27 | [EntregasController.ReconhecerAlertaEmergencia, L264](../src/MglConnect.Web/Controllers/EntregasController.cs#L264) | POST `/Entregas/ReconhecerAlertaEmergencia` | A / AF+ | `AcknowledgeAsync` por GUID e ator atual; 403 sem claim; redireciona 25 com resultado. |
| 28 | [EntregasController.AtualizarStatusAlertaEmergencia, L281](../src/MglConnect.Web/Controllers/EntregasController.cs#L281) | POST `/Entregas/AtualizarStatusAlertaEmergencia` | A / AF+ | `UpdateStatusAsync` aplica transição de alerta; 403 sem claim; redireciona 25. |
| 29 | [EntregasController.PerfilMotorista, L296](../src/MglConnect.Web/Controllers/EntregasController.cs#L296) | GET `/Entregas/PerfilMotorista` | AM / AF− | EF: próprio usuário ativo com perfil Motorista; modelo com CPF mascarado. 403 claim inválida; 404 sem motorista correspondente; V20. |
| 30 | [EntregasController.Create, L320](../src/MglConnect.Web/Controllers/EntregasController.cs#L320) | `* /Entregas/Create` | A / AF− | Pedidos Registrado/EmProcessamento sem entrega; `PrepararOpcoesDespachoAsync`; preenche destinatário por `pedidoId`; V15. |
| 31 | [EntregasController.Create, L349](../src/MglConnect.Web/Controllers/EntregasController.cs#L349) | POST `/Entregas/Create` | A / AF+ | Transação serializável quando relacional; valida pedido, motorista, veículo, capacidade/reservas; gera código e entrega/checkpoint; atualiza pedido. V15 inválido; redireciona 32 sucesso ou 18 em conflito de banco. |
| 32 | [EntregasController.Details, L532](../src/MglConnect.Web/Controllers/EntregasController.cs#L532) | `* /Entregas/Details` | AM / AF− | Motorista sem Admin: verifica atribuição e redireciona 33; demais: carrega relacionamentos e opções administrativas. 404/403 ou V16. |
| 33 | [EntregasController.Navegar, L574](../src/MglConnect.Web/Controllers/EntregasController.cs#L574) | GET `/Entregas/Navegar` | AM / AF− | EF entrega/veículo; vínculo e `HasActiveShiftAsync` para motorista; `CalcularRotaParaEntregaAsync`; `CalcularCustoViagem` somente admin. 404/403 ou V19. |
| 34 | [EntregasController.FinalizarEntregaPOD, L621](../src/MglConnect.Web/Controllers/EntregasController.cs#L621) | POST `/Entregas/FinalizarEntregaPOD` | AM / AF+ | Valida nome, vínculo, turno e estado; `FinalizarEntregaPODAsync`. 404/403; repetição compara recebedor; redirecionamentos 20/32/33 conforme perfil/resultado, inclusive falha de banco tratada. |
| 35 | [EntregasController.AtribuirVeiculo, L699](../src/MglConnect.Web/Controllers/EntregasController.cs#L699) | POST `/Entregas/AtribuirVeiculo` | A / AF+ | EF valida entrega, turno, veículo/capacidade/reserva; `CancelCodeAsync`; atualiza veículo, parâmetros e versão. 404/403; 409 turno ativo; demais resultados redirecionam 32. |
| 36 | [EntregasController.AdicionarCheckpoint, L784](../src/MglConnect.Web/Controllers/EntregasController.cs#L784) | POST `/Entregas/AdicionarCheckpoint` | AM / AF+ | Modelo, vínculo e turno; impede conclusão por checkpoint e mudança de estado; `AddCheckpointAsync`. 404/403 ou redireciona 32 com mensagem. |
| 37 | [EntregasController.AtribuirMotorista, L839](../src/MglConnect.Web/Controllers/EntregasController.cs#L839) | POST `/Entregas/AtribuirMotorista` | A / AF+ | EF valida estado, turno, motorista e reservas; `CancelCodeAsync`; redefine pendente, grava evento/versão. 404/403 ou redireciona 32, incluindo conflito tratado. |

## 4. REST — 10 ações por atributo

Os três controllers têm `[ApiController]`. Erros de binding/validação podem gerar 400 automaticamente, além dos retornos explícitos. Nenhuma destas ações renderiza Razor. O prefixo de Auth usa `[Route("api/[controller]")]`; os demais são literais.

| ID | Arquivo / símbolo / linha | HTTP e rota | Auth / AF | Chamadas e respostas explícitas |
|---|---|---|---|---|
| 45 | [AuthController.Login, L37](../src/MglConnect.Web/Controllers/Api/AuthController.cs#L37) | POST `/api/Auth/login` | P / AF− | JSON `Login`/`Senha`; usuário ativo e verificação de hash; cria cookie e ticket bearer protegido por Data Protection, duração 8h. 400 campos vazios; 401 credenciais; 200 sucesso, token, perfil e expiração. |
| 46 | [EntregasApiController.GetEntregasMotorista, L47](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L47) | GET `/api/entregas/motorista/{motoristaId:int}` | U / AF− | Admin/Administrador consulta motorista solicitado; motorista exige próprio ID e usuário ativo. EF lista reduzida de viagens ativas para motorista; projeção ampliada para admin. 200/403. |
| 47 | [EntregasApiController.GetDetalhes, L168](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L168) | GET `/api/entregas/{id:int}` | U / AF− | EF + autorização por objeto; motorista exige turno e recebe projeção reduzida; demais autorizados recebem destinatário/documento/endereço/eventos. 200/403/404. |
| 48 | [EntregasApiController.AtualizarStatus, L234](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L234) | PUT `/api/entregas/{id:int}/status` | U / AF− | Autoriza modificação; igualdade de estado é idempotente; bloqueia Entregue por este fluxo; cancelamento efetivo somente administrativo desde Pendente/Em transporte. Cancela código, encerra turnos e grava evento/auditoria. 200/400/403/404/409. |
| 49 | [EntregasApiController.AdicionarCheckpoint, L333](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L333) | POST `/api/entregas/{id:int}/checkpoint` | U / AF− | Autoriza leitura/modificação; impede Entregue e mudança de estado; chama `AddCheckpointAsync`. 200 sucesso/ID/data; 400 argumento inválido ou conclusão por POD; 403/404/409. |
| 50 | [EntregasApiController.FinalizarPod, L378](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L378) | POST `/api/entregas/{id:int}/pod` | U / AF− | Valida nome e acesso ao objeto; entrega concluída compara recebedor antes de exigir modificação; demais chamam `FinalizarEntregaPODAsync`. 200 sucesso/repetição; 400/403/404/409. |
| 51 | [EntregasApiController.ObterTrafegoEta, L421](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L421) | GET `/api/entregas/{id:int}/trafego-eta` | U / AF− | Autoriza objeto/turno; valida `latAtual`/`lngAtual`; calcula rota e consulta `ObterCondicaoTrafegoAsync` com `tempoBaseMinutos`. 200 dados ou indisponibilidade; 400/403/404. |
| 52 | [EntregasApiController.DownloadPodPdf, L498](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L498) | GET `/api/entregas/{id:int}/pod-pdf` | U + roles `Admin,Motorista,Cliente,Intermediador` / AF− | Autoriza objeto; exige Entregue e nome do recebedor; `GerarPodPdfAsync`, serviço por `[FromServices]`. 200 arquivo `application/pdf`; 400/403/404. Não testa assinatura preenchida nesta condição, apesar do texto da mensagem. |
| 53 | [EntregasApiController.ObterEsgEntrega, L526](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L526) | GET `/api/entregas/{id:int}/esg` | U + A / AF− | EF entrega/veículo; `CalcularEmissoesCo2`, serviço por `[FromServices]`, com distância/consumo fixos conforme perfil. 200 relatório estimado ou 404. |
| 54 | [RastreioApiController.Consultar, L21](../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs#L21) | GET `/api/rastreio/{codigo}` | P / AF− | `GetTrackingDetailsAsync`; 400 vazio; 404 ausente; 200 código, status, destinatário, cidade/UF, datas e eventos. |

### Precisões da contagem e dos aliases

- As 54 ações ocupam os IDs 01 a 54; a organização por área deixa os IDs 20 a 37 na tabela de entregas.
- As duas combinações adicionais são o segundo template de 39 e o segundo template de 42. GET e POST de uma mesma URL continuam combinações distintas.
- Os defaults convencionais não foram contados como novas ações. O padrão opcional permite variações de URL; não há um número finito de URLs concretas ao expandir os parâmetros.
- `/Cliente` e `/Cliente/Dashboard` são aliases reais da mesma ação. `/Cliente/Pedido/Novo` tem ações GET e POST distintas.
- Controllers/ações por atributo não ganham automaticamente rotas convencionais adicionais: não foram declarados `/ClientePortal/Index`, `/Cliente/Index`, `/ClientePortal/NovoPedido` ou `/ClientePortal/PedidoDetalhes`.
- `/Rastreio/Index` pode corresponder ao código literal `Index` no template `/Rastreio/{codigo}`; não é um alias convencional para a ação.
- `SemCliente`, `IndexMotorista` e `CodigoTurno` são nomes de views sem ações GET próprias. Métodos privados auxiliares não foram contados como ações.

## 5. Razor — 32 arquivos completos

Os IDs de ações remetem às seções anteriores. Os números entre parênteses na coluna de saídas são linhas relevantes do próprio arquivo. Todos os arquivos desta tabela foram lidos integralmente.

| ID | Arquivo — linhas | Ação que renderiza | Conteúdo, chamadas e saídas |
|---|---|---|---|
| V01 | [Account/Login.cshtml](../src/MglConnect.Web/Views/Account/Login.cshtml#L1) — 80 | 04/05 | Modelo de login; POST→05 (25), ReturnUrl oculto; credenciais demonstrativas somente Development (58). |
| V02 | [Account/AccessDenied.cshtml](../src/MglConnect.Web/Views/Account/AccessDenied.cshtml#L1) — 18 | 07 | Aviso de restrição; link→01 (15). |
| V03 | [ClientePortal/Index.cshtml](../src/MglConnect.Web/Views/ClientePortal/Index.cshtml#L1) — 176 | 42 | Indicadores e cinco pedidos; links→40/43/44 (19/22/166); utilitário de copiar código. |
| V04 | [ClientePortal/NovoPedido.cshtml](../src/MglConnect.Web/Views/ClientePortal/NovoPedido.cshtml#L1) — 131 | 40/41 | Carga, dimensões, coleta/destino; POST→41 com chave de envio oculta (19); retorno→43. |
| V05 | [ClientePortal/PedidoDetalhes.cshtml](../src/MglConnect.Web/Views/ClientePortal/PedidoDetalhes.cshtml#L1) — 287 | 44 | Timeline, endereço, carga e POD/documento/assinatura; links→39/43 (53/58); SignalR (250). |
| V06 | [ClientePortal/Pedidos.cshtml](../src/MglConnect.Web/Views/ClientePortal/Pedidos.cshtml#L1) — 134 | 43 | GET próprio com termo/status (27); links→40/42/39/44; copiar código. |
| V07 | [ClientePortal/SemCliente.cshtml](../src/MglConnect.Web/Views/ClientePortal/SemCliente.cshtml#L1) — 18 | 40/42/43/44 sem vínculo | Aviso; link→01 (15). Sem ação/rota própria. |
| V08 | [Clientes/Create.cshtml](../src/MglConnect.Web/Views/Clientes/Create.cshtml#L1) — 119 | 10/11 | Formulário cadastral POST→11 (22); retorno→09; busca CEP (116). |
| V09 | [Clientes/Details.cshtml](../src/MglConnect.Web/Views/Clientes/Details.cshtml#L1) — 106 | 14 | Cadastro/endereço/pedidos; links→12/09/16/18/39/30. |
| V10 | [Clientes/Edit.cshtml](../src/MglConnect.Web/Views/Clientes/Edit.cshtml#L1) — 123 | 12/13 | POST→13 (22); ID/data ocultos, Ativo editável; retorno→09; busca CEP (120). |
| V11 | [Clientes/Index.cshtml](../src/MglConnect.Web/Views/Clientes/Index.cshtml#L1) — 97 | 09 | GET próprio por termo (23); links→10/14/12. |
| V12 | [Dashboard/Index.cshtml](../src/MglConnect.Web/Views/Dashboard/Index.cshtml#L1) — 268 | 08 | KPIs, custos/margem, entregas/pedidos e ESG; links→38/16/30/20/39/15. |
| V13 | [Entregas/AlertasEmergencia.cshtml](../src/MglConnect.Web/Views/Entregas/AlertasEmergencia.cshtml#L1) — 120 | 25 | Alertas/localização/auditoria; POST→27/28 conforme estado (60/65/74/83/89); retorno→20. |
| V14 | [Entregas/CodigoTurno.cshtml](../src/MglConnect.Web/Views/Entregas/CodigoTurno.cshtml#L1) — 22 | 23 | Código/validade; POST→24 (15); link→32. Sem ação GET própria. |
| V15 | [Entregas/Create.cshtml](../src/MglConnect.Web/Views/Entregas/Create.cshtml#L1) — 205 | 30/31 | Pedido, motorista, veículo e destinatário; POST→31 (25); busca CEP (174); sugestão local por peso. |
| V16 | [Entregas/Details.cshtml](../src/MglConnect.Web/Views/Entregas/Details.cshtml#L1) — 461 | 32, ramo administrativo | Dados/POD/timeline; POST→35/37/23/24/36/34 (134/167/189/194/214/314); links→33/38/39/20; canvas e SignalR (427). |
| V17 | [Entregas/Index.cshtml](../src/MglConnect.Web/Views/Entregas/Index.cshtml#L1) — 167 | 20, ramo administrativo | GET próprio status/termo (37); links→25/30/39/33/32. |
| V18 | [Entregas/IndexMotorista.cshtml](../src/MglConnect.Web/Views/Entregas/IndexMotorista.cshtml#L1) — 114 | 20, ramo motorista | Viagens reduzidas; links→29/33; POST→06/22/21 (18/83/90). Sem ação própria. |
| V19 | [Entregas/Navegar.cshtml](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml#L1) — 769 | 33 | Cockpit/GPS/canvas; retorno→20/32; POST→26 por fetch/FormData (232/684) e →34 (268); tracker (320), mapas externos e telemetria; simulação visível para Admin. |
| V20 | [Entregas/PerfilMotorista.cshtml](../src/MglConnect.Web/Views/Entregas/PerfilMotorista.cshtml#L1) — 36 | 29 | Nome, nascimento, telefone e CPF mascarado; retorno→20 (13). |
| V21 | [Home/Index.cshtml](../src/MglConnect.Web/Views/Home/Index.cshtml#L1) — 119 | Nenhuma ação atual a renderiza | Landing existente; GET→39 (21), links→39/42/38 e `/swagger`; 01 sempre redireciona. |
| V22 | [Home/Privacy.cshtml](../src/MglConnect.Web/Views/Home/Privacy.cshtml#L1) — 41 | 02 | Texto de privacidade; sem formulário/chamada dinâmica própria. |
| V23 | [Pedidos/Create.cshtml](../src/MglConnect.Web/Views/Pedidos/Create.cshtml#L1) — 124 | 16/17 | POST→17 (22); retorno→15; sugestão de veículo local por peso. |
| V24 | [Pedidos/Details.cshtml](../src/MglConnect.Web/Views/Pedidos/Details.cshtml#L1) — 85 | 18 | Pedido/expedição; POST→19 (35), condicionado a perfil/estado/ausência de entrega; links→15/39/32/30. |
| V25 | [Pedidos/Index.cshtml](../src/MglConnect.Web/Views/Pedidos/Index.cshtml#L1) — 108 | 15 | Lista; links→16/39/30/18. Não contém formulário para o filtro `status` aceito pela ação. |
| V26 | [Rastreio/Index.cshtml](../src/MglConnect.Web/Views/Rastreio/Index.cshtml#L1) — 386 | 39 | GET→39 (47); estados inicial/ausente/encontrado; endereço/documentos/POD; SignalR (350). |
| V27 | [Roteamento/Index.cshtml](../src/MglConnect.Web/Views/Roteamento/Index.cshtml#L1) — 362 | 38 | GET próprio (52): perfil/entrega/preço/consumo/eixos; link→33; mapa, alertas, manobras/custos; tiles Esri/OSM. |
| V28 | [Shared/Error.cshtml](../src/MglConnect.Web/Views/Shared/Error.cshtml#L1) — 25 | 03, por convenção | Erro e identificador da requisição. |
| V29 | [Shared/_Layout.cshtml](../src/MglConnect.Web/Views/Shared/_Layout.cshtml#L1) — 271 | Infraestrutura comum | Corpo/seção Scripts (232/260); menus por roles; links→01/39/38/42/20/08/09/15/04 e Swagger; POST→06 (188); bibliotecas externas, utilitários e service worker. |
| V30 | [Shared/_ValidationScriptsPartial.cshtml](../src/MglConnect.Web/Views/Shared/_ValidationScriptsPartial.cshtml#L1) — 2 | Sem chamada encontrada | Partial com scripts jquery-validation/unobtrusive; nenhum `PartialView`, `<partial>` ou renderizador de partial encontrado nas views/controllers inventariados. |
| V31 | [_ViewImports.cshtml](../src/MglConnect.Web/Views/_ViewImports.cshtml#L1) — 3 | Infraestrutura | Imports e Tag Helpers MVC. |
| V32 | [_ViewStart.cshtml](../src/MglConnect.Web/Views/_ViewStart.cshtml#L1) — 3 | Infraestrutura | Seleciona V29 como layout. |

Os formulários POST com Tag Helpers geram antiforgery; vários também possuem `Html.AntiForgeryToken()` explícito. O fetch de emergência transporta o token no FormData. Não houve inspeção de HTML renderizado em execução. Condicionais de roles nas views controlam apresentação; a autorização dos endpoints está nos atributos, middleware e verificações do servidor.

## 6. Autenticação, autorização e antiforgery

| Regra / fonte | Comportamento observado |
|---|---|
| [Registro de autenticação, Program.cs L118](../src/MglConnect.Web/Program.cs#L118) | Esquema padrão de autenticação, challenge e forbid é `MglSmart`, que encaminha para cookie ou `MglBearer`. Cookie tem duração de 8h e renovação deslizante. Login `/Account/Login`, logout `/Account/Logout`, acesso negado `/Account/AccessDenied`. |
| [SelectScheme, L19](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L19) | Header Bearer explícito tem prioridade, inclusive fora de `/api`. GET terminado em `/trafego-eta` com cookie pode usar cookie. Demais caminhos `/api` usam bearer; outros usam cookie. |
| [Login MVC, L63](../src/MglConnect.Web/Controllers/AccountController.cs#L63) | Perfis Admin/Administrador são normalizados para claim Admin; adiciona também Administrador. Claims incluem identificador, nome, role, Login e TenantId. ReturnUrl precisa ser local. |
| [Login REST, L52](../src/MglConnect.Web/Controllers/Api/AuthController.cs#L52) | Monta claims, emite cookie e token protegido; converte Administrador para Admin e adiciona Administrador para perfis administrativos. Expiração explícita de 8h. Não é JWT emitido pelo código lido. |
| [ApiBearerToken.Protect, L42](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L42) | Protege `AuthenticationTicket` com Data Protection e propósito próprio. |
| [HandleAuthenticateAsync, L76](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L76) | Exige header Bearer, desprotege ticket e verifica expiração; ausente não autentica; inválido/expirado falha. |
| [ActiveAccountMiddleware, L18](../src/MglConnect.Web/Middleware/ActiveAccountMiddleware.cs#L18) | Reconsulta usuário autenticado e confere ativo/perfil esperado; inválido encerra cookie e responde 401 para `/api`, `/trackingHub`, `/telemetryHub`, ou redireciona login para outros caminhos. |
| [ClientePortal.ObterClienteAtualAsync, L25](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L25) | Usa identificador da claim; exige usuário ativo de perfil Cliente/Intermediador e Cliente vinculado ativo. Sem fallback para outro cliente. |
| [Entregas.PodeAcessarEntrega, L923](../src/MglConnect.Web/Controllers/EntregasController.cs#L923) | Admin autorizado; motorista exige claim numérica e `MotoristaId` correspondente. |
| [EntregasApi.PodeAcessarEntregaAsync, L122](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L122) | Revalida usuário ativo/perfil. Admin/Administrador; motorista atribuído; Cliente/Intermediador vinculado ao cliente do pedido. |
| [EntregasApi.PodeModificarEntregaAsync, L149](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L149) | Admin/Administrador revalidado ou motorista atribuído com turno ativo. Cliente/Intermediador não modifica por este helper. |
| [DriverShiftService.HasActiveShiftAsync, L286](../src/MglConnect.Web/Services/DriverShiftService.cs#L286) | Exige turno ativo do motorista/entrega e usuário ativo com perfil Motorista. |
| [DriverShiftService.StartAsync, L126](../src/MglConnect.Web/Services/DriverShiftService.cs#L126) | Confere motorista ativo, atribuição ativa, veículo, estado, turno concorrente e código. Código tem validade de 15 minutos e limite de cinco tentativas; geração/hash/verificação no mesmo serviço. |
| [MglDbContext, L43](../src/MglConnect.Web/Data/MglDbContext.cs#L43) | Filtros globais de tenant para entidades operacionais quando contexto está vinculado. Eventos filtram pelo tenant da entrega; códigos, turnos, alertas e auditoria também possuem filtros. |
| [HttpContextTenantProvider, L321](../src/MglConnect.Web/Data/MglDbContext.cs#L321) | Captura estado autenticado e TenantId da claim. Autenticado sem GUID válido continua vinculado ao tenant vazio; anônimo não está vinculado. |
| [ApplyTenantOwnership, L279](../src/MglConnect.Web/Data/MglDbContext.cs#L279) | Em inclusão, atribui tenant atual; em modificação/exclusão, compara tenant da entidade e valor original rastreado. |
| Antiforgery nas tabelas de ações | Todos os 19 POST MVC têm `[ValidateAntiForgeryToken]`. Os três POST REST e o PUT REST não têm esse atributo. Ausência nas APIs não demonstra por si só CSRF, considerando o seletor de autenticação. |

`Forbid()` representa recusa de autorização, mas a resposta efetiva depende do esquema selecionado: bearer e cookie podem produzir respostas diferentes. As páginas de erro também podem ser reexecutadas pelo pipeline. Nada disso foi validado por requisição HTTP nesta fase.

## 7. Pipeline e endpoints fora dos controllers

| Ordem / fonte | Comportamento |
|---|---|
| [Program.cs L140](../src/MglConnect.Web/Program.cs#L140) | `AddControllersWithViews`; JSON com `ReferenceHandler.IgnoreCycles`. Não há registro de Razor Pages no trecho integral lido. |
| [Program.cs L173](../src/MglConnect.Web/Program.cs#L173) | Na inicialização, resolve contexto/hasher e chama inicializador de banco. A aplicação não foi iniciada para este inventário. |
| [Program.cs L180](../src/MglConnect.Web/Program.cs#L180) | Fora de Development: tratamento de exceção em `/Home/Error`, reexecução de status em `/Home/Error`, HSTS. |
| [Program.cs L187](../src/MglConnect.Web/Program.cs#L187) | Ordem: arquivos estáticos → routing → authentication → ActiveAccountMiddleware → authorization → AdminOnlySwaggerMiddleware → Swagger/UI. |
| [AdminOnlySwaggerMiddleware L5](../src/MglConnect.Web/Middleware/AdminOnlySwaggerMiddleware.cs#L5) | Caminhos `/swagger`: 401 sem autenticação; 403 sem role Admin; demais seguem. |
| [Program.cs L194](../src/MglConnect.Web/Program.cs#L194) | UI em `/swagger`; documento `/swagger/v1/swagger.json`. Esses caminhos não são ações dos 12 controllers. |
| [Program.cs L200](../src/MglConnect.Web/Program.cs#L200) | Mapeia rota convencional default. As rotas por atributo dos controllers integram o mapeamento MVC. |
| [Program.cs L204](../src/MglConnect.Web/Program.cs#L204) | Mapeia `/trackingHub` e `/telemetryHub`; não incluídos no total de 54 ações/56 combinações de controller. |
| [TrackingHub.JoinTrackingGroup L17](../src/MglConnect.Web/Hubs/TrackingHub.cs#L17) | Sem `[Authorize]` na classe; normaliza código, confere existência e adiciona conexão ao grupo. `LeaveTrackingGroup` remove do grupo. |
| [TelemetryHub L14](../src/MglConnect.Web/Hubs/TelemetryHub.cs#L14) | `[Authorize]`; entrada no grupo verifica acesso por objeto. Envio de telemetria verifica operação autorizada; simulação somente Admin. Leitura deste arquivo foi parcial. |

## 8. Consumidores HTTP, formulários e chamadas do navegador

Esta seção registra consumidores observados nas leituras iniciais e não pressupõe chamadas de aplicativos externos. Provedores, contratos de dados, falhas, offline e referências foram complementados nas seções 12 a 14, sem repetir o inventário de ações/views já concluído.

| Consumidor observado | Destino / verbo / dados | Fonte e limite |
|---|---|---|
| Navegação/links MVC | GET para ações indicadas na tabela Razor; IDs, código, `pedidoId` e `entregaId` por route values/query conforme link | As 32 views foram lidas integralmente; os links não ampliam os verbos permitidos pelos endpoints. |
| Formulários MVC | POST para IDs 05, 06, 11, 13, 17, 19, 21–24, 26–28, 31, 34–37 e 41 | Fontes/linhas na tabela Razor; entidades/modelos, IDs ocultos e campos de negócio. O servidor continua responsável por autorização. |
| V19 — emergência | POST para ação 26 com `FormData`, `credentials: same-origin`, `Accept: application/json`; token e chave de idempotência incluídos | [Navegar.cshtml L684](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml#L684). Exige `response.ok` e `registrado === true`; falha permite reenviar a mesma solicitação. |
| V19 — tracker | GET `/api/entregas/{id}/trafego-eta?latAtual=...&lngAtual=...&tempoBaseMinutos=...` | [tracker.js L619](../src/MglConnect.Web/wwwroot/js/tracker.js#L619). Fetch sem bearer explícito neste trecho; seletor admite cookie nesse GET. |
| V19 — periodicidade do tráfego | Consulta inicial e intervalo de 300.000 ms, se há entrega e pelo menos dois pontos de rota | [tracker.js L612](../src/MglConnect.Web/wwwroot/js/tracker.js#L612). Leitura parcial do arquivo, focada nas chamadas. |
| V08/V10/V15 — busca CEP | GET externo `https://viacep.com.br/ws/{cep}/json/`, no blur de CEP com oito dígitos | [site.js L4](../src/MglConnect.Web/wwwroot/js/site.js#L4); chamadas nas linhas 116/120/174 das respectivas views. |
| V19/V27 — mapas | GET de tiles Esri e OpenStreetMap pelo Leaflet | [Navegar.cshtml L340](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml#L340), [Roteamento/Index.cshtml L256](../src/MglConnect.Web/Views/Roteamento/Index.cshtml#L256). Sem validação de rede/disponibilidade nesta fase. |
| V29 — recursos comuns | Bootstrap, ícones, Google Fonts, Leaflet, SignalR; recursos locais e registro `/sw.js` | [_Layout.cshtml L16](../src/MglConnect.Web/Views/Shared/_Layout.cshtml#L16), [L253](../src/MglConnect.Web/Views/Shared/_Layout.cshtml#L253), [L264](../src/MglConnect.Web/Views/Shared/_Layout.cshtml#L264). Não auditados provedores, caches ou service worker integralmente. |
| Links Swagger | Navegação para `/swagger` | V21 e V29; autorização própria do middleware, não uma action MVC. |
| APIs REST 45–50/52–54 | Não foi identificado consumidor direto dessas ações nas Razor lidas; links de rastreio usam 39, não 54 | Não constitui conclusão sobre ausência de consumidores no restante do workspace. Não houve inventário integral de aplicativos/clientes externos. |

### Consumidores SignalR observados

| Consumidor | Hub / métodos / eventos | Fonte |
|---|---|---|
| V05, V16 e V26 | `/trackingHub`; invocam `JoinTrackingGroup(codigo)` após conectar/reconectar; recebem `ReceiveCheckpointUpdate` | [PedidoDetalhes.cshtml L250](../src/MglConnect.Web/Views/ClientePortal/PedidoDetalhes.cshtml#L250), [Details.cshtml L425](../src/MglConnect.Web/Views/Entregas/Details.cshtml#L425), [Rastreio/Index.cshtml L349](../src/MglConnect.Web/Views/Rastreio/Index.cshtml#L349). |
| V19 por tracker | `/telemetryHub`; `JoinLiveTracking`, `SendVehicleTelemetry`, `LeaveLiveTracking` | [tracker.js L270](../src/MglConnect.Web/wwwroot/js/tracker.js#L270), [L478](../src/MglConnect.Web/wwwroot/js/tracker.js#L478), [L681](../src/MglConnect.Web/wwwroot/js/tracker.js#L681). |
| V19 — simulação/desvio | Invoca envio de telemetria com indicador de simulação; controles exibidos para Admin | [Navegar.cshtml L533](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml#L533), [L586](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml#L586). |
| Tracker — recepção | `ReceiveVehiclePosition`, `ReceiveRouteRecalculated`, `ReceiveTelemetryMetrics`, `ReceiveRouteRecalculationFailed` | [tracker.js L281](../src/MglConnect.Web/wwwroot/js/tracker.js#L281). |

## 9. Chamadas de serviços e auxiliares verificadas

| Ações / origem | Implementação / efeito |
|---|---|
| 05/45 | Verificação de senha e autenticação; [ApiBearerToken.Protect L42](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L42). Implementação do hasher não foi auditada integralmente. |
| 31 | [TrackingService.GenerateTrackingCode L30](../src/MglConnect.Web/Services/TrackingService.cs#L30): gera código; controller verifica colisões antes de persistir. |
| 36/49 | [TrackingService.AddCheckpointAsync L40](../src/MglConnect.Web/Services/TrackingService.cs#L40): valida estado, salva evento e transmite `ReceiveCheckpointUpdate`; falha de notificação é registrada sem desfazer persistência. |
| 39/54 | [TrackingService.GetTrackingDetailsAsync L102](../src/MglConnect.Web/Services/TrackingService.cs#L102): normaliza código e carrega entrega/pedido/cliente/motorista/veículo/eventos. |
| 34/50 | [TrackingService.FinalizarEntregaPODAsync L117](../src/MglConnect.Web/Services/TrackingService.cs#L117): conclui entrega/pedido, encerra turnos e transmite evento. |
| 21/22/23/24/35/37/48 | [StartAsync L126](../src/MglConnect.Web/Services/DriverShiftService.cs#L126), [EndAsync L298](../src/MglConnect.Web/Services/DriverShiftService.cs#L298), [IssueCodeAsync L23](../src/MglConnect.Web/Services/DriverShiftService.cs#L23) e [CancelCodeAsync L99](../src/MglConnect.Web/Services/DriverShiftService.cs#L99). |
| 26/27/28 | [CreateAsync L19](../src/MglConnect.Web/Services/EmergencyAlertService.cs#L19), [AcknowledgeAsync L132](../src/MglConnect.Web/Services/EmergencyAlertService.cs#L132) e [UpdateStatusAsync L163](../src/MglConnect.Web/Services/EmergencyAlertService.cs#L163): idempotência, vínculo/turno e transições/auditoria. |
| 30/31 | [PrepararOpcoesDespachoAsync L506](../src/MglConnect.Web/Controllers/EntregasController.cs#L506) e [PreencherDestinatario L519](../src/MglConnect.Web/Controllers/EntregasController.cs#L519): opções de despacho e preenchimento a partir de pedido/cliente. |
| 33/38/51 | Serviços de rota/custos/tráfego conforme chamadas nos controllers. A implementação de [CalcularRotaParaEntregaAsync L460](../src/MglConnect.Web/Services/TruckRoutingService.cs#L460) foi localizada por pesquisa; não foi auditada integralmente. |
| 08/38/52/53 | Resumo de frota, custo de viagem, geração PDF e emissões CO₂ estão explicitados nas ações; implementações desses serviços não foram auditadas integralmente. |

## 10. Nuances de autorização e risco de IDOR

São observações estáticas. **Não foi comprovada exploração, enumeração, acesso cruzado, CSRF ou obtenção indevida de dados.** Também não houve validação dinâmica de status HTTP ou isolamento efetivo de banco.

1. **Filtros de tenant contextualizam consultas por ID.** [MglDbContext L43](../src/MglConnect.Web/Data/MglDbContext.cs#L43) aplica filtros globais quando vinculado. Portanto, uma consulta aparentemente apenas por ID não demonstra acesso entre tenants. Anônimos não têm o mesmo vínculo global. O ciclo de vida efetivo do contexto por requisição não foi verificado em execução.

2. **Atualização de cliente destacado é ponto prioritário de revisão.** A ação 13 recebe entidade e executa [Update(cliente), L92](../src/MglConnect.Web/Controllers/ClientesController.cs#L92) sem carregar primeiro o cliente-alvo no escopo autorizado. [Cliente.TenantId L16](../src/MglConnect.Web/Models/Cliente.cs#L16) é público; [ApplyTenantOwnership L279](../src/MglConnect.Web/Data/MglDbContext.cs#L279) compara valores da entidade rastreada. Isso merece análise específica de overposting/isolamento: filtro de consulta não demonstra sozinho a restrição do UPDATE. Não foi demonstrado desvio dessa guarda nem modificação entre tenants.

3. **Portal possui controles por objeto observáveis.** A ação 44 consulta ID e cliente conjuntamente; a 41 fixa cliente no servidor e verifica propriedade na repetição idempotente. [ObterClienteAtualAsync L25](../src/MglConnect.Web/Controllers/ClientePortalController.cs#L25) exige vínculo explícito ativo, sem fallback.

4. **Vínculo e turno são controles distintos.** [PodeAcessarEntrega L923](../src/MglConnect.Web/Controllers/EntregasController.cs#L923) verifica motorista atribuído; a API separa [leitura L122](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L122) e [modificação L149](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L149). A ação 52 permite leitura de POD ao motorista vinculado sem exigir turno ativo. Em 34/50, repetição de POD concluído é tratada antes da verificação de turno/modificação; permanece verificação de acesso ao objeto.

5. **Rastreio público mostra dados além da projeção REST.** [V26 L249](../src/MglConnect.Web/Views/Rastreio/Index.cshtml#L249) contém recebedor/documento/assinatura e [L273](../src/MglConnect.Web/Views/Rastreio/Index.cshtml#L273) contém endereço/documento do destinatário. A projeção de 54 é menor, mas descrições de eventos podem carregar documento inserido pelo [serviço POD L141](../src/MglConnect.Web/Services/TrackingService.cs#L141). O grupo público verifica existência do código, sem vínculo de proprietário. É exposição por posse do código a avaliar, sem enumeração ou obtenção indevida demonstrada.

6. **Verificações administrativas não são uniformes em nomes de role.** Parte dos helpers REST aceita Admin/Administrador; diversos atributos e condicionais MVC exigem Admin. Os logins normalmente emitem as claims administrativas descritas na seção 6. Não foi testada combinação de claims externas ou antigas.

7. **Menus não são prova de permissão.** [V29 L67](../src/MglConnect.Web/Views/Shared/_Layout.cshtml#L67) oferece portal para Admin e Cliente, omitindo Intermediador; o endpoint exige CI. Acesso real depende do servidor, não do menu.

8. **Antiforgery REST deve ser avaliado junto ao esquema.** [SelectScheme L19](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L19) normalmente usa bearer em `/api`, com exceção do GET de tráfego com cookie. A ausência de antiforgery nessas ações não basta para concluir CSRF.

9. **Resposta por ausência ou recusa pode diferir.** Algumas ações consultam existência antes de autorizar objeto, retornando 404 versus Forbid; a ação MVC pública de rastreio retorna view com mensagem em vez de 404. Não houve teste de diferenciação observável, inferência de existência ou resposta final após middleware.

## 11. Fontes auxiliares e lacunas

Além dos 12 controllers e 32 Razor, foram lidos integralmente os seguintes arquivos auxiliares relevantes:

| Fonte | Cobertura |
|---|---|
| [Program.cs](../src/MglConnect.Web/Program.cs#L1) | Integral; 207 linhas. |
| [MglDbContext.cs](../src/MglConnect.Web/Data/MglDbContext.cs#L1) | Integral; 332 linhas, incluindo provider de tenant. |
| [ActiveAccountMiddleware.cs](../src/MglConnect.Web/Middleware/ActiveAccountMiddleware.cs#L1) | Integral; 53 linhas. |
| [AdminOnlySwaggerMiddleware.cs](../src/MglConnect.Web/Middleware/AdminOnlySwaggerMiddleware.cs#L1) | Integral; 24 linhas. |
| [ApiBearerAuthenticationHandler.cs](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs#L1) | Integral; 111 linhas, incluindo seletor e proteção do ticket. |
| [TrackingService.cs](../src/MglConnect.Web/Services/TrackingService.cs#L1) | Integral; 215 linhas. |
| [DriverShiftService.cs](../src/MglConnect.Web/Services/DriverShiftService.cs#L1) | Integral; 355 linhas. |
| [EmergencyAlertService.cs](../src/MglConnect.Web/Services/EmergencyAlertService.cs#L1) | Integral; 215 linhas. |
| [TrackingHub.cs](../src/MglConnect.Web/Hubs/TrackingHub.cs#L1) | Integral; 37 linhas. |
| [site.js](../src/MglConnect.Web/wwwroot/js/site.js#L1) | Integral; 66 linhas. |
| [Cliente.cs](../src/MglConnect.Web/Models/Cliente.cs#L1) | Integral; 67 linhas. |
| [ITenantOwned.cs](../src/MglConnect.Web/Models/ITenantOwned.cs#L1) | Integral; 6 linhas. |
| [MglConnect.Web.csproj](../src/MglConnect.Web/MglConnect.Web.csproj) | Integral; framework e referências lidos; sem restore/build no levantamento inicial. A execução de 28/10 consta no encerramento do inventário. |

Leituras parciais e limites:

- [tracker.js](../src/MglConnect.Web/wwwroot/js/tracker.js#L260): trechos de linhas 260 a 327, 465 a 512 e 612 a 690, além de pesquisa textual de chamadas. Não houve leitura integral das 690 linhas.
- [TelemetryHub.cs](../src/MglConnect.Web/Hubs/TelemetryHub.cs#L1): linhas 1 a 82 e 210 a 279; não foi auditado todo o processamento intermediário.
- [TruckRoutingService.cs](../src/MglConnect.Web/Services/TruckRoutingService.cs#L460): pesquisa textual de métodos e consultas; implementação não lida integralmente.
- Hasher, serviços de custos, tráfego, PDF e CO₂, inicializadores e service worker não foram lidos integralmente no recorte inicial deste mapa. A leitura de serviços centrais/dados está consolidada no [inventário](./FASE_2_INVENTARIO_FUNCIONAL.md), e a complementação de integrações está nas seções 12 a 14. Clientes móveis externos não foram inventariados.
- Não houve contratação, consulta a faturamento/quota de conta, levantamento de volume real ou teste de disponibilidade de provedor. Valores comerciais e elegibilidade de licenças permanecem pendentes, não presumidos gratuitos.
- Nenhuma rota, negociação SignalR, HTML gerado, política de autenticação efetiva, resposta final do pipeline, isolamento de banco ou hipótese de exploração foi validada em execução.
- As fontes e linhas refletem o workspace com alterações preexistentes; não representam exclusivamente o HEAD. A frase de ausência de comandos se refere à leitura inicial; resultados da retomada estão no [encerramento](./FASE_2_INVENTARIO_FUNCIONAL.md#13-encerramento-reproduzível-da-fase-2--28102026).

## 12. Provedores e contratos de integração — complementação de 28/10

Esta matriz confronta os consumidores com [TruckRoutingService](../src/MglConnect.Web/Services/TruckRoutingService.cs), [TrafficMatchService](../src/MglConnect.Web/Services/TrafficMatchService.cs), [site.js](../src/MglConnect.Web/wwwroot/js/site.js), [layout](../src/MglConnect.Web/Views/Shared/_Layout.cshtml) e views de mapas. Nenhuma chamada de negócio foi enviada aos provedores nesta retomada; a consulta de páginas documentais da seção 14 é leitura de referência, não teste de integração.

| Integração / origem | Contrato e configuração no código | Dados enviados / retorno | Falha, cache e limite |
|---|---|---|---|
| Google Geocoding, servidor | GET `maps.googleapis.com/maps/api/geocode/json`; `address`, região/país BR e chave. Preferência: `GoogleMaps:GeocodingApiKey`, `GoogleMaps:ApiKey`, configuração/ambiente `GOOGLE_MAPS_API_KEY`. | Endereço de coleta/destino pode conter dado de cliente. Usa primeira coordenada válida no retângulo brasileiro. | Timeout 8 s; erro HTTP/JSON/timeout produz ausência de coordenada. Cache em memória por provedor/endereço por 7 dias. Se Google foi selecionado e falha, não há fallback automático para Photon nesse método. Compatibilidade contratual de cache e exibição no mapa precisa de revisão antes de uso comercial. |
| Photon, servidor | GET `/api/?q=...&limit=1&bbox=-75,-34,-34,6`; habilitação explícita `Geocoding:PhotonEnabled`; base HTTPS configurável. Development habilita demo `photon.komoot.io`. | Texto do endereço; retorna primeira feature com coordenada válida. Não há anonimização automática. | Timeout 8 s; cache de 7 dias. Demo tem throttle local de um segundo entre inícios de requisição; não é quota ou SLA contratado. Sem Google/Photon habilitado, geocodificação indisponível. |
| OSRM, servidor | GET `/route/v1/driving/{lon,lat;lon,lat}?overview=full&geometries=geojson&steps=true`; `Routing:OsrmBaseUrl` HTTPS, demo em Development. | Coordenadas de origem/destino; usa distância, geometria e manobras. Não envia altura/peso/eixos como restrições ao grafo. | Timeout 20 s; falha não gera percurso fictício. Throttle local de 1 s nos hosts públicos reconhecidos. Perfil do grafo não muda porque a aplicação chama a viagem de Pesado. |
| Cache de rotas, servidor | IMemoryCache; por entrega/perfil e por parâmetros; recálculo por código/posição/perfil. | Planos e geometria apenas no processo; nenhuma persistência relacional. | Entrega/plano: sliding 30 min e absoluto 1 h; recálculo: 5/10 min. Cache por entrega não inclui versão/tenant; troca de veículo não invalida explicitamente a entrada. Não é cache distribuído nem garantia de autorização. |
| Google Routes, servidor | POST `routes.googleapis.com/directions/v2:computeRoutes`; chave em header; DRIVE e TRAFFIC_AWARE; field mask de duração/duração estática/descrição. | Coordenadas atuais/destino; usa diferença de duração para atraso estimado. | Timeout 5 s. Sem chave/resultado, retorna indisponibilidade e estimativa base, sem inventar congestionamento. Não representa restrição pesada nem camada visual de tráfego. Quota/faturamento efetivos não consultados. |
| OpenStreetMap tiles, navegador | Leaflet usa `https://tile.openstreetmap.org/{z}/{x}/{y}.png`; URL escrita nas views, com atribuição. | Coordenada do tile, IP/referer e metadados da requisição; imagem raster. | Não é API de rota nem serviço de tráfego. Nenhuma integração de cache offline de tiles foi comprovada. Política pública impede presumir download em massa/SLA. |
| Esri tiles, navegador | World_Dark_Gray_Base e World_Imagery em `server.arcgisonline.com`, escritos nas duas views de mapas. | Tile e metadados da requisição; imagem raster. | Não são substituíveis somente pelas chaves Photon/OSRM. Direitos de uso, limites e conta de destino não foram validados; nenhuma contratação inferida. |
| ViaCEP, navegador | GET `https://viacep.com.br/ws/{cep}/json/`, CEP de oito dígitos no blur. | CEP; preenche endereço quando disponível. | Formato local não valida existência. Resposta de erro/rede não equivale a endereço confirmado; não há teste real de disponibilidade. |
| Bibliotecas/CDNs/fontes, navegador | Bootstrap 5.3.2, ícones 1.11.3, Leaflet 1.9.4, cliente SignalR 6.0.25; Google Fonts; bibliotecas locais adicionais. | HTTP externo para scripts, estilos/fontes; metadados do navegador. | Dependência de rede/cache do navegador; não implica funcionamento offline. Cliente SignalR 6 com servidor .NET 10 precisa ensaio funcional, sem concluir incompatibilidade por diferença de versão apenas. |
| SignalR, aplicação | Grupos por código em `/trackingHub` e `/telemetryHub`; métodos/eventos descritos acima. | Checkpoints/POD e amostras de posição conforme projeção; identificador de grupo. | Não foi localizado backplane ou persistência da fila de mensagens. Hub de tracking é público por código; telemetria tem autorização. Sem garantia de replay de eventos. |
| PDF, servidor | QuestPDF e assets Skia/HarfBuzz; Community selecionada em Program. | Dados cadastrais/POD são usados na geração local de bytes; não foi localizada API remota de geração. | Dependências nativas/fontes/licença precisam validação no host/finalidade. A propriedade de gerar bytes não prova autenticidade criptográfica ou validade jurídica. |
| SMS, push, pagamento, central de socorro | Nenhum provedor/contrato funcional localizado nos serviços e registros DI lidos. | Não há envio externo implementado demonstrado. | Alertas persistidos na aplicação não significam notificação por esses canais. |

O depot Cajamar tem coordenadas constantes no geocodificador. Outros endereços dependem do provedor selecionado. Os timeouts e throttles são mecanismos do código, não comprovação de conformidade com quota ou capacidade de serviço público; também não coordenam todas as instâncias de um SaaS.

### Configuração e privacidade antes de um piloto

- Definir provedor contratado ou próprio, volume de veículos, frequência de amostras, retenção e orçamento antes de cotar custo mensal. Nenhum preço foi inventado e nenhuma API/conta foi ativada.
- Endereços são enviados ao geocodificador e coordenadas ao roteador/tráfego. Decidir finalidade, minimização, transparência e contrato do destino; não usar dados reais em demos públicas como se estivessem anonimizados.
- O código pode combinar resultado Google com Leaflet/Esri/OSM e mantê-lo em cache por sete dias. As políticas Google têm regras de cache, atribuição e exibição. A compatibilidade depende do produto/contrato e está **pendente de revisão**, não aprovada por este diagnóstico.
- URL em configuração não é autorização para redirecionar tráfego de produção. Validar endpoint, TLS, segredos e contingência sem imprimir chave/connection string.
- SQL Server é referência do projeto; SQLite/InMemory não substituem ensaio no provedor de destino. Npgsql continua preservado e sem homologação real nesta fase.

## 13. Offline: contratos desconectados e comportamento de falha

Fontes: [pwa-offline.js](../src/MglConnect.Web/wwwroot/js/pwa-offline.js), [sw.js](../src/MglConnect.Web/wwwroot/sw.js), [tracker.js](../src/MglConnect.Web/wwwroot/js/tracker.js) e [Navegar](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml). Trata-se de confronto estático, sem execução browser.

| Etapa | O que existe | Ligação ausente ou limite |
|---|---|---|
| Captura GPS | Geolocation no cockpit e emissão SignalR online. | Busca não localizou consumidores de `MglOfflineStorage.addGPS`; fila `gpsOfflineQueue` inicia vazia sem produtor localizado. |
| Armazenamento | Módulo abre `mgl-offline-db` v1, stores `gpsQueue` e `podQueue`. | Worker abre a mesma versão, mas cria `pendingSync` e `gpsQueue`; a ordem de abertura pode deixar store esperado ausente. |
| Identidade do item | Métodos de storage criam registros GPS/POD com timestamp/ID. | Não registram os mesmos campos `url`, `method`, `data` que o worker espera; também não estabelecem contrato de usuário/tenant/entrega por item. |
| Recuperação | `init` carrega GPS; há métodos de limpeza. | Não reidrata POD na mesma rotina; `clearAll` abre transação apenas de GPS e tenta acessar POD. Não demonstra recuperação após reload. |
| Agendamento | Worker escuta evento `sync-entregas`. | Não foi localizado registro `sync.register` ligando a interface ao evento. Listener isolado não agenda sincronização. |
| Reenvio | Worker faz fetch e tenta excluir item; tracker reemite fila em memória. | Worker não verifica `response.ok` antes de excluir; transação atravessa await de rede. São riscos de implementação a reproduzir futuramente, não teste executado. Tracker limpa fila sem recibo durável. |
| Resposta offline de API | Worker devolve JSON de erro quando fetch falha. | Response usa status padrão 200 e promete sincronização futura sem demonstrar fila preenchida. Consumidor precisa validar semântica, não apenas status HTTP. |
| POD | Canvas converte para PNG quando há desenho e submete formulário. | Nenhuma chamada de persistência offline da assinatura localizada; servidor exige conclusão online. |
| Emergência | FormData mantém chave para reenvio; UI só exibe sucesso com `response.ok` e `registrado === true`. | Sem rede não garante registro/atendimento. Não há fila de socorro ou canal externo; alerta local não é alerta recebido. |
| Assets | Worker pré-carrega parte dos recursos locais. | Pré-cache filtra URLs externas; não mantém automaticamente todas as telas, scripts/CDNs e tiles necessários ao cockpit offline. |

Portanto, **offline é estrutura inicial, não fluxo concluído**. O alvo aprovado deve definir um armazenamento/versionamento, vínculo de identidade/viagem, idempotência, recibo de servidor, retry/conflitos, quota, retenção e estados visíveis distintos. GPS com aplicação fechada não é consequência automática de instalar PWA. O [backlog B09–B12](./FASE_2_BACKLOG_PRIORIZADO.md) contém os critérios antes de implementação.

## 14. Referências externas consultadas e limites comerciais

Leitura documental em 28/10/2026; não foram executadas consultas de endereços/rotas/CEP, teste de chave, cobrança ou contratação. Fontes fundamentam os limites indicados, não provam funcionamento deste produto:

| Fonte primária | O que sustenta / limite da consulta |
|---|---|
| [Photon — README do projeto](https://github.com/komoot/photon) | Demo permite uso razoável, pode limitar/bloquear e não garante disponibilidade; instância própria é alternativa, não instalada nesta fase. |
| [OSRM HTTP API v5.24](https://project-osrm.org/docs/v5.24.0/api/) | Contrato route/coordenadas e perfil determinado na preparação do grafo. A versão do serviço público efetivo não foi identificada. |
| [OSMF — política de tiles](https://operations.osmfoundation.org/policies/tiles/) | URL/atribuição/cache, capacidade limitada, ausência de SLA e restrições a download em massa. Não foi feito ensaio de conformidade do navegador. |
| [Google — políticas de Geocoding](https://developers.google.com/maps/documentation/geocoding/policies) | Regras de cache, atribuição e exibição; contrato aplicável deve ser revisto para a combinação com mapas de terceiros. Não foi emitida aprovação jurídica. |
| [MDN — Background Synchronization](https://developer.mozilla.org/en-US/docs/Web/API/Background_Synchronization_API) | Contexto seguro, disponibilidade limitada e registro da tarefa pela aplicação. Não garante execução em todos os dispositivos nem GPS contínuo com app fechado. |
| [ViaCEP — documentação oficial](https://viacep.com.br/) | Formato de oito dígitos, erro de formato e retorno para CEP inexistente; não comprova SLA. |
| [QuestPDF — termos e licenças](https://www.questpdf.com/license/) | Portal oficial para seleção/licença; Community configurada no código não prova elegibilidade comercial da organização ou da versão usada. |

A leitura da página Google Routes `/maps/documentation/routes/traffic` e da página FluentAssertions `/license/` falhou pelo mecanismo de consulta. **Quotas, preços, termos específicos e elegibilidade comercial desses itens não foram validados**, e não houve tentativa de contornar o acesso. Isso não bloqueia o inventário local; é pendência de B05/B15 antes de contratação/publicação. Direitos e limites Esri/CDNs/fontes também permanecem para validação pelo responsável do ambiente de destino.

**Encerramento:** mapa de ações/views preservado e integrações complementadas; [validação e preservação final](./FASE_2_INVENTARIO_FUNCIONAL.md#13-encerramento-reproduzível-da-fase-2--28102026). Produto sem alteração; Fase 1B, publicação e contratação não iniciadas.
