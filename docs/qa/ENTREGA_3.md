# Entrega 3 — estados, métricas e geometria viária

Implementação `6fdb83d`, branch `feat/mgl-functional-gps-mobile`. Preservados `32d1057`, `21d14a5`, `01a6f74`, `a21844c` e `b4a947a`. Nenhum push, merge, deploy, alteração de main ou de outra worktree.

## Abrir os resultados

Prévia local desta sessão: `http://127.0.0.1:5287`.

- Administrador: abrir **Rotas e estimativas**. Sem parâmetros, nenhum cálculo é anunciado. **Abrir captura histórica MASP → Praça da Sé** desenha o replay viário, identificado como histórico e não reconsultado.
- Motorista: entrar pela conta demonstrativa exibida no login, abrir a viagem 1 com turno já iniciado e usar **Iniciar GPS sintético**. Esse cockpit continua usando o circuito autorado da Entrega 2, identificado como fictício. O replay histórico administrativo não é silenciosamente atribuído à entrega.
- Não há credenciais pagas. Para reproduzir em outra porta livre com dados novos:

```sh
MGL_LOCAL_DATA_DIR=$(mktemp -d /tmp/mgl-delivery3-XXXXXX) \
MGL_LOCAL_PORT=5288 MGL_DEMO_ROUTES=true \
MGL_ROAD_CAPTURE="$PWD/src/MglConnect.Tests/Fixtures/route-trust-public-landmarks.json" \
bash scripts/dev-local.sh
```

O administrador emite o código e o motorista inicia o turno na instância nova. O script é loopback/SQLite com seed fictício. Sem os opt-ins, demonstração e replay não são habilitados. `MGL_OSRM_BASE_URL` configura explicitamente o provedor genérico; permaneceu vazio ou apontado para mock loopback nos ensaios desta entrega.

## Defeitos reproduzidos e corrigidos

| Reprodução anterior | Correção comprovada |
|---|---|
| Zero vértices e 0.0 km, mas selo verde **OSRM Curvo** | Estado e métricas dependem de geometria finita, distinta e utilizável. O selo incondicional foi removido. Sem rota, métricas aparecem como **—**. |
| R$ 92.50 de pedágio e total mesmo sem rota | Origem identificada: constante de **R$ 18,50 × 5 eixos**. Pedágio real da rota agora é indisponível. Hipótese fixa permanece separada, somente compõe cenário com distância disponível. |
| Duração OSRM substituída por mínimos de 60/90 min e modelos de velocidade | Preservados segundos e minutos do provedor; removidos esses pisos e o modelo do recálculo genérico. Métricas/proveniência acompanham a avaliação persistida. |
| Fallback de 147/120,5 km nos custos, sem rota | Removido. Custo da entrega tem `RouteCostAvailable`; resumo informa viagens sem estimativa e não fornece margem aparentemente completa nesses casos. São estimativas de cenário, não contabilidade de despesas. |
| Altura/massa padrão pareciam dados do veículo | Dimensões ausentes permanecem desconhecidas. Dados declarados não são medição nem prova de restrições. Perfil completo continua vinculado à entrega no serviço de confiança existente. |
| Cockpit administrativo mostrava zero como distância/combustível sem orientação disponível | Distância restante, ETA e combustível indisponíveis são explicitados; simulador administrativo antigo não sobrepõe valores quando a orientação está bloqueada. |
| GPS sintético envelhecia mantendo ETA aparentemente atual | A idade do sinal é mostrada, e capturas sintéticas antigas também suspendem o ETA. |
| Formulário empurrava o mapa para baixo | No desktop, parâmetros ficam ao lado do mapa; no celular, recolhem após resultado válido. Sidebar preservada; diagnósticos agrupados/recolhidos. |

Capturas usam a cultura do processo (por exemplo, 92.50); os valores acima estão escritos na notação brasileira. Inputs e transporte decimal continuam invariáveis, sem alterar o contrato das entregas anteriores.

## Contrato entre backend e frontend

[RoutePresentation](../../src/MglConnect.Web/Services/RoutePresentation.cs) fornece estado explícito, geometria disponível, métricas anuláveis e proveniência. O JSON da página preserva medidas numéricas sem parsing de texto regional.

