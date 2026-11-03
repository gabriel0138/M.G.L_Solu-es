# Etapa 7B.1 — Provedor experimental e cobertura — 30/10/2026

## Entrega e limites

Adaptador HTTP Valhalla integrado ao cálculo por viagem, desativado por padrão, com perfil `truck`, contratos explícitos, histórico e bloqueio conservador. Declarações de carga/composição agora pertencem à viagem; o cadastro permanente do veículo permanece separado. Inventário real de atributos OSM num território brasileiro pequeno, sem convertê-lo em autorização de circulação. A Etapa 7B.2 não foi iniciada.

**Não há uma instância Valhalla real autorizada/configurada neste ambiente.** O protocolo foi exercitado com respostas sintéticas identificadas, inclusive por HTTP real em loopback e Chromium. Não se mediram desempenho do motor, construção do grafo, cobertura Valhalla brasileira, legalidade do percurso ou precisão física de GPS. Resultados do adaptador permanecem, no máximo, ESTIMADOS; orientação e ETA operacional continuam suspensos.

## Implementação

- `Entrega.PerfilViagemJson/Versao` e `PerfisViagemRevisoes`: declaração por viagem e veículo atribuído, revisão otimista, responsável, UTC, snapshot do cadastro permanente e histórico append-only na aplicação. Cada avaliação mantém seu próprio snapshot; alterar carga/composição/veículo/endereço/fonte/atribuição invalida o vínculo. Cálculos tardios não substituem o ticket vigente.
- Medidas permanentes continuam no cadastro existente de veículos. Valores legados/padrões não são promovidos a medições verificadas ou PBT/PBTC. O envelope efetivo, carga, distribuição por eixo, composição e permissões precisam ser declarados para cada viagem.
- Compatibilidade **somente de leitura** com `Veiculo.PerfilOperacionalJson` quando seu `TripId` corresponde exatamente à viagem daquele veículo. Não se apaga, copia ou reescreve esse JSON ao salvar a nova declaração. Não se recuperam automaticamente valores de outras viagens. Revisões antigas da 7A continuam nos snapshots das avaliações.
- Atualização aditiva/idempotente em SQLite/PostgreSQL e SQL gerado para SQL Server; três FKs novas, total de 37 exercitadas nos bancos disponíveis. Testes removem somente estruturas novas em bancos sintéticos e comprovam upgrade a partir do formato 7A sem alterar o JSON legado. SQL Server não executado.
- Contratos separados: localizações, dados físicos com unidades, solicitação, resposta geográfica, falhas, segmentos/IDs, grafo, direção, topologia, cobertura espacial, validade e fonte. A correspondência estrutural exige todos esses identificadores compatíveis; divergência ou ausência retorna DESCONHECIDO. Correspondência estrutural não certifica completude legal.
- Valhalla nativo `/route`: POST JSON, medidas em metros, kg convertidos para toneladas métricas, maior carga declarada por eixo, número de eixos e `hazmat`. Dados obrigatórios desconhecidos não viram defaults do fornecedor. Perfil fora do domínio técnico suportado é recusado antes da rede; os limites de domínio não são limites legais.
- HTTPS ou HTTP exclusivamente loopback, endpoint de configuração do servidor, sem credenciais na URL/query/fragmento, sem redirects. Timeout de oito segundos, limite de 2 MiB/20.000 pontos, polyline6 sem simplificação, continuidade entre pernas, rejeição de dados incompletos/avisos de ajuste. Sem linha de fallback nem mudança artificial do GPS.
- HTTP 429 preserva `Retry-After`, com espera compartilhada por endpoint no cache da instância, limitada a uma hora; sem repetição automática. Rede, timeout, HTTP, falta de cobertura e configuração inválida são diferenciados. Respostas não verificáveis não entregam geometria parcial. Em múltiplas instâncias, essa espera ainda não é distribuída.
- O retorno conserva checksum da resposta válida, instante de recebimento e parâmetros físicos. A resposta básica não comprova versão do grafo nem IDs direcionais das vias: esses campos e todas as coberturas permanecem desconhecidos. Nenhuma tag OSM ou ordinal de aresta é importado como regra da rota. Intervalos nas verificações são da geometria do próprio cálculo, não IDs viários de outro roteador.
- A configuração do provedor participa da política de invalidação; habilitar/trocar o endpoint invalida avaliações incompatíveis. O motor `RestrictionEngine` e a interface de roteamento existentes foram preservados. Recálculo experimental permanece suspenso enquanto faltar evidência, sem cair silenciosamente no OSRM.
- Command Center: provedor/perfil, estado em português, dados físicos, confiança, cobertura desconhecida, inventário público, revisões, mapa e fontes. Tabela de restrições com rolagem interna acessível; hashes completos nos detalhes. Corrigida a edição de “nenhuma autorização” para não voltar a desconhecido. Turnos, emergência, GPS observado, offline e POD/PDF preservados.
- Backup SQLite verifica e restaura também revisões, associação e snapshot do veículo, mantendo compatibilidade com pacotes antigos sem a tabela. PostgreSQL teve dump/restore nativo incluindo a declaração/revisão e as permissões HTTP.

