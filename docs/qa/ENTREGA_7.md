# Entrega 7 — confiabilidade do GPS e preparação mobile

2026-11-05. Base limpa `49a28e9`, implementação `c9d08b0`, branch `feat/mgl-functional-gps-mobile`, pasta oficial. Preservados `cb966fd`, `49a28e9` e seus ancestrais. Sem troca de branch, nova worktree, alteração de main/PR #1, push, merge ou deploy.

**Fechamento parcial: partes automatizadas concluídas; Android físico permanece NÃO TESTADO.** Não se declara concluída a validação física da Entrega 7.

## Preservação e backup restaurado

Lidos os relatórios das Entregas 5 e 6 antes de editar. Criado backup novo **antes das mudanças**, contendo bundle completo da branch até `49a28e9ece68596682df35d4fa9cfcd858b17ece`, snapshots SQLite consistentes das prévias fictícias 5/6 e suas chaves Data Protection. Outros checkouts e bases operacionais não foram copiados ou modificados.

Arquivo: `/home/kali/mgl-auditorias/backups/MGL_ENTREGA7_20261105T073846/development.tar.gz.gpg`, AES256, 2.643.989 bytes, modo 0600. SHA256: `2869a60f359fd8c4b26c7c6f9ffbf4eed7b77abddfea7ae07168c66f3ca891b6`.

A solicitação inicial pelo pinentry local expirou. A alternativa executada gerou uma chave aleatória local, guardada separadamente em `~/.local/share/mglconnect/backup-keys/MGL_ENTREGA7_20261105T073846.key`, diretório 0700/arquivo 0600. Seu conteúdo não foi exibido, colocado em argumentos, relatório, arquivo de backup ou Git. O GPG leu o arquivo local por `--passphrase-file`. A chave precisa ser preservada separadamente do arquivo criptografado; não foi feita cópia externa/offline nesta entrega.

O backup foi descriptografado e extraído em área privada descartável, com comparação SHA256 dos cinco arquivos, clone **bare** do bundle e `git fsck --full`, HEAD restaurado idêntico, `integrity_check` e `foreign_key_check` das duas bases aprovados. Os arquivos restaurados e o staging sem criptografia foram removidos após a verificação; o staging da primeira tentativa também foi removido. Nenhuma worktree foi criada na restauração.

Evidências privadas locais: `evidence/delivery7/backup-verified.log` e `VERIFIED.json` junto ao backup. É um backup da base anterior às mudanças desta entrega, não de todo o Kali ou dos novos commits da Entrega 7.

## Defeitos reproduzidos → corrigidos → retestados

| Defeito | Evidência anterior | Correção/regressão |
|---|---|---|
| GPS nativo recentraliza com Acompanhar desligado | Browser recebe outra leitura após posicionar manualmente a câmera; teste falha e captura mostra retorno ao veículo | Removido `setView` duplicado do callback Razor. O tracker existente governa a câmera e respeita Follow. A posição continua avançando sem deslocar a câmera manual |
| Aviso de erro persiste após recuperar permissão/sinal | `permission-before-measured.log`: posição válida recebida, mas o aviso anterior permanece | Estados de coleta explícitos (`waiting`, `received`, `denied`, `unavailable`, `timeout`), limpeza do aviso e recuperação do ícone após leitura aceita |
| Motivo da falha GPS fica escondido nos diagnósticos | `hidden-error-hit-before.log`: hit testing falha com diagnósticos recolhidos; screenshot sem explicação visível | Aviso operacional compacto fora do painel técnico, com `role=status`. Mensagens em português para permissão, indisponibilidade e timeout; sem repetir o erro nos diagnósticos ou imprimir mensagem bruta no console |

As regressões foram acrescentadas ao [harness existente](../../scripts/driver-navigation-browser-check.cjs), invocado pela jornada existente. Não foram criados outro cliente GPS, motor de rotas, framework ou scripts E2E duplicados. A mudança de produto está limitada ao [cockpit Razor](../../src/MglConnect.Web/Views/Entregas/Navegar.cshtml); Leaflet, Photon, OSRM, SignalR, serviços, persistência e filas permanecem no stack existente.

