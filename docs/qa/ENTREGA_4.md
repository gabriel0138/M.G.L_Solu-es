# Entrega 4 — planejamento viário ligado à entrega

Implementação local de 2026-11-04 (São Paulo), com execução também após 00h UTC de 2026-11-05. Base `7b38b3e`, branch `feat/mgl-functional-gps-mobile`. Preservados `32d1057`, `21d14a5`, `01a6f74`, `a21844c`, `b4a947a`, `6fdb83d` e `7b38b3e`. A pasta estava limpa antes das alterações. Sem push, merge, deploy, alteração de main, PR #1 ou outras worktrees.

## Abrir a demonstração preparada

Aplicação: **http://127.0.0.1:5288**. Motor local: `127.0.0.1:5007`. SQLite descartável em `~/.local/share/mglconnect/preview-delivery4`, protegido pelo marcador de prévia. O ensaio de comparação Valhalla usa somente loopback `127.0.0.1:8008`.

- Administrador: `admin` / `admin123`, abrir `/Roteamento?entregaId=3` ou o mapa operacional. As credenciais são exclusivamente as contas fictícias do inicializador Development.
- Motorista: `carlos.motorista` / `motorista123`, abrir `/Entregas/Navegar/3`. Turno já iniciado. Usar **Iniciar GPS sintético** para percorrer a malha calculada, **Desvio e recálculo OSRM** para um novo cálculo e **Assinar e entregar** para POD. A viagem 3 foi deixada **em transporte**, sem conclusão automática.
- Cliente: `cliente.demo` / `cliente123`, consultar seu pedido. Nenhum GPS sintético é exposto na projeção do cliente ou no rastreamento público.

O pedido preparado usa marcos públicos MASP/Avenida Paulista e Praça da Sé, com cliente e carga fictícios. Coordenadas são declaradas explicitamente pelo administrador; não houve geocodificação automática silenciosa. Os testes completos utilizaram outros bancos descartáveis e confirmaram entrega/POD.

## Fluxo implementado

`Roteamento/Planejar` é POST com antiforgery e autorização de administrador do tenant. O serviço `DeliveryRoadPlanning` reutiliza `OsrmRoadRouter`, persiste uma tentativa independente por entrega e conserva geometria, paradas declaradas/localizadas, distância, duração, passos, alternativas, perfil, versão do motor, fonte/data e hash da evidência.

Leituras do cockpit e da operação usam esse planejamento persistido; não chamam o provedor novamente. O endpoint privado `/Entregas/{id}/Planejamento` restringe acesso a administrador e motorista atribuído. Clientes e público não recebem o planejamento privado completo.

Cada tentativa, inclusive falha/cancelamento, tem identidade própria. A última solicitação prevalece; terminar uma solicitação antiga não substitui uma mais recente. O vínculo inclui pedido, endereços, tenant, motorista, veículo e revisão de perfil. Vínculo alterado ou validade de quatro horas expirada exige novo cálculo. Um erro não resgata silenciosamente a rota anterior.

A simulação utiliza vértices da geometria persistida e registra `Origem=Simulacao`; não escreve o snapshot de GPS físico da entrega nem usa a fila nativa de GPS. `SimulationEnabled` não transforma a geometria em `SyntheticCircuit`. O circuito autorado anterior permanece como cenário separado para entregas sem planejamento viário no modo de demonstração explícito.

Desvio de ensaio desloca explicitamente uma posição sintética e solicita outra rota ao mesmo adaptador OSRM. Não há reta substituta. Projeção de progresso não move a coordenada observada; GPS físico não avança o ensaio sintético. Alcançar 100% não conclui entrega: somente o fluxo autorizado de confirmação/POD o faz.

## Motor, dados e reprodução

Recursos observados: 28 CPUs lógicas, 15 GiB RAM (7,2 GiB disponíveis), 320 GiB livres. Docker instalado, porém socket inacessível ao usuário e sudo exige senha. Sem alterar grupos/permissões, a imagem oficial foi executada com bubblewrap em namespace de usuário, com raiz da imagem somente leitura e diretório descartável de dados. Extração OSRM usou quatro threads e aproximadamente 49 MB de pico reportado pelo motor.