## Configuração e privacidade

Sem novas dependências de pacotes, serviços pagos, Docker ou instalação de motor. Configuração de servidor, deliberadamente ausente/desativada por padrão:

```text
Routing__Valhalla__Enabled=false
Routing__Valhalla__BaseUrl=https://INSTANCIA-AUTORIZADA
```

Habilitar exige instância autorizada e geocodificação existente configurada. O endpoint recebe somente coordenadas de origem/destino e parâmetros físicos, sem IDs de cliente/tenant, nomes de destinatários, códigos de turno ou tokens. Geocodificação pode transmitir endereços ao provedor configurado e requer os cuidados operacionais existentes. Nenhum dado privado foi enviado a serviços externos nos testes desta etapa. Uma alternativa genérica não recebe confiança por ausência de avisos.

## Comparação documental

Fontes oficiais consultadas em 30/10/2026. HEAD observado do Valhalla: `f221535790225f8cff53296b6c01b9304101e22e`; isso não é versão de um grafo executado.

| Critério | Valhalla — escolhido experimentalmente | openrouteservice — comparação, não integrado |
|---|---|---|
| Perfil e unidades | [`truck`](https://github.com/valhalla/valhalla/blob/master/docs/docs/api/route/api-reference.md); dimensões em m, massa/carga por eixo em toneladas, eixos e hazmat | [`driving-hgv`](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/routing-options); m, toneladas, axleload/hazmat; tipo de veículo explícito |
| Dados e restrições | Dependem do grafo efetivamente construído, acesso e atributos presentes; nomes de perfis não certificam vias | Extensão [HeavyVehicle](https://giscience.github.io/openrouteservice/run-instance/configuration/engine/profiles/build) baseada em atributos disponíveis, sem garantia de completude |
| Identificação | [`trace_attributes`](https://github.com/valhalla/valhalla/blob/master/docs/docs/api/map-matching.md) documenta IDs/way_id/direção/índices; não se assumiu que um novo map matching reproduz a mesma rota | Formato e extras exigem contrato específico; índices geométricos não foram tratados como IDs equivalentes ao Valhalla/OSRM |
| Versão do grafo | [`/status`](https://github.com/valhalla/valhalla/blob/master/docs/docs/api/status.md) informa versão do software/tileset e, quando disponível, changeset; uma consulta separada não prova atomicidade com a rota | Metadados de grafo do servidor escolhido precisam ser avaliados em ensaio próprio |
| Licença do motor | [MIT](https://github.com/valhalla/valhalla/blob/master/COPYING) | [GPL-3.0](https://github.com/GIScience/openrouteservice/blob/main/LICENSE), distinta dos termos de uma API hospedada |
| Operação/custo | Autohospedagem possível; compilação, atualização, CPU/RAM/disco e construção do grafo não foram benchmarkados; sem valor monetário inventado | Execução própria inclusive [JAR](https://giscience.github.io/openrouteservice/run-instance/system-requirements), com capacidade dependente de recorte/perfis |
| Serviço hospedado | Nenhum endpoint público contratado, autorizado ou ensaiado; contrato/cotas dependem do operador | Nenhuma chave criada; páginas antigas `plans`/`restrictions` retornaram 404 nesta consulta. Não se confirmou preço ou quota atual |
| Brasil | Inventário OSM pequeno disponível; cobertura e conformidade reais do grafo desconhecidas | Cobertura brasileira do serviço não foi medida |

Escolha: manter somente o adaptador Valhalla para não ampliar o escopo antes de disponibilizar grafo e correspondência verificáveis. Código aberto não implica hospedagem gratuita nem dispensa a [licença dos dados OSM](https://www.openstreetmap.org/copyright).

## Inventário brasileiro efetivamente obtido

Uma coleta pública pontual: bbox `[-46.6575,-23.5635,-46.6540,-23.5600]`, entorno do MASP/Bela Vista, São Paulo. Resposta XML de 1.740.737 bytes, SHA-256 `5d97d08b8c996a215ffc21f0c75f371ad64055c89cbddd3b092fa4f43a37e4b4`. Seleção determinística de 49 ways de classes motorizáveis, IDs/versões/timestamps e nós ordenados, sem nomes de editores ou dados pessoais no extrato incorporado. O horário da captura/importação e a fonte constam no JSON.

| Tipo de informação | Vias com atributo relevante, inclusive condicional | Sem atributo na amostra |
|---|---:|---:|
| Altura | 2 | 47 |
| Massa bruta | 0 | 49 |
| Largura | 0 | 49 |
| Comprimento | 0 | 49 |
| Carga por eixo | 0 | 49 |
| Número de eixos | 0 | 49 |
| Categoria hgv/goods | 0 | 49 |
| Acesso motorizado | 10 | 39 |
| Condições temporais motorizadas | 10 | 39 |
| Carga perigosa | 0 | 49 |

Duas ways têm `bridge` e duas têm `tunnel`; isso não informa suas capacidades estruturais. As alturas declaradas no OSM são `4.7` nas ways `72767325` v21 e `474493465` v11: **não verificadas independentemente nem aplicadas à rota**. As condições temporais não são interpretadas como calendário legal ou interdição atual. A presença de uma tag é disponibilidade de atributo, não prova de cobertura/ausência de outras restrições.

A [CET publica ZMRC e exceções](https://www.cetsp.com.br/consultas/caminhoes/locais-com-restricao-ao-caminhao/zona-de-maxima-restricao-de-circulacao-zmrc.aspx) e [mapas de ZMRC/VERs e referência a portaria](https://www.cetsp.com.br/consultas/caminhoes/mapa.aspx). São fontes candidatas de escopo territorial/autorizações; não foram transcritas como regras completas por segmento. A vigência integral, exceções, sinalização, feriados e correspondência com o grafo exigem verificação própria. Não se encontrou neste ensaio uma camada completa, versionada e ligada ao grafo para altura/peso/largura/eixos, pontes/túneis, cargas perigosas e interdições. Isso significa desconhecido, não inexistência de regras.

A [política da API OSM](https://operations.osmfoundation.org/policies/api/) não sustenta polling de projeto/produção. Não há consulta dessa API no runtime; o importador é offline. Atualizações futuras devem usar extratos/serviço de dados apropriado, respeitando licença e capacidade. O original público foi preservado fora de `/tmp`: `[caminho local omitido]`. O JSON derivado foi reproduzido byte a byte com o script e o mesmo horário registrado, sempre para um arquivo novo. Também passaram as verificações de recusa de sobrescrita e de entidades XML. IDs de nós OSM e versões não foram confundidos com versões de grafo nem com correspondência à geometria OSRM.

## Validação

Baseline: build limpo, .NET 406 aprovados/0 falhos/4 SQL Server ignorados, JS 107/0/0. Bateria final .NET com SQLite/PostgreSQL: **456 aprovados, 0 falhos, 4 SQL Server ignorados, 460 total**. JS: **107 aprovados, 0 falhos/ignorados**. Build: zero erros/avisos. PostgreSQL 18.6 em cluster temporário sem rede, cinco testes específicos incluídos na suíte; SQLite real nos testes HTTP e recuperação. SQL Server continua não validado.

Casos: unidades, obrigatoriedade/dominios físicos, dados de outra viagem, reatribuição, JSON legado, migração 7A, revisões imutáveis, backup/restore, cadastro permanente preservado, fontes/identificadores/direção/topologia/versão/validade divergentes, geometria incompleta, pernas desconectadas, ausência de cobertura, HTTP, timeout, cancelamento, limite de resposta, 429/backoff, ausência de avisos sem aprovação, mudança durante cálculo, cálculos concorrentes e regressões de acesso entre clientes/tenants, turnos, GPS e POD. Não se enfraqueceram expectativas de testes existentes para ocultar falhas: a asserção HTTP de perfil foi migrada para a persistência por viagem, verificando que o veículo permanece intacto. Nos novos testes, corrigidas fixtures que reutilizavam pedido já vinculado ou divergiam do peso declarado; a contagem real de atributos foi conferida contra os IDs do XML.

Logs finais: `[artefato temporário local omitido],dotnet,js}.log`. Cinco ensaios Chromium finais aprovados: 260 combinações de layout (60 adaptador + 60 restrições + 40 offline + 30 histórico + 70 jornada/restore/erro), larguras 1440/1280/768/390/360 e temas claro/escuro. Sem overflow global indevido ou exceções de página. Capturas finais revisadas em desktop/mobile; acessibilidade básica da jornada mantida. Sem garantia de GPS em segundo plano, ensaios físicos ou homologação legal.

## Preservação e Git

Backup de entrada anterior verificado e nova cópia de 312 arquivos de código/testes/scripts/docs, incluindo não rastreados, em `[caminho local omitido]`. Tar SHA-256 `f78a4a9e75ff092f5c2b5af089078b76ca15c52c00f07442bf5f4f820116f34f`, manifesto com hashes por arquivo/status/staging, permissões locais restritas. Sem bancos/chaves/appsettings/credenciais. É cópia de código de entrada no mesmo host; não inclui futuras alterações, não é backup offsite nem cópia completa dos dados operacionais. O snapshot anterior não foi modificado.

Staging original SHA-256 `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b` preservado. Exclusões preexistentes de trabalho em README/render mantidas. Nenhum commit/push/deploy/reset, serviço pesado instalado ou banco real alterado. Git final: 222 caminhos (55 modificados sem staging, 165 não rastreados e dois com staging/exclusões preexistentes). Esta etapa: 11 novos, 20 modificados, nenhum removido. `git diff --check` sem erros. Inventário abaixo.

## Riscos e decisão sobre 7B.2

A 7B.1 entrega modelagem, adaptador e diagnóstico verificáveis no escopo disponível; não entrega cobertura viária homologada. Para iniciar 7B.2, disponibilizar uma instância autorizada com grafo brasileiro versionado, dimensionar sua construção/atualização e obter evidência independente do território selecionado. Demonstrar correspondência direcional/topológica entre resposta e regras, validade/contradições, restrições temporais e acesso à carga/composição efetiva; definir critérios operacionais e responsáveis. Sem isso, continuar suspensa a orientação. SQL Server, infraestrutura externa/backup fora do host e testes físicos Android/iOS continuam pendências próprias. Não iniciar 7B.2 automaticamente.

## Evidências Chromium finais

| Ensaio | Log | Dados e captura |
|---|---|---|
| Adaptador controlado 7B.1 | `[artefato temporário local omitido]` | `mgl-stage7a-browser-fyUwSs`; capturas `mgl-cc-browser-CvSLWc`/`73Z0rm` |
| Restrições/reassociação/concorrência | `[artefato temporário local omitido]` | `mgl-stage7a-browser-m0ytO8`; capturas `mgl-cc-browser-8eNS9W`/`FN6kr2` |
| Offline GPS | `[artefato temporário local omitido]` | `mgl-gps-offline-SIHGrr` |
| Histórico/compactação/recuperação local | `[artefato temporário local omitido]` | `mgl-gps-history-e9Sv3u` |
| Jornada completa e restauração | `[artefato temporário local omitido]` | `mgl-e2e-journey-Qrsn2G` |

Diretório base dos ensaios: `[artefato temporário local omitido]`. Processos próprios dos ensaios encerrados; não são evidências permanentes fora da máquina. O teste experimental confia somente no certificado temporário do seu geocodificador loopback no processo filho, sem desativar verificação TLS nem alterar certificados do sistema. Fornecedor HTTP loopback devolve envelope **sintético**, não um motor Valhalla em execução. Journey: pedido do cliente, aprovação/despacho, turno, GPS, perda da resposta do POD, PDF autorizado, compactação, backup consistente, SIGKILL e restore isolado com os mesmos perfis. CLI no banco pequeno: backup 923 ms, verify 889 ms, restore 888 ms; não é SLA de produção.

```sh
dotnet build MglConnect.sln --no-restore
bash scripts/test-postgres-local.sh --no-build --logger 'console;verbosity=normal'
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
MGL_VALHALLA_CONTRACT=1 node scripts/route-restrictions-browser-check.cjs
node scripts/route-restrictions-browser-check.cjs
node scripts/gps-offline-browser-check.cjs
node scripts/gps-history-browser-check.cjs
MGL_RECOVERY_CHECK=1 node scripts/logistics-journey-browser-check.cjs
```

## Inventário desta etapa

Relativo ao manifesto de entrada, incluindo arquivos que já estavam não rastreados. Nenhuma remoção.

| Estado | Arquivo |
|---|---|
| Criado | [docs/ETAPA_7B1_PROVEDOR_E_COBERTURA.md](../docs/ETAPA_7B1_PROVEDOR_E_COBERTURA.md) |
| Criado | [scripts/import-routing-coverage.py](../scripts/import-routing-coverage.py) |
| Criado | [src/MglConnect.Tests/Fixtures/valhalla-contract-synthetic.json](../src/MglConnect.Tests/Fixtures/valhalla-contract-synthetic.json) |
| Criado | [src/MglConnect.Tests/TripProfileIsolationTests.cs](../src/MglConnect.Tests/TripProfileIsolationTests.cs) |
| Criado | [src/MglConnect.Tests/ValhallaContractTests.cs](../src/MglConnect.Tests/ValhallaContractTests.cs) |
| Criado | [src/MglConnect.Web/Models/PerfilViagemRevisao.cs](../src/MglConnect.Web/Models/PerfilViagemRevisao.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/BrazilCoverageInventory.cs](../src/MglConnect.Web/Services/RouteTrust/BrazilCoverageInventory.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/Data/brazil-pilot-osm.json](../src/MglConnect.Web/Services/RouteTrust/Data/brazil-pilot-osm.json) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/ProviderContracts.cs](../src/MglConnect.Web/Services/RouteTrust/ProviderContracts.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/TripProfiles.cs](../src/MglConnect.Web/Services/RouteTrust/TripProfiles.cs) |
| Criado | [src/MglConnect.Web/Services/RouteTrust/ValhallaExperimentalRouter.cs](../src/MglConnect.Web/Services/RouteTrust/ValhallaExperimentalRouter.cs) |
| Modificado | [docs/EXECUCAO_CONTINUA.md](../docs/EXECUCAO_CONTINUA.md) |
| Modificado | [scripts/route-restrictions-browser-check.cjs](../scripts/route-restrictions-browser-check.cjs) |
| Modificado | [src/MglConnect.Tests/Fixtures/README.md](../src/MglConnect.Tests/Fixtures/README.md) |
| Modificado | [src/MglConnect.Tests/PostgresIntegrityTests.cs](../src/MglConnect.Tests/PostgresIntegrityTests.cs) |
| Modificado | [src/MglConnect.Tests/RelationalIntegrityTests.cs](../src/MglConnect.Tests/RelationalIntegrityTests.cs) |
| Modificado | [src/MglConnect.Tests/RouteAssessmentIntegrationTests.cs](../src/MglConnect.Tests/RouteAssessmentIntegrationTests.cs) |
| Modificado | [src/MglConnect.Tests/SqlServerIntegrityTests.cs](../src/MglConnect.Tests/SqlServerIntegrityTests.cs) |
| Modificado | [src/MglConnect.Web/Controllers/RouteAssessmentController.cs](../src/MglConnect.Web/Controllers/RouteAssessmentController.cs) |
| Modificado | [src/MglConnect.Web/Data/MglDbContext.cs](../src/MglConnect.Web/Data/MglDbContext.cs) |
| Modificado | [src/MglConnect.Web/Data/RouteTrustSchemaUpdater.cs](../src/MglConnect.Web/Data/RouteTrustSchemaUpdater.cs) |
| Modificado | [src/MglConnect.Web/MglConnect.Web.csproj](../src/MglConnect.Web/MglConnect.Web.csproj) |
| Modificado | [src/MglConnect.Web/Models/Entrega.cs](../src/MglConnect.Web/Models/Entrega.cs) |
| Modificado | [src/MglConnect.Web/Operations/SqliteRecovery.cs](../src/MglConnect.Web/Operations/SqliteRecovery.cs) |
| Modificado | [src/MglConnect.Web/Program.cs](../src/MglConnect.Web/Program.cs) |
| Modificado | [src/MglConnect.Web/Services/RouteTrust/RouteContracts.cs](../src/MglConnect.Web/Services/RouteTrust/RouteContracts.cs) |
| Modificado | [src/MglConnect.Web/Services/RouteTrust/RouteTrustService.cs](../src/MglConnect.Web/Services/RouteTrust/RouteTrustService.cs) |
| Modificado | [src/MglConnect.Web/Services/TruckRoutingService.cs](../src/MglConnect.Web/Services/TruckRoutingService.cs) |
| Modificado | [src/MglConnect.Web/ViewModels/RouteAssessmentViewModel.cs](../src/MglConnect.Web/ViewModels/RouteAssessmentViewModel.cs) |
| Modificado | [src/MglConnect.Web/Views/RouteAssessment/Index.cshtml](../src/MglConnect.Web/Views/RouteAssessment/Index.cshtml) |
| Modificado | [src/MglConnect.Web/wwwroot/css/command-center.css](../src/MglConnect.Web/wwwroot/css/command-center.css) |
