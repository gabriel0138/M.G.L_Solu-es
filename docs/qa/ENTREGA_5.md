# Entrega 5 — integração geográfica e atualização privada de planejamento

Base: `e754f34`, em `feat/mgl-functional-gps-mobile`, pasta oficial `mgl-connect`, inicialmente limpa. `b98387c`, `e754f34` e seus ancestrais foram preservados. Sem alteração de main/PR #1, outra worktree, push, merge ou deploy.

## Demonstração preparada

Aplicação: **http://127.0.0.1:5289**, com SQLite descartável em `~/.local/share/mglconnect/preview-delivery5`. OSRM local na porta loopback **5007**, mesma malha pública pequena e proveniência da Entrega 4. A viagem **3** foi deixada **em transporte**, com turno ativo e planejamento geográfico confirmado. Não foi concluída automaticamente.

Use perfis de navegador separados:

1. Administrador `admin` / `admin123`: `/Roteamento?entregaId=3`. Busque origem/destino no Photon, selecione sugestões, confira os marcadores e marque cada confirmação. Para uma parada, clique em **Adicionar parada → Escolher no mapa**, indique o ponto e confirme. **Calcular e salvar pontos confirmados** gera outra revisão.
2. Motorista `carlos.motorista` / `motorista123`: `/Entregas/Navegar/3`. Recebe a nova revisão por SignalR sem reload. O aviso mostra versão, distância anterior/nova e quantidade de paradas. **Ver rota** enquadra o novo traçado; **Acompanhar** controla o seguimento. O GPS sintético pausa durante a troca e pode ser retomado explicitamente.
3. Operação: `/Operacoes`, selecionar a viagem. A mesma identidade/revisão de planejamento é consultada pelo endpoint privado, inclusive antes de qualquer GPS.
4. Cliente `cliente.demo` / `cliente123`: pedido privado, status e POD; sem receber a notificação ou a geometria privada completa.

Contas e dados são de demonstração Development. A preparação dessa viagem utilizou **Photon público real**, consulta explícita de marcos públicos, confirmação no navegador e OSRM local. As jornadas completas até POD usam outros bancos descartáveis e Photon gravado para regressão. Não confundir essas duas evidências.

## Implementação

O Photon antes embutido em `TruckRoutingService` foi extraído para `PhotonGeocoder`, compartilhado pelo fluxo legado e pela nova busca. A administração recebe até cinco candidatos GeoJSON `Point`, limitados ao domínio geográfico do protótipo. Resultado, erro, timeout, cancelamento, limitação de consultas e ausência de resultados não criam coordenadas fictícias. Há limite de corpo, validação de números/faixas, deadline e controle do serviço público. Não foram introduzidos outro stack, cliente OSRM ou biblioteca de frontend.

Uma sugestão é sempre **aproximada**. Nenhum candidato vira endereço exato automaticamente. A interface permite selecionar Photon ou clicar no mapa, confirmações começam desmarcadas e editar o endereço invalida a seleção. O servidor exige todos os pontos confirmados, 2–10 paradas válidas e antiforgery. Uma impressão do vínculo/endereço do pedido evita salvar uma confirmação antiga após alteração de endereço/atribuição: HTTP 409 solicita nova conferência. O pedido original não é sobrescrito pela consulta de geocodificação; o planejamento guarda os rótulos, fonte, coordenadas e instante de confirmação do operador.

O planejamento guarda uma revisão sequencial por entrega. A revisão é reservada por incremento atômico em transação curta **antes** do HTTP; o banco não fica bloqueado enquanto o OSRM responde. Leituras priorizam essa revisão, sem depender de UUID ou relógio. Registros anteriores da Entrega 4 mantêm revisão zero, preservados; a ordenação histórica só serve como desempate desses registros antigos.

`PlanningUpdated` envia apenas identidade da tentativa, revisão e estado. A audiência é revalidada no banco e restringida a administrador e motorista atribuído com turno; clientes, público, outros tenants e motorista revogado não recebem o evento. O frontend busca a geometria novamente pelo endpoint autorizado, rejeita respostas antigas, trata expiração/reconexão e conserva posição observada e filas pendentes. Alteração de rota genérica não concede permissão de navegação pesada.

