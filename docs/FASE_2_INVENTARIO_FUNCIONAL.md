# Fase 2 — Inventário funcional e evidências

Levantamento: 27/10/2026. Consolidação documental: 28/10/2026. **FASE_2_DOCUMENTACAO_CONCLUIDA — aguardando revisão humana.** Escopo: produto em **somente leitura**; alterações limitadas aos quatro relatórios da Fase 2 e validação autorizada de restore/build/test. **Fase 1B não executada**; não houve nova auditoria ofensiva. A leitura não representa homologação de segurança, operação real ou produção. As alterações de produto preexistentes da Fase 1A integram o estado observado, sem serem modificadas nesta documentação.

Este inventário mantém os vínculos entre código, capacidade, testes e limites. Complementos: [mapa de APIs e integrações](./FASE_2_MAPA_APIS_E_INTEGRACOES.md), [arquitetura-alvo](./FASE_2_ARQUITETURA_ALVO_SAAS.md) e [backlog](./FASE_2_BACKLOG_PRIORIZADO.md). O confronto documental e a validação final estão nas seções 12 e 13; prevalecem sobre pendências de complementação das versões iniciais.

## 1. Método, validação e sete estados

Foram lidos integralmente os modelos, [MglDbContext](../src/MglConnect.Web/Data/MglDbContext.cs), [DatabaseSchemaUpdater](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs), [RelationalIntegrityUpdater](../src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs), [DbInitializer](../src/MglConnect.Web/Data/DbInitializer.cs), autenticação, middlewares, serviços de turno/emergência/tracking/PDF e os 21 arquivos de testes. Controladores dos fluxos e hubs também foram lidos. Roteamento extenso, frontend e documentação histórica receberam leitura dos trechos pertinentes e buscas; não se afirma leitura integral de todos esses materiais.

Na elaboração inicial do inventário não foram executados restore, build, testes, aplicação, SQL, sudo ou Docker. Os seguintes resultados de 27/10 foram fornecidos pelo solicitante. A retomada autorizada em 28/10 reexecutou restore/build/test, com resultados próprios na seção 13, sem iniciar aplicação ou banco externo:

| Verificação informada | Resultado | Limite de interpretação |
|---|---|---|
| SDK | 10.0.403 | Identifica a execução informada; não foi reinstalado ou consultado por esta autoria. |
| Restore | Exit 0 | Não equivale a validação funcional. |
| Build | Exit 0; 0 warnings; 0 errors | Não comprova operação em produção. |
| Testes principais da Fase 2 | 98 aprovados; 0 falhos; 4 ignorados; conexão SQL Server removida do ambiente da execução | Os quatro ignorados são integração SQL Server opcional. Não foram aprovados nesta execução. |
| SQL Server na Fase 1A | 102 aprovados no relato histórico | Somente histórico da [Fase 1A](./FASE_1A_INTEGRIDADE_DADOS_SQLSERVER.md); não é resultado de execução SQL Server na Fase 2. |

O inventário usa sete estados operacionais, com cobertura discriminada separadamente. Os rótulos abaixo são a taxonomia adotada neste documento; implementação não significa homologação integral:

| Estado | Critério |
|---|---|
| **Implementado** | Caminho funcional identificável no código, com entradas, regras e saída; cobertura e limites devem ser lidos na mesma linha ou na matriz de testes. |
| **Parcial** | Existe caminho utilizável ou regra implementada, mas faltam partes relevantes da capacidade anunciada. |
| **Simulado** | Resultado explicitamente fictício, destinado a demonstração; não representa dado operacional real. |
| **Estrutura inicial** | Existem modelos, métodos ou componentes, mas falta ligação suficiente para completar o fluxo. |
| **Planejado** | Requisito ou desenho futuro identificado, sem implementação funcional correspondente localizada. |
| **Ausente** | Não foi localizada implementação da capacidade no escopo lido e pesquisado. |
| **Não verificado** | Há configuração ou alegação, mas a evidência disponível não permite concluir sobre funcionamento no ambiente necessário. |

## 2. Entidades e dados

O contexto expõe **11 entidades de aplicação e DataProtectionKeys**, totalizando 12 conjuntos/tabelas do modelo. [ITenantOwned](../src/MglConnect.Web/Models/ITenantOwned.cs) é uma interface; [ErrorViewModel](../src/MglConnect.Web/Models/ErrorViewModel.cs) não é entidade persistida.

| Entidade | Identidade e dados | Vínculos e limites |
|---|---|---|
| [Empresa](../src/MglConnect.Web/Models/Empresa.cs) | GUID; nome, CNPJ, slug, plano, atividade e criação. | Slug único. Não há FK das demais entidades para empresa por TenantId. Não implementa ITenantOwned. |
| [Usuario](../src/MglConnect.Web/Models/Usuario.cs) | ID inteiro; tenant, nome, login, hash de senha, perfil, telefone, CPF, nascimento e atividade. | Cliente opcional; login único global. Não existe coleção de clientes representados. |
| [Cliente](../src/MglConnect.Web/Models/Cliente.cs) | ID inteiro; tenant, razão social, CNPJ, contato, endereço e atividade. | Vários pedidos; CNPJ único global. Cliente comercial não é automaticamente empresa/tenant. |
| [Pedido](../src/MglConnect.Web/Models/Pedido.cs) | ID inteiro; tenant, número, chave idempotente opcional, versão, descrição, valor, peso, dimensões, carga, origem/destino, data e estado. | Cliente obrigatório; zero ou uma entrega. Número único por tenant; chave idempotente única global quando preenchida. |
| [Entrega](../src/MglConnect.Web/Models/Entrega.cs) | ID inteiro; tenant, código de rastreio, destinatário/documento/endereço, estado, atribuição ativa, versão, prazos, veículo/carga e POD. | Pedido obrigatório; motorista/veículo opcionais; vários eventos. Rastreio único global. |
| [Veiculo](../src/MglConnect.Web/Models/Veiculo.cs) | ID inteiro; tenant, placa, modelo, capacidade, altura/largura/comprimento, peso, eixos, carga e atividade. | Placa única global. Pode aparecer em entregas e histórico operacional. |
| [EventoRastreio](../src/MglConnect.Web/Models/EventoRastreio.cs) | ID inteiro; instante, título, descrição, localidade textual, estado associado e ícone. | Entrega obrigatória. Sem tenant próprio; filtro depende da entrega. Localidade não é coordenada GPS estruturada. |
| [CodigoTurnoMotorista](../src/MglConnect.Web/Models/CodigoTurnoMotorista.cs) | GUID; tenant, hash, emissão/expiração/uso/cancelamento, tentativas e versão. | Entrega, motorista e emissor obrigatórios. Segredo emitido não é armazenado em texto aberto. |
| [TurnoMotorista](../src/MglConnect.Web/Models/TurnoMotorista.cs) | GUID; tenant, início, encerramento, atividade e versão. | Entrega, motorista, veículo e código obrigatórios. Turno operacional não constitui cálculo de jornada/folha. |
| [AlertaEmergencia](../src/MglConnect.Web/Models/AlertaEmergencia.cs) | GUID; tenant, idempotência, criação/estado, localização/precisão/instante, reconhecimento e versão. | Entrega/motorista obrigatórios; veículo/reconhecedor opcionais. |
| [AuditoriaOperacional](../src/MglConnect.Web/Models/AuditoriaOperacional.cs) | ID longo; tenant, ator, evento, tipo e identificador de entidade, detalhes e instante. | Identificador polimórfico e ator sem FK. Não há mecanismo append-only de banco declarado. |
| [DataProtectionKeys](../src/MglConnect.Web/Data/MglDbContext.cs) | Chaves de proteção de tickets/cookies. | Infraestrutura global, sem filtro de tenant; não confundir com credenciais de usuários. |

