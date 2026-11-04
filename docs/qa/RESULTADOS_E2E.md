# Execução funcional — 2026-11-04

Base `01bcd82`; implementação/regressões no commit `32d1057`, branch `feat/mgl-functional-gps-mobile`. O checkout de estabilização e a branch de integração foram preservados. Nenhum push ou merge.

## Ambiente e isolamento

Kali Linux, SDK .NET 10 local, Chromium headless via CDP e Node.js. Cada harness cria sua própria raiz temporária, SQLite, chaves e perfis de navegador. GPS usa override do Chromium e amostras determinísticas; não são localizações físicas. O teste offline altera somente o início do turno da própria fixture para validar capturas antigas.

Photon, OSRM e chaves Google são desativados pelo script local. Sem banco operacional, PostgreSQL/SQL Server, containers ou deploy. CSS/JS públicos e camadas de tiles continuam com as dependências de rede já existentes no layout; isso não é uma implantação autossuficiente offline. Rota viária externa não foi consultada/homologada.

## Comandos e resultados observados

Executados na raiz do repositório; `dotnet` abaixo corresponde ao SDK local. Os comandos .NET test foram executados com as variáveis `MGLCONNECT_SQLSERVER_TEST_CONNECTION` e `MGLCONNECT_POSTGRES_TEST_CONNECTION` removidas do processo.

| Comando | Resultado |
|---|---|
| `dotnet restore MglConnect.sln --verbosity minimal` | APROVADO; projetos atualizados. |
| `bash scripts/dev-local.sh` com `MGL_LOCAL_DATA_DIR` novo e porta loopback | Build Debug APROVADO, 0 warnings/erros; aplicação inicia com seed fictício. O script faz restore/build antes de iniciar. |
| `dotnet test MglConnect.sln --no-build --no-restore -c Debug --verbosity minimal` | 635 casos: **626 aprovados, 0 falhos, 9 ignorados**, duração reportada 1 min 9 s. Ignorados dependem de provedores externos. |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **108 aprovados, 0 falhos**; inclui a nova regressão de GPS em curva. |
| `node scripts/logistics-journey-browser-check.cjs` | APROVADO: jornada completa e regressões prioritárias integradas. |
| `node scripts/gps-offline-browser-check.cjs` | APROVADO: GPS antigo, IndexedDB, offline, parada/reinício, recibo perdido, reenvio sem duplicação e limite concorrente de 2.000 eventos. |
| `git diff --check` e `git diff --cached --check` | APROVADOS antes do commit de implementação. |
| `git diff --cached \| gitleaks stdin --redact --no-banner` | Diff de implementação sem ocorrências; log local preservado. |

A jornada exercita: autenticações separadas → pedido do cliente → aprovação → despacho → código temporário → turno do motorista → GPS sintético → posição persistida consumida pelo mapa da operação → assinatura desenhada via ponteiro → POD → perda proposital do recibo após commit → reenvio idempotente → convergência de estados → PDF privado autorizado → viagem encerrada e GPS desativado.

Verificações adicionais no navegador: anônimo recebe 401 na API GPS; cliente e motorista recebem 404 para entrega arbitrária; ambos são impedidos de acessar Dashboard. A suíte C# cobre isolamento de outros clientes/tenants e PDF/SignalR privados. Isso não equivale a homologação visual de todas as telas ou a sessão de navegador de intermediador.

A execução final monitora exceções JS não tratadas e respostas HTTP locais >=500: nenhuma registrada na jornada final. A falha de rede do POD é injetada intencionalmente pelo teste.

## Cobertura visual e funcional

- Navegação ativa do motorista: **320, 360, 390, 428, 768, 1024, 1280 e 1440 px**, claro/escuro; hit testing de GPS, ausência de overflow e espaço entre painéis; marcador recebido permanece visível na faixa do mapa.
- Alturas reduzidas: **844×390 e 390×500**, detalhes GPS abertos; GPS, emergência e POD alcançáveis por rolagem. É simulação de viewport; teclado móvel físico NÃO TESTADO.
- Perfil motorista: 360 e 390 px.
- Estados finais de administrador, cliente, motorista e rastreio público: 360, 390, 768, 1280 e 1440 px nos dois temas; nomes acessíveis e foco fora de áreas inert.
- Offline: cockpit, detalhes administrativos, histórico do cliente e operação nas mesmas cinco larguras/dois temas.
- Fixture de curva marcada como sintética; resize apenas do container Leaflet para 280 px.

## Evidências locais fora do Git

Raiz: `$HOME/.local/share/mglconnect/evidence/functional-20261104/`.

117 PNGs, manifest e logs de testes/servidores. Bancos, perfis e chaves descartáveis ficam nas raízes temporárias criadas pelos harnesses; não foram adicionados ao Git. Diretórios de interesse:

- `before-driver/`: 360×800, 390×844, paisagem e altura reduzida.
- `before-geometry/`: diagonal entre amostras na fixture.
- `after-driver/`: cockpit corrigido, GPS confirmado, curva, perfil e entrega.
- `after-admin/`, `after-client/`, `after-public/`: despacho e estados finais.
- `offline-driver/`, `offline-admin/`, `offline-client/`: estados GPS/offline.

As falhas de ensaio anteriores estão descritas em [bugs e correções](BUGS_E_CORRECOES.md); os logs `journey-final.log` e `offline.log` representam as execuções aprovadas. Screenshots isoladas não são critério de aprovação: os harnesses verificam estado, entrada de ponteiro, persistência e respostas.

## Não testado nesta entrega

Todos os CRUDs administrativos, navegador autenticado de intermediador, dispositivo móvel físico, GPS em estrada, 2560 px, roteamento pesado externo, infra de produção e provedores SQL Server/PostgreSQL. Detalhamento por tela na [matriz](MATRIZ_FUNCIONAL.md); próximos trabalhos no [roadmap](ROADMAP_PROTOTIPO.md).