A regressão de câmera tolera até 5 m de arredondamento em pixels/resize do Leaflet; o ensaio final mediu 1,43 m, enquanto a observação GPS mudou. Uma exigência inicial submétrica detectava esse arredondamento como falha; o critério foi corrigido. Capturas originais comprovam a recentralização indevida anterior. Houve também um erro inicial do harness ao tentar serializar um objeto Leaflet por CDP; corrigido sem alteração de produto. Essas tentativas não foram contadas como aprovação.

## Matriz de execução e limites

| Ambiente | O que foi comprovado | Limite |
|---|---|---|
| Chromium 156.0.8078.47, Linux, perfis separados | Cockpit, API geolocation emulada, permissão concedida/negada/restaurada/revogada, sinal indisponível/restaurado, câmera manual e jornada até POD | Não é Chrome Android nem sensor físico |
| Brave 156.1.98.63, Linux | Intermediador → pedido → cadastros/despacho → OSRM → revisão privada → GPS sintético → operação → POD/PDF | Viewports automatizadas, sem aparelho |
| Android por `adb devices -l` | Comando executado duas vezes; nenhum aparelho listado | **NÃO TESTADO**; não foi executado `adb reverse` |
| SQL Server | Quatro testes permanecem ignorados | Sem instância descartável autorizada |

Viewport normal/circuito: 320, 360, 390, 428, 768, 1024, 1280, 1440; rota OSRM: 360, 390, 428, 768, 1024, 1440; também 844×390 e 390×500, claro/escuro. Verificados overflow, dimensões úteis do mapa, botões/área de toque, origem/destino após resize, `ResizeObserver` sem resize de janela, falhas de tiles e reconexão. As novas transições de permissão e o aviso operacional foram exercitados a 390×844. Não se afirma que todas as combinações de erro foram testadas em todos os tamanhos.

Não testados: GPS/precisão real, diálogo de permissão Android, teclado virtual e safe areas de hardware, rede móvel real, economia de bateria, bloqueio/desbloqueio e coleta em segundo plano. O aviso existente em **Limites da coleta e do armazenamento** informa que a página precisa permanecer aberta e que o navegador não garante rastreamento em segundo plano. Nenhuma garantia de continuidade de PWA foi adicionada.

## Validações finais

Evidências externas: `/home/kali/.local/share/mglconnect/evidence/delivery7/`. Execuções sequenciais com `TMPDIR` exclusivo no disco e `DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false` somente nos processos de teste. Nenhuma limpeza global de `/tmp` ou alteração global de limites/configuração.

| Comando/cenário | Resultado | Log |
|---|---|---|
| `.NET build MglConnect.sln --no-restore` | Aprovado, zero warnings/erros | `build-final.log` |
| `.NET test MglConnect.sln --no-build --no-restore` | **676 aprovados, 10 ignorados**, zero falhas, 1m14s | `dotnet-final.log` |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **118 aprovados**, zero falhas/ignorados | `javascript-final.log` |
| `bash scripts/test-postgres-local.sh` | **6 aprovados**, zero falhas/ignorados, 9s; cluster descartável encerrado pelo script | `postgres-final.log` |
| Jornada nativa emulada Chromium | Aprovada até POD, incluindo câmera e permissões novas | `native-final.log` |
| Cliente/cadastros/OSRM Chromium, proxy de falhas | Aprovada até POD, com provedor indisponível/NoRoute/recuperação | `road-chromium.log` |
| Intermediador/cadastros/OSRM Brave | Aprovada até POD, com isolamento e reenvio idempotente | `road-brave.log` |
| `gps-offline-browser-check.cjs` | Aprovada: parada/reinício do servidor, Service Worker, reload offline, recibo perdido, IndexedDB e replay idempotente | `offline.log` |
| Diff staged da implementação + Gitleaks | Diff revisado, sem whitespace inválido ou segredos detectados | `gitleaks-implementation.log` |

O lote final build → C# → JS → PostgreSQL → Chromium → Brave → offline terminou com exit 0. Os dez ignorados C# são seis PostgreSQL, executados separadamente e aprovados, e quatro SQL Server, não executados. Não há aumento da contagem JS: as regressões novas estão no navegador real. Nenhuma execução ignorada/interrompida foi contada como aprovada.