Dados pessoais/documentais aparecem em cadastro, destinatário, recebedor, assinatura e auditoria; localização estruturada é persistida nos alertas. A telemetria contínua utiliza estado volátil, sem entidade histórica GPS localizada. A assinatura POD fica na entrega como string; PDF é gerado sob demanda, sem repositório documental versionado identificado.

## 3. As 17 FKs e as ações de exclusão

Fonte: [OnModelCreating](../src/MglConnect.Web/Data/MglDbContext.cs). A relação pedido/cliente usa Cascade por convenção; as demais ações abaixo estão configuradas explicitamente. Esta é a definição do modelo, não consulta do catálogo de um banco instalado.

| Nº | Dependente / coluna | Principal | Exclusão do principal |
|---|---|---|---|
| 1 | Pedido.ClienteId | Cliente.Id | Cascade, por convenção |
| 2 | Usuario.ClienteId, opcional | Cliente.Id | SetNull |
| 3 | Entrega.PedidoId | Pedido.Id | Restrict |
| 4 | Entrega.MotoristaId, opcional | Usuario.Id | SetNull |
| 5 | Entrega.VeiculoId, opcional | Veiculo.Id | SetNull |
| 6 | EventoRastreio.EntregaId | Entrega.Id | Cascade |
| 7 | CodigoTurnoMotorista.EntregaId | Entrega.Id | Restrict |
| 8 | CodigoTurnoMotorista.MotoristaId | Usuario.Id | Restrict |
| 9 | CodigoTurnoMotorista.EmitidoPorUsuarioId | Usuario.Id | Restrict |
| 10 | TurnoMotorista.EntregaId | Entrega.Id | Restrict |
| 11 | TurnoMotorista.MotoristaId | Usuario.Id | Restrict |
| 12 | TurnoMotorista.VeiculoId | Veiculo.Id | Restrict |
| 13 | TurnoMotorista.CodigoTurnoId | CodigoTurnoMotorista.Id | Restrict |
| 14 | AlertaEmergencia.EntregaId | Entrega.Id | Restrict |
| 15 | AlertaEmergencia.MotoristaId | Usuario.Id | Restrict |
| 16 | AlertaEmergencia.VeiculoId, opcional | Veiculo.Id | Restrict |
| 17 | AlertaEmergencia.ReconhecidoPorUsuarioId, opcional | Usuario.Id | Restrict |

As seis relações básicas mais onze operacionais totalizam 17. A auditoria e TenantId não acrescentam FKs. Referências por ID provam existência do pai, mas **não garantem igualdade de tenant**, perfil Motorista, atividade, estado compatível ou correspondência entre código, entrega, veículo e turno. Essas regras dependem da aplicação e da consistência dos dados.

## 4. Índices e concorrência

Há 14 índices únicos de negócio no modelo, excluindo PKs. Seis têm filtro explícito. Fonte: [MglDbContext](../src/MglConnect.Web/Data/MglDbContext.cs), incluindo geradores de filtro específicos por provedor.

| Entidade / chave | Unicidade e filtro | Garantia / limite |
|---|---|---|
| Empresa.Slug | Único global | Não valida existência de memberships. |
| Usuario.Login | Único global | Consulta de login normaliza caixa; sem comparação de collation entre provedores nesta fase. |
| Cliente.Cnpj | Único global | Não é unicidade por tenant. |
| Veiculo.Placa | Único global | Não é unicidade por tenant. |
| Pedido.(TenantId, NumeroPedido) | Único composto | Número pode repetir em outro tenant. |
| Pedido.ChaveIdempotencia | Único; chave não nula | Idempotência não é particionada por cliente/tenant no índice. |
| Entrega.PedidoId | Único | No máximo uma entrega por pedido. |
| Entrega.CodigoRastreio | Único global | Não assegura sigilo nem autorização por identidade. |
| Entrega.MotoristaId | Único quando atribuição ativa e ID não nulo | Uma reserva ativa por motorista. |
| Entrega.VeiculoId | Único quando atribuição ativa e ID não nulo | Uma reserva ativa por veículo. |
| CodigoTurnoMotorista.EntregaId | Único quando uso e cancelamento nulos | Expiração temporal não participa do filtro. |
| TurnoMotorista.MotoristaId | Único quando ativo | Um turno ativo por motorista. |
| TurnoMotorista.EntregaId | Único quando ativo | Um turno ativo por entrega; não existe índice equivalente por veículo no turno. |
| AlertaEmergencia.ChaveIdempotencia | Único global | Deduplicação da solicitação. |

Existe índice não único de auditoria por tipo de entidade, identificador e instante. FKs também podem receber índices convencionais do EF na criação nova; o SQL aditivo não recria necessariamente todos os índices não únicos de uma base legada.

Pedido, entrega, código, turno e alerta possuem Versao como token de concorrência inteiro, atualizado manualmente; não é rowversion automático. Os caminhos de POD e cancelamento encerram turnos sem incrementar sua versão. Os atualizadores verificam existência de índices, sem uma comparação geral de todas as definições antigas. Não se conclui compatibilidade de qualquer índice customizado somente pelo nome.

## 5. Provedores, inicialização e seed

| Caminho | Implementação observada | Limites |
|---|---|---|
| SQLite | Padrão de [appsettings](../src/MglConnect.Web/appsettings.json); UseSqlite. | Configuração prevista não identifica o banco efetivo de uma implantação. |
| SQL Server | UseSqlServer quando selecionado e sem URL PostgreSQL prioritária. | Banco de referência decidido para o projeto; nesta execução principal os testes externos foram ignorados. |
| PostgreSQL | URL ambiental prioritária ou seleção explícita; [parser](../src/MglConnect.Web/Services/PostgresConnectionStringParser.cs) converte URI e exige SSL. | Parser testado não equivale a integração com servidor. |
| Chaves de proteção | Diretório configurado; na ausência dele, persistência via contexto quando PostgreSQL. | SQL Server/SQLite sem diretório explícito usam a estratégia padrão do host; restart/escala precisam de validação própria. |

Fonte de seleção, precedência e DI: [Program](../src/MglConnect.Web/Program.cs). Uma URL PostgreSQL seleciona Npgsql antes do ramo explícito SQL Server. O provedor configurado no repositório não é evidência do provedor usado em produção.

