# Etapa 6 — homologação técnica local e preparação operacional

Data: 29/10/2026. Escopo executado: itens disponíveis no ambiente local, com dados sintéticos. **Não é liberação comercial nem homologação física de GPS. Etapa 7 não iniciada.**

## 1. Implementações e correções

- CLI operacional de backup/verify/restore SQLite, antes da inicialização web. Snapshot consistente com WAL, cópias gzip GPS, índices permanentes, POD/assinatura no banco e chaves externas opcionais. Manifesto, hashes, integridade SQL/FKs, fingerprints/metadados e cópia online verificados antes da publicação. Restauração somente em destino novo, sem overwrite. Symlinks/travessia/XML externo recusados; permissões Unix privadas. Fotos POD externas recusam publicação até haver procedimento coordenado.
- Instância PostgreSQL 18.6 temporária por socket privado e bancos novos, sem sudo/Docker/credenciais reais. Ensaios de relacionamentos, serviços, atualização aditiva e backup/restauração nativos com HTTP.
- Defeito reproduzido/corrigido no PostgreSQL: timestamps conservam microssegundos; amostra .NET de 100ns era rejeitada no reenvio por divergência no fingerprint. Normalização anterior a comparação/gravação agora preserva idempotência online e arquivada.
- Verificação GPS reforçada para horários de recebimento/ordenação, associações e indicador fora de ordem no índice permanente; backup recusa cópia online divergente. Falha após INSERT/DELETE tem rollback e repetição testados.
- Correlação HTTP e logs com campos estruturados/ID explícito; métricas `MglConnect.Operational` (`mgl.http.duration`, `mgl.database.failures`). Resposta 503 segura com Retry-After para falha de banco, sem detalhes de SQL/credenciais. Conexão administrativa em `/operacional/saude`, privada/no-store; não representa saúde completa da plataforma.
- Logs automáticos de URLs HttpClient removidos: poderiam registrar endereço de geocodificação, coordenadas de rota ou chave de API. Logs próprios preservam provedor/status/tipo de erro. Página de erro localizada; PDF não afirma validade jurídica automática; avisos de gabarito não apresentam limites numéricos como regra urbana universal. Nenhum novo motor de roteamento foi integrado.

## 2. Inventário e preservação

Os 177 caminhos iniciais foram inventariados por status/tamanho/checksum; inspeção funcional focou persistência, GPS, recuperação, segurança e integrações. Havia 48 modificados, 127 não rastreados e dois com staging/exclusão de working tree preexistentes. Nenhum candidato a temporário foi encontrado nesses caminhos. Relatórios anteriores foram mantidos; helpers de fallback sem chamadas foram identificados, sem reativação/exclusão.

Alterações desta etapa (classificação relativa à entrada, incluindo arquivos que já eram não rastreados):

| Estado | Arquivo |
|---|---|
| Criado | [docs/ETAPA_6_HOMOLOGACAO.md](../docs/ETAPA_6_HOMOLOGACAO.md) |
| Criado | [docs/ETAPA_7_ESPECIFICACAO_ROTEAMENTO.md](../docs/ETAPA_7_ESPECIFICACAO_ROTEAMENTO.md) |
| Modificado | [docs/EXECUCAO_CONTINUA.md](../docs/EXECUCAO_CONTINUA.md) |
| Criado | [docs/HOMOLOGACAO_GPS_DISPOSITIVOS.md](../docs/HOMOLOGACAO_GPS_DISPOSITIVOS.md) |
| Criado | [docs/OPERACAO_BACKUP_RESTAURACAO.md](../docs/OPERACAO_BACKUP_RESTAURACAO.md) |
| Modificado | [scripts/logistics-journey-browser-check.cjs](../scripts/logistics-journey-browser-check.cjs) |
| Criado | [scripts/test-postgres-local.sh](../scripts/test-postgres-local.sh) |
| Modificado | [src/MglConnect.Tests/DriverRoutingTests.cs](../src/MglConnect.Tests/DriverRoutingTests.cs) |
| Criado | [src/MglConnect.Tests/OperationalResilienceTests.cs](../src/MglConnect.Tests/OperationalResilienceTests.cs) |
| Criado | [src/MglConnect.Tests/PostgresIntegrityTests.cs](../src/MglConnect.Tests/PostgresIntegrityTests.cs) |
| Criado | [src/MglConnect.Tests/RecoveryTests.cs](../src/MglConnect.Tests/RecoveryTests.cs) |
| Modificado | [src/MglConnect.Tests/SecurityHttpFixture.cs](../src/MglConnect.Tests/SecurityHttpFixture.cs) |
| Modificado | [src/MglConnect.Web/Controllers/GpsHistoricoController.cs](../src/MglConnect.Web/Controllers/GpsHistoricoController.cs) |
| Modificado | [src/MglConnect.Web/Controllers/HomeController.cs](../src/MglConnect.Web/Controllers/HomeController.cs) |
| Criado | [src/MglConnect.Web/Controllers/OperationalHealthController.cs](../src/MglConnect.Web/Controllers/OperationalHealthController.cs) |
| Criado | [src/MglConnect.Web/Middleware/OperationalDiagnosticsMiddleware.cs](../src/MglConnect.Web/Middleware/OperationalDiagnosticsMiddleware.cs) |
| Criado | [src/MglConnect.Web/Operations/RecoveryCommand.cs](../src/MglConnect.Web/Operations/RecoveryCommand.cs) |
| Criado | [src/MglConnect.Web/Operations/SqliteRecovery.cs](../src/MglConnect.Web/Operations/SqliteRecovery.cs) |
| Modificado | [src/MglConnect.Web/Program.cs](../src/MglConnect.Web/Program.cs) |
| Modificado | [src/MglConnect.Web/Services/GpsArchiveService.cs](../src/MglConnect.Web/Services/GpsArchiveService.cs) |
| Modificado | [src/MglConnect.Web/Services/GpsHistoryReader.cs](../src/MglConnect.Web/Services/GpsHistoryReader.cs) |
| Modificado | [src/MglConnect.Web/Services/GpsIngestionService.cs](../src/MglConnect.Web/Services/GpsIngestionService.cs) |
| Modificado | [src/MglConnect.Web/Services/PodPdfService.cs](../src/MglConnect.Web/Services/PodPdfService.cs) |
| Modificado | [src/MglConnect.Web/Services/TruckRoutingService.cs](../src/MglConnect.Web/Services/TruckRoutingService.cs) |
| Modificado | [src/MglConnect.Web/Views/Shared/Error.cshtml](../src/MglConnect.Web/Views/Shared/Error.cshtml) |