| Estado | Critério / comportamento |
|---|---|
| `AwaitingParameters` | Sem solicitação ou com entrada inválida; não anunciar cálculo. |
| `Calculating` | Submit real do formulário, botão desabilitado e região ocupada enquanto navega. |
| `Calculated` | Geometria genérica válida; não equivale a validação jurídica/física do veículo. |
| `NoRoute` | Sem cobertura/segmento ou sem coordenadas resolvidas; nenhuma linha de fallback. |
| `ProviderUnavailable` | Configuração ausente/inválida, falha HTTP/rede, timeout, 429 ou resposta inválida. |
| `GenericUnverifiedTruck` | Geometria existe; uso por caminhão não foi validado. |
| `SyntheticDemo` | Circuito autorado explícito, separado de evidências sintéticas de restrição. |
| `InsufficientVehicleData` | Perfil de veículo sem informações necessárias. |

`DistanceKm`, `DurationSeconds`, `FuelCost`, `RouteToll` e `ScenarioCost` podem ser null. Pedágio de rota continua desconhecido: não há consulta às praças. `FixedTollScenario` é hipótese do modelo por eixo, não preço retornado pelo OSRM. Geometria válida permite observar o traçado; não habilita orientação pesada, progresso operacional ou ETA sem a confiança necessária.

## Fonte viária e adaptadores

[OsrmRoadRouter](../../src/MglConnect.Web/Services/OsrmRoadRouter.cs) foi extraído do cliente OSRM existente, evitando duplicar provedores. Usa GeoJSON `LineString`, ordem longitude/latitude, entre 2 e 10 paradas, limites de coordenadas/20 mil vértices/2 MiB, distância e duração positivas, timeout limitado e cancelamento propagado. HTTP 400 com `NoRoute`/`NoSegment` difere de falha do servidor. 429 estabelece espera sem repetição automática. Redirecionamentos e logs HTTP contendo URL/coordenadas são desativados no cliente nomeado.

A chamada pública é espaçada em pelo menos um segundo por processo. Não há cache de sucesso no adaptador direto; o cache legado do planejamento por endereço tem TTL absoluto de cinco minutos e chave que inclui provedor/origem da captura. Falhas não viram rotas de cache. Origem, destino e paradas solicitadas são marcados separadamente de GPS.

Três origens distintas são exibidas:

