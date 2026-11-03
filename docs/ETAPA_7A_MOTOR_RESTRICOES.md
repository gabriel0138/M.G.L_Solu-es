# Etapa 7A — motor de restrições e confiança das rotas

Estado: Etapa 7A concluída no escopo de núcleo/fixtures e integrações locais, com validações disponíveis aprovadas. Esta etapa não oferece rotas homologadas para todos os caminhões e não ativa um fornecedor externo de restrições.

## Implementações reais

- Perfil operacional JSON/revisão em veículo, vinculado à viagem declarada. Altura/largura/comprimento em metros; tara, PBT/PBTC efetivos e carga em kg; eixos/cargas por eixo, categoria/composição, tipo de carga/periculosidade/classe, autorizações e requisitos de acesso. Origem/destino vêm da viagem/pedido e participam do fingerprint. Campos desconhecidos ficam nulos; `PesoToneladas` legado não é reinterpretado como PBT/PBTC. Uma declaração de outra viagem não é reutilizada automaticamente.
- Medidas guardam origem DECLARADO/CALCULADO/VERIFICADO e referência; o formulário administrativo só cria DECLARADO. Massa derivada de tara+carga é CALCULADA, sem sugerir pesagem. Validação de unidade, sinal/faixa técnica, massa, PBTC/PBT, quantidade/distribuição por eixo e classificação perigosa. Divergência entre massa declarada do pedido e carga efetiva impede promoção da avaliação. Autorizações em branco são desconhecidas; `nenhuma` significa ausência declarada, não autorização concedida.
- Motor determinístico com altura, massa bruta, largura, comprimento, carga por eixo, número de eixos, categoria, acesso condicionado, horário/fuso e carga perigosa. Cada regra mantém ID, fonte, versão, território, intervalo UTC de validade, hash da geometria e arestas inicial/final. Retorna COMPATÍVEL, INCOMPATÍVEL ou DESCONHECIDO; ausência, expiração, conflito, unidade inválida ou associação não comprovada não significam aprovação.
- Cobertura exige evidência conclusiva para todos os tipos e todas as arestas, sem associação por proximidade. Coordenadas e sua ordem compõem o hash; textos de apresentação não mudam a identidade geométrica. Geometria do provedor é preservada até 20.000 vértices; acima disso o cálculo é recusado, sem simplificação/linha fictícia. Janela horária é avaliada no instante especificado e não comprova horários futuros de passagem.
- Contrato com ID/versão/relógio do cálculo, provedor/perfil, fonte/versão geográfica, regras, território, perfil/revisão/identidade do veículo, verificações, faltas, motivos e geometria/distância/tempo estimados quando disponíveis. Classes ESTIMADA, VALIDADA e HOMOLOGADA existem no contrato; o motor nunca gera HOMOLOGADA. Sem geometria há estado INDISPONÍVEL. Incompatibilidade aparece como BLOQUEADA na interface, separada da classe de confiança.
- `RotaAvaliacoes` persiste snapshots e histórico; tickets por viagem impedem que um cálculo antigo substitua o mais novo. Fingerprints incluem organização, atribuição, veículo/perfil/revisão, carga, endereços e versões. Mudanças/expiração tornam o resultado obsoleto. O uso corrente reavalia regras/fatos; um booleano gravado ou aceite visual não libera navegação. Cada solicitação e resultado administrativo têm auditoria.
- Atualização aditiva/transacional, mantendo registros anteriores: dois campos de perfil no veículo, ticket nullable na entrega, tabela de avaliações e índices. As três FKs novas foram acrescentadas às fixtures relacionais; testes exercitam 34 FKs. Backup/restore inclui a tabela quando presente e verifica consistência do resultado/ID/hash, preservando leitura de pacotes anteriores sem a tabela.

## Integração operacional

`/Rotas/Entrega/{id}` é exclusivo de administrador da organização, ligado aos detalhes da entrega. Mostra perfil, fatos do cálculo, regras/fontes/arestas, faltas, bloqueios, mapa estático do cálculo e últimas 20 avaliações, com consulta dos snapshots históricos. Salvamento usa CSRF, revisão/concurrency token e invalida avaliações anteriores. Viagens encerradas preservam o perfil histórico; não reescrevem a declaração.

