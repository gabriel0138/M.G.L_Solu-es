# Entrega 9 — paradas ordenáveis e publicação aprovada no servidor

Fechamento: 2026-11-06. Base `4f83ad1`, implementação `202a485`, branch `feat/mgl-functional-gps-mobile`, checkout oficial. Preservados os commits das Entregas 4–8, main, PR #1 e outras worktrees. Sem push, merge, deploy, reset, clean ou mudança de branch.

## Recuperação da execução interrompida

A retomada encontrou os 11 arquivos modificados e os três novos informados pelo usuário. Todos foram preservados antes de editar em snapshot privado verificado por SHA256: `~/.local/share/mglconnect/evidence/delivery9/recovery-20261105T232508/`, com cópia dos 14 arquivos, manifesto da base e patches staged/unstaged. Diretório privado e arquivos 0600; nada foi descartado.

O E2E original foi executado primeiro e reproduziu **Reorder invalidates preview**. A instrumentação mostrou:

| Observação imediata | Antes | Depois da correção |
|---|---|---|
| Botão focado | Descer, habilitado | Descer, habilitado |
| Enter recebido pelo Chromium | `keyCode: 0` | `keyCode: 13` |
| Clique nativo gerado | Não | Sim |
| Chaves visuais da sequência | `1,3,4,5,2` → inalteradas | `1,3,4,5,2` → `1,4,3,5,2` |
| Publicar desabilitado após a ação | Não | Sim, imediatamente |

**Causa: harness CDP, não falha de `moveStop()` ou corrida do provedor.** O helper enviava nome/código textual da tecla, mas não o virtual key code necessário à ação padrão do navegador. O botão não era ativado e a reordenação não ocorria. O [helper de teclado](../../scripts/command-center-browser-check.cjs) foi corrigido; não se acrescentou espera à asserção de reordenação nem tratamento especial de Enter na aplicação. O mesmo E2E passou até POD antes de avançar às demais suítes.

Logs `reorder-reproduced.log`, `reorder-fixed.log` e `keyboard-proof.json` preservam a evidência. Os números da sequência são chaves efêmeras de elementos da interface, não identificadores pessoais ou comprovantes de aprovação.

## Editor operacional entregue

- Origem sempre na primeira posição e destino sempre na última, com identificação de posição fixa e sem controles de reordenação.
- Paradas intermediárias com Subir/Descer acessíveis, operação por teclado, clique e toque emulado; arrastar/soltar HTML5 como alternativa.
- Inclusão, remoção e edição; reordenar preserva endereços, coordenadas, fonte e confirmação dos pontos. A ordem inteira entra no contrato de aprovação.
- Qualquer alteração da proposta cancela a consulta pendente, limpa o comprovante selecionado e invalida imediatamente a prévia/publicação. Respostas antigas são descartadas por geração e identidade da proposta.
- Numeração compacta no mapa correspondente à lista; os nomes/estados completos permanecem nos rótulos acessíveis dos marcadores.
- Comparação de distância, duração do provedor e paradas intermediárias: publicada, proposta e diferença. Sem dados suficientes, **Indisponível/Não comparável**; não se inventam zeros ou ETA.
- Publicada contínua, proposta tracejada e proposta anterior pontilhada quando houver recálculo diferente. A distinção não depende apenas de cor.

O [editor JavaScript](../../src/MglConnect.Web/wwwroot/js/geographic-planning.js), a [view Razor](../../src/MglConnect.Web/Views/Roteamento/Index.cshtml) e o [CSS existente](../../src/MglConnect.Web/wwwroot/css/command-routing.css) foram evoluídos sem trocar o stack.

## Contrato seguro de prévia e publicação