A chegada de nova rota pausa o replay sintético, atualiza geometria/métricas/paradas e mostra o que mudou. Não limpa assinatura/POD, IndexedDB ou outbox do GPS físico. Arrastar o mapa desliga o acompanhamento. Enquadrar a rota é uma ação explícita; resize conserva o centro escolhido pelo usuário. GPS físico, GPS sintético, geometria viária e circuito autorado continuam separados.

O recálculo de ensaio conserva as paradas ainda à frente do índice sintético percorrido. Esse índice existe porque o servidor escolhe os vértices de ensaio; não é map matching silencioso de GPS físico. Uma amostra atrasada não inicia recálculo do estado atual. Um recálculo de revisão antiga é recusado se outro planejamento já a substituiu. A origem recalculada é identificada como posição GPS sintética de ensaio.

## Defeitos reproduzidos e corrigidos

| Defeito | Correção e evidência |
|---|---|
| Primeiro resultado Photon podia ser estação/local próximo, sem escolha explícita | Busca de candidatos aproximados e confirmação individual no mapa; consultas públicas retornaram alternativas distintas para MASP |
| Data/UUID escolhiam a tentativa mais recente | Revisão atômica; teste força horários idênticos, dois cálculos simultâneos e finalização invertida |
| Motorista precisava de reload após planejamento administrativo | Evento privado, fetch autorizado, banner de revisão/diferenças e troca automática; sentinela do documento comprova ausência de reload |
| Banner alterava altura e deslocava o centro do Leaflet | Resize preserva centro/zoom em modo manual; regressão compara o centro antes/depois da troca |
| Clique propagado por marcador podia reutilizar a coordenada da origem | Coordenada do evento original do mouse e guarda de movimento/zoom; teste nativo exige posição-alvo correta e geometria realmente diferente |
| Resize administrativo reenquadrava a rota antiga enquanto se escolhia um ponto | Escolha manual mantém a câmera, sem fitBounds compulsivo; foco Photon sem animação ambígua |
| Recálculo podia perder parada pendente ou ser iniciado por posição atrasada | Mantém paradas à frente do índice de ensaio, exige amostra atual e revisão ainda vigente |
| Editor de nova revisão podia omitir paradas após recálculo sem confirmações novas | Coordenadas anteriores continuam disponíveis como rascunho, exigindo nova confirmação; sem converter posição de ensaio em endereço exato |

No cenário determinístico com OSRM real, a confirmação de dois pontos retornou **3,1098 km / 134 vértices**. Adicionar uma parada selecionada no mapa alterou efetivamente a geometria para **4,1873 km / 170 vértices**. Não foi apenas uma mudança de identificador. Diferenças pequenas em relação aos marcos digitados na Entrega 4 decorrem dos pontos Photon escolhidos e localizados pelo OSRM, conservados separadamente no contrato.

## Validação

Todos os bancos são SQLite descartáveis e as contas/cargas são fictícias. Os harnesses existentes foram ampliados, com quatro perfis Chromium isolados e repetição no Brave.

| Comando/cenário | Resultado |
|---|---|
| `.NET build MglConnect.sln --no-restore` | Aprovado, zero warnings/erros |
| `.NET test MglConnect.sln --no-restore` | **674 aprovados, 9 ignorados, zero falhos** |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **116 aprovados**, zero falhos |
| Jornada geográfica Chromium e Brave | Aprovada: cliente → despacho → Photon/confirmar pontos → OSRM → alteração → atualização privada no motorista → GPS sintético → operação → POD/PDF |
| Jornada geográfica com proxy local de falhas | Aprovada: provedor indisponível, NoRoute, falha de recálculo e recuperação; sucessos encaminhados ao OSRM real |
| `gps-offline-browser-check.cjs` | Aprovado: Service Worker, interrupção/reinício, reload offline, recibo perdido, reenvio idempotente e limites de IndexedDB |
| Prévia preparada com Photon público | Aprovada, viagem deixada aberta deliberadamente; não é substituto da jornada determinística |
| Aparelho físico via ADB | **Não executado**: nenhum dispositivo disponível/autorizado foi listado |

