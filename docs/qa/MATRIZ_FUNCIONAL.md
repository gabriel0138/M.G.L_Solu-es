# Matriz funcional — primeira entrega, 2026-11-04

Estados referem-se aos cenários descritos, não a todos os botões possíveis de uma página. APROVADO exige assertions executadas; NÃO TESTADO não é aprovação por compilação. Ver [resultados e comandos](RESULTADOS_E2E.md).

| Rota/fluxo | Perfil | Ação e resultado esperado | Estado/evidência |
|---|---|---|---|
| `/Account/Login` | Admin, Motorista, Cliente | Autenticar e redirecionar ao painel de cada perfil | APROVADO — Chromium com cookies separados |
| `/Dashboard` direto | Motorista, Cliente | Impedir acesso administrativo | APROVADO — 403 ou redirecionamento AccessDenied |
| `/api/gps/historico/1` | Anônimo | Não fornecer histórico | APROVADO — HTTP 401 |
| `/api/gps/historico/999999999` | Motorista, Cliente | Não fornecer recurso arbitrário | APROVADO — HTTP 404 |
| `/Cliente/Pedido/Novo` → `/Cliente/Pedido/{id}` | Cliente | Salvar pedido e mostrar vínculo à entrega depois do despacho | APROVADO — formulário real e persistência |
| `/Pedidos`, `/Pedidos/Details/{id}` → `/Entregas/Create` | Admin | Aprovar e despachar com motorista/veículo próprios | APROVADO — nova entrega persistida |
| `/Entregas/Details/{id}`, `/Entregas/EmitirCodigoTurno` | Admin | Emitir código e preservar código ao repetir mesma atribuição | APROVADO — código real usado pelo motorista |
| `/Entregas` → `/Entregas/IniciarTurno` → `/Entregas/Navegar/{id}` | Motorista | Iniciar turno, acessar cockpit autorizado e conectar hub | APROVADO — turno e SignalR reais |
| `/Entregas/Navegar/{id}` | Motorista | Botão GPS recebe clique e envia posição sintética até confirmação | CORRIGIDO E APROVADO — ponteiro CDP, badge recebido e mapa da operação |
| Cockpit/fixture de curva | Motorista/ensaio local | Exibir amostras sem inventar diagonal e atualizar Leaflet após resize do container | CORRIGIDO E APROVADO — JS e Chromium |
| Cockpit ativo | Motorista | Sem overflow nas oito larguras, GPS sem sobreposição; ações alcançáveis em viewport curto | CORRIGIDO E APROVADO — claro/escuro, detalhes expandidos e hit testing |
| `/Entregas/PerfilMotorista` | Motorista | Renderizar perfil próprio sem overflow em 360/390 px | APROVADO — abertura/assertions/capturas; perfil somente leitura |
| `/Operacoes`, `/Operacoes/Posicoes` | Admin | Exibir coordenada persistida e retirar viagem encerrada | APROVADO — API e mapa reais |
| `/api/entregas/{id}/pod` | Motorista com turno | Assinar, concluir, recuperar resposta perdida sem duplicar | APROVADO — assinatura via ponteiro e recibo idempotente |
| `/Cliente/Pedido/{id}/Comprovante` | Cliente vinculado | Baixar PDF autorizado após entrega | APROVADO — content type e assinatura `%PDF` |
| `/Rastreio/{codigo}` | Público | Ver estado final sem documento privado do POD | APROVADO — SignalR e projeção mínima exercitados |
| GPS offline/IndexedDB | Motorista | Preservar horário/UUID, reiniciar app e reenviar sem duplicação | APROVADO — harness GPS offline, incluindo Service Worker real |
| Pedido/PDF/SignalR de outro cliente ou tenant | Usuários sintéticos C# | Recusar acesso por servidor | APROVADO nos testes C#; cenário entre tenants no navegador NÃO TESTADO |
| Rota externa homologada para veículo pesado | Operação/Motorista | Somente orientar com evidência suficiente | BLOQUEADO — provedor desativado; tela mantém orientação suspensa |
| Perfil Intermediador no navegador | Intermediador | Percorrer vínculo autorizado e pedidos | NÃO TESTADO — não há esse perfil no seed navegador usado |
| Telas administrativas restantes, sessão expirada, emergência efetivamente enviada, logout/troca de conta | Diversos | Jornadas próprias e verificações de persistência/isolamento | NÃO TESTADO no navegador nesta entrega |