O navegador recebe somente um identificador opaco aleatório de 256 bits. O [armazenamento de prévias](../../src/MglConnect.Web/Services/RoadPreviewStore.cs) conserva a proposta serializada **no servidor**, vinculada a tenant, administrador, entrega/pedido, binding de endereço/atribuição, ordem confirmada e revisão-base. Validade máxima de cinco minutos; entrada de até 2 MiB e cache limitado a 32 MiB. Identificador consumido atomicamente no início da tentativa de publicação; não serve para outro administrador/tenant/entrega nem para reaproveitamento.

O [serviço de planejamento](../../src/MglConnect.Web/Services/DeliveryRoadPlanning.cs) revalida autorização e contexto antes/depois do OSRM e dentro da transação. A comparação é conservadora: geometria, waypoints, distância, duração, instruções, provedor/perfil e identidade da malha/configuração relevante precisam corresponder. Datas de recebimento e checksum textual da resposta HTTP não são tratados como mudança de percurso.

Se o recálculo mudar esses dados, retorna **409 Changed**, sem escrever plano, reservar revisão ou notificar motorista. A interface mantém a rota publicada, apresenta a proposta recalculada e exige **Aprovar nova proposta e publicar**. O servidor emite outro comprovante, também vinculado ao estado atual; o clique seguinte recalcula/verifica novamente.

Se estiver consistente, persiste o snapshot aprovado guardado no servidor, com metadados da nova revisão. Não confia em geometria/hash enviados pelo navegador. A gravação usa comparação atômica da revisão-base e transação serializable curta; nenhuma transação fica aberta durante HTTP. Uma publicação vence e as concorrentes da mesma base exigem nova prévia. A audiência SignalR continua privada; GPS, filas e POD não são alterados pela prévia.

O [controller](../../src/MglConnect.Web/Controllers/RoteamentoController.cs) mantém autorização administrativa, escopo, antiforgery e `no-store`. **Mudança intencional de contrato:** `/Roteamento/Confirmar` exige o comprovante; a antiga publicação direta `/Roteamento/Planejar` responde 409 orientando ao planejador, e seu formulário foi removido. Isso fecha o atalho que contornaria a aprovação. O cálculo avulso sem publicação e o recálculo sintético controlado conservam seus contratos, identificados como ensaio; não se tornam autorização de navegação pesada.

As prévias são voláteis: restart, expiração ou eviction exigem nova consulta/aprovação. Cancelar a prévia na interface preserva os pontos e a rota publicada; a consulta pendente é abortada e o comprovante local descartado. Não há promessa de rascunho durável ou compartilhado entre instâncias. Publicação com resposta perdida exige consultar a revisão persistida; não há repetição automática do comprovante.

## Outros defeitos reproduzidos e corrigidos

| Defeito observado | Reprodução | Correção e regressão |
|---|---|---|
| Conflito PostgreSQL escapava ao tratamento da publicação | Novo teste concorrente: `40001` encapsulado por `InvalidOperationException` do EF; seis testes PG passaram e um falhou | Desempacotamento somente dos códigos conhecidos de concorrência/serialização. Sem retry automático. Os sete testes PG passaram com uma publicação vencedora e uma rejeitada |
| Lista longa empurrava mapa abaixo da primeira tela desktop | Cinco pontos: topo do mapa em **1762,58 px**, viewport de 1000 px; asserção falhou | Resultados agrupados em uma única coluna/trilha, sem distribuir a altura do editor pelas linhas do grid. Topo final **692,92 px**, mapa com 450 px e painel desktop rolável. Chromium/Brave aprovados |
| Rótulos de mapa longos eram cortados | Capturas mostravam tooltips permanentes extensos junto às bordas | Índices curtos 1–5, nomes completos acessíveis, teste da sequência de marcadores |

Na limpeza do Razor foi removida indevidamente uma variável ainda usada pelos parâmetros numéricos; o build detectou CS0103, a variável foi restaurada e o build final passou. `build-release.log` foi preservado como execução falha, sem contá-la como aprovação.

## Validação recente e reproduzível