Imagem OSRM: `ghcr.io/project-osrm/osrm-backend`, 26.7.0 amd64 Debian, manifesto `sha256:98f7d637b703fb165a9f3f0381d31c008e3dcfc3929cdc18b885b0162bf233e2`. Perfil `car.lua`, algoritmo MLD (`extract → partition → customize → routed`). Não é perfil de caminhão.

Área OSM: longitude -46.658 a -46.632, latitude -23.564 a -23.548. A API recusou a consulta inicial maior por ultrapassar 50 mil nós; foram coletados quatro quadrantes menores. Recorte: 9.499 nós referenciados, 2.306 vias `highway`, relações completas de restrição disponíveis. Borda incompleta. Coleta 2026-11-05T00:08:42Z. XML do ensaio: SHA256 `3af516bd1d4204f2591d9d15c97a90baf56aac7675648d6f6f2443ed64589ba3`.

A preparação reproduzível também ordena relações por ID. A normalização altera o hash do XML para `a038c83e4d34f8b33613b508405316922aa4b2ad76429fc7e90f9afcfde274cc`, preservando as entidades. Uma segunda construção em outro diretório descartável retornou **as mesmas 134 coordenadas, distância e duração**. Use sempre o hash do manifesto do diretório utilizado, não um hash copiado de outra preparação.

```bash
bash scripts/local-osrm.sh prepare
bash scripts/local-osrm.sh serve
# Em outro terminal:
bash scripts/local-osrm.sh probe
MGL_OSRM_BASE_URL=http://127.0.0.1:5007 \
MGL_LOCAL_ROAD_ENGINE=true MGL_OSRM_ENGINE_VERSION=26.7.0 \
MGL_DEMO_ROUTES=true bash scripts/dev-local.sh
```

`MGL_OSRM_DATA_DIR` seleciona outro diretório descartável; `MGL_OSRM_PORT` seleciona porta loopback. Há execução Docker se o daemon já estiver acessível; o caminho efetivamente testado aqui foi bubblewrap/Python 3.14. Não foram instalados pacotes globais. Para persistir proveniência completa, informar também `MGL_OSRM_DATASET_VERSION=sha256:<hash do dataset.json>` e `MGL_OSRM_DATASET_REFERENCE` com fonte e data. A prévia preparada já contém esses campos. Na ausência desses metadados, a interface informa que a versão não foi declarada.

A instância local continua calculando sem consultar um servidor público de rotas. Tiles de fundo continuam externos; sua falha é identificada sem retirar geometria ou GPS. Dados © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), ODbL 1.0. Fixtures e proveniência em [Fixtures/osrm](../../src/MglConnect.Tests/Fixtures/osrm/README.md); respostas capturadas são regressões, não uma resposta atual. A captura histórica da Entrega 3 foi preservada.

## Resultado dos motores — mesmo par de marcos

| Motor/perfil | Distância | Duração | Restrições verificadas |
|---|---:|---:|---|
| OSRM 26.7.0 / driving | 3.107,7 m | 357,9 s | Nenhuma homologação de caminhão |
| Valhalla 3.9.1 / truck | 3,388 km | 299,223 s | Cobertura permanece desconhecida no adaptador |

OSRM retornou `code=Ok`, GeoJSON `LineString`, 134 vértices, duas paradas localizadas e manobras. Alternativas foram solicitadas; esse par retornou apenas uma rota. Parser preserva alternativas válidas quando retornadas. Ordem longitude/latitude, medidas, limites e associação dos extremos são validados; raio de busca de 100 m explicitado. Respostas vazias/inválidas, NoRoute, NoSegment, 429/Retry-After, HTTP 500, timeout e cancelamento não produzem métricas utilizáveis. Não há cache persistente reutilizado como resposta atual: o planejamento tem validade e recálculo explícitos.