O cockpit recebe classificação real e uma permissão explícita. Sem avaliação suficiente, geometria de orientação, ETA operacional e instruções ficam suspensos; GPS observado, fila offline, emergência e POD continuam. Alteração do perfil notifica os consumidores autorizados. Expiração e falha de recálculo suspendem a orientação sem mover o marcador para a linha. O arquivo de fixture não habilita orientação real.

Cálculos explícitos são limitados a oito/minuto/usuário/instância. Leituras GET reaproveitam avaliação compatível por até 20 segundos; mudança relevante/fonte invalida esse reaproveitamento. Detecção de desvio continua exigindo precisão/idade adequadas e cooldown existente; resultados recalculados também passam pelo motor, vínculo e ticket. Requests HTTP do provedor respeitam cancelamento do chamador/timeouts. Falhas não produzem geometria substituta.

## Fontes, provedores e decisão

Consulta documental em 29/10/2026, sem conta/contratação/chaves novas. A compatibilidade considerada é HTTP/JSON com a arquitetura atual, não homologação de restrições no Brasil.

| Candidato | Dados/capacidade | Licença/operação/custos | Brasil, privacidade e limite |
|---|---|---|---|
| OSRM atual | Perfil `driving` e geometria/steps; não valida sozinho o envelope pesado brasileiro | Preservado; demo não é infraestrutura comercial garantida | [Operador](https://routing.openstreetmap.de/about.html) informa car global, máximo 1 req/s, sem scraping/uso pesado e logs das consultas. Não enviar dados privados a um demo sem autorização. |
| openrouteservice/HeiGIT | [HeavyVehicle](https://giscience.github.io/openrouteservice/v9.10.0/run-instance/configuration/engine/profiles/build) armazena limites de tamanho/massa e acesso/hazmat a partir dos dados disponíveis | Repositório [GPL-3.0](https://github.com/GIScience/openrouteservice/blob/main/LICENSE); autohospedagem possível, com custo de infraestrutura. Hosted exige chave/termos/cotas próprios | Cobertura/correção brasileira não ensaiada. [Limites publicados](https://openrouteservice.org/restrictions/); página de planos antiga retornou 404, portanto não se confirmou quota diária comercial. [Aviso oficial](https://ask.openrouteservice.org/t/reducing-the-quota-of-deprecated-api-api-openrouteservice-org/8013) anuncia migração de domínio para api.heigit.org; não fixar endpoint legado. |
| GraphHopper | API/engine candidatos; parâmetros pesados e disponibilização por plano precisam de verificação no contrato escolhido | Engine [Apache-2.0](https://github.com/graphhopper/graphhopper/blob/master/LICENSE.txt); [API comercial por créditos/planos](https://www.graphhopper.com/pricing/) distinta do engine aberto | [Documentação API](https://docs.graphhopper.com/openapi) não forneceu conteúdo legível nesta ferramenta; não afirmar homologação de integração ou cobertura BR. Contato/conta não criados; nenhum dado enviado. |
| Valhalla | [Truck costing](https://github.com/valhalla/valhalla/blob/master/docs/docs/api/route/api-reference.md) oferece altura/largura/comprimento/massa, eixos/carga por eixo e hazmat conforme atributos do grafo | [MIT](https://github.com/valhalla/valhalla/blob/master/COPYING); pode ser autohospedado, com atualização/infraestrutura a dimensionar, sem tornar Docker obrigatório | Dados OSM disponíveis não provam completude brasileira. Hospedar internamente evita enviar trajetos a terceiros, mas exige operação do grafo; não instalado/benchmarkado nesta etapa. |

Licença de código não substitui licença dos dados nem termos de uma instância hospedada. Não se afirmou respeito a restrições legais, interdições ou áreas de risco sem base identificada.

**Decisão implementada:** `IRouteEvidenceSource` conservador para produção (sem inventar regras) e adaptador de fixture explícita em Development. Arquivo exige `Synthetic=true`, limite de tamanho/regras e não libera navegação, mesmo com validação lógica completa. A geometria de fixture foi gravada uma vez entre MASP/Praça da Sé, marcos públicos, sem dados de cliente/motorista. São 189 vértices reais do retorno do provedor; as restrições são fictícias e identificadas como ensaio, não limites daquelas vias. A captura não identifica a versão da base OSM e não corresponde aos endereços da entrega demonstrativa. Referência e atribuição estão em [Fixtures/README](../src/MglConnect.Tests/Fixtures/README.md).

## Validação e evidências

Baseline confirmado: build limpo, 365 .NET aprovados/4 SQL Server ignorados, JS 105 aprovados. Inventário inicial de 193 caminhos; backup verificável de 293 arquivos de código/scripts/docs, incluindo não rastreados, fora do repositório: `[artefato temporário local omitido]` (arquivo SHA-256 `a2d06c752ebde1554b2b4d791284b27cae0ad99093b48d274aa7e4fab32bb875`). Manifesto por arquivo/status/staging, sem bancos reais/configurações secretas.

Últimas baterias concluídas: 406 .NET aprovados, zero falhos, quatro SQL Server ignorados (410 total), SQLite e PostgreSQL local; JS 107 aprovados, zero falhos/ignorados. Build final: 0 erros/0 avisos. Bateria geral inclui 34 FKs em SQLite/PostgreSQL; quatro testes SQL Server permanecem ignorados, sem homologação desse provedor.

Casos novos: compatibilidade completa em escopo sintético, todos os limites incompatíveis, faltas, unidade, expiração/futuro/território, hash/arestas/trechos parciais, conflito entre fontes, horário/fuso, geometria inválida, coerência de massas, campos legados não reinterpretados, formulário inválido/forjado, revisões, revogação, outro cliente/organização, veículo/carga/motorista/endereço/fonte alterados, término de cálculo fora de ordem, corrupção por flags e restauração. Recálculo tem três cenários com provedor controlado: alternativa incompatível com as evidências, alteração concorrente e resultado compatível no escopo de teste. O ramo confiável desse teste não registra um provedor de produção.

O primeiro Chromium 7A passou por falta de dados, estimada, bloqueada, validação sintética, falha de fonte, reassociação real de veículo, turno e GPS sintético fora do trajeto com ETA suspenso, cliente autorizado e assinatura/POD/PDF. Cinco estados administrativos e cockpit × cinco larguras (1440/1280/768/390/360) × claro/escuro = 60 combinações de layout. A bateria final passou também na concorrência HTTP e nas regressões GPS/offline/histórico/jornada/restore: quatro ensaios aprovados, 200 combinações de layout no total (60 restrições + 40 offline + 30 histórico + 70 jornada/restore/erro). Não é teste físico de GPS ou navegação homologada.

## Problemas encontrados e corrigidos

- Hash geométrico não deve depender de nomes/labels; parser preserva vértices/ordem e evita duplicar o destino, mantendo associação às arestas verificável.
- Um resultado tardio não pode substituir o ticket atual nem apagar estado mais novo; consulta prioriza o cálculo vigente e revalida o vínculo.
- “Suspensa” não significa sempre “obsoleta”: feedback do cockpit diferencia bloqueio atual, invalidação e expiração; posição observada é preservada.
- Geometria inválida/NaN e dados malformados produzem indisponibilidade/desconhecido, não uma linha fictícia nem aprovação por flags.
- Declaração de carga/veículo de outra viagem, dados alterados e divergência de massa não podem ser reutilizados como evidência atual.

## Arquivos principais

Novos: Models/RotaAvaliacao; Data/RouteTrustSchemaUpdater; Services/RouteTrust/{RouteContracts,RestrictionEngine,RouteTrustService}; Controllers/RouteAssessmentController; ViewModels/RouteAssessmentViewModel; Views/RouteAssessment/Index; testes RouteRestrictionTests/RouteAssessmentIntegrationTests/RouteRecalculationTrustTests; fixture e seu README; scripts/route-restrictions-browser-check.cjs; este relatório.

Alterados: Veiculo/Entrega, MglDbContext/DatabaseSchemaUpdater/Program, ITruckRoutingService/TruckRoutingService, GpsTelemetryPublisher/TelemetryHub, FrotaManagementController, detalhes/cockpit/frota, tracker e regressões JS, testes relacionais/PostgreSQL/SQL Server, SqliteRecovery e checkpoint. Nenhum arquivo removido; staging original preservado. Os rótulos de massa legada também foram corrigidos para não afirmar PBT/PBTC sem classificação. Inventário final: 15 novos, 22 modificados nesta etapa e nenhum removido. Git com 210 caminhos alterados (54 modificados sem staging, 154 não rastreados e dois caminhos com staging/exclusões preexistentes). Staging original SHA-256 e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b intacto; git diff --check limpo.

## Riscos e próximo incremento

Sem fonte real de restrições completa/identificada no runtime, a operação pesada permanece sem orientação autorizada. Autorizações e medições declaradas não equivalem a vistoria, balança ou permissão legal verificada. Janelas avaliadas no instante não provam horários futuros de passagem. Regras de calendário/feriados, comprovação por segmentos/identificadores viários de fontes reais, ocorrências temporárias e cobertura territorial independente exigem o próximo adaptador. O histórico cresce sem exclusão automática; dimensionar retenção/capacidade operacional das avaliações, como demais registros de auditoria.

SQL Server, aparelhos Android/iOS físicos e infraestrutura externa continuam não homologados. Preservar os cuidados de backup/recuperação da Etapa 6. Próxima etapa recomendada: 7B, selecionar e homologar um adaptador real com dados de restrições/versionamento/cobertura brasileira verificáveis, qualidade e unidades do envelope/composição, permissões e horários de passagem comprováveis. Não promover ESTIMADA a VALIDADA/HOMOLOGADA por clique, rótulo do fornecedor ou ausência de atributos. Sem commit/push/deploy.

## Evidências finais e reprodução

- Build: `[artefato temporário local omitido]`; .NET completo com PostgreSQL temporário: `[artefato temporário local omitido]` (406/0/4); JS: `[artefato temporário local omitido]` (107/0/0).
- Chromium: `[artefato temporário local omitido]`, `[artefato temporário local omitido]`, `[artefato temporário local omitido]`, `[artefato temporário local omitido]`. Processos dos ensaios encerrados; dados sintéticos e bancos próprios.
- Diretório base de evidências: `[artefato temporário local omitido]`. Restrições: `mgl-stage7a-browser-vG16QZ` (capturas `mgl-cc-browser-CNKdFw`, motorista `Bpfv5N`). Offline: `mgl-gps-offline-fLWcY8`; histórico: `mgl-gps-history-IgNAJR`; jornada/restore: `mgl-e2e-journey-vj52f5`. Capturas revisadas em desktop/mobile e temas; estrutura de acessibilidade da jornada mantida.
- Restauração da jornada inclui avaliações novas e chaves; encerramento forçado do servidor, mesmos perfis, PDF e UUIDs recuperados. Comandos medidos, incluindo partida do processo no banco pequeno de ensaio: backup 916 ms, verify 860 ms, restore 869 ms; não é SLA/RTO de produção.

```sh
dotnet build MglConnect.sln --no-restore
bash scripts/test-postgres-local.sh --no-build --logger 'console;verbosity=normal'
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
node scripts/route-restrictions-browser-check.cjs
node scripts/gps-offline-browser-check.cjs
node scripts/gps-history-browser-check.cjs
MGL_RECOVERY_CHECK=1 node scripts/logistics-journey-browser-check.cjs
```

## Inventário desta etapa

Classificação relativa à entrada, incluindo arquivos previamente não rastreados. Nenhum segredo ou banco real faz parte do relatório/backup de código.

| Estado | Arquivo |
|---|---|
| Criado | [docs/ETAPA_7A_MOTOR_RESTRICOES.md](../docs/ETAPA_7A_MOTOR_RESTRICOES.md) |
| Modificado | [docs/EXECUCAO_CONTINUA.md](../docs/EXECUCAO_CONTINUA.md) |
| Criado | [scripts/route-restrictions-browser-check.cjs](../scripts/route-restrictions-browser-check.cjs) |
| Criado | [src/MglConnect.Tests/Fixtures/README.md](../src/MglConnect.Tests/Fixtures/README.md) |
| Criado | [src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json](../src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json) |
| Modificado | [src/MglConnect.Tests/JavaScript/tracker.test.cjs](../src/MglConnect.Tests/JavaScript/tracker.test.cjs) |
| Modificado | [src/MglConnect.Tests/PostgresIntegrityTests.cs](../src/MglConnect.Tests/PostgresIntegrityTests.cs) |
| Modificado | [src/MglConnect.Tests/RelationalIntegrityTests.cs](../src/MglConnect.Tests/RelationalIntegrityTests.cs) |
| Criado | [src/MglConnect.Tests/RouteAssessmentIntegrationTests.cs](../src/MglConnect.Tests/RouteAssessmentIntegrationTests.cs) |
| Criado | [src/MglConnect.Tests/RouteRecalculationTrustTests.cs](../src/MglConnect.Tests/RouteRecalculationTrustTests.cs) |
| Criado | [src/MglConnect.Tests/RouteRestrictionTests.cs](../src/MglConnect.Tests/RouteRestrictionTests.cs) |
| Modificado | [src/MglConnect.Tests/SqlServerIntegrityTests.cs](../src/MglConnect.Tests/SqlServerIntegrityTests.cs) |
| Modificado | [src/MglConnect.Web/Controllers/FrotaManagementController.cs](../src/MglConnect.Web/Controllers/FrotaManagementController.cs) |
| Criado | [src/MglConnect.Web/Controllers/RouteAssessmentController.cs](../src/MglConnect.Web/Controllers/RouteAssessmentController.cs) |
| Modificado | [src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs](../src/MglConnect.Web/Data/DatabaseSchemaUpdater.cs) |
| Modificado | [src/MglConnect.Web/Data/MglDbContext.cs](../src/MglConnect.Web/Data/MglDbContext.cs) |
| Criado | [src/MglConnect.Web/Data/RouteTrustSchemaUpdater.cs](../src/MglConnect.Web/Data/RouteTrustSchemaUpdater.cs) |
| Modificado | [src/MglConnect.Web/Hubs/TelemetryHub.cs](../src/MglConnect.Web/Hubs/TelemetryHub.cs) |
| Modificado | [src/MglConnect.Web/Models/Entrega.cs](../src/MglConnect.Web/Models/Entrega.cs) |
| Criado | [src/MglConnect.Web/Models/RotaAvaliacao.cs](../src/MglConnect.Web/Models/RotaAvaliacao.cs) |
| Modificado | [src/MglConnect.Web/Models/Veiculo.cs](../src/MglConnect.Web/Models/Veiculo.cs) |
| Modificado | [src/MglConnect.Web/Operations/SqliteRecovery.cs](../src/MglConnect.Web/Operations/SqliteRecovery.cs) |
| Modificado | [src/MglConnect.Web/Program.cs](../src/MglConnect.Web/Program.cs) |
| Modificado | [src/MglConnect.Web/Services/GpsTelemetryPublisher.cs](../src/MglConnect.Web/Services/GpsTelemetryPublisher.cs) |
| Modificado | [src/MglConnect.Web/Services/ITruckRoutingService.cs](../src/MglConnect.Web/Services/ITruckRoutingService.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/RestrictionEngine.cs](../src/MglConnect.Web/Services/RouteTrust/RestrictionEngine.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/RouteContracts.cs](../src/MglConnect.Web/Services/RouteTrust/RouteContracts.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/RouteTrustService.cs](../src/MglConnect.Web/Services/RouteTrust/RouteTrustService.cs) |
| Modificado | [src/MglConnect.Web/Services/TruckRoutingService.cs](../src/MglConnect.Web/Services/TruckRoutingService.cs) |
| Modificado | [src/MglConnect.Web/ViewModels/FleetManagementViewModels.cs](../src/MglConnect.Web/ViewModels/FleetManagementViewModels.cs) |
| Criado | [src/MglConnect.Web/ViewModels/RouteAssessmentViewModel.cs](../src/MglConnect.Web/ViewModels/RouteAssessmentViewModel.cs) |
| Modificado | [src/MglConnect.Web/Views/Entregas/Details.cshtml](../src/MglConnect.Web/Views/Entregas/Details.cshtml) |
| Modificado | [src/MglConnect.Web/Views/Entregas/Navegar.cshtml](../src/MglConnect.Web/Views/Entregas/Navegar.cshtml) |
| Modificado | [src/MglConnect.Web/Views/Frota/ResourceDetails.cshtml](../src/MglConnect.Web/Views/Frota/ResourceDetails.cshtml) |
| Modificado | [src/MglConnect.Web/Views/Frota/VeiculoForm.cshtml](../src/MglConnect.Web/Views/Frota/VeiculoForm.cshtml) |
| Criado | [src/MglConnect.Web/Views/RouteAssessment/Index.cshtml](../src/MglConnect.Web/Views/RouteAssessment/Index.cshtml) |
| Modificado | [src/MglConnect.Web/wwwroot/js/tracker.js](../src/MglConnect.Web/wwwroot/js/tracker.js) |