Evidências fora do Git: `/home/kali/.local/share/mglconnect/evidence/delivery9/`. Temporários em `tmp/` no disco; `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` limitado aos processos de teste. Execuções sequenciais, sem limpeza global, alteração de sysctl, banco operacional ou instalação de dependências.

| Etapa | Resultado | Log |
|---|---|---|
| `.NET build MglConnect.sln --no-restore` | Aprovado, zero warnings/erros | `build-ready.log` |
| C# completo | **691 aprovados, 11 ignorados**, zero falhas, 1m14s | `dotnet-verified.log` |
| JavaScript completo | **120 aprovados**, zero falhas/ignorados | `javascript-ready.log` |
| PostgreSQL descartável | **7 aprovados**, zero falhas/ignorados, 9s | `postgres-fixed.log` |
| Direcionados de planejamento/aprovação/notificação | **22 aprovados** | `approval-tests-final.log` |
| Chromium, editor final e jornada até POD | Aprovado: três intermediárias, teclado/clique/toque, drag/drop emulado, comparação, reaprovação, operação e POD | `layout-fixed.log` |
| Brave/intermediador, editor final e jornada até POD | Aprovado com as mesmas regressões e isolamento | `brave-ready.log` |
| GPS nativo emulado Chromium | Aprovado: permissões, câmera manual, recuperação e POD | `native-verified.log` |
| Offline Chromium | Aprovado: Service Worker/reload offline, parada/reinício do servidor, recibo perdido, fila e replay idempotente | `offline-verified.log` |
| Gitleaks da implementação staged | Sem ocorrências; diff revisado, `git diff --check` aprovado | `gitleaks-implementation.log` |

Dos 11 ignorados da suíte padrão, sete PostgreSQL foram executados separadamente e passaram; quatro SQL Server continuam não executados. Não foram somados como aprovação da suíte padrão. O teste PostgreSQL novo usa cluster/banco exclusivo, valida a publicação concorrente real e o cluster foi encerrado pelo script.

Os testes novos cobrem geometria diferente mesmo com métricas iguais, métricas diferentes, expiração, token falso/reutilizado, ordem incompatível, outro administrador/tenant, endereço/motorista alterados, autorização revogada, falha/cancelamento e duas publicações concorrentes. As regressões anteriores de revisão, GPS, cliente/intermediador e POD permanecem aprovadas.

No E2E, o proxy local encaminha ao OSRM real e injeta **+100 m/+30 s** para simular mudança durante a publicação. A primeira tentativa não altera a versão salva; a segunda aprovação explícita publica o snapshot substituto. Esses valores alterados não são anunciados como uma medição real do motor. A geometria isoladamente alterada é coberta pelo teste C# determinístico. Photon fixture é usado nas jornadas obrigatórias; a preparação local abaixo usou Photon público separadamente.

Viewports 320/360/390/428/768/1024/1440, claro/escuro; cockpit também preserva 1280, 844×390 e 390×500. Controles com nomes acessíveis, hit testing e área de toque; sequência e confirmação mantidas na publicação. Teclado/mouse são entradas CDP, toque é emulado e drag/drop usa eventos HTML5/DataTransfer dentro do navegador real. Não representam gesto físico de Android. Nenhum HTTP 500 ou exceção JS não tratada nas jornadas aprovadas; falhas deliberadas de provedor/tiles são identificadas.

## Demonstração preparada

Administração: **http://127.0.0.1:5290/Roteamento?entregaId=3**

Motorista: **http://127.0.0.1:5290/Entregas/Navegar/3**

Base fictícia nova `~/.local/share/mglconnect/preview-delivery9`; demais prévias preservadas. Serviços anteriores estavam parados e foram reativados a partir do OSRM já preparado, em loopback 5007, sem downloads/preparação de outra malha. Prévia na porta 5290; PID/logs em `preview-live.pid`/`preview-live.log` e `preview-ready.log`.