O [inicializador](../src/MglConnect.Web/Data/DbInitializer.cs) chama EnsureCreatedAsync, atualizador de pedido, atualizador de perfil e atualizador operacional, nessa ordem, antes do seed opcional. Não foram localizadas migrations EF versionadas. Bases parcialmente existentes não são automaticamente convertidas em qualquer esquema completo esperado.

O [atualizador operacional](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs) usa transação serializável para tabelas, colunas, backfill, índices e integridade. Criação EF e atualizadores anteriores ficam fora desse bloco: **não há atomicidade de toda a inicialização**. Backfill ocorre somente ao introduzir a coluna de atribuição, exige motorista e veículo e estados Pendente/Em transporte; valores explícitos antigos não são saneados.

O [atualizador de integridade](../src/MglConnect.Web/Data/RelationalIntegrityUpdater.cs) deriva as relações do modelo e verifica órfãos antes de instalar proteções. SQL Server adiciona/revalida FKs e compara definições existentes; SQLite novo recebe FKs nativas e SQLite legado pode receber triggers aditivas. Triggers não aparecem como FKs nativas no catálogo. O ramo PostgreSQL não implementa toda a comparação/revalidação de constraints existentes presente no ramo SQL Server, e não foi validado contra servidor nesta fase.

O seed habilitado cria três veículos, dois clientes, três usuários, três pedidos, duas entregas e oito eventos, sem criar empresa. Os registros usam tenant vazio. Há saves separados e a presença de dados em qualquer uma das sete tabelas verificadas impede completar o seed. A entrega demonstrativa Em transporte é criada sem atribuição ativa e sem turno; o pedido da entrega já entregue permanece ComEntregaAtribuida. O seed ocorre depois do updater e não é reparado pelo backfill nessa criação. Sua assinatura demonstrativa é SVG textual; o PDF tenta decodificar a parte após a vírgula como Base64 e prevê mensagem de formato incompatível.

## 6. Identidade, perfis e escopos

| Perfil/contexto | Comportamento | Evidência / limite |
|---|---|---|
| Admin / Administrador | Backoffice, despacho, atribuições, emissão/cancelamento de códigos, gestão de alertas e comandos administrativos. | [Account](../src/MglConnect.Web/Controllers/AccountController.cs), [Auth API](../src/MglConnect.Web/Controllers/Api/AuthController.cs). Normalização e aceitação de aliases variam entre caminhos; algumas verificações exigem Admin literal. |
| Motorista | Lista e perfil próprios; navegação, checkpoints, POD e telemetria condicionados ao turno nos caminhos previstos. | [Entregas](../src/MglConnect.Web/Controllers/EntregasController.cs), [API](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs), [hub](../src/MglConnect.Web/Hubs/TelemetryHub.cs). Download de PDF concluído exige propriedade, sem turno ativo. |
| Cliente | Portal resolve cliente ativo do usuário; consulta do próprio pedido/entrega; sem alteração operacional. | [Portal](../src/MglConnect.Web/Controllers/ClientePortalController.cs). O cliente não escolhe livremente o vínculo no formulário. |
| Intermediador | Mesmo vínculo único Usuario.ClienteId no portal, API e telemetria. | Busca textual não localizou IntermediadorClienteIds. Não há coleção de representados, delegação multicliente ou tabela associativa. |
| Autenticado sem tenant válido | IsTenantBound verdadeiro; tenant ausente/inválido/vazio torna-se Guid.Empty. | [Provider e contexto](../src/MglConnect.Web/Data/MglDbContext.cs). Filtro restringe ao tenant vazio; não rejeita automaticamente a identidade. |
| Contexto não vinculado | Filtros permitem consulta global; usado em login/inicialização e rastreio público. | Mesmo contexto. Não confundir Guid.Empty autenticado com ausência de vínculo. |

O contexto captura tenant e vínculo no construtor. Quando vinculado, sobrescreve tenant de inclusões e valida tenant original/atual em alterações/exclusões de ITenantOwned. Empresa e evento não participam dessa proteção de escrita; não há validação geral de tenant nas FKs. Não foi comprovada a resolução do contexto no pipeline completo, com todas as dependências de autenticação e Data Protection.

O [hasher](../src/MglConnect.Web/Services/SimplePasswordHasher.cs) usa PBKDF2-SHA256, salt aleatório, 100 mil iterações e comparação em tempo constante; aceita hash legado sem rehash automático. O [bearer](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs) é ticket protegido por Data Protection, não JWT. O login API emite cookie e token de oito horas. A seleção privilegia bearer explícito e usa bearer nas APIs; GET de tráfego/ETA possui exceção para cookie.

O [middleware de conta ativa](../src/MglConnect.Web/Middleware/ActiveAccountMiddleware.cs) consulta usuário ativo e compatibilidade do papel na requisição autenticada. Não valida atividade da empresa. Logout elimina cookie; não foi localizado mecanismo persistido de revogação individual do bearer. [Swagger](../src/MglConnect.Web/Middleware/AdminOnlySwaggerMiddleware.cs) exige autenticação e Admin. Testes isolados desses componentes não substituem exercício do pipeline HTTP.

## 7. Serviços, dependências e DI

Fonte dos registros: [Program](../src/MglConnect.Web/Program.cs). Serviços de negócio abaixo são scoped, salvo engine singleton; TimeProvider.System é singleton. Contexto e provider de tenant são scoped. Também são registrados HTTP, Data Protection, MVC/Razor, SignalR e Swagger.