As suítes exercitam ingestão autenticada, posições válidas/inválidas/atrasadas/duplicadas, projeção de progresso sem mover a observação para uma rua, UUID/recibo idempotentes, isolamento entre tenants e atribuições, usuários revogados, confirmação obsoleta e conclusão fora de ordem. O navegador confirma a mesma revisão no motorista/operação, sem fornecer geometria privada a cliente/público; assinatura/POD e fila offline permanecem preservados. Nas jornadas aprovadas não houve HTTP 500 da aplicação ou exceção JavaScript não tratada. Falhas de tiles/provedor injetadas são esperadas e identificadas.

As coordenadas dos ensaios são marcos públicos fictícios/controlados. A emulação da API nativa usa o contrato de ingestão do aparelho (`isSimulated=false`) **somente nos bancos descartáveis do teste**; isso testa o canal, não comprova sensor físico. O replay de demonstração mantém sua identificação sintética separada. Não foi coletada posição pessoal real.

## Capturas antes/depois

Sob `evidence/delivery7/screenshots/`, fora do Git:

- `camera-forced-before.png` / `camera-manual-after.png`: nova leitura e comportamento da câmera.
- `permission-hidden-before.png` / `permission-visible-after.png`: motivo da negação antes escondido e agora visível.
- `permission-recovered.png`: recuperação sem aviso de erro antigo.

Diretórios completos sob `tmp/`: nativo `mgl-e2e-journey-CjiQcx`, motorista `mgl-cc-browser-c4RPzB`; Chromium/OSRM `mgl-e2e-journey-84Yv8M`; Brave `mgl-e2e-journey-dZMAiY`; offline `mgl-gps-offline-uCNNf0`. Screenshots e bancos são evidência local, não conteúdo publicável.

## Prévia e próxima validação

**http://127.0.0.1:5290/Entregas/Navegar/1** — versão atual, base nova `~/.local/share/mglconnect/preview-delivery7`. A prévia antiga própria da porta 5290 foi encerrada após conferir PID, assembly, provedor SQLite e diretório de demonstração; os bancos anteriores 5/6 foram preservados. OSRM local 5007 permanece ativo.

Esta prévia está em **modo GPS do aparelho**, `MGL_DEMO_ROUTES=false`, sem iniciar replay sintético. Login real de admin/motorista, emissão de código, início do turno e hub privado foram conferidos no Chromium. A viagem 1 ficou aberta, sem GPS iniciado, sem posição inventada e sem entrega automática. Não há rota persistida nessa prévia nova: o administrador pode confirmar pontos e planejar via Photon/OSRM; sem rota/GPS o mapa aguarda dados, sem centro fictício ou métricas. As jornadas OSRM aprovadas estão nos bancos E2E separados.

Contas Development: acessos exibidos no login; motorista `carlos.motorista` / `motorista123`. É necessário novo login porque a prévia nova tem outra chave de sessão. Disponibilidade depende dos processos locais; logs/PID em `preview.log`/`preview.pid`. `preview-check.log` registra a preparação sem ligar GPS.

Para repetir a regressão nativa, com o SDK local e os exports de temporários acima: `MGL_NATIVE_GPS_CHECK=true node scripts/logistics-journey-browser-check.cjs`. `only` é um atalho de diagnóstico, sem a matriz responsiva completa. As jornadas OSRM usam as flags da Entrega 6, alterando apenas o diretório de evidências/TMPDIR; Photon gravado e OSRM local real continuam separados do uso manual de Photon público na prévia.

**Próximo passo prioritário:** conectar Android pessoal autorizado, verificar novamente ADB e só então executar `adb reverse tcp:5290 tcp:5290`, abrir Chrome Android e validar permissão, GPS real, rotação/teclado, perda de rede, suspensão da tela, fila, assinatura e emergência. Não foi feita revisão independente nesta entrega. SQL Server e cópia externa do backup/chave seguem pendentes explícitas.

O OSRM `car.lua` segue genérico, sem validação de restrições de caminhão. Navegação pesada permanece bloqueada. O resultado desta entrega é confiabilidade comprovada do navegador e preparação do ensaio físico, sem declarar homologação rodoviária ou validação Android inexistente.