**Arquivos removidos nesta etapa: nenhum.** As exclusões preexistentes de README/render não foram restauradas nem alteradas no staging.

## 3. Validação automatizada

| Bateria | Aprovados | Falhos | Ignorados | Escopo |
|---|---:|---:|---:|---|
| .NET solução completa | 365 | 0 | 4 | 369 casos, SQLite e PostgreSQL temporário |
| JavaScript completo | 105 | 0 | 0 | filas, GPS, SW, mapas, UI e regressões |
| PostgreSQL | 3 | 0 | 0 | Subconjunto da suíte .NET, não somar novamente |
| Recuperação | 16 | 0 | 0 | Subconjunto .NET: WAL, HTTP restaurado, falhas após escrita, integridade, destino, XML/links |
| Resiliência operacional | 2 | 0 | 0 | Subconjunto .NET: 503 sanitizado/correlação e autorização da saúde |
| Chromium | 3 ensaios | 0 | 0 | Jornada com recuperação, GPS offline e histórico |

Build completo: **0 erros, 0 avisos**. A suíte geral inclui autorização/revogação, isolamento TenantId/ClienteId, idempotência de pedido/despacho/GPS/POD, atribuições, PDF/CSV, turnos e concorrência. Falhas de rede, timeout, HTTP 503 e JSON inválido de geocodificação/rotas têm oito casos novos determinísticos; não houve consultas externas reais nesses testes.

Comandos reproduzíveis a partir da raiz, usando o SDK .NET 10 configurado:

```sh
dotnet build MglConnect.sln --no-restore
bash scripts/test-postgres-local.sh --no-build --logger 'console;verbosity=normal'
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
node scripts/gps-offline-browser-check.cjs
node scripts/gps-history-browser-check.cjs
MGL_RECOVERY_CHECK=1 node scripts/logistics-journey-browser-check.cjs
```

## 4. Provedores efetivamente executados

| Provedor | Evidência e diferenças |
|---|---|
| SQLite | Integração HTTP completa, 31 FKs, índices, atualização aditiva, concorrência, WAL/snapshot, recuperação. Tipagem/clock e locking diferem dos servidores; isso não certifica outro provedor. |
| PostgreSQL 18.6 | Banco novo/31 FKs/serviços, upgrade GPS Etapa 4 preservando captura NULL/UUID, HTTP GPS/POD/CSV/PDF/permissões, dump/restore e recibo repetido online/arquivado. Datas UTC/microssegundos e tipos boolean/bytea/numeric exercitados. Não representa toda a matriz funcional em PostgreSQL ou produção. |
| SQL Server | **Não executado**: instância/binário e conexão autorizada não disponíveis. Os quatro testes continuam condicionados a `MGLCONNECT_SQLSERVER_TEST_CONNECTION` para master; nenhuma expectativa foi removida para contornar isso. |

O projeto usa criação EF e atualizadores aditivos/transacionais; não foi criada uma migração destrutiva nem alterada a semântica de tenancy.

## 5. Backup, restauração e falhas

