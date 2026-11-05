# Entrega 6 — estabilidade funcional e experiência mobile

Fechamento: 2026-11-05. Pasta oficial, branch `feat/mgl-functional-gps-mobile`; base `9a2b339`, implementação `cb966fd`. Sem push, merge, deploy, rebase, alteração de main/PR #1 ou de outras worktrees.

## Resultado

**Funcional e comprovado:** criação/edição de clientes, veículos e motoristas pelos formulários reais; pedido de cliente no Chromium e de intermediador no Brave; aprovação, despacho com os recursos recém-cadastrados, turno, planejamento OSRM, atualização privada de rota, GPS sintético, operação, assinatura/POD e PDF autorizado. Cada jornada usa perfis independentes e SQLite novo, exclusivamente fictício.

**Parcialmente funcional:** roteamento genérico `car.lua` na pequena malha central de São Paulo. OSRM, circuito autorado, posição sintética e GPS observado permanecem separados. A navegação pesada continua bloqueada sem evidência das restrições. Não há homologação de caminhão, cobertura geral nem validação física de GPS.

**Não testado:** Android/iOS físico, GPS real, permissões do sistema, precisão em movimento, bloqueio de tela, teclado/safe areas de hardware e bateria. `adb devices` não listou aparelho. SQL Server sem conexão de teste autorizada configurada e sem listener local 1433. Não houve revisão independente Copilot: a revisão focada abaixo é do próprio responsável pela implementação.

**Bloqueador real remanescente:** aparelho autorizado e instância SQL Server descartável são necessários para suas respectivas validações. Não impedem o fechamento das jornadas locais efetivamente aprovadas.

## Defeitos reproduzidos e corrigidos

| Defeito | Reprodução anterior | Correção e regressão |
|---|---|---|
| Status do cliente mostra HTML literal | A ficha apresentava `<span class='badge…'>` como texto; E2E falhou na leitura do conteúdo | Markup Razor normal, sem `Html.Raw`; ficha real aprovada no navegador |
| Ficha excede a tela a 320 px | Razão social longa sem espaços; E2E detectou `scrollWidth > innerWidth` | CSS restrito aos cadastros de clientes: quebra de texto, flex wrap, filhos encolhíveis e botões de 44 px; 320/360/390/428/768/1024, claro/escuro, sem overflow e com hit testing do Editar |
| HTTP 401 mantém rota privada | Nova regressão JS falhou com `tracker.cleared` indefinido | 401 passa pelo mesmo tratamento de 403/404. Testes preservam identidade do GPS observado e da fila pendente; limpeza real do tracker remove somente camadas/steps da rota |
| Colisão global de CNPJ entre tenants causa exceção | POST de criação e edição retornaram 503 no tratamento global de erro, em vez de validação | Validação genérica do índice global e tratamento da corrida de unicidade. Os dois testes HTTP retornam o formulário sem dados do outro tenant, sem modificar registros |

O HTTP 403 já removia a geometria antes desta entrega; a mudança acrescenta o 401 e regressão explícita de ambos. Não se atribui ao 403 um defeito que o teste não reproduziu.

O intermediador de demonstração está vinculado ao segundo cliente fictício. A conta só é criada pelo inicializador de demo em banco vazio, sob o mecanismo existente `DemoData`; as credenciais visíveis na tela de login continuam condicionadas a Development. Nenhum usuário foi inserido em banco operacional. O portal identifica o intermediador e informa o limite do cliente vinculado.

## Resultados finais rastreáveis

Evidências fora do Git: `/home/kali/.local/share/mglconnect/evidence/delivery6/`.

