# Consolidação M.G.L Connect — 2026-11-04

## Objetivo e estado

Uma pasta ativa `mgl-connect`, uma sessão principal Codex e Copilot como revisor pontual. Branch `integration/mgl-unificado-2026-11`, criada sobre `6966b11e2e12ecfd477732649bee78e0dcedc455`. A main permanece sem integração; merge depende de aprovação final do usuário. Publicação e eventual revisão devem ser conferidas pelo SHA remoto e pelo PR informados na entrega, não inferidas deste documento.

Nenhuma funcionalidade nova foi desenvolvida. O código C#/Razor/JavaScript da aplicação e dos testes foi mantido byte a byte na versão decimal validada. Foram recuperadas ferramentas e infraestrutura selecionadas, atualizadas instruções de trabalho e corrigida a descrição pública do estado do produto.

## Recuperação antes de qualquer mudança estrutural

- Backup anterior: verificação de descriptografia e leitura TAR/gzip executada pessoalmente pelo usuário; evidência fornecida nesta missão, não atribuída ao Codex.
- Novo backup: `$HOME/mgl-auditorias/backups/MGL_CONSOLIDACAO_20261104T085829_phtsb381/estado-atual.tar.gz.gpg`, **1.414.858.336 bytes**.
- SHA-256: `cdcfdaf370e8a0b9aa2d97e4658caaaa960bf1afcfcbfdce64e1c53a2e18827d`.
- AES-256, S2K iterado com SHA-512; senha por pinentry local do GnuPG, nunca em chat, arquivo de senha ou argumento. Arquivo 0600, diretório 0700.
- Incluídos Git comum completo (objetos, refs, índices e metadados das worktrees), as seis árvores existentes antes da consolidação, arquivos privados/ignorados e pendências staged/unstaged/untracked. O novo checkout ainda não existia no instante desse snapshot.
- Descriptografia integral em memória, verificação GnuPG e gzip e comparação de conteúdo/permissões contra manifesto: **7.800 arquivos/links conferidos**, fontes e estados Git estáveis antes/depois. Não houve extração de conteúdo privado no repositório. Manifesto privado SHA-256 `7dec00e7b7af7a4f22d60194e24c45a1f9f5d13afbede0005d81cce351ece83f`.
- Backup anterior não sobrescrito; nenhum original removido. Esse backup é local, no mesmo notebook: não equivale a cópia offsite nem a homologação de recuperação de banco operacional. O backup de arquivos não substitui o procedimento de snapshot/restauração do banco em uso.

## Origens e preservação

| Origem | HEAD conferido | Estado de entrada / decisão |
|---|---|---|
| Estabilização original | `823ac5353ae4b99020a2d8c67f18976b0862a67b` | 4 modificações sem staging, 2 estados mistos de índice/worktree e 43 não rastreados; todos preservados |
| Snapshot publicado | `5fe505a09a76a42843b181cf972d583d9030013b` | 3 documentos locais modificados; preservados sem commit nessa worktree |
| Diagnóstico | `a888d56414fb54f3aefec86914fcef34c5622a74` | Limpo; preservado |
| Codex decimal | `6966b11e2e12ecfd477732649bee78e0dcedc455` | Limpo; base da consolidação |
| Publicação anterior | `bc41aeb7ba1afc4b9e77a19b79c470959a9e0874` | Limpa; versão substituída pelo snapshot sanitizado |
| Publicação sanitizada anterior | `e4a685aa0e94df095b5df59e4c347ca1e8c1f605` | Limpa; versão substituída pelo snapshot público |
| Pasta oficial nova | base `6966b11` + commits desta missão | Única pasta ativa para desenvolvimento; outras worktrees não foram removidas |

Consulta ao remoto confirmou `main=d8a54d38342a8dc87a52126db52849a838740e53` e `publication/mgl-snapshot-2026-11-public=5fe505a09a76a42843b181cf972d583d9030013b`. Não se confundiram as versões locais antigas com commits remotos. O histórico funcional de estabilização foi reconstituído/sanitizado no snapshot; não se fez merge indiscriminado da branch antiga. Sanitizações de contatos/seed e correções decimais não foram substituídas por versões antigas.

As alterações documentais pendentes da worktree publicada registram a baseline histórica de 13 HTTP 500 anterior ao diagnóstico. Elas ficam preservadas no backup e na origem; não substituem o contrato decimal e os resultados posteriores. README/render com estados mistos na origem também não foram restaurados nem descartados.

## Classificação e seleção