| Serviço | Dependências | Saída / limite |
|---|---|---|
| [SimplePasswordHasher](../src/MglConnect.Web/Services/SimplePasswordHasher.cs) | Criptografia .NET. | Hash/verificação; suporte legado sem atualização automática. |
| [ApiBearerAuthenticationHandler](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs) | Data Protection, opções, logger, encoder e relógio do handler. | Valida ticket/expiração; depende de chaves disponíveis; lifetime gerenciado pela autenticação. |
| [HttpContextTenantProvider](../src/MglConnect.Web/Data/MglDbContext.cs) | IHttpContextAccessor e claims. | Snapshot do tenant e autenticação; não valida cadastro da empresa. |
| [DriverShiftService](../src/MglConnect.Web/Services/DriverShiftService.cs) | Contexto, relógio e criptografia; transações em provedor relacional. | Código, início/fim e auditoria; distribuição do segredo ao motorista é tarefa manual. Métodos de cancelamento/encerramento dependem da autorização do chamador. |
| [EmergencyAlertService](../src/MglConnect.Web/Services/EmergencyAlertService.cs) | Contexto e relógio. | Alerta/auditoria e transições; sem SMS/push/central externa. Operador administrativo é validado na entrada web, não dentro de todos os métodos do serviço. |
| [TrackingService](../src/MglConnect.Web/Services/TrackingService.cs) | Contexto, IHubContext de TrackingHub e logger opcional. | Rastreio, evento e POD; autorização/turno ficam nos chamadores. Falha SignalR não desfaz persistência. |
| [PodPdfService](../src/MglConnect.Web/Services/PodPdfService.cs) | Contexto, logger, QuestPDF, fontes e bibliotecas nativas. | Bytes de PDF sob demanda; sem arquivamento versionado, assinatura criptográfica ou fotografia renderizada. |
| [TruckRoutingService](../src/MglConnect.Web/Services/TruckRoutingService.cs) | Contexto, HTTP, IMemoryCache, configuração, logger e engine. | Google Geocoding ou Photon e OSRM; rota genérica. Cache por entrega não inclui versão/tenant e é consultado antes do banco. Não foi identificado mecanismo de invalidação por mudança de atribuição. |
| [RouteDeviationEngine](../src/MglConnect.Web/Services/RouteDeviationEngine.cs) | Logger, relógio do sistema e dicionário concorrente. | Singleton de estado por rastreio; sem durabilidade, compartilhamento entre processos ou expiração automática localizada. |
| [TrafficMatchService](../src/MglConnect.Web/Services/TrafficMatchService.cs) | Configuração, HTTP e logger. | Google Routes com DRIVE; sem chave retorna indisponibilidade. Não fornece rota pesada nem camada visual de tráfego. |
| [FuelCostService](../src/MglConnect.Web/Services/FuelCostService.cs) | Contexto e roteamento. | Estimativa por parâmetros de consumo/preço/pedágio; resumo da frota e fallback individual usam distâncias fixas. |
| [AnttFreteService](../src/MglConnect.Web/Services/AnttFreteService.cs) | Tabelas constantes internas. | Cálculo de referência declarada 2024; sem atualização externa; pedágio assume seis praças. |
| [Co2EsgService](../src/MglConnect.Web/Services/Co2EsgService.cs) | Fatores constantes e entradas fornecidas. | Estimativa; sem medição de combustível. Endpoint ESG usa distância fixa por perfil. |

O [parser PostgreSQL](../src/MglConnect.Web/Services/PostgresConnectionStringParser.cs) e os atualizadores são utilitários estáticos, não serviços scoped adicionais. Pedido/despacho ainda possuem regras e consultas nos controladores; não foi localizado um serviço único que concentre todos esses casos de uso.

## 8. Fluxos, fronteiras transacionais e falhas

| Fluxo | Sequência e persistência | Limites da evidência |
|---|---|---|
| Pedido do portal | Resolve cliente ativo; valida ViewModel; normaliza chave GUID; consulta reenvio; cria Registrado e salva. Índice ampara corrida de chave duplicada. | [Portal](../src/MglConnect.Web/Controllers/ClientePortalController.cs), [ViewModel](../src/MglConnect.Web/ViewModels/NovoPedidoClienteViewModel.cs). Reenvio sequencial coberto em InMemory; não comprova corrida relacional. |
| Estado do pedido | Registrado pode passar a EmProcessamento/Cancelado; EmProcessamento pode cancelar. Despacho atribui e POD conclui. | [Pedidos](../src/MglConnect.Web/Controllers/PedidosController.cs). Estado é string; regras são de aplicação, sem máquina de estados global imposta pelo banco. |
| Despacho | Admin valida pedido sem entrega, estado, motorista/veículo ativos, ocupação e capacidade nominal. Transação serializável grava entrega Pendente, pedido atribuído e evento. | [Entregas.Create](../src/MglConnect.Web/Controllers/EntregasController.cs). Permite despacho incompleto; reserva ativa exige ambos. Não verifica acomodação dimensional nem toda compatibilidade da carga. |
| Reatribuição | Bloqueia turno ativo; valida substituto; cancela código pendente; altera vínculo/estado/reserva e versão. | Mesmo controlador. Cancelamento do código pode salvar antes da alteração principal; não há uma transação envolvendo todos os saves desse fluxo. |
| Emissão do código | Admin válido; atribuição completa ativa e entrega não terminal; recusa turno ativo; cancela código anterior; emite 32 bytes aleatórios e persiste SHA-256, validade de 15 minutos e auditoria. | [DriverShiftService](../src/MglConnect.Web/Services/DriverShiftService.cs). Serviço não revalida atividade/perfil do motorista atribuído nem atividade do veículo nessa emissão. |
| Início de turno | Valida motorista ativo, atribuição e código mais recente; aplica expiração, cancelamento e cinco tentativas; consome código; entrega passa a Em transporte; cria turno/evento/auditoria em transação. Reenvio de turno já ativo retorna AlreadyActive. | Mesmo serviço. Não revalida atividade do veículo. Transações/índices não demonstram por si só concorrência multi-instância. |
| Encerramento do turno | Desativa turno, registra instante, incrementa versão e audita. | Mantém entrega e reserva; novo código é necessário para iniciar outro turno após uso do anterior. Não é cancelamento da viagem. |
| Emergência | Exige chave não vazia; devolve duplicata correspondente; para nova solicitação exige motorista ativo, turno, entrega em transporte e atribuição ativa. Valida par/limites de coordenadas e precisão; grava alerta e auditoria antes de sucesso. | [EmergencyAlertService](../src/MglConnect.Web/Services/EmergencyAlertService.cs). Duplicata é resolvida antes de revalidar turno. Sem captura temporal, localização não é associada como disponível. |
| Localização de emergência | Janela de -30 segundos a dois minutos; fora dela, posição recebida é registrada como desatualizada; ausência de posição é indisponível. | Mesmo serviço. Registro não significa reconhecimento humano ou atendimento; sem integração automática de socorro. |
| Gestão de emergência | Aberto → Reconhecido → EmAtendimento → Resolvido; FalsoAlarme nos estados definidos. Salva estado, ator/instantes, versão e auditoria. | Serviço e [ações Admin](../src/MglConnect.Web/Controllers/EntregasController.cs). Consulta administrativa limitada aos 100 alertas e 250 registros de auditoria carregados; sem canal de alerta ao vivo. |
| Checkpoint | Exige mesmo estado atual Pendente/Em transporte; salva evento; depois tenta notificar grupo SignalR. Não pode concluir ou mudar operação. | [TrackingService](../src/MglConnect.Web/Services/TrackingService.cs). Sem chave idempotente; sem outbox/reentrega. Autorização está nos controladores. |
| POD | Controladores exigem recebedor. Serviço exige Em transporte; conclui entrega/pedido, desativa reserva, preenche POD e encerra turnos; um save; depois notifica. | [TrackingService](../src/MglConnect.Web/Services/TrackingService.cs), [API](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs). Assinatura/documento opcionais; foto nula pelos endpoints. Reenvio compara somente nome. Versão do turno não é incrementada nesse encerramento. |
| Cancelamento | Admin cancela códigos; desativa entrega/reserva; cancela pedido; encerra turnos; grava evento/auditoria. | [API](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs). Cancelamento prévio de códigos pode ser um save separado; versão de turno não é incrementada. |
| PDF | Endpoint verifica acesso, estado Entregue e nome; serviço recarrega relações e gera bytes. | [Endpoint](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs), [serviço](../src/MglConnect.Web/Services/PodPdfService.cs). Não exige assinatura preenchida apesar do texto da mensagem. Local GPS é localidade textual do evento; não há assinatura criptográfica do arquivo. |
| Rastreio público | Consulta por código; inclui relações/eventos; API projeta campos; página pública também exibe endereço/documentos/recebedor/assinatura quando preenchidos. | [Consulta](../src/MglConnect.Web/Controllers/RastreioController.cs), [API](../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs), [página](../src/MglConnect.Web/Views/Rastreio/Index.cshtml). Documento do recebedor também pode aparecer na descrição pública do evento POD. Constatação estática, sem exploração executada. |
| SignalR | TrackingHub permite grupo público por código existente. TelemetryHub exige identidade e verifica acesso; motorista precisa atribuição/turno; simulação exige Admin. | [TrackingHub](../src/MglConnect.Web/Hubs/TrackingHub.cs), [TelemetryHub](../src/MglConnect.Web/Hubs/TelemetryHub.cs). Sem histórico durável de posições, backplane ou revalidação contínua de todos os assinantes já inscritos demonstrada. |
| Navegação/desvio | Rota geocodificada via HTTP, geometria OSRM e estado registrado em memória. Engine avalia precisão, salto e três frames; recálculo pesado é bloqueado. | [Roteamento](../src/MglConnect.Web/Services/TruckRoutingService.cs), [engine](../src/MglConnect.Web/Services/RouteDeviationEngine.cs). Não é homologação física. Reinício perde estado; cache e engine podem ter ciclos distintos. |

