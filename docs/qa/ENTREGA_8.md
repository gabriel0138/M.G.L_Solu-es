# Entrega 8 — prévia operacional antes da publicação

2026-11-05. Base `59dbf41`, implementação `ddf7250`, branch `feat/mgl-functional-gps-mobile`, checkout oficial inicialmente limpo. Preservados os commits das Entregas 4–7, main, PR #1 e demais worktrees. Sem push, merge, deploy, troca de branch ou reescrita de histórico.

**Entregue e comprovado:** administrador confirma origem/destino/paradas, calcula uma prévia viária sem publicar, compara com o planejamento salvo e decide publicar. A publicação segue o fluxo existente de revisão persistida, SignalR privado, motorista, operação, GPS sintético e POD.

**Android físico: NÃO TESTADO.** `adb devices -l` não listou aparelho em duas verificações; nenhum reverse ou ensaio de sensor/câmera física foi executado. SQL Server e revisão independente continuam pendentes.

## Melhoria operacional

Antes havia somente **Calcular e salvar pontos confirmados**: conferir a geometria calculada exigia já substituir o planejamento. Agora há dois passos explícitos:

1. **Pré-visualizar percurso:** consulta o adaptador OSRM existente, desenha uma linha laranja tracejada e apresenta distância/duração confiáveis do provedor. A linha verde e os indicadores identificados como **planejamento salvo** continuam representando a versão publicada.
2. **Publicar planejamento confirmado:** exige uma prévia válida e atual no fluxo visual. O servidor recalcula pelo serviço existente e só então persiste/publica a nova revisão. Não reaproveita geometria enviada pelo cliente nem promete que um provedor mutável retornará exatamente a mesma resposta da prévia.

**Ver prévia no mapa** e **Voltar à confirmação** permitem comparar o percurso sem perder os pontos digitados. A confirmação de endereço/coordenada continua explícita e não garante precisão cadastral ou acesso de caminhão.

Alterar endereço, ponto, checkbox, adicionar ou remover parada cancela/invalida o rascunho e bloqueia sua publicação. Respostas atrasadas não restauram uma prévia antiga. Falha do provedor deixa a publicação bloqueada e conserva a versão salva; a tela identifica que a falha pertence à **prévia não publicada**. Durante publicação, a interação com o formulário fica bloqueada até resposta/navegação, evitando duplo envio por interface.

O novo POST administrativo `/Roteamento/Previa` exige autenticação, tenant, vínculo à entrega, antiforgery, 2–10 pontos confirmados válidos e binding atual do pedido/atribuição. Revalida o escopo após o HTTP externo e responde `no-store`. O serviço verifica o vínculo antes/depois da consulta, propaga cancelamento e não grava plano/GPS, não reserva revisão e não publica evento SignalR.

O parser HTTP de confirmação é compartilhado com o POST existente. Os contratos de publicação manual legados permanecem disponíveis e validados no servidor; a prévia não é um novo mecanismo de autorização ou homologação. Não houve migração de banco ou criação de outro cliente OSRM.

Implementação: [serviço de planejamento](../../src/MglConnect.Web/Services/DeliveryRoadPlanning.cs), [controller](../../src/MglConnect.Web/Controllers/RoteamentoController.cs), [interface Razor](../../src/MglConnect.Web/Views/Roteamento/Index.cshtml) e [editor geográfico](../../src/MglConnect.Web/wwwroot/js/geographic-planning.js).

## Defeito reproduzido e corrigido

O aviso do motorista contava origem/destino como paradas. Um planejamento com duas intermediárias era apresentado como **paradas 2 → 4**, enquanto a administração informava duas paradas. A contagem agora é explícita: **paradas intermediárias 0 → 2**. Sem metadados, informa **não informadas**, em vez de inventar zero.

Os dois testes JS novos falharam antes da correção (`stop-count-before.log`) e passaram depois. Chromium e Brave confirmam o texto no cockpit após receber a nova revisão. Capturas antes/depois foram preservadas. O payload, os pontos persistidos e as coordenadas observadas não foram alterados por essa correção.

## Demonstração viária com duas paradas

Nos E2Es, o pedido fictício parte do marco público MASP e termina na Sé. Duas paradas intermediárias são selecionadas por interação real com o mapa na região Bela Vista. Todos os quatro pontos são confirmados antes da prévia.

O motor OSRM local calculou **5,3096 km**, **735,4 segundos** e **242 vértices**, com quatro pontos confirmados e perfil `driving`. A interface arredonda a duração para 13 minutos do provedor; não é ETA com trânsito. A rota original de dois pontos tinha aproximadamente 3,11 km/134 vértices. A mudança alterou a geometria efetivamente retornada pelo motor, não apenas o identificador.