## Inventário automático de views

Inventário de todos os arquivos Razor encontrado em disco. Parciais/layout não representam rotas independentes. Para linhas APROVADO, vale somente o cenário da tabela acima.

| View | Estado no navegador nesta entrega |
|---|---|
| [Account/AccessDenied.cshtml](../../src/MglConnect.Web/Views/Account/AccessDenied.cshtml) | APROVADO somente para recusa de acesso direto |
| [Account/Login.cshtml](../../src/MglConnect.Web/Views/Account/Login.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [ClientePortal/Index.cshtml](../../src/MglConnect.Web/Views/ClientePortal/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [ClientePortal/NovoPedido.cshtml](../../src/MglConnect.Web/Views/ClientePortal/NovoPedido.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [ClientePortal/PedidoDetalhes.cshtml](../../src/MglConnect.Web/Views/ClientePortal/PedidoDetalhes.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [ClientePortal/Pedidos.cshtml](../../src/MglConnect.Web/Views/ClientePortal/Pedidos.cshtml) | NÃO TESTADO |
| [ClientePortal/SemCliente.cshtml](../../src/MglConnect.Web/Views/ClientePortal/SemCliente.cshtml) | NÃO TESTADO |
| [ClientePortal/_ClientOperationsPanel.cshtml](../../src/MglConnect.Web/Views/ClientePortal/_ClientOperationsPanel.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Clientes/Create.cshtml](../../src/MglConnect.Web/Views/Clientes/Create.cshtml) | NÃO TESTADO |
| [Clientes/Details.cshtml](../../src/MglConnect.Web/Views/Clientes/Details.cshtml) | NÃO TESTADO |
| [Clientes/Edit.cshtml](../../src/MglConnect.Web/Views/Clientes/Edit.cshtml) | NÃO TESTADO |
| [Clientes/Index.cshtml](../../src/MglConnect.Web/Views/Clientes/Index.cshtml) | NÃO TESTADO |
| [Dashboard/Index.cshtml](../../src/MglConnect.Web/Views/Dashboard/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Dashboard/_OperationalPanel.cshtml](../../src/MglConnect.Web/Views/Dashboard/_OperationalPanel.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Entregas/AlertasEmergencia.cshtml](../../src/MglConnect.Web/Views/Entregas/AlertasEmergencia.cshtml) | NÃO TESTADO |
| [Entregas/CodigoTurno.cshtml](../../src/MglConnect.Web/Views/Entregas/CodigoTurno.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Entregas/Create.cshtml](../../src/MglConnect.Web/Views/Entregas/Create.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Entregas/Details.cshtml](../../src/MglConnect.Web/Views/Entregas/Details.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Entregas/Index.cshtml](../../src/MglConnect.Web/Views/Entregas/Index.cshtml) | NÃO TESTADO |
| [Entregas/IndexMotorista.cshtml](../../src/MglConnect.Web/Views/Entregas/IndexMotorista.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Entregas/Navegar.cshtml](../../src/MglConnect.Web/Views/Entregas/Navegar.cshtml) | CORRIGIDO E APROVADO — GPS/layout nos cenários acima |
| [Entregas/PerfilMotorista.cshtml](../../src/MglConnect.Web/Views/Entregas/PerfilMotorista.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Frota/Index.cshtml](../../src/MglConnect.Web/Views/Frota/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Frota/MotoristaForm.cshtml](../../src/MglConnect.Web/Views/Frota/MotoristaForm.cshtml) | NÃO TESTADO |
| [Frota/ResourceDetails.cshtml](../../src/MglConnect.Web/Views/Frota/ResourceDetails.cshtml) | NÃO TESTADO |
| [Frota/VeiculoForm.cshtml](../../src/MglConnect.Web/Views/Frota/VeiculoForm.cshtml) | NÃO TESTADO |
| [Frota/_RosterPanel.cshtml](../../src/MglConnect.Web/Views/Frota/_RosterPanel.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [GpsHistorico/Index.cshtml](../../src/MglConnect.Web/Views/GpsHistorico/Index.cshtml) | NÃO TESTADO |
| [Home/Index.cshtml](../../src/MglConnect.Web/Views/Home/Index.cshtml) | NÃO TESTADO |
| [Home/Privacy.cshtml](../../src/MglConnect.Web/Views/Home/Privacy.cshtml) | NÃO TESTADO |
| [Operacoes/Index.cshtml](../../src/MglConnect.Web/Views/Operacoes/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Pedidos/Create.cshtml](../../src/MglConnect.Web/Views/Pedidos/Create.cshtml) | NÃO TESTADO |
| [Pedidos/Details.cshtml](../../src/MglConnect.Web/Views/Pedidos/Details.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Pedidos/Index.cshtml](../../src/MglConnect.Web/Views/Pedidos/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Rastreio/Index.cshtml](../../src/MglConnect.Web/Views/Rastreio/Index.cshtml) | APROVADO somente para o cenário acima; demais ações NÃO TESTADAS |
| [Roteamento/Index.cshtml](../../src/MglConnect.Web/Views/Roteamento/Index.cshtml) | NÃO TESTADO |
| [RouteAssessment/Index.cshtml](../../src/MglConnect.Web/Views/RouteAssessment/Index.cshtml) | NÃO TESTADO |
| [Shared/Error.cshtml](../../src/MglConnect.Web/Views/Shared/Error.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_CommandAccount.cshtml](../../src/MglConnect.Web/Views/Shared/_CommandAccount.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_CommandNavigation.cshtml](../../src/MglConnect.Web/Views/Shared/_CommandNavigation.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_CommandStatus.cshtml](../../src/MglConnect.Web/Views/Shared/_CommandStatus.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_DriverGpsSync.cshtml](../../src/MglConnect.Web/Views/Shared/_DriverGpsSync.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_DriverOfflineQueueState.cshtml](../../src/MglConnect.Web/Views/Shared/_DriverOfflineQueueState.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_GpsHistory.cshtml](../../src/MglConnect.Web/Views/Shared/_GpsHistory.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_Layout.cshtml](../../src/MglConnect.Web/Views/Shared/_Layout.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [Shared/_ValidationScriptsPartial.cshtml](../../src/MglConnect.Web/Views/Shared/_ValidationScriptsPartial.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [_ViewImports.cshtml](../../src/MglConnect.Web/Views/_ViewImports.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |
| [_ViewStart.cshtml](../../src/MglConnect.Web/Views/_ViewStart.cshtml) | COMPOSIÇÃO — ver cenário da tela hospedeira; sem aprovação independente |

## Inventário automático de actions MVC/APIs

Extração estática de assinaturas públicas e atributos de rota/verbos nos controllers. Não é descoberta de endpoints em runtime nem teste de acessibilidade. Atributos de classe e convenções MVC compõem o caminho efetivo; use a tabela funcional acima para rotas efetivamente exercitadas. Métodos auxiliares retornando outros tipos não foram incluídos.

| Fonte | Action | Atributos próximos declarados |
|---|---|---|
| [AccountController:26](../../src/MglConnect.Web/Controllers/AccountController.cs#L26) | Login | [HttpGet] |
| [AccountController:47](../../src/MglConnect.Web/Controllers/AccountController.cs#L47) | Login | [HttpPost] [ValidateAntiForgeryToken] |
| [AccountController:112](../../src/MglConnect.Web/Controllers/AccountController.cs#L112) | Logout | [HttpPost] [ValidateAntiForgeryToken] |
| [AccountController:119](../../src/MglConnect.Web/Controllers/AccountController.cs#L119) | AccessDenied | [HttpGet] |
| [AuthController:37](../../src/MglConnect.Web/Controllers/Api/AuthController.cs#L37) | Login | [HttpPost("login")] |
| [EntregasApiController:47](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L47) | GetEntregasMotorista | [HttpGet("motorista/{motoristaId:int}")] |
| [EntregasApiController:169](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L169) | GetDetalhes | [HttpGet("{id:int}")] |
| [EntregasApiController:235](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L235) | AtualizarStatus | [HttpPut("{id:int}/status")] |
| [EntregasApiController:334](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L334) | AdicionarCheckpoint | [HttpPost("{id:int}/checkpoint")] |
| [EntregasApiController:380](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L380) | FinalizarPod | [HttpPost("{id:int}/pod")] [RequestSizeLimit(2 * 1024 * 1024)] |
| [EntregasApiController:427](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L427) | ObterTrafegoEta | [HttpGet("{id:int}/trafego-eta")] |
| [EntregasApiController:504](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L504) | DownloadPodPdf | [HttpGet("{id:int}/pod-pdf")] [Authorize(Roles = "Admin,Motorista,Cliente,Intermediador")] |
| [EntregasApiController:532](../../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs#L532) | ObterEsgEntrega | [HttpGet("{id:int}/esg")] [Authorize(Roles = "Admin")] |
| [GpsController:19](../../src/MglConnect.Web/Controllers/Api/GpsController.cs#L19) | Sincronizar | [HttpPost("sincronizar"), Authorize(Roles = "Motorista", AuthenticationSchemes = MglConnectApiBearerDefaults.Scheme)] [RequestSizeLimit(65536)] |
| [GpsController:52](../../src/MglConnect.Web/Controllers/Api/GpsController.cs#L52) | Historico | [HttpGet("historico/{entregaId:int}")] [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme + "," + MglConnectApiBearerDefaults.Scheme)] |
| [RastreioApiController:21](../../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs#L21) | Consultar | [HttpGet("{codigo}")] |
| [ClientePortalController:51](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L51) | NovoPedido | [HttpGet("Pedido/Novo")] |
| [ClientePortalController:63](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L63) | NovoPedido | [HttpPost("Pedido/Novo")] [ValidateAntiForgeryToken] |
| [ClientePortalController:148](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L148) | Index | [HttpGet("")] [HttpGet("Dashboard")] [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] |
| [ClientePortalController:157](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L157) | Painel | [HttpGet("Painel")] [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] |
| [ClientePortalController:177](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L177) | Pedidos | [HttpGet("Pedidos")] |
| [ClientePortalController:218](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L218) | PedidoDetalhes | [HttpGet("Pedido/{id:int}")] |
| [ClientePortalController:245](../../src/MglConnect.Web/Controllers/ClientePortalController.cs#L245) | Comprovante | [HttpGet("Pedido/{id:int}/Comprovante")] [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] |
| [ClientesController:23](../../src/MglConnect.Web/Controllers/ClientesController.cs#L23) | Index | Convenção/atributos da classe |
| [ClientesController:41](../../src/MglConnect.Web/Controllers/ClientesController.cs#L41) | Create | Convenção/atributos da classe |
| [ClientesController:48](../../src/MglConnect.Web/Controllers/ClientesController.cs#L48) | Create | [HttpPost] [ValidateAntiForgeryToken] |
| [ClientesController:67](../../src/MglConnect.Web/Controllers/ClientesController.cs#L67) | Edit | Convenção/atributos da classe |
| [ClientesController:79](../../src/MglConnect.Web/Controllers/ClientesController.cs#L79) | Edit | [HttpPost] [ValidateAntiForgeryToken] |
| [ClientesController:124](../../src/MglConnect.Web/Controllers/ClientesController.cs#L124) | Details | Convenção/atributos da classe |
| [ContainerHealthController:12](../../src/MglConnect.Web/Controllers/ContainerHealthController.cs#L12) | Live | [HttpGet("health/live")] |
| [ContainerHealthController:15](../../src/MglConnect.Web/Controllers/ContainerHealthController.cs#L15) | Ready | [HttpGet("health/ready")] |
| [DashboardController:18](../../src/MglConnect.Web/Controllers/DashboardController.cs#L18) | Index | Convenção/atributos da classe |
| [DashboardController:29](../../src/MglConnect.Web/Controllers/DashboardController.cs#L29) | Painel | [HttpGet] |
| [DriverOfflineController:17](../../src/MglConnect.Web/Controllers/DriverOfflineController.cs#L17) | Contexto | [HttpGet("Contexto")] |
| [EntregasController:47](../../src/MglConnect.Web/Controllers/EntregasController.cs#L47) | Index | Convenção/atributos da classe |
| [EntregasController:119](../../src/MglConnect.Web/Controllers/EntregasController.cs#L119) | IniciarTurno | [HttpPost] [ValidateAntiForgeryToken] |
| [EntregasController:144](../../src/MglConnect.Web/Controllers/EntregasController.cs#L144) | EncerrarTurno | [HttpPost] [ValidateAntiForgeryToken] |
| [EntregasController:161](../../src/MglConnect.Web/Controllers/EntregasController.cs#L161) | EmitirCodigoTurno | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:184](../../src/MglConnect.Web/Controllers/EntregasController.cs#L184) | CancelarCodigoTurno | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:196](../../src/MglConnect.Web/Controllers/EntregasController.cs#L196) | AlertasEmergencia | [HttpGet] [Authorize(Roles = "Admin")] |
| [EntregasController:214](../../src/MglConnect.Web/Controllers/EntregasController.cs#L214) | CriarAlertaEmergencia | [HttpPost] [ValidateAntiForgeryToken] |
| [EntregasController:265](../../src/MglConnect.Web/Controllers/EntregasController.cs#L265) | ReconhecerAlertaEmergencia | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:282](../../src/MglConnect.Web/Controllers/EntregasController.cs#L282) | AtualizarStatusAlertaEmergencia | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:297](../../src/MglConnect.Web/Controllers/EntregasController.cs#L297) | PerfilMotorista | [HttpGet] |
| [EntregasController:321](../../src/MglConnect.Web/Controllers/EntregasController.cs#L321) | Create | [Authorize(Roles = "Admin")] |
| [EntregasController:350](../../src/MglConnect.Web/Controllers/EntregasController.cs#L350) | Create | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:553](../../src/MglConnect.Web/Controllers/EntregasController.cs#L553) | Details | [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] |
| [EntregasController:604](../../src/MglConnect.Web/Controllers/EntregasController.cs#L604) | RotacionarCodigoRastreio | [HttpPost] [Authorize(Roles = "Admin")] [ValidateAntiForgeryToken] |
| [EntregasController:655](../../src/MglConnect.Web/Controllers/EntregasController.cs#L655) | Navegar | [HttpGet] [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] |
| [EntregasController:705](../../src/MglConnect.Web/Controllers/EntregasController.cs#L705) | FinalizarEntregaPOD | [HttpPost] [ValidateAntiForgeryToken] |
| [EntregasController:785](../../src/MglConnect.Web/Controllers/EntregasController.cs#L785) | AtribuirVeiculo | [HttpPost] [ValidateAntiForgeryToken] [Authorize(Roles = "Admin")] |
| [EntregasController:898](../../src/MglConnect.Web/Controllers/EntregasController.cs#L898) | AdicionarCheckpoint | [HttpPost] [ValidateAntiForgeryToken] |
| [EntregasController:953](../../src/MglConnect.Web/Controllers/EntregasController.cs#L953) | AtribuirMotorista | [HttpPost] [ValidateAntiForgeryToken] [Authorize(Roles = "Admin")] |
| [FrotaController:19](../../src/MglConnect.Web/Controllers/FrotaController.cs#L19) | Index | [HttpGet] |
| [FrotaController:23](../../src/MglConnect.Web/Controllers/FrotaController.cs#L23) | Painel | [HttpGet] |
| [FrotaManagementController:49](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L49) | NovoVeiculo | [HttpGet] |
| [FrotaManagementController:52](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L52) | EditarVeiculo | [HttpGet] |
| [FrotaManagementController:59](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L59) | NovoVeiculo | [HttpPost, ValidateAntiForgeryToken] |
| [FrotaManagementController:62](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L62) | EditarVeiculo | [HttpPost, ValidateAntiForgeryToken] |
| [FrotaManagementController:118](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L118) | NovoMotorista | [HttpGet] |
| [FrotaManagementController:121](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L121) | EditarMotorista | [HttpGet] |
| [FrotaManagementController:128](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L128) | NovoMotorista | [HttpPost, ValidateAntiForgeryToken] |
| [FrotaManagementController:131](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L131) | EditarMotorista | [HttpPost, ValidateAntiForgeryToken] |
| [FrotaManagementController:180](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L180) | Veiculo | [HttpGet] |
| [FrotaManagementController:182](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L182) | Motorista | [HttpGet] |
| [FrotaManagementController:206](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L206) | SituacaoVeiculo | [HttpPost, ValidateAntiForgeryToken] |
| [FrotaManagementController:209](../../src/MglConnect.Web/Controllers/FrotaManagementController.cs#L209) | SituacaoMotorista | [HttpPost, ValidateAntiForgeryToken] |
| [GpsHistoricoController:19](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L19) | Index | [HttpGet] |
| [GpsHistoricoController:40](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L40) | Dados | [HttpGet] |
| [GpsHistoricoController:55](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L55) | Arquivar | [HttpPost,ValidateAntiForgeryToken] |
| [GpsHistoricoController:63](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L63) | Compactar | [HttpPost,ValidateAntiForgeryToken] |
| [GpsHistoricoController:71](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L71) | Arquivo | [HttpGet] |
| [GpsHistoricoController:81](../../src/MglConnect.Web/Controllers/GpsHistoricoController.cs#L81) | Exportar | [HttpGet,EnableRateLimiting("gps-export")] |
| [HomeController:20](../../src/MglConnect.Web/Controllers/HomeController.cs#L20) | Index | Convenção/atributos da classe |
| [HomeController:38](../../src/MglConnect.Web/Controllers/HomeController.cs#L38) | Privacy | Convenção/atributos da classe |
| [HomeController:44](../../src/MglConnect.Web/Controllers/HomeController.cs#L44) | Error | [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)] |
| [OperacoesController:17](../../src/MglConnect.Web/Controllers/OperacoesController.cs#L17) | Index | [HttpGet("")] |
| [OperacoesController:20](../../src/MglConnect.Web/Controllers/OperacoesController.cs#L20) | Posicoes | [HttpGet("Posicoes")] |
| [OperationalHealthController:11](../../src/MglConnect.Web/Controllers/OperationalHealthController.cs#L11) | Index | [HttpGet("operacional/saude")] |
| [PedidosController:23](../../src/MglConnect.Web/Controllers/PedidosController.cs#L23) | Index | Convenção/atributos da classe |
| [PedidosController:53](../../src/MglConnect.Web/Controllers/PedidosController.cs#L53) | Create | Convenção/atributos da classe |
| [PedidosController:61](../../src/MglConnect.Web/Controllers/PedidosController.cs#L61) | Create | [HttpPost] [ValidateAntiForgeryToken] |
| [PedidosController:87](../../src/MglConnect.Web/Controllers/PedidosController.cs#L87) | Details | Convenção/atributos da classe |
| [PedidosController:106](../../src/MglConnect.Web/Controllers/PedidosController.cs#L106) | AtualizarStatus | [HttpPost] [ValidateAntiForgeryToken] |
| [RastreioController:20](../../src/MglConnect.Web/Controllers/RastreioController.cs#L20) | Index | [HttpGet] [Route("Rastreio")] [Route("Rastreio/{codigo}")] |
| [RoteamentoController:30](../../src/MglConnect.Web/Controllers/RoteamentoController.cs#L30) | Index | [HttpGet] |
| [RouteAssessmentController:20](../../src/MglConnect.Web/Controllers/RouteAssessmentController.cs#L20) | Index | [HttpGet("Rotas/Entrega/{id:int}")] |
| [RouteAssessmentController:37](../../src/MglConnect.Web/Controllers/RouteAssessmentController.cs#L37) | Profile | [HttpPost("Rotas/Entrega/{id:int}/Perfil"),ValidateAntiForgeryToken] |
| [RouteAssessmentController:60](../../src/MglConnect.Web/Controllers/RouteAssessmentController.cs#L60) | Calculate | [HttpPost("Rotas/Entrega/{id:int}/Calcular"),ValidateAntiForgeryToken,EnableRateLimiting("route-assess")] |