Notificação após gravação é esforço de comunicação, não confirmação durável de entrega ao navegador. Campos de estado/versão, índices e transações são controles complementares; não existe garantia geral de consistência de todos os fluxos e referências apenas por possuí-los.

## 9. Funcionalidades classificadas nos sete estados

Cada estado refere-se à capacidade delimitada na linha, não ao produto inteiro. Uma funcionalidade implementada pode depender de outra parcial ou não verificada. Evidência de código não equivale a teste de ponta a ponta.

| Capacidade | Estado | Evidência | Limite / parte faltante |
|---|---|---|---|
| Login individual web e emissão de bearer | Implementado | [Account](../src/MglConnect.Web/Controllers/AccountController.cs), [Auth API](../src/MglConnect.Web/Controllers/Api/AuthController.cs), [handler](../src/MglConnect.Web/Services/ApiBearerAuthenticationHandler.cs). | Sem testes integrais de login, token, logout e restart localizados. |
| Verificação de conta ativa e proteção de Swagger | Implementado | [Middlewares](../src/MglConnect.Web/Middleware). | Swagger possui testes isolados; conta ativa não possui teste específico localizado. |
| Isolamento organizacional completo | Parcial | [Contexto](../src/MglConnect.Web/Data/MglDbContext.cs), [testes tenant](../src/MglConnect.Tests/TenantIsolationTests.cs). | FKs por ID, tenant vazio válido, ausência de vínculo quando anônimo e sem validação integral do pipeline. |
| Intermediador vinculado a um cliente | Implementado | [Portal](../src/MglConnect.Web/Controllers/ClientePortalController.cs), [ownership](../src/MglConnect.Tests/ResourceOwnershipTests.cs). | Escopo único; não é representação multicliente. |
| Delegação por IntermediadorClienteIds | Ausente | Busca textual e [modelo de usuário](../src/MglConnect.Web/Models/Usuario.cs). | Não há propriedade, associação ou consumo desse nome localizado. |
| Solicitação idempotente de pedido no portal | Implementado | [Portal](../src/MglConnect.Web/Controllers/ClientePortalController.cs), [testes](../src/MglConnect.Tests/ClientePortalOrderTests.cs). | Cobertura InMemory; sem concorrência relacional/HTTP. |
| Transições administrativas do pedido | Implementado | [Pedidos](../src/MglConnect.Web/Controllers/PedidosController.cs), [testes](../src/MglConnect.Tests/OrderStateMachineTests.cs). | Não comprova todos os estados com todos os perfis e provedores. |
| Despacho e reserva motorista/veículo | Implementado | [Entregas](../src/MglConnect.Web/Controllers/EntregasController.cs), [testes](../src/MglConnect.Tests/DispatchValidationTests.cs), [índices](../src/MglConnect.Web/Data/MglDbContext.cs). | Capacidade nominal coberta; sem adequação dimensional completa ou corrida distribuída. |
| Código temporário e início/fim de turno | Implementado | [Serviço](../src/MglConnect.Web/Services/DriverShiftService.cs), [testes SQLite](../src/MglConnect.Tests/OperationalSecurityTests.cs). | Não equivale a escala/RH; atividade de veículo não revalidada no serviço. |
| Perfil e lista minimizada do motorista | Implementado | [Controlador](../src/MglConnect.Web/Controllers/EntregasController.cs), [testes](../src/MglConnect.Tests/DriverExperienceTests.cs). | Sem inspeção visual/browser nesta autoria. |
| Emergência online persistida e reenvio | Implementado | [Serviço](../src/MglConnect.Web/Services/EmergencyAlertService.cs), [testes](../src/MglConnect.Tests/OperationalSecurityTests.cs). | Registro não significa leitura humana nem atendimento. |
| Reconhecimento e estados de atendimento | Implementado | Mesmo serviço e [ações administrativas](../src/MglConnect.Web/Controllers/EntregasController.cs). | Sem SMS/push/SignalR de alertas; sem promessa de monitoramento contínuo. |
| Acionamento externo automático de socorro | Ausente | [Serviço de emergência](../src/MglConnect.Web/Services/EmergencyAlertService.cs). | Nenhum provedor/fluxo de chamada externa localizado. |
| Checkpoints e consulta por código | Implementado | [Tracking](../src/MglConnect.Web/Services/TrackingService.cs), [rastreio API](../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs). | Exposição pública depende do conhecimento do código; projeções não são todas mínimas. |
| Notificação durável/reentrega de eventos | Ausente | [Tracking](../src/MglConnect.Web/Services/TrackingService.cs), [registro DI](../src/MglConnect.Web/Program.cs). | Sem outbox, broker ou protocolo de recuperação identificado. |
| Conclusão por POD e preservação do comprovante | Implementado | [Tracking](../src/MglConnect.Web/Services/TrackingService.cs), [testes de estado](../src/MglConnect.Tests/DeliveryStateMachineTests.cs). | Testes InMemory; assinatura não é obrigatória no servidor. |
| POD com foto, GPS estruturado e autenticação da assinatura | Parcial | [Entrega](../src/MglConnect.Web/Models/Entrega.cs), [PDF](../src/MglConnect.Web/Services/PodPdfService.cs). | Há campo de foto e string de assinatura; endpoints passam foto nula, localidade é textual e não há assinatura criptográfica. |
| Download PDF | Implementado | [Endpoint](../src/MglConnect.Web/Controllers/Api/EntregasApiController.cs), [PDF](../src/MglConnect.Web/Services/PodPdfService.cs). | Sem teste de geração, fontes/nativos, conteúdo ou regressão visual. |
| Telemetria online e detecção de desvio | Parcial | [Hub](../src/MglConnect.Web/Hubs/TelemetryHub.cs), [engine](../src/MglConnect.Web/Services/RouteDeviationEngine.cs), [testes](../src/MglConnect.Tests/RouteDeviationEngineTests.cs). | Estado volátil; não há histórico GPS durável nem ensaio com dispositivo real. |
| Simulação administrativa de posição/desvio | Simulado | [Hub](../src/MglConnect.Web/Hubs/TelemetryHub.cs), [cockpit](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml). | Fictícia e restrita a Admin; não comprova GPS/viagem/POD real. |
| Cálculo genérico de rotas | Implementado | [Serviço](../src/MglConnect.Web/Services/TruckRoutingService.cs), [testes HTTP simulados](../src/MglConnect.Tests/DriverRoutingTests.cs). | Não valida fornecedor real, gabarito, restrições locais ou segurança do trajeto. |
| Homologação de rota pesada | Ausente | [Serviço](../src/MglConnect.Web/Services/TruckRoutingService.cs). | Homologação permanece falsa; recálculo automático pesado bloqueado. |
| Trânsito ao vivo no ambiente de destino | Não verificado | [TrafficMatchService](../src/MglConnect.Web/Services/TrafficMatchService.cs). | Depende de chave/serviço; teste localizado cobre indisponibilidade sem provedor. |
| Estimativas combustível/frete/CO2 | Implementado | [Fuel](../src/MglConnect.Web/Services/FuelCostService.cs), [ANTT](../src/MglConnect.Web/Services/AnttFreteService.cs), [CO2](../src/MglConnect.Web/Services/Co2EsgService.cs). | Constantes e distâncias fixas em caminhos específicos; não são custos medidos ou inventário certificado. |
| Persistência offline GPS/POD integrada | Estrutura inicial | [Módulo](../src/MglConnect.Web/wwwroot/js/pwa-offline.js), [worker](../src/MglConnect.Web/wwwroot/sw.js), [tracker](../src/MglConnect.Web/wwwroot/js/tracker.js). | Métodos de fila sem consumidores localizados; stores divergentes, fila GPS em memória e POD online. |
| Provisionamento de organizações SaaS | Estrutura inicial | [Empresa](../src/MglConnect.Web/Models/Empresa.cs), [contexto](../src/MglConnect.Web/Data/MglDbContext.cs). | Entidade/filtros não constituem onboarding, memberships ou gestão de assinatura. |
| Jornada, salário e folha | Planejado | [Requisitos P1](../REQUISITOS_MGL_CONNECT_MOTORISTA_E_PEDIDOS.md). | Fonte oficial/regras de RH não implementadas; não inferir jornada legal do turno/GPS. |
| Volumes, contatos e janelas adicionais do pedido | Planejado | Mesmo documento, FR-03. | Não confundir dimensões existentes com cubagem/volumes/janelas completos. |
| Migrations versionadas | Ausente | [Inicializador](../src/MglConnect.Web/Data/DbInitializer.cs), [atualizador](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs). | Há atualização aditiva customizada, não histórico de migrations EF. |
| Integração PostgreSQL real | Não verificado | [Program](../src/MglConnect.Web/Program.cs), [parser tests](../src/MglConnect.Tests/PostgresConnectionStringParserTests.cs). | Nenhum teste com servidor localizado; nenhuma execução nova nessa fase. |
| Produção, restore de backup e escala | Não verificado | [Configuração/DI](../src/MglConnect.Web/Program.cs). | Sem ensaio operacional, restart/múltiplas instâncias ou restauração nesta autoria. |