A prévia foi exibida com a identidade da versão salva inalterada; após publicar, o motorista recebeu a mesma geometria/revisão sem reload ou recenter compulsivo, e a operação apresentou a mesma versão antes do primeiro GPS. O replay sintético percorreu a geometria e a jornada terminou com assinatura/POD e PDF autorizado. A simulação não conclui a entrega automaticamente.

Photon usa fixtures locais nos testes obrigatórios; as consultas de rota são encaminhadas ao OSRM local real na porta 5007. A malha/proveniência da Entrega 4 foi preservada, sem expansão de cobertura. Fixtures históricas não foram substituídas por respostas atuais nem reutilizadas como restrições de veículo.

## Validação recente

Todos os testes usam SQLite/cluster PostgreSQL descartáveis e dados fictícios. Temporários e capturas em `/home/kali/.local/share/mglconnect/evidence/delivery8/tmp`, no disco. Execuções sequenciais; `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` apenas nos processos de teste. Nenhuma limpeza global de `/tmp`, alteração de sysctl, instalação de dependências ou encerramento de navegador pessoal.

| Comando/cenário | Resultado | Log em `evidence/delivery8/` |
|---|---|---|
| `.NET build MglConnect.sln --no-restore` | Aprovado; zero warnings/erros | `build.log` |
| `.NET test MglConnect.sln --no-build --no-restore` | **680 aprovados, 10 ignorados**, zero falhas, 1m20s | `dotnet.log` |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **120 aprovados**, zero falhas/ignorados | `javascript-final.log` |
| `bash scripts/test-postgres-local.sh` | **6 aprovados**, zero falhas/ignorados, 10s; cluster encerrado pelo cleanup | `postgres.log` |
| Jornada GPS nativo emulado Chromium | Aprovada, incluindo câmera, permissão, recuperação, turno e POD | `native.log` |
| Cliente/cadastros/OSRM Chromium | Aprovada, incluindo prévia, duas intermediárias, falha/recuperação, resposta atrasada e publicação até POD | `chromium-final.log` |
| Intermediador/cadastros/OSRM Brave | Aprovada com as mesmas regressões e isolamento | `brave-final.log` |
| Jornada offline Chromium | Aprovada, incluindo Service Worker/reload offline, parada/reinício do servidor, recibo perdido e replay idempotente | `offline-final.log` |
| Gitleaks do diff staged | Sem ocorrências; diff revisado e `git diff --check` aprovado | `gitleaks-implementation.log` |

Os dez ignorados padrão são seis PostgreSQL (executados separadamente e aprovados) e quatro SQL Server (não executados). Não são contados como aprovação na execução padrão. Há quatro casos C# novos: prévia sem escrita/publicação, falha sem substituir versão salva, cancelamento/mudança de contexto e proteção HTTP. O conjunto direcionado de planejamento passou nos dez casos, incluindo os existentes (`planning-tests.log`).

As regressões de navegador ampliam os harnesses existentes, sem duplicar infraestrutura. Verificam desenho tracejado real, plano salvo inalterado, publicação bloqueada sem prévia atual, recuperação do provedor, descarte de resposta atrasada e contagem coerente no motorista. A primeira ampliação do harness tinha um template CDP mal interpolado; foi corrigido. Uma asserção imediata de scroll foi trocada por espera da navegação ao mapa. Essas falhas de teste foram preservadas em logs e não contadas como falhas de produto ou aprovação.

Nova prévia: 320/360/390/428/768/1024/1440 px, claro/escuro, sem overflow e com hit testing/área de toque da publicação. Os cenários anteriores mantêm cockpit 320–1440, 844×390 e 390×500, resize de contêiner, tiles, reconexão, GPS atrasado/duplicado, desvio/retorno e permissões. Os testes HTTP/C# mantêm isolamento entre tenants, clientes e atribuições, expiração e revisões concorrentes. Não houve HTTP 500 da aplicação ou exceção JS não tratada nas jornadas aprovadas; falhas de tiles/provedor injetadas são esperadas e identificadas.

## Backup atualizado e restaurado

Antes de editar, criado `/home/kali/mgl-auditorias/backups/MGL_ENTREGA8_20261105T080257/development.tar.gz.gpg`: AES256, modo 0600, 2.604.114 bytes. SHA256 `040465f2ebf80fb43fd9cbd6c857de79bb2391f04dedb32b1291f7f4ab4e4bec`.