Ensaio SQLite por HTTP conserva GPS online e arquivado simultaneamente, pedido/entrega/POD, PDF autorizado, conta/isolamento e bearer original com chaves copiadas; novo processo e novo banco. Backup durante WAL com escrita não confirmada exclui essa escrita. Gzip, metadados, manifesto/extra files/links/XML malformado ou entidade externa impedem publicação. Destino existente não é sobrescrito.

No Chromium, backup ao vivo → verify → restore em outro diretório → SIGKILL da aplicação original → inicialização do banco restaurado/chaves na origem local de ensaio. Mesmos perfis autenticados, UUIDs arquivados e PDF foram recuperados; viagem concluída não voltou a ficar navegável pelo motorista.

Pacote verificável preservado em `[artefato temporário local omitido]`:

- Banco: 348.160 bytes, SHA-256 `DC42AB6F5F53283416A7E8C9C95BF8B4D9F4A735DE9AA72B0774D72B8C0C7955`.
- Snapshot sintético: 4 pedidos, 3 entregas, 3 POD, 1 arquivo GPS/1 UUID permanente; 5 arquivos no manifesto, com chaves externas.
- Comandos medidos incluindo partida do processo: backup **861 ms**, verify **770 ms**, restore **771 ms**. Não são RTO/SLA de produção.
- PostgreSQL: pg_dump custom/pg_restore em banco novo, após POD/arquivamento/compactação; GPS online/arquivado, permissões/PDF/CSV e idempotência conferidos via HTTP.

Não se executou falha física de disco/queda elétrica ou recuperação entre máquinas. Backup síncrono SQLite pode bloquear gravações; planejar janela e dimensionar volume. Checksums não autenticam pacote malicioso; usar origem confiável e proteção externa. Procedimento completo em [backup/restauração](OPERACAO_BACKUP_RESTAURACAO.md).

## 6. Chromium e Command Center

Três ensaios passaram: cliente cria pedido → administração aprova/despacha → turno/motorista → navegação/GPS → assinatura/POD → PDF → histórico; recuperação de resposta perdida, offline/reconexão/reinício, histórico arquivado e corrupção 409. **140 combinações de layout**: (4 telas offline + 3 histórico + 4 jornada + 2 restauradas + 1 erro operacional) × 1440/1280/768/390/360 × claro/escuro. Sem overflow indevido/exceções JavaScript; capturas revisadas.

Na jornada, árvore de acessibilidade verifica nomes de controles, idioma português e foco fora de conteúdo inert; a referência da página de erro foi comparada ao X-Correlation-ID real. Isso é acessibilidade básica, não certificação WCAG nem ensaio de leitor de tela. Geolocalização/horários são sintéticos e identificados como evidência de software, não de precisão física.

## 7. Evidências e Git

Logs em `[artefato temporário local omitido],dotnet,js}.log`, `[artefato temporário local omitido],history,journey-final}.log`. Inventário inicial `[artefato temporário local omitido]`; clusters privados `[artefato temporário local omitido]`, preservados e encerrados pelo script. Evidência Chromium final: jornada `mgl-e2e-journey-AK3lwF`, offline `mgl-gps-offline-SFzBQw`, histórico `mgl-gps-history-pBMoNt` no diretório temporário da sessão.

Staging original intacto: SHA-256 `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b`. Sem commit, push, deploy, instalação de serviço, limpeza destrutiva ou uso de dados reais. `git diff --check` limpo. Trabalho anterior mantido. Estado final: **193 caminhos** (52 modificados sem staging, 139 não rastreados, dois com staging original e exclusões preexistentes). Nesta etapa: 12 arquivos novos e 13 modificados, nenhum removido.

## 8. Limitações, riscos e Etapa 7

Pendências externas: SQL Server, dispositivos Android/iOS físicos (permissão, precisão, bateria, tela bloqueada/segundo plano/reinício), HTTPS acessível ao aparelho, armazenamento externo cifrado, recuperação de chaves/certificados, RPO/RTO medidos e operação em múltiplas instâncias. Reconciliar revogações/perfis posteriores ao snapshot antes de reabrir tráfego externo; restauração recupera o estado daquele instante. Pacote local não inclui anexos externos nem cifragem própria; índice permanente/arquivos exigem capacidade. Rate limit e caches por instância não homologam operação distribuída.

Não há falha crítica conhecida sem tratamento nos ensaios disponíveis; isso não elimina as pendências comerciais. Recomenda-se concluir as homologações externas antes de produção. [Protocolo físico](HOMOLOGACAO_GPS_DISPOSITIVOS.md) e [especificação preparatória da Etapa 7](ETAPA_7_ESPECIFICACAO_ROTEAMENTO.md) distinguem testes sintéticos, rota estimada, validação de restrições e homologação. Etapa 7 exige escopo/veículo/carga completos, fonte geográfica/regulatória versionada, licença/custo/cobertura compatíveis e critérios independentes de validação; **não foi iniciada**.