## 10. Cobertura evidenciada dos testes

Contagem estática: **91 métodos, 102 casos declarados, 21 arquivos**. Theories foram expandidas pelos InlineData presentes. Distribuição: **27 SQLite, 4 SQL Server opcionais e 71 de unidade/mocks/InMemory**. Tanto a execução principal informada de 27/10 quanto a retomada de 28/10 aprovaram 98 e ignoraram quatro; os resultados de execução estão separados da contagem estática na seção 13.

| Arquivo(s) | Casos | Provedor e verificação real | Limite |
|---|---:|---|---|
| [ClientePortalOrderTests](../src/MglConnect.Tests/ClientePortalOrderTests.cs) | 4 | InMemory e validação direta: cultura, criação/reenvio, inatividade e intermediador. | Sem browser/model binding HTTP; reenvio sequencial. |
| [OrderStateMachineTests](../src/MglConnect.Tests/OrderStateMachineTests.cs) | 2 | InMemory: transições diretas de controlador. | Sem concorrência relacional. |
| [DispatchValidationTests](../src/MglConnect.Tests/DispatchValidationTests.cs) | 13 | InMemory/mocks: duplicidade, atividade, capacidade, ocupação, parcialidade e reatribuição. | Não exercita FKs, índices SQL ou transação serializável. |
| [DriverExperienceTests](../src/MglConnect.Tests/DriverExperienceTests.cs) | 5 | InMemory/mocks: listagem, redirecionamento, propriedade, turno e CPF mascarado. | Não renderiza ou mede layout mobile. |
| [ResourceOwnershipTests](../src/MglConnect.Tests/ResourceOwnershipTests.cs) | 4 | InMemory/mocks: propriedade de motorista e cliente do intermediador. | Sem pipeline de autenticação/autorização completo. |
| [TelemetryHubAuthorizationTests](../src/MglConnect.Tests/TelemetryHubAuthorizationTests.cs) | 3 | InMemory/mocks: recusa cliente emissor, outro motorista e simulação não administrativa. | Não prova matriz integral de assinaturas, revogação ou transmissão autorizada. |
| [TenantIsolationTests](../src/MglConnect.Tests/TenantIsolationTests.cs) | 5 | InMemory: clientes por tenant, atribuição, modificação/movimentação negadas e claim ausente. | Não cobre todas as entidades, FKs cruzadas ou DI real. |
| [DeliveryStateMachineTests](../src/MglConnect.Tests/DeliveryStateMachineTests.cs) | 5 | InMemory: bloqueios de estado, conclusão POD e preservação do recebedor existente. | Fixture aceita referências inválidas para banco relacional; hub nulo não comprova envio. Dois testes exercitam essencialmente a mesma rejeição de reversão. |
| [OperationalSecurityTests](../src/MglConnect.Tests/OperationalSecurityTests.cs) | 7 | SQLite: códigos/turno, expiração/cancelamento/tentativas, reenvio, emergência, localização stale e falha injetada de save. | Duas chamadas via Task.WhenAll no mesmo processo, sem barreira que assegure sobreposição; falha injetada não é pane real do banco/rede. |
| [DatabaseSchemaUpdaterTests](../src/MglConnect.Tests/DatabaseSchemaUpdaterTests.cs) | 3 | SQLite: colunas, preenchimento e repetição de schema. | Fixtures legadas delimitadas. |
| [AssignmentBackfillTests](../src/MglConnect.Tests/AssignmentBackfillTests.cs) | 11 | SQLite: nove combinações, flag explícita preservada e inconsistência não saneada. | Não é inventário de dados legados reais. |
| [RelationalIntegrityTests](../src/MglConnect.Tests/RelationalIntegrityTests.cs) | 6 | SQLite: 17 relações, turno/alerta reais, reserva, repetição, triggers, órfãos, rollback, REPLACE e chaves nulas. | Não comprova toda forma de SQL/DDL customizada ou todos os deletes/estados. |
| [SqlServerIntegrityTests](../src/MglConnect.Tests/SqlServerIntegrityTests.cs) | 4 | Externos opcionais: novo/legado, FKs, índices, serviços, rollback e incompatibilidade. | Ignorados sem MGLCONNECT_SQLSERVER_TEST_CONNECTION; não executados na suíte principal informada desta fase. Não cobrem fluxo POD completo ou concorrência distribuída. |
| [SimplePasswordHasherTests](../src/MglConnect.Tests/SimplePasswordHasherTests.cs) | 5 | Unidade: salt, senha correta/incorreta e vazios. | Sem teste localizado de migração/rehash ou login integral. |
| [AuthenticationSchemeSelectionTests](../src/MglConnect.Tests/AuthenticationSchemeSelectionTests.cs) | 3 | Unidade: escolha cookie/bearer. | Não valida descriptografia/expiração/revogação real do ticket. |
| [AdminOnlySwaggerMiddlewareTests](../src/MglConnect.Tests/AdminOnlySwaggerMiddlewareTests.cs) | 4 | Middleware isolado: anônimo, não Admin, Admin e rota comum. | Não inclui middleware de conta ativa nem pipeline real. |
| [DriverRoutingTests](../src/MglConnect.Tests/DriverRoutingTests.cs) | 4 | HTTP simulado/InMemory: endereço, Photon, cache e indisponibilidade. | Sem fornecedores reais, tráfego contratado ou geometria de campo. |
| [RouteDeviationEngineTests](../src/MglConnect.Tests/RouteDeviationEngineTests.cs) | 3 | Memória: precisão, três frames e salto implausível. | Sem carga, campo ou ciclo entre processos. |
| [AnttFreteTests](../src/MglConnect.Tests/AnttFreteTests.cs) | 4 | Fórmulas/tabelas internas e pedágio. | Não certifica vigência normativa; teste com nome de exceção verifica valor positivo. |
| [Co2EsgTests](../src/MglConnect.Tests/Co2EsgTests.cs) | 5 | Fórmulas/fatores e agregação. | Não certifica metodologia; teste com nome de exceção verifica fallback positivo. |
| [PostgresConnectionStringParserTests](../src/MglConnect.Tests/PostgresConnectionStringParserTests.cs) | 2 | Parser, escape e SSL. | Sem conexão, DDL, transação ou consulta PostgreSQL. |