Valhalla oficial 3.9.1 amd64: manifesto `sha256:18cb42988e03c70dc4bc4ca095a259ec4db5e5df928be65956fc1c64c280b47a`. Mesmas entidades OSM, ordenadas, sem base administrativa/fuso/elevação adicional. Perfil **informado de ensaio**, não cadastro validado: altura 4,2 m, largura 2,6 m, comprimento 18,6 m, massa 28,5 t, máximo 8 t/eixo, cinco eixos, sem perigosos. O adaptador existente foi executado contra o serviço local e manteve todas as categorias de restrição desconhecidas. A duração em segundos, antes descartada por esse adaptador, passou a ser conservada.

No recorte inteiro há 42 vias com `maxheight`, 14 com `hgv`, 143 com `access` e 1.030 com `oneway`; zero com `maxwidth`, `maxlength`, `maxweight`, `maxaxleload` ou `hgv:conditional`. Contagem regional não comprova correspondência/cobertura na rota. Sem base legal/local e restrições completas, orientação pesada permanece suspensa. Diferença de geometria/custo entre motores não prova superioridade ou autorização legal.

O [ORS driving-hgv](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/routing-options) aceita dimensões, peso, carga por eixo e perigosos. Não foram adicionadas chaves, dependências ou consultas ORS. A [API Valhalla](https://valhalla.github.io/valhalla/api/route/api-reference/) também distingue parâmetros de custo de comprovação da malha. Nenhum deles fornece homologação independente por aceitar parâmetros.

## Defeitos reproduzidos, correções e regressões

| Defeito / evidência anterior | Correção e nova verificação |
|---|---|
| Cálculo avulso não associado à entrega; cockpit ainda consumia circuito independente | Persistência vinculada e leitura comum no administrador, motorista e operação; E2E compara identidade e coordenadas |
| Toda URL loopback era identificada como fixture, inclusive motor real | Configuração explícita de motor local e proveniência; fixture, captura histórica e malha atual continuam distintas |
| Abertura do cockpit aproximava origem e cortava destino | Overview com padding para controles; verificação de ambos os extremos dentro do mapa em cada tamanho; arrastar desativa follow |
| Falha de recálculo podia manter avaliação operacional anterior | Nova falha registrada, orientação suspensa e estado de desvio removido; regressão de confiança/rota invertida |
| Erro de provedor podia deixar controles de ensaio e métricas anteriores aparentes | Limpeza da geometria/métricas, título indisponível, GPS físico acessível, recuperação por novo planejamento |
| Amostra recusada permanecia pendente no simulador | Erro semântico libera a amostra; UUID/corpo idêntico é idempotente, corpo divergente é recusado |
| Operação sem GPS podia encobrir planejamento com overlay de espera na atualização | Rota planejada independente do marcador e do overlay; regressão JS específica |
| Passos OSRM perdiam distância, duração e modificador; Valhalla perdia segundos | Campos preservados e validados nas capturas locais reais |

## Validação executada

Todos os bancos usados são SQLite descartáveis com contas fictícias. Não houve banco operacional.

| Verificação | Resultado |
|---|---|
| `.NET build --no-restore` da solução | Zero warnings/erros |
| `.NET test --no-restore` da solução | **665 aprovados, 9 ignorados, zero falhos** |
| Regressões adicionais finais de planejamento/recibo simulado | 10 aprovadas, incluindo corpo divergente e cancelamento/recuperação |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **113 aprovados**, zero falhos |
| Jornada completa com OSRM local, Chromium | Aprovada, incluindo falha/NoRoute/recuperação e falha de recálculo |
| Mesma jornada, Brave | Aprovada |
| `gps-offline-browser-check.cjs` | Aprovado: Service Worker, parada/reinício do servidor, reload offline, IndexedDB, recibo perdido e reenvio sem duplicação |
| Adaptador Valhalla contra loopback real | Aprovado, explicitamente separado da regressão determinística |
| Segundo grafo via scripts de preparação/execução | Aprovado; geometria e métricas reproduzidas |

Os nove ignorados são testes existentes de PostgreSQL/SQL Server sem instâncias isoladas configuradas; não foram tratados como aprovados. O caminho Docker não foi executado por indisponibilidade de acesso ao daemon; bubblewrap foi executado.

A jornada realiza pedido do cliente → aprovação/despacho → cálculo/vínculo → turno → GPS sintético sobre geometria → progressão, atraso, duplicação, desvio/retorno e recálculo → operação → assinatura/POD → consulta privada e pública → PDF autorizado. Também intercepta a resposta do POD após commit e verifica reenvio idempotente. Sem HTTP 500 local ou exceção JavaScript não tratada nos cenários aprovados. HTTP 503 injetado pertence ao provedor de teste, não à aplicação.

Cockpit/roteamento vinculados: 360, 390, 428, 768, 1024 e 1440 px, temas claro/escuro; cockpit também 844×390 e 390×500. O circuito anterior continua testado em 320 e 1280 px. Há hit testing real dos controles/ações críticas, extremos visíveis após resize, resize apenas do contêiner, falha/recuperação dos tiles, reload e fechamento do WebSocket nativo seguido de reconexão/reautorização e restauração persistida. GPS anterior ao turno é recusado; atraso dentro do turno não retrocede a posição. Não houve aparelho físico: permissões reais do Android/iOS, bloqueio de tela, bateria, precisão em movimento e redes móveis seguem pendentes.

Reproduzir jornada (com OSRM local em execução):

```bash
MGL_ROAD_PROXY=true MGL_LINKED_ROAD=true MGL_DEMO_ROUTES=true \
MGL_OSRM_BASE_URL=http://127.0.0.1:5007 MGL_LOCAL_ROAD_ENGINE=true \
node scripts/logistics-journey-browser-check.cjs
# Brave: acrescentar MGL_CHROMIUM_PATH=/usr/bin/brave-browser
# Valhalla, somente ensaio local explicitamente selecionado:
MGL_VALHALLA_COMPARE_URL=http://127.0.0.1:8008 dotnet test MglConnect.sln \
  --filter FullyQualifiedName~RoadEngineComparisonTests
```

O proxy local de teste apenas injeta falhas e encaminha os sucessos ao motor real; não substitui a geometria por fixture. Cada jornada inicia servidor e banco próprios e encerra somente esses processos. Para preparar outra viagem em uma prévia já marcada, o mesmo harness aceita `MGL_PREVIEW_READY_DIR` e `MGL_UI_BASE_URL`; esse modo deixa a viagem em transporte e não anuncia jornada completa/POD aprovado.

## Evidências e continuidade

Evidências fora do Git: `~/.local/share/mglconnect/evidence/delivery4/` — capturas antes/depois, perfis Chromium/Brave, falhas de provedor/tiles, progresso, recálculo, operação/POD, logs de build/C#/JS/offline, manifestos e respostas dos motores. Nenhum banco, imagem de container ou screenshot foi incorporado ao repositório.

Pendências reais: coleta pequena com borda incompleta; coordenadas ainda declaradas manualmente; não há homologação pesada, trânsito ao vivo, inventário completo de restrições ou pedágio consultado. Não há seleção interativa de alternativas (o cenário retornou uma). Após novo planejamento feito pelo administrador com cockpit já aberto, o motorista deve atualizar/recarregar a rota; o recálculo do ensaio e a reconexão já aplicam estado atualizado. A operação consulta a versão na atualização periódica. Comparação Valhalla é experimental, separada da navegação operacional e sem bases administrativas completas.

Próxima entrega recomendada: confirmação visual de coordenadas ligada aos endereços, notificação explícita de revisão de planejamento ao motorista e ensaio em aparelho físico. A validação pesada deve avançar com cobertura de restrições por segmento e evidência local; aumentar o recorte sem esses dados não autoriza navegação de caminhões.

Implementação consolidada no commit local `b98387c`. Diff staged revisado; Gitleaks não encontrou segredos no conjunto de implementação. Capturas da prévia e ensaios permanecem fora do Git, com manifesto SHA256. Nenhuma revisão independente de Copilot foi solicitada ou atribuída a esta validação.