O harness existente preparou uma entrega com turno, adicionou três intermediárias, trocou as duas primeiras, publicou e confirmou a mesma revisão na operação e no motorista. A viagem 3 ficou **aberta na revisão 2**, sem iniciar automaticamente o replay GPS nem concluir a entrega:

| Percurso da prévia local, sem injeção | Distância | Duração do provedor | Vértices |
|---|---|---|---|
| Origem/destino, v1 | 3,1098 km | 358,1 s | 134 |
| Três intermediárias reordenadas, v2 | **5,9937 km** | **821,5 s** | **262** |

São marcos públicos de teste na malha pequena de São Paulo. Usar perfis separados, contas Development indicadas no login e **Iniciar GPS sintético** no cockpit para percorrer a geometria. Para editar novamente, reconfirmar os pontos, reordenar, gerar prévia e publicar; o motorista mantém a versão salva até a publicação autorizada. A disponibilidade depende dos processos locais.

Capturas selecionadas em `screenshots/`: `editor-before.png`, `desktop-map-before.png`, `desktop-reapproval-after.png`, `reordered-three-stops.png`, `planner-390-light.png`, `planner-390-dark.png`, `driver-same-revision.png` e `operation-same-revision.png`. Diretórios finais: Chromium `mgl-e2e-journey-qLUoMo`, Brave `mgl-e2e-journey-zCxs1d`, offline `mgl-gps-offline-PMubS8`, sob `tmp/`. Métricas da prévia local em `live-metrics.json`.

Para repetir a jornada: usar `TMPDIR` no disco, o export de recarga acima e `MGL_PROFESSIONAL_PLANNER_CHECK=true MGL_DRAFT_ROUTE_CHECK=true MGL_GEOGRAPHIC_CHECK=true MGL_LINKED_ROAD=true MGL_DEMO_ROUTES=true MGL_ROAD_PROXY=true MGL_OPERATIONS_CHECK=true MGL_OSRM_BASE_URL=http://127.0.0.1:5007 MGL_LOCAL_ROAD_ENGINE=true node scripts/logistics-journey-browser-check.cjs`. Em execução separada, acrescentar `MGL_CHROMIUM_PATH=/usr/bin/brave-browser MGL_REQUESTER=broker`. O snapshot de recuperação e os logs vermelhos continuam disponíveis fora do Git.

## Backup e pendências reais

O backup da base até `4f83ad1` (Entrega 8), anterior às alterações, foi verificado na execução original: `/home/kali/mgl-auditorias/backups/MGL_ENTREGA9_20261105T171309/development.tar.gz.gpg`, AES256/0600, 2.644.274 bytes, SHA256 `032fe2a98fdea730deb3c3e4f378c2cfa3111be3d6d125c6adda810896c87416`. Restauração de nove arquivos/quatro bases, clone bare/fsck e integridade/FKs passaram. Chave local separada em diretório privado `backup-keys`, sem conteúdo em chat/Git/relatório. A recuperação adicionou o snapshot dos arquivos pendentes descrito no início; não confundimos o backup da base com um backup dos novos commits finais.

**NÃO TESTADOS:** Android físico, GPS/câmera reais, permissões do sistema operacional, teclado/safe areas de hardware, rede móvel e suspensão/bloqueio de tela. ADB não listou aparelho autorizado; nenhum reverse foi executado. SQL Server permanece sem validação nesta entrega. Não houve revisão independente: a revisão do diff é do responsável pela implementação, sem atribuição ao Copilot.

Limitações: aprovações em memória exigem afinidade de instância e nova prévia após restart/eviction; sem compartilhamento ou rascunho durável. Comparação de rota é conservadora e pode exigir reaprovação por alterações pequenas. Não houve ampliação regional da malha, trânsito ao vivo, consulta de pedágio ou certificação de restrições. **Orientação pesada continua bloqueada; OSRM `car.lua` é roteamento genérico.** Próximas prioridades: ensaio Android e ampliação regional controlada com proveniência, mantendo o planejador e os fluxos atuais.