| Etapa | Resultado recente | Log |
|---|---|---|
| Build `.NET build MglConnect.sln --no-restore` | Aprovado, zero warnings/erros | `build.log` |
| C# `.NET test MglConnect.sln --no-build --no-restore` | **676 aprovados, 10 ignorados, zero falhos**, 1m17s; processo terminou com exit 0 | `dotnet-disk-polling.log` |
| JavaScript `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **118 aprovados**, zero falhos/ignorados | `javascript.log` |
| PostgreSQL `bash scripts/test-postgres-local.sh` | **6 aprovados**, zero falhos/ignorados, 10s | `postgres-final.log` |
| Cliente Chromium + cadastros + proxy local de falhas | Aprovado, exit 0 | `client-chromium-complete.log` |
| Intermediador Brave + cadastros | Aprovado, exit 0; terminou mesmo após a interrupção do turno e foi recuperado pelo log | `broker-brave-complete.log` |
| Offline Chromium | Aprovado, exit 0 | `offline-complete.log` |
| Gitleaks do diff staged da implementação | Sem segredos detectados | `gitleaks-staged.log`, `gitleaks-staged.json` |

Os 10 ignorados da execução C# padrão são seis PostgreSQL e quatro SQL Server. Os **seis PostgreSQL foram executados separadamente e aprovados**; os quatro SQL Server continuam não executados. Assim, há 682 casos C# aprovados entre as duas execuções, sem contar os mesmos testes duas vezes. Ignorado não significa aprovado.

O PostgreSQL 18 usa cluster descartável, socket Unix, conexão TCP desabilitada e bancos de teste novos. Foi encerrado pelo cleanup do script. A regressão nova remove as duas colunas de revisão apenas nesse banco descartável, executa o atualizador aditivo duas vezes, preserva o planejamento histórico e verifica a reserva incremental. Os testes anteriores incluem integridade relacional, GPS/POD, permissões e dump/restore nativo.

### O que as jornadas verificam

- Administrador cria e edita cliente, veículo e motorista; o motorista criado autentica-se e é atribuído junto com o veículo editado no despacho real.
- Cliente e intermediador criam pedidos. O reenvio do mesmo formulário retorna o pedido original. Outro cliente recebe 404 no pedido/POD. O intermediador não recebe acesso administrativo. O teste HTTP multitenant complementa o isolamento entre clientes da demo (que compartilham tenant).
- Confirmação explícita das sugestões Photon e de uma parada no mapa; OSRM local calcula a geometria. Nova revisão chega ao motorista sem reload/recenter e aparece na operação; GPS sintético permanece fora da projeção pública/cliente.
- Provedor indisponível, NoRoute, falha de recálculo e recuperação exercitados pelo proxy local no Chromium; sucessos encaminhados ao motor OSRM local, sem rota reta substituta. Photon usa fixtures locais nas jornadas obrigatórias.
- Origem/destino, progresso, atraso/duplicação GPS, reload, reconexão WebSocket nativa, resize do contêiner e falha/recuperação de tiles. Não houve HTTP 500 da aplicação nem exceção JavaScript não tratada nas jornadas aprovadas. Falhas de tiles/provedor injetadas são esperadas e identificadas.
- POD com assinatura, perda do recibo após persistir, reenvio idempotente, confirmação privada/pública e download PDF autorizado.
- Offline: parada/reinício real do servidor, Service Worker/reload offline, fila durável, recibo perdido, reenvio com UUID/data originais e limites/corridas de IndexedDB. A geolocalização nativa é emulada pelo Chromium; não comprova sensor físico.
- Cockpit, tema claro/escuro e controles em 320–1440 px nos cenários existentes; rota vinculada em 360/390/428/768/1024/1440, além de 844×390 e 390×500. A ficha nova tem matriz própria e área de toque verificada.

## Recuperação dos recursos do Kali

O `/tmp` é tmpfs de 7,7 GiB e atingiu 100%; o disco continuava com aproximadamente 324 GiB livres. Tentativas anteriores registraram ENOSPC, falhas SQLite e depois limite de 128 instâncias inotify. Esses resultados **não foram contados como aprovação nem classificados como defeitos funcionais**. Os logs anteriores foram preservados (`dotnet.log`, `dotnet-final.log`, logs de jornadas interrompidas).

A recuperação efetiva usa `TMPDIR` exclusivo no disco e `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` apenas nos processos de teste, dispensando watchers de configuração nesse ensaio. Não houve limpeza global, alteração de sysctl, encerramento de navegador pessoal ou mudança global de configuração. As jornadas finais foram executadas sequencialmente. A prévia anterior 5289 e OSRM 5007 foram preservados.

Também foram corrigidos erros do harness durante sua ampliação: placa de teste fora do formato aceito, escape da expressão de espera e retenção do formulário para reenvio após navegar. Uma captura feita durante a transição da sidebar foi substituída por captura após terminar a animação; não foi tratada como defeito permanente do produto.

## Evidências visuais

Selecionadas no diretório externo `screenshots/`, com as demais capturas e bases fictícias mantidas em `tmp/`:

- `customer-status-before.png`: defeito original do badge.
- `customer-320-before.png`: overflow, depois da correção do badge e antes do CSS.
- `customer-320-after-dark.png` e `customer-320-after-light.png`: ficha corrigida.
- `driver-road-390.png`: cockpit com geometria viária e identificação da simulação.
- `broker-completed-pod.png`: intermediador no resultado com comprovante.

Diretórios completos da execução final: cliente Chromium `mgl-e2e-journey-sStPjz`, admin `mgl-cc-browser-JQKPtU`, motorista `mgl-cc-browser-bAJc4r`; intermediador Brave `mgl-e2e-journey-Jcvxyl`, portal `mgl-cc-browser-ZT8Ch3`; offline `mgl-gps-offline-4qZHB3`, todos sob o `tmp/` exclusivo desta entrega.

## Revisão focada e preservação

Revisado o diff completo desta entrega. Nas integrações da Entrega 5, conferidos os POSTs com antiforgery/autorização administrativa e resolução de escopo antes de geocodificar/planejar; leitura privada por entrega; publicação SignalR restrita e sem geometria no evento; revisão atômica antes da chamada externa e vínculo de endereço/atribuição; rejeição de confirmação obsoleta. As regressões existentes de autorização, cancelamento, expiração e conclusão fora de ordem passaram na suíte final. A única alteração no mecanismo de atualizações é a limpeza no HTTP 401.

`git fsck --connectivity-only --no-dangling` e ancestralidade dos commits anteriores verificados; `3a4e19b`, `9a2b339`, `b98387c`, `e754f34` e histórico preservados. Worktree inicialmente limpa; nenhum trabalho preexistente descartado.

Backup criptografado existente `MGL_CONSOLIDACAO_20261104T085829_phtsb381/estado-atual.tar.gz.gpg`, em `/home/kali/mgl-auditorias/backups/`: SHA256 conferido `cdcfdaf370e8a0b9aa2d97e4658caaaa960bf1afcfcbfdce64e1c53a2e18827d`, modo 0600. É snapshot histórico anterior às entregas recentes, não backup novo da Entrega 6. Não houve novo teste de descriptografia/restore desse arquivo nem consolidação estrutural.

## Reproduzir e continuar

Use o SDK local `~/.local/share/mglconnect-dotnet10/dotnet`. Antes das execuções:

```bash
mkdir -p ~/.local/share/mglconnect/evidence/delivery6/tmp
export TMPDIR="$HOME/.local/share/mglconnect/evidence/delivery6/tmp"
export DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
# .NET build/test e node --test: comandos da tabela acima
MGL_TEST_ARTIFACTS_ROOT="$HOME/.local/share/mglconnect/evidence/delivery6" \
  bash scripts/test-postgres-local.sh