Não foram localizados testes específicos de ActiveAccountMiddleware, processamento integral do bearer, login/logout HTTP, inicializador/seed, PDF real, fluxo integral pedido → despacho → turno → emergência → POD ou browser/offline. Não há percentual de cobertura comprovado nesta leitura; referência a coletor de cobertura no projeto não constitui relatório medido.

## 11. Lacunas e fronteiras da conclusão

- Nenhum dado real de negócio, catálogo de banco instalado, segredo, configuração ativa de implantação ou log da aplicação foi consultado nesta autoria. O inventário descreve código e testes disponíveis; as saídas de restore/build/test da retomada estão registradas na seção 13.
- Não foram lidos os manuais DOCX nem o PDF acadêmico nesta autoria. Afirmações sobre esses anexos dependem de leitura própria, fora desta entrega.
- Permanecem sem comprovação nesta fase: integração PostgreSQL real, concorrência entre processos/instâncias, resolução de tenant no pipeline completo, regeneração/visual do PDF e operação em dispositivo real.
- Persistência no banco, confirmação HTTP, envio SignalR, recepção no navegador e reconhecimento humano são eventos diferentes. O código e os testes não devem ser usados para afirmar equivalência entre eles.
- Offline integrado é capacidade incompleta: a existência de IndexedDB, Service Worker e métodos de fila não comprova preservação/sincronização de GPS/POD. Não foi feito teste de navegador nesta autoria.
- As 17 FKs e os índices são avanço preexistente da Fase 1A; não demonstram isolamento estrutural completo entre organizações. Não houve saneamento de dados nem migração de tenant nesta documentação.
- Fase 1B continua não executada. Os limites acima não são declaração de exploração reproduzida, correção aplicada ou nova auditoria ofensiva concluída.

## 12. Confronto documental consolidado

Os documentos históricos foram preservados. Divergência não autoriza reescrever código para fazê-lo corresponder a uma afirmação antiga. A coluna atual abaixo se apoia no código e nos testes do workspace, incluindo alterações preexistentes.

| Fonte / afirmação histórica | Evidência atual | Interpretação para os quatro relatórios |
|---|---|---|
| [Relatório geográfico](../relatorio_auditoria_e_roadmap_geolocalizacao_mgl_connect.md), seção 1, e [Fase 4](../RELATORIO_FASE4_COCKPIT_GIS.md): .NET 6. | [Web](../src/MglConnect.Web/MglConnect.Web.csproj) e [testes](../src/MglConnect.Tests/MglConnect.Tests.csproj) direcionam net10.0. | Baseline atual é .NET 10; não recomendar downgrade para reproduzir narrativa antiga. |
| [Fase 4](../RELATORIO_FASE4_COCKPIT_GIS.md), seções 2 e 8: força desvios, impede túneis e aplica restrições físicas. | [TruckRoutingService](../src/MglConnect.Web/Services/TruckRoutingService.cs) emite avisos, usa OSRM driving, mantém homologação falsa e bloqueia recálculo automático pesado. | Cálculo genérico existe; homologação pesada está ausente. |
| [Fase 4](../RELATORIO_FASE4_COCKPIT_GIS.md), seções 4 e 8: GPS/POD sobrevivem offline e sincronizam. | Módulo, worker e tracker têm contratos desconectados; POD submete online. Ver seção 13 do [mapa](./FASE_2_MAPA_APIS_E_INTEGRACOES.md). | Estrutura inicial; não declarar o requisito offline do MVP cumprido. |
| [Fase 5](../RELATORIO_FASE5_ESTABILIDADE_PERFORMANCE.md), seção 2: compressão Brotli instalada. | [Program](../src/MglConnect.Web/Program.cs) não registra middleware/serviço de compressão correspondente. | Não comprovada no produto atual; não houve implementação nesta fase. |
| [Fase 5](../RELATORIO_FASE5_ESTABILIDADE_PERFORMANCE.md), seção 4: limite POD de 1 MB e classes ApiSanitizationTests/TelemetrySimulationTests. | Busca não localizou essas classes ou limite específico da assinatura. Existem limites gerais possíveis do framework/host, não auditados dinamicamente. | Não anunciar sanitização/cobertura exaustiva ou limite específico já testado. |
| [Fase 3](../AUDITORIA_PREDITIVA_FASE3.md) e [Fase 5](../RELATORIO_FASE5_ESTABILIDADE_PERFORMANCE.md): totais conflitantes de 17/22/30 e cobertura completa. | 102 casos declarados; 98 aprovados e quatro integrações ignoradas nesta retomada; nenhum percentual de cobertura produzido. | Aprovação de testes não é percentual de código coberto nem homologação funcional total. |
| [PodPdfService](../src/MglConnect.Web/Services/PodPdfService.cs) e sua interface anunciam GPS, foto e assinatura autenticada. | Local GPS usa texto do evento; foto não é renderizada; assinatura é imagem/string, sem assinatura criptográfica do PDF. | Download implementado; conjunto de evidências documentais é parcial. Não emitir conclusão jurídica com base no texto do PDF. |
| [Requisitos](../REQUISITOS_MGL_CONNECT_MOTORISTA_E_PEDIDOS.md), etapa 2: emissão com motorista/veículo ativos e mensagens que pressupõem assinatura. | Serviço não revalida toda atividade na emissão/início; POD/endpoint PDF não exigem assinatura preenchida. | Registrar limites; correções dependem de etapa autorizada, sem afirmar teste ofensivo. |
| [Diagnóstico Fase 0](./ETAPA_1_DIAGNOSTICO_BASE_TECNICA.md): seis FKs, backfill incorreto e ausência de testes SQL Server. | [Fase 1A](./FASE_1A_INTEGRIDADE_DADOS_SQLSERVER.md) e código preexistente têm 17 FKs, backfill condicionado e quatro testes externos opcionais. | Diagnóstico é histórico anterior à correção. Dados antigos não foram saneados automaticamente. |
| [Requisitos](../REQUISITOS_MGL_CONNECT_MOTORISTA_E_PEDIDOS.md): 75 aprovados e SQL Server não executado naquela etapa. | Relato posterior da Fase 1A registra 102 com SQL Server; Fase 2 atual executa 98 e ignora quatro. | Manter data/provedor de cada resultado; SQL Server não foi revalidado nesta Fase 2. |
| [SQL acadêmico](../sql/banco_pim_sqlserver.sql) contém procedure/trigger e convenções de IDs/perfis próprias. | Startup usa EF/atualizadores e não executa esse script. | Não afirmar instalação da procedure/trigger no banco do produto. |