Os nove ignorados são os testes existentes de PostgreSQL/SQL Server sem instâncias isoladas configuradas. Não foram contados como aprovados. Não houve HTTP 500 da aplicação ou exceção JS não tratada nos cenários aprovados. Erros injetados no provedor são separados das respostas da aplicação.

Regressões direcionadas cobrem solicitações concorrentes e respostas fora de ordem, cancelamento, timeout OSRM/Photon, coordenadas inválidas, confirmação ausente, alteração de endereço/atribuição, expiração, motorista revogado, isolamento entre tenants e exclusão de clientes das notificações. Dois fixtures antigos tinham geocodificação sem `Point`/propriedades válidas; foram corrigidos para refletir GeoJSON válido, sem afrouxar o parser.

Cockpit e roteamento: 360, 390, 428, 768, 1024 e 1440 px em claro/escuro; cockpit também 844×390 e 390×500, incluindo aviso de revisão. O cenário autorado anterior mantém suas regressões de 320/1280 px. Há hit testing de controles/ações críticas, resize só do contêiner, perda/retorno de tiles, fechamento do WebSocket nativo, reconexão/reautorização e reload. A operação também exibe a nova revisão antes do primeiro GPS, sem inventar um marcador de veículo.

Essas verificações usam viewport e ponteiro automatizados. Geolocalização emulada do Chromium, inclusive no teste offline do contrato nativo, **não comprova GPS físico**. Android/iOS, diálogo de permissão do sistema, precisão em movimento, teclado físico/virtual real, safe areas de hardware, bateria, bloqueio de tela e rede móvel continuam sem validação em aparelho.

## Reproduzir

Com o OSRM da Entrega 4 na porta 5007:

```bash
MGL_GEOGRAPHIC_CHECK=true MGL_LINKED_ROAD=true MGL_DEMO_ROUTES=true \
MGL_OSRM_BASE_URL=http://127.0.0.1:5007 MGL_LOCAL_ROAD_ENGINE=true \
node scripts/logistics-journey-browser-check.cjs
# Brave: adicionar MGL_CHROMIUM_PATH=/usr/bin/brave-browser
# Falhas de provedor: adicionar MGL_ROAD_PROXY=true
```

O harness usa Photon HTTP **local com respostas gravadas** de [marcos públicos](../../src/MglConnect.Tests/Fixtures/geocoding/README.md), e OSRM **local real**. Para uso manual com Photon público, `scripts/dev-local.sh` aceita `MGL_PHOTON_ENABLED=true` e `MGL_PHOTON_BASE_URL` (padrão HTTPS público); só uma busca explícita envia o texto digitado ao provedor. Não configurar bases/dados operacionais.

Evidências fora do Git: `~/.local/share/mglconnect/evidence/delivery5/`, com capturas antes/depois, casos corrigidos, perfis Chromium/Brave, logs, respostas públicas Photon e manifesto de arquivos estáticos. Nenhum banco, screenshot ou imagem de container foi versionado.

## Limitações e próximo passo

A área OSRM continua pequena. Photon sugere candidatos; confirmação humana não é certificação cadastral. A notificação é ao vivo, sem fila durável de mensagens, e reload/reconexão recuperam a versão persistida. Falhas de transporte mantêm aviso de atualização pendente; não autorizam navegação pesada. Tiles de fundo continuam externos, com falha identificada. Não há trânsito ao vivo, pedágio consultado ou homologação para caminhões. A comparação Valhalla permanece experimental, preservada.

A jornada E2E desta entrega cobre o cliente de demonstração, administração, motorista, operação e projeção pública. Não representa cobertura de todos os CRUDs ou uma nova jornada completa de intermediador; permanecem prioridades P1. Próxima entrega recomendada: ensaio USB em aparelho físico e, em seguida, jornada de intermediador/CRUDs pendentes, mantendo restrições pesadas bloqueadas até evidência suficiente por segmento.

Implementação consolidada no commit local `3a4e19b`. Diff staged revisado e Gitleaks sem ocorrências. A prévia e o OSRM foram deixados em sessões locais independentes do terminal, somente loopback, sem instalar autostart ou serviço global; logs e PIDs próprios ficam no diretório de evidências. Não houve revisão independente de Copilot nesta execução.