| Conteúdo | Classificação / tratamento |
|---|---|
| Login, perfis, pedidos, despacho, frota, turnos, emergência, POD, GPS/histórico e PWA | Implementados, com regressões locais; preservados da base decimal, sem nova regra operacional |
| Contrato decimal e cargas por eixo | Implementado e validado; commits `6c19dfe`, `c43406b`, handoff `6966b11` incluídos por ancestralidade |
| Custos/frete/ESG | Estimativas implementadas; não promover a despesas medidas ou piso legal vigente |
| Equipamentos/manutenção completa e delegação multicliente | Pendentes; não implementados nesta missão |
| Dockerfile, Compose, ignore de contexto, exemplos/override de deploy | Recuperados seletivamente das pendências; sintaxe/contratos estáticos verificados; imagem e execução não homologadas |
| 12 scripts versionados na estabilização | Recuperados: ferramentas de browser/jornadas e smoke Release; sintaxe conferida, execução não autorizada nesta missão |
| 5 scripts auxiliares pendentes | Recuperados: execução local, geração de segredos sintéticos, importador offline de cobertura, teste PostgreSQL isolado e validador estático; execução externa/servidor exige autorização própria |
| Workflow original | Preservado fora da publicação; acionava Docker e PostgreSQL automaticamente. Substituído aqui por workflow manual seguro, sem execução remota nesta missão |
| Documentos acadêmicos, geradores acadêmicos, histórico SQL acadêmico e configurações privadas | Preservados no backup/origem; não importados indiscriminadamente |
| Três binários acadêmicos herdados da main | Retirados somente do índice desta branch e ignorados; cópias locais e originais continuam existentes. O histórico público anterior não foi reescrito nem expurgado |
| Bancos, chaves, backups e conteúdo Fleetbase | Fora dos novos commits públicos; nada do Fleetbase foi copiado |
| Provedores de caminhão, cobertura viária e GPS em segundo plano | Experimentais ou sem evidência suficiente; não homologados |

Os 22 arquivos recuperados são Dockerfile, Compose, ignore de contexto, dois arquivos de deploy e 17 scripts. O manifesto privado `selected-files.json` registra hashes e proveniência. Dois telefones de fixture Chromium foram substituídos por placeholders zerados antes da publicação, evitando números discáveis no novo conteúdo; o script continua sem execução de browser nesta missão. Os arquivos exatos relativos à main e ao snapshot estão em [ARQUIVOS_VS_MAIN.txt](./ARQUIVOS_VS_MAIN.txt) e [ARQUIVOS_VS_PUBLICADO.txt](./ARQUIVOS_VS_PUBLICADO.txt). Listas de caminhos não constituem aprovação funcional.

## Validação e limites

Ambiente: Kali Linux x64, SDK isolado .NET 10.0.403/runtime 10.0.14, Node v24.21.0. Raiz privada de fixtures/evidências: `$HOME/.local/share/mglconnect/tests/consolidacao.K5pjDc`. Conexões PostgreSQL/SQL Server removidas apenas dos processos de teste. HTTP/isolamento usam Kestrel loopback e SQLite descartável.

Restore e build Debug: aprovados; **0 erros e 0 warnings**. JavaScript: **107 aprovados, 0 falhos, 0 ignorados**. Sintaxe dos 17 scripts aprovada por AST Python, `bash -n` e `node --check`; validador de infraestrutura: **12 checks estáticos aprovados**. C# completa: **627 casos, 618 aprovados, 0 falhos, 9 ignorados** (cinco PostgreSQL, quatro SQL Server). O TRX confirma as regressões HTTP, de tenant, decimal e persistência. Gitleaks da árvore: sem ocorrências; varredura dos commits é condição adicional antes do push. Os seis estados históricos e o hash do índice original foram novamente conferidos e permaneceram idênticos ao manifesto do backup.

Comandos .NET usaram `DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10`, `TMPDIR` na raiz privada acima e o executável desse SDK:

```sh
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet restore MglConnect.sln --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet build MglConnect.sln --no-restore -c Debug --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet test MglConnect.sln --no-build --no-restore -c Debug --logger 'trx;LogFileName=consolidacao.trx' --results-directory "$TMPDIR/results" --verbosity minimal
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
python3 scripts/validate-infrastructure.py
git diff --check
gitleaks detect --source . --no-git --redact --no-banner
```

Não executados: Docker, PostgreSQL/SQL Server, browser automation, smoke Release, deploy, migrations operacionais ou APIs de roteamento. Workflow preparado com ações fixadas por SHA consultado nos repositórios oficiais, acionamento manual e sem bancos externos/Docker. Não foi executado no GitHub.

## Revisão e aprovação de main

Codex revisou seleção, diff e evidências; isso não é revisão independente. O PR deve solicitar revisão somente do diff pelo Copilot quando disponível, sem fix automático, edição da pasta oficial, browser ou serviços. A existência da solicitação não prova conclusão/aprovação. Eventual indisponibilidade deve ser registrada.

Antes do push, conferir backup, staging exato, privacidade, Gitleaks da árvore/novos commits, validação e remoto sem concorrência. Push da branch foi expressamente autorizado condicionado a essas verificações. Merge em main continua proibido até aprovação final. Não ativar automerge nem apagar referências antigas.

Pendências: revisão independente, decisão sobre merge, homologação dos provedores em ambiente descartável, imagem/Compose, backup offsite/recuperação entre máquinas, licenças, dados legados, dispositivos físicos, TLS/nuvem e cobertura legal de roteamento pesado. Nenhum resultado local elimina essas pendências.