MGL_OPERATIONS_CHECK=true MGL_GEOGRAPHIC_CHECK=true MGL_LINKED_ROAD=true \
  MGL_DEMO_ROUTES=true MGL_OSRM_BASE_URL=http://127.0.0.1:5007 \
  MGL_LOCAL_ROAD_ENGINE=true MGL_ROAD_PROXY=true \
  node scripts/logistics-journey-browser-check.cjs
# Em execução separada: remover MGL_ROAD_PROXY e acrescentar
# MGL_REQUESTER=broker MGL_CHROMIUM_PATH=/usr/bin/brave-browser
node scripts/gps-offline-browser-check.cjs
```

Prévia nova da implementação em **http://127.0.0.1:5290**, banco fictício `~/.local/share/mglconnect/preview-delivery6`, sem tocar na prévia anterior. Login real do intermediador e texto de escopo conferidos pelo Chromium. Os acessos demonstrativos aparecem somente na tela Development. OSRM local continua na porta 5007; Photon público pode ser consultado manualmente nessa prévia, enquanto os E2Es usam fixtures. A prévia inicia com dados novos; não se afirma que a viagem finalizada pelos testes está aberta nela. Depende dos processos locais permanecerem ativos.

Próxima entrega recomendada: aparelho Android autorizado via ADB/reverse para GPS físico, rede móvel/offline e ciclo de bloqueio; em seguida acompanhamento prolongado de viagens. SQL Server e revisão independente continuam pendentes explícitas. Manter o roteamento pesado suspenso e trabalhar nos defeitos observados, sem trocar a stack.