Inclui o histórico da branch até `59dbf41baba66cd9e1ef4365888d59c2d68484b3` (Entrega 7), snapshots SQLite consistentes das prévias 5/6/7 e suas chaves de desenvolvimento. Chave aleatória local separada em diretório privado `~/.local/share/mglconnect/backup-keys/`, arquivo 0600, sem seu conteúdo em chat, relatório, argumentos ou Git. A chave não integra o arquivo criptografado.

Restauração real em área privada descartável: sete arquivos comparados por SHA256, clone bare/fsck do bundle, HEAD idêntico e integridade/FKs das três bases aprovados. Staging/restauração sem criptografia removidos. Nenhuma worktree nova. Evidências `backup.log` e `VERIFIED.json` junto ao backup. É backup da base antes da Entrega 8, não dos novos commits desta entrega; cópia externa/offline permanece não executada.

## Prévia local para demonstrar

**Administração:** http://127.0.0.1:5290/Roteamento?entregaId=3

**Motorista:** http://127.0.0.1:5290/Entregas/Navegar/3

Base fictícia nova em `~/.local/share/mglconnect/preview-delivery8`; bases anteriores preservadas. Somente a prévia própria anterior foi encerrada, após conferir PID/assembly/provedor/diretório. OSRM 5007 permaneceu ativo. Logs/PID em `preview-server.log`, `preview.pid` e `preview-ready.log`.

A viagem 3 foi deixada aberta, com turno ativo e rota viária persistida, **sem concluir a entrega**. A preparação reaproveitou o harness existente com `MGL_PREVIEW_READY_DIR`, Photon público consultado explicitamente para marcos públicos, confirmação e OSRM local. É um ensaio separado das regressões determinísticas com Photon fixture. A linha viária e os controles de GPS sintético estão disponíveis; o replay dessa viagem não foi iniciado automaticamente.

Use perfis separados e os acessos Development exibidos no login. Confirme novamente os pontos existentes, adicione paradas, pré-visualize, compare e publique. O motorista recebe a atualização; a operação acompanha a versão salva. Uma consulta de prévia, sozinha, não muda essas telas privadas. A disponibilidade depende dos processos locais.

## Evidências, reprodução e pendências

Capturas selecionadas fora do Git, em `evidence/delivery8/screenshots/`: `planning-before.png`, `planning-preview-390.png`, `planning-preview-two-stops.png`, `preview-unavailable.png`, `driver-stop-count-before.png` e `driver-stop-count-after.png`. Métricas agregadas em `road-metrics.json`; sem coordenadas pessoais. Diretórios finais: Chromium `mgl-e2e-journey-QKPaA3`, Brave `mgl-e2e-journey-Y8NMsy`, offline `mgl-gps-offline-aeVYuM`, sob `tmp/`.

Para repetir: exportar `TMPDIR` no disco e `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false`, usar o SDK local e os comandos da tabela. Para a jornada nova:

```bash
MGL_OPERATIONS_CHECK=true MGL_GEOGRAPHIC_CHECK=true MGL_LINKED_ROAD=true \
  MGL_DEMO_ROUTES=true MGL_ROAD_PROXY=true MGL_DRAFT_ROUTE_CHECK=true \
  MGL_OSRM_BASE_URL=http://127.0.0.1:5007 MGL_LOCAL_ROAD_ENGINE=true \
  node scripts/logistics-journey-browser-check.cjs
# Em execução separada, acrescentar para intermediador/Brave:
# MGL_REQUESTER=broker MGL_CHROMIUM_PATH=/usr/bin/brave-browser
```

Revisão própria focada no diff: escopo/antiforgery/no-store do endpoint, validação antes/depois do provedor, ausência de efeitos persistidos na prévia, cancelamento/geração do frontend, publicação bloqueada durante envio e invariância dos contratos GPS/SignalR. **Revisão independente não foi realizada**; não se atribui esta conferência ao Copilot ou a outro revisor.

**Não testados:** Android físico, GPS real, câmera do aparelho, permissões do sistema, teclado/safe areas de hardware, rede móvel e suspensão/bloqueio do navegador. Nenhuma coleta contínua em segundo plano foi prometida. Quando houver aparelho autorizado, usar `adb reverse tcp:5290 tcp:5290` e um banco privado descartável dedicado ao ensaio, com replay sintético desabilitado; coordenadas pessoais não devem entrar nesta prévia persistente de demonstração ou nas capturas publicáveis.

Limitações preservadas: cobertura OSRM pequena; sem trânsito ao vivo, pedágio consultado ou restrições pesadas verificadas; prévia é volátil e exige nova confirmação após reload. Próximas prioridades: ensaio Android, ordenação acessível de paradas e acompanhamento prolongado de viagens. **OSRM `car.lua` continua genérico; orientação pesada permanece bloqueada.**