As afirmações sobre Flutter e escopo acadêmico do PDF permanecem atribuídas à leitura anterior registrada na [arquitetura](./FASE_2_ARQUITETURA_ALVO_SAAS.md); não houve nova extração do PDF/DOCX nesta retomada. A necessidade acadêmica deve ser decidida pelo grupo/orientação, não inferida como autorização para implementar outro frontend.

## 13. Encerramento reproduzível da Fase 2 — 28/10/2026

### Execução real desta retomada

Plataforma identificada: xUnit v2.9.3, Microsoft.NET.Test.Sdk e adapter Visual Studio, portanto VSTest. Nenhum global.json, Directory.Build.props ou Directory.Packages.props foi localizado para alterar essa seleção. SDK local verificado: **10.0.403**. Não houve instalação global, publish, deploy, aplicação iniciada ou acesso a banco externo.

Comandos executados na raiz da solução, nessa ordem. A retirada da conexão SQL Server ocorreu somente no ambiente filho e não alterou configuração persistente:

```bash
$HOME/.local/share/mglconnect-dotnet10/dotnet --version
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet restore MglConnect.sln --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet build MglConnect.sln --no-restore --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 $HOME/.local/share/mglconnect-dotnet10/dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal
```

| Comando | Exit | Resultado observado |
|---|---:|---|
| Versão SDK | 0 | 10.0.403 |
| Restore | 0 | All projects are up-to-date for restore. |
| Build | 0 | Build succeeded; 0 Warning(s); 0 Error(s). |
| Testes | 0 | Failed 0; Passed 98; Skipped 4; Total 102; duração reportada de 1 segundo. |

Ignorados explicitamente em [SqlServerIntegrityTests](../src/MglConnect.Tests/SqlServerIntegrityTests.cs): `FreshDatabaseRunsOperationalServicesAndEnforcesAllSeventeenForeignKeys`, `LegacyUpgradeBackfillsOnlyCompleteAssignmentsAndPreservesExplicitFlags`, `LegacyOrphanBlocksAndRollsBackOperationalUpgrade` e `IncompatibleExistingConstraintIsNotSilentlyAcceptedOrRemoved`. Não são aprovações SQL Server atuais. Os **102 aprovados com SQL Server da Fase 1A são somente históricos**.

Os testes executados usam unidade/mocks/InMemory e SQLite, com arquivos/fixtures temporários próprios. Não houve execução de aplicação contra banco local existente, browser/E2E, PDF nativo, produção, testes de carga ou concorrência distribuída. Não foram gerados TRX ou coleta de cobertura nesta retomada.

### Preservação e consistência

Antes das validações, o Git já apresentava alterações da Fase 1A em código/testes, arquivos novos de integridade, docs não rastreados e os estados `MD README.md` / `AD render.yaml`. Esses estados não foram restaurados, preparados ou commitados. Foram calculados SHA-256 de todos os arquivos tracked/untracked não ignorados fora dos quatro relatórios e do diff staged, para comparação ao fim. Artefatos ignorados de build não integram esse conjunto.

| Registro anterior à retomada documental | SHA-256 |
|---|---|
| Mapa ordenado caminho → hash/ausência dos demais arquivos | `34cedd34ce44bc884bf742396e3780db6ed5c7095464226165984be46ff0a63c` |
| Diff staged binário | `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b` |

A conferência final confirmou **212 arquivos/estados de ausência fora dos quatro relatórios sem alteração**, com os mesmos hashes por caminho e diff staged idêntico. Foram verificados links locais e âncoras Markdown, fechamento de blocos de código e contagens de controllers/Razor: 12 controllers/2.540 linhas e 32 Razor/4.980 linhas. Nenhum link local/âncora ausente ou diagnóstico de editor foi encontrado nos quatro relatórios; `git diff --check` retornou exit 0. Somente os quatro relatórios foram alvos de escrita documental; artefatos ignorados de compilação foram gerados pelos comandos autorizados. Este fechamento não aprova Fase 1B, piloto ou produção.

### Resultado e regra de idempotência

**Conclusão documental entregue para revisão humana**, composta por este inventário, mapa de APIs/integrações, arquitetura-alvo e backlog. Os outros três relatórios apontam para este registro de execução para não multiplicar baselines conflitantes. A publicação, correção de produto, saneamento e Fase 1B dependem de autorização posterior.

Em nova automação, verificar primeiro a existência dos quatro relatórios, este marcador de conclusão, os links e o fingerprint dos demais arquivos. Se o conteúdo do produto e as restrições não mudaram, registrar que já está concluído, sem reescrever relatórios ou repetir restore/build/test por rotina. Alteração relevante de produto, evidência faltante ou novo pedido explícito exige avaliação pontual; não usar o marcador para ignorar um bloqueio real. Encerrada esta retomada, aguardar revisão do usuário.