1. **`HistoricalRoadCapture`:** fixture já existente, declarada como captura OSRM MASP → Praça da Sé, com **189 vértices e 4,962 km**. Sua [referência pública original](https://router.project-osrm.org/route/v1/driving/-46.6559,-23.5614;-46.6333,-23.5505?overview=full&geometries=geojson&steps=true) está preservada no arquivo. O replay lê somente geometria/distância; ignora as regras sintéticas que acompanham o cenário antigo. **Não é consulta atual.** Os 10 min do fixture antigo não são promovidos a duração original do provedor: a duração aparece indisponível. O SHA é do arquivo histórico, não de uma resposta bruta original preservada.
2. **`LocalProviderFixture`:** resposta HTTP local de regressão, com geometria reaproveitada e duração de teste explícita (410,25 s). Não é um ensaio de engine viária local nem integração pública ao vivo.
3. **`SyntheticCircuit`:** circuito fictício autorado da Entrega 2, com GPS sintético e seu modelo de ETA, sem prometer correspondência a ruas reais.

`RoadNetwork` identifica uma resposta estruturalmente validada do provedor configurado; não comprova sozinho grafo, legalidade ou cobertura de caminhões. O ensaio público opcional **não foi executado nesta entrega**; a pergunta de autorização separada ficou sem resposta. Nenhum endpoint público de rotas integra os E2E obrigatórios.

### Caminhões

O adaptador Valhalla experimental existente foi mantido, sem acrescentar um segundo adaptador HGV incompleto. Ele recebe dimensões/massa/carga máxima por eixo/quantidade de eixos explicitamente, sem preencher o perfil desconhecido. Parâmetros enviados não se tornam restrições verificadas na resposta. A engine de confiança mantém checagens de fonte, versão, segmentos, validade, vínculo ao veículo/viagem e cobertura; desconhecido não vira compatível.

A investigação da [documentação oficial do ORS](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/routing-options) confirmou que `driving-hgv` aceita comprimento/largura/altura em metros, massa/carga por eixo em toneladas e informação de carga perigosa. Não foi localizado ali contrato para vetor individual de cargas ou quantidade de eixos; não se presume sua verificação. Não foi adicionada chave de ORS nem ensaiado esse engine. A [documentação OSRM](https://project-osrm.org/docs/v5.24.0/api/) define perfil conforme a preparação Lua do grafo, coordenadas longitude/latitude e duração em segundos; seu nome `driving` não comprova homologação pesada. Nenhum dos ensaios fornece trânsito ao vivo ou autorização legal para circular.

## Validação executada

Kali, SDK .NET 10 local, Node.js, Chromium e **Brave instalado**, ambos headless com perfis novos e dados fictícios. Nenhum perfil pessoal de navegador, banco operacional, container, API paga, SQL Server ou PostgreSQL foi utilizado. Assets/tiles públicos continuam dependentes de rede.

| Comando / grupo | Resultado |
|---|---|
| Build/restore pelo `scripts/dev-local.sh` | Aprovado, zero warnings/erros. |
| `dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal` | **657 aprovados, 0 falhos, 9 ignorados**, de 666; cinco PostgreSQL e quatro SQL Server sem conexão autorizada. Variáveis de conexão removidas do processo. Duração final: 1 min 8 s. |
| Contratos direcionados OSRM/apresentação | **24 aprovados**, incluídos na suíte completa. |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **112 aprovados, zero falhos**. |
| `MGL_ROUTING_UI_CHECK=provider node scripts/logistics-journey-browser-check.cjs` | Aprovado: estados, valores, controles, falta de rota, resposta inválida, HTTP 503, recuperação e jornada completa. |
| `MGL_ROUTING_UI_CHECK=historical MGL_DEMO_ROUTES=true node scripts/logistics-journey-browser-check.cjs` | Aprovado: replay viário, captura sem duração falsa, circuito separado e jornada completa com GPS/progresso/desvio/retorno/alternativa/reconexão/POD. |
| Mesmo ensaio de provedor com `MGL_CHROMIUM_PATH=/usr/bin/brave-browser` | **Aprovado no Brave**, inclusive jornada completa e verificações de layout. |
| `node scripts/gps-offline-browser-check.cjs` | Aprovado: IndexedDB, captura antiga, servidor parado/reiniciado, replay com IDs/horários originais, sem duplicação, limites concorrentes. |
| Diff, sintaxe dos scripts e Gitleaks staged | Aprovados; sem ocorrências no conjunto de implementação. |

Roteamento: 320, 360, 390, 428, 768, 1024, 1280 e 1440 px; 844×390 e 390×500; claro/escuro. Verificados campos/botões por hit testing, parâmetros recolhíveis e resize do container sem resize de janela. Motorista: mesmos dez viewports no circuito, diagnóstico recolhido/expansível, ações alcançáveis, tiles bloqueados e recuperados e reconexão do WebSocket nativo. Foco, nomes acessíveis, estados de cliente/admin/público e PDF são conferidos na jornada. Nenhuma exceção JS não tratada ou HTTP 500 local nas execuções finais.

C# cobre isolamento entre clientes/tenants, dados atrasados/duplicados, cancelamento, fonte histórica bloqueada em Production, desconhecidos do veículo e preservação das métricas/proveniência na avaliação. Browser GPS do aparelho usa override sintético; não houve GPS físico.

As primeiras execuções também encontraram problemas do harness, corrigidos sem relaxar critérios: captura de `Calculating` depois de a navegação já terminar; largura clientWidth descontando borda; variável de viewport fora do loop; fake console sem `debug`. Capturas full-document agora reposicionam o scroll instantaneamente e o restauram; isso evita que sticky/fixed apareçam falsamente no meio da imagem por causa de scroll suave, sem esconder esses elementos no produto.

## Evidências e próximo passo

Evidências fora do Git: `$HOME/.local/share/mglconnect/evidence/delivery3-20261104/`, com **329 capturas reais** em `before/`, `historical/`, `protocol/`, `brave/`, `offline/`, logs, manifest e diff. Nenhum banco, screenshot, chave ou perfil entrou no Git.

Prioridades seguintes: ensaio separado autorizado que preserve resposta bruta/data/versão de um provedor atual; instância viária local documentada para comparar ruas e alternativas; cobertura real de restrições por segmento antes de qualquer orientação pesada; aparelho físico/Brave móvel com teclado e safe areas. Intermediador no navegador, todos os CRUDs e contabilidade efetiva permanecem fora desta validação. O circuito/replay não vinculam automaticamente um novo pedido ao endereço da captura e não substituem GPS em estrada.
