# Etapa 8A — infraestrutura, Docker, nuvem e integração mobile — 30/10/2026

## Resultado e fronteiras da evidência

Implementação local concluída: infraestrutura de execução/segredos/sondas/proxy, pacote Release testado, configuração de containers/CI e documentação coerente com a PWA existente. **Imagem Docker, Compose, SQL Server e hospedagem em nuvem não foram executados nesta etapa.** O daemon local recusou acesso ao socket, Compose não está instalado e não houve sudo, alteração de permissões do daemon, instalação global ou contratação/deploy. A configuração de containers permanece pendente de ensaio real; publicação de arquivos por `dotnet publish` não significa publicação de site.

Documentos complementares: [mobile/API/banco](./ARQUITETURA_MOBILE_API_BANCO.md), [implantação e arquitetura de referência](./DIAGRAMA_IMPLANTACAO.md), [backup/restauração](./OPERACAO_BACKUP_RESTAURACAO.md). A Etapa 7B.2 não foi iniciada.

## Manual institucional e texto acadêmico

Leitura integral do [Manual_PIM_IV - ADS.docx](../Manual_PIM_IV%20-%20ADS.docx) e de [PIM-agora.pdf](../PIM-agora.pdf), além do checkpoint e relatórios [6](./ETAPA_6_HOMOLOGACAO.md), [7A](./ETAPA_7A_MOTOR_RESTRICOES.md) e [7B.1](./ETAPA_7B1_PROVEDOR_E_COBERTURA.md). A cópia `Manual_PIM_IV - ADS (1).docx` é idêntica: SHA-256 de ambos `11e203beb48b3fa09d0e97c5d2306e9c504c1a1672dad1b5f60c059b9dd06901`. O PDF possui 31 páginas físicas, SHA-256 `5a8600b83d514388c60a7c6da442ca707b68fcbfbdba223e1c2f9513513c4909`; os documentos originais não foram modificados.

O manual §2.4, **Etapa 8 — Infraestrutura em Nuvem e DevOps**, exige contemplar arquitetura em nuvem, serviços, containers, pipeline CI/CD, monitoramento, escalabilidade e segurança. Não exige nominalmente Docker, Kubernetes, um provedor específico ou uma URL já publicada. Docker é a escolha de implementação desta etapa. A exigência geral de desenvolver uma solução não deve ser reduzida a uma promessa de implementação; as evidências abaixo distinguem o que realmente executou.

Referências de localização no DOCX: seção 2.1 objetivo geral; 2.2 objetivos específicos; 2.4 Etapas 4–8. Para localização reproduzível, os parágrafos XML não vazios relacionados são P424–434 (Web), P435–444 (Mobile), P445–454 (arquitetura), P455–466 (banco), P467–478 (nuvem/DevOps), contando `w:p` em `word/document.xml`. A paginação do editor pode variar; a seção é a referência normativa.

| Exigência / origem | Evidência presente | Estado real e limites |
|---|---|---|
| Web, REST, autenticação, segurança e integração — manual §2.4 Etapa 4 | MVC, API, autorização, testes HTTP e jornada Chromium | Implementado/testado localmente |
| Telas/fluxo/autenticação/sincronização/API mobile — manual §2.4 Etapa 5 e objetivos §2.2 | PWA/cockpit, IndexedDB, bearer curto, GPS/POD e regressões | Integração web mobile implementada; não é aplicativo Flutter/nativo nem ensaio físico; aceitação acadêmica da escolha não é presumida |
| OO/camadas/modularização/padrões/manutenção — manual §2.4 Etapa 6 | Controllers/Services/Data, DI, componentes e sequências nos novos documentos | Arquitetura lógica documentada conforme código; monólito, não microserviços |
| Modelo lógico/tabelas/relacionamentos/otimização/segurança — manual §2.4 Etapa 7 | EF, 37 FKs exercitadas, SQLite/PostgreSQL, índices e isolamento | Testes locais disponíveis; SQL Server de referência não homologado |
| Procedures e triggers — manual §2.4 Etapa 7 | Script acadêmico existente em `sql/banco_pim_sqlserver.sql` | Existência não comprova execução/aderência ao schema atual; revisar/exercitar em SQL Server isolado na preparação acadêmica, sem aplicá-lo automaticamente em banco existente |
| Containers — manual §2.4 Etapa 8 | Dockerfile/ignore/Compose/override, segredos e 12 checks estáticos | Configuração implementada; build/run/renderização Compose pendentes por indisponibilidade/permissões |
| Arquitetura de nuvem/serviços/escalabilidade/segurança — manual §2.4 Etapa 8 e §2.2 | Diagrama de referência, DNS/TLS/banco privado/backup/chaves/operação | Proposta técnica documentada; nenhum recurso externo provisionado |
| Pipeline CI/CD — manual §2.4 Etapa 8 | Workflow de validação sem deploy, comandos locais reproduzidos | CI configurada, não executada no GitHub; CD apenas definida como processo futuro condicionado à homologação |
| Monitoramento — manual §2.4 Etapa 8 | Logs/correlação/métricas existentes, sondas mínimas e saúde privada | Implementado local; coletor externo/alertas não provisionados |
| Diagramas de componentes/sequência/implantação — solicitação 8A | Mermaid nos dois documentos de arquitetura | Entregues; o manual exige apresentar arquitetura/modelo, sem nomear esses tipos específicos |
| Evidências, fundamentação e conclusões coerentes — manual §1.4/§1.7 | Logs/resultados/backup/smoke test/matriz e fontes | Evidência local; não inventar publicação, dispositivo real ou homologação |

Divergências que devem orientar **8B**, sem reescrever o PDF agora: §5.2 do PDF (p.16) planeja Flutter/Dart; §5.3 exclui GPS/assinatura/rotas e §5.7 (p.18) exclui offline, enquanto o código atual contém PWA, GPS persistente, fila e POD. §4.5 (p.14) lista endpoints previstos diferentes dos reais. §7.1 (p.20) mantém SQL Server, ainda sem ensaio neste ambiente. §8.1–8.3 (pp.22–23) permite execução local e trata nuvem/containers como evolução; §8.4 propõe Actions. Não apresentar esses trechos como prova de execução nem afirmar Flutter existente. A adequação formal à disciplina Mobile deve ser discutida na preparação 8B, mantendo a descrição técnica verdadeira.

## Implementações e correções

- [Dockerfile](../Dockerfile): SDK/runtime .NET 10 Noble separados, restore/publish, fontes/fontconfig para PDF dentro da imagem, usuário `APP_UID`, porta 8080, exec entrypoint, health CLI e SIGTERM. Digest da imagem e dependências nativas devem ser fixados/confirmados após build real; configuração não prova compatibilidade do PDF dentro do Ubuntu/container.
- [.dockerignore](../.dockerignore): allowlist do código web; exclui Git, bancos, assets de usuário, chaves, appsettings e arquivos .env. Nenhuma senha real/arquivo de configuração do host é necessário no contexto de build.
- [Compose SQLite](../docker-compose.yml): projeto de demonstração separado, porta somente loopback, volumes de dados e chaves, root filesystem somente leitura, tmpfs, capacidades removidas, no-new-privileges e parada em até 30 s. Não usar como publicação em nuvem.
- [Override SQL Server](../deploy/compose.sqlserver.yml): opcional, banco privado sem porta no host, Developer, aceitação de licença explícita, segredo lido em arquivo e dependência de readiness. Uso SA/certificado de servidor confiado no cliente é somente para esse ensaio sintético; produção precisa usuário restrito e certificado validado. SQL Server/Compose não executados.
- [Gerador de segredos](../scripts/create-demo-secrets.py): cria diretório novo privado e senhas sintéticas aleatórias, nunca imprime valores nem sobrescreve arquivo. Arquivos de leitura ficam em pai 0700 para funcionar com os UIDs distintos dos containers; não são segredos cifrados ou um vault. Não montar o segredo SA na aplicação, somente a conexão necessária do ambiente isolado.
- [HostingConfiguration](../src/MglConnect.Web/Operations/HostingConfiguration.cs): leitura KeyPerFile em caminho absoluto explicitamente configurado; sobrescreve valores correspondentes sem precisar de senha em variável de ambiente. Proxy aceita somente IPs explicitamente confiáveis, um salto e par consistente de headers; HTTPS/cookie Secure configuráveis. Configuração ausente preserva execução nativa anterior.
- [Sondas](../src/MglConnect.Web/Controllers/ContainerHealthController.cs) e [CLI](../src/MglConnect.Web/Operations/ContainerHealthCommand.cs): liveness/readiness sem informações operacionais; timeout e 503 sanitizado; CLI usa apenas HTTP loopback no caminho exato, sem redirects/proxy externo ou inicialização de banco. `/operacional/saude` continua protegido por Admin. Readiness certifica conexão, não schema, rotas ou GPS.
- [Program](../src/MglConnect.Web/Program.cs): opções de infraestrutura e janela de encerramento do host de 20 s. Sondas locais não entram em loop de redirecionamento HTTPS. Middleware de conta ativa, autorização, rate limit, hubs e fluxo de rotas foram preservados.
- [PostgreSQL local](../scripts/test-postgres-local.sh): raiz de artefatos configurável por `MGL_TEST_ARTIFACTS_ROOT`/`TMPDIR`; não força o uso de um tmpfs esgotado. Dados antigos não foram apagados.
- [Smoke Release](../scripts/published-host-smoke.py): sobe pacote publicado em loopback com SQLite/chaves próprios e secrets de demonstração, verifica API/negação/PDF e reinício normal. Não abre site externo ou container.
- [CI](../.github/workflows/ci.yml): ações oficiais pinadas por SHA, permissões de leitura, build/.NET/JS/PostgreSQL temporário, checks estáticos e configuração de ensaio Compose num runner isolado; sem push de imagem/deploy. Workflow não enviado/executado. A ferramenta estática utiliza PyYAML 6.0.3 [MIT](https://pypi.org/project/PyYAML/6.0.3/), já disponível localmente; CI prevê venv isolado. Não foi adicionada dependência de runtime .NET/JavaScript.

## Preservação e problema de ambiente

Entrada: 222 caminhos alterados (55 modificados sem staging, 165 não rastreados e dois com staging/exclusões preexistentes). Inventário completo por caminho/hash e cópia verificável de **364 arquivos**, incluindo assets, código/testes/scripts/docs, em `[caminho local omitido]`; tar SHA-256 `a9c39a5f42b582661d75e72c468a49bd9d53b96a4d26ab4357ce2f4647166f07`. Manifesto registra exclusões: appsettings, documentos acadêmicos binários, bancos/chaves/.env; são configurações/entradas preservadas no local original, não removidas. Ausências de README/render na árvore de trabalho permanecem como encontradas. A cópia não contém histórico/index Git, dados operacionais ou configurações secretas; não é backup externo da instalação. Backups anteriores 7A/7B.1 tiveram tar e hashes por arquivo verificados, sem alteração.

O baseline inicial de testes encontrou `/tmp` (tmpfs 7,7 GiB) lotado: 314 aprovados, **142 falhos por SQLITE_FULL**, quatro ignorados. Não era evidência suficiente para afirmar uma regressão de código. Sem limpar evidência/dados antigos, os novos testes passaram a usar `[caminho local omitido]`, em disco com espaço. Baseline completo repetido: **456/0/4**; JS **107/0/0**. A mudança do script PostgreSQL evita a dependência do `/tmp` fixo. Não houve limpeza global nem alteração de dados reais para esconder falhas.

## Testes efetivamente executados

| Bateria | Aprovados | Falhos | Ignorados | Evidência/escopo |
|---|---:|---:|---:|---|
| .NET geral com SQLite/PostgreSQL | 474 | 0 | 4 | 478 total; inclui cinco casos PostgreSQL e regressões de rotas |
| Infraestrutura .NET | 18 | 0 | 0 | Subconjunto acima: health, indisponibilidade, segredos, proxy confiável/não confiável, HTTPS/Secure cookie |
| JavaScript completo | 107 | 0 | 0 | GPS/fila/POD/Service Worker/mapas/rotas/UI |
| Configuração de infraestrutura | 12 checks | 0 | — | YAML/contratos de Dockerfile/Compose; somente estático, não valida daemon/imagem |
| Pacote publicado Release | 8 verificações | 0 | — | HTTP nativo, secrets, API/negação, PDF, CLI, SIGTERM e persistência/reinício |
| SQL Server | 0 | 0 | 4 | Sem instância autorizada; não homologado |
| Docker image / Compose run | 0 | 0 | pendente | Acesso negado ao daemon local; plugin Compose ausente; sem contornar permissões |
| Workflow GitHub / nuvem | 0 | 0 | não executado | Somente arquivos de configuração/arquitetura; sem push/deploy |

Build completo e publish Release sem erros/avisos. O smoke Release executou oito verificações em banco sintético: sondas/no-store, código de saída do health CLI com servidor ligado/desligado, login usando arquivos de segredo, API do motorista, negação de outro motorista, PDF real, SIGTERM e reabertura com o token anterior/banco/chaves persistidos. Tempo medido do encerramento normal: **64 ms** no ensaio, sem equivaler a SLA de produção. A consulta SQLite direta no smoke é uma asserção da fixture para obter IDs/conferir persistência, não acesso do aplicativo móvel ao banco.

Logs/evidências ficam em `[caminho local omitido]`, incluindo `baseline-dotnet.log`, `hosting-focused2.log`, `full1.log`, `final-build.log`, `final-js.log`, `infrastructure-static.json`, `publish-initial.log`, `published-smoke-initial.log` e `mgl-8a-published-w8jyoljm/evidence.json`. O relatório não publica senhas/tokens do diretório de ensaio. Cinco ensaios Chromium finais aprovados, detalhados abaixo. Testes adicionais dos scripts confirmaram YAML/SHAs/permissões de CI e geração de segredos sem imprimir valores/sobrescrever arquivos, com pai 0700 e arquivos montáveis 0444; não são execução remota ou validação de permissões dentro de um container.

## Comandos reproduzíveis

Execução nativa continua disponível por [dev-local.sh](../scripts/dev-local.sh), sem Docker. Para ensaios, usar uma raiz privada de artefatos com espaço e caminho curto (socket PostgreSQL tem limite de comprimento). O SDK local instalado nesta máquina está em `$HOME/.local/share/mglconnect-dotnet10/dotnet`; nos comandos seguintes `dotnet` representa esse executável no PATH.

```sh
export TMPDIR="$HOME/.local/share/mglconnect/tests/8a"
export MGL_TEST_ARTIFACTS_ROOT="$TMPDIR"
mkdir -p "$TMPDIR"
chmod 700 "$TMPDIR"
dotnet build MglConnect.sln --no-restore
bash scripts/test-postgres-local.sh --no-build --logger 'console;verbosity=normal'
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
python3 scripts/validate-infrastructure.py
# Escolher um diretório de publish/artefatos NOVO para cada repetição.
dotnet publish src/MglConnect.Web/MglConnect.Web.csproj --no-restore -c Release -o "$TMPDIR/published-NOVO" /p:UseAppHost=false
python3 scripts/published-host-smoke.py --package "$TMPDIR/published-NOVO" --dotnet "$HOME/.local/share/mglconnect-dotnet10/dotnet"
MGL_VALHALLA_CONTRACT=1 node scripts/route-restrictions-browser-check.cjs
node scripts/route-restrictions-browser-check.cjs
node scripts/gps-offline-browser-check.cjs
node scripts/gps-history-browser-check.cjs
MGL_RECOVERY_CHECK=1 node scripts/logistics-journey-browser-check.cjs
```

**Containers — comandos preparados, não executados aqui.** Depois de disponibilizar Docker/Compose por procedimento autorizado, gerar segredos em diretório **novo**, fora do repositório, e informar o caminho. Não usar senha de produção. Nunca executar `down -v` ou apagar volume/banco existente para repetir a demonstração.

```sh
export MGL_DEMO_RUN_ID="$(date -u +%Y%m%d%H%M%S)"
export MGL_SECRETS_DIR="$HOME/.local/share/mglconnect/demo-secrets-$MGL_DEMO_RUN_ID"
python3 scripts/create-demo-secrets.py "$MGL_SECRETS_DIR"
export MGL_DEMO_PORT=5084
docker compose -p "mgl8a-$MGL_DEMO_RUN_ID" config --quiet
docker compose -p "mgl8a-$MGL_DEMO_RUN_ID" build
docker compose -p "mgl8a-$MGL_DEMO_RUN_ID" up -d --wait --wait-timeout 180
docker compose -p "mgl8a-$MGL_DEMO_RUN_ID" ps
# Abrir http://127.0.0.1:5084; ler as credenciais sintéticas somente no diretório privado.
docker compose -p "mgl8a-$MGL_DEMO_RUN_ID" stop
# Nova partida deve conservar banco e chaves; parar não apaga volumes.
```

Para SQL Server opcional: gerar **outro** diretório com `--sqlserver`, revisar termos Microsoft antes de definir `MGL_SQLSERVER_ACCEPT_EULA=Y`, usar **outro** projeto Compose e `-f docker-compose.yml -f deploy/compose.sqlserver.yml`. Não reaproveitar o arquivo de conexão SQL no ensaio SQLite: KeyPerFile tem precedência. O override monta a conexão somente no app e a senha SA somente no banco. Não passar dados reais, nem considerar esse desenho homologação do SQL Server. Imagens SQL Server Linux requerem plataforma x86-64 suportada; não foram baixadas/instaladas nesta etapa.

## Fontes técnicas e limites operacionais

- [.NET 10 usa Ubuntu Noble](https://learn.microsoft.com/en-us/dotnet/core/compatibility/containers/10.0/default-images-use-ubuntu) e [construção de imagens .NET](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container): imagens de SDK/runtime separadas; execução local do pacote não substitui ensaio da imagem.
- [Microsoft — proxy/forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0): metadados de transporte exigem fronteira de confiança explícita; não confiar em headers de origem arbitrária.
- [Docker — secrets Compose](https://docs.docker.com/compose/how-tos/use-secrets/): montagem de arquivos e controle por serviço; não é cifragem automática em repouso de arquivos do host.
- [Microsoft — SQL Server em containers](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver17): plataforma/licença/persistência devem ser verificadas antes do ensaio; Developer não é licença de produção.
- Ações oficiais [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet) e [setup-node](https://github.com/actions/setup-node): versões/SHAs consultados e fixados; nenhuma execução remota realizada.

Pendências: build/run real das imagens (incluindo bibliotecas PDF, permissões de volumes/secrets, SIGTERM e restore no container), Compose renderizado com plugin real, SQL Server e backup nativo, TLS/DNS público, aparelhos físicos, proteção externa de chaves/backups, monitoramento externo e distribuição de SignalR/cache/rate limit se houver mais de uma instância. Manter navegação pesada suspensa sem evidência; nenhuma mudança desta etapa homologa restrições viárias. A etapa 8B pode consolidar material acadêmico e evidências disponíveis, mantendo essas pendências visíveis.

## Chromium final e evidências de recuperação

Cinco ensaios completos passaram: **260 combinações** (60 adaptador experimental + 60 restrições + 40 offline + 30 histórico + 70 jornada/restore/erro), nas larguras 1440, 1280, 768, 390 e 360, temas claro/escuro. Sem overflow global indevido ou exceções de página. Capturas desktop/mobile revisadas; a jornada mantém verificações básicas de nomes acessíveis/idioma/foco. Isso não é certificação WCAG nem teste de leitor de tela/dispositivo físico.

| Ensaio | Log na raiz de evidências | Diretório próprio |
|---|---|---|
| Adaptador Valhalla controlado/sintético | `browser-experimental.log` | `mgl-stage7a-browser-JJBwsA` |
| Restrições, falhas e reassociação concorrente | `browser-restrictions.log` | `mgl-stage7a-browser-I85EmC` |
| GPS offline / perda de resposta / reinícios | `browser-offline.log` | `mgl-gps-offline-F0bvkg` |
| Histórico, sete dias, CSV, arquivo e compactação | `browser-history.log` | `mgl-gps-history-Fq4ZND` |
| Pedido até POD/PDF, backup e restore isolado | `browser-journey.log` | `mgl-e2e-journey-CgegKS` |

Raiz: `[caminho local omitido]`. Capturas representativas: `mgl-cc-browser-7GXzzS` (admin experimental), `mgl-cc-browser-iPSu0b` (motorista), `mgl-cc-browser-z3AJ0d`/`ZZNTmO` (jornada). Os servidores/processos próprios dos ensaios foram encerrados. Dados são sintéticos e permanecem no host; não são logs de produção ou evidência de GPS físico.

A jornada incluiu cliente→pedido→aprovação→despacho→código/turno→GPS→POD/assinatura→PDF autorizado→histórico, perda da confirmação e reenvio idempotente, backup consistente/chaves, SIGKILL e restauração em destino novo, com os mesmos perfis e UUIDs recuperados. Medidas da CLI nesse banco pequeno: backup **969 ms**, verify **857 ms**, restore **902 ms**; não são RPO/RTO/SLA de produção. O ensaio é nativo, não um teste Docker disfarçado.

## Git final e inventário

**239 caminhos**: 59 modificados sem staging, 178 não rastreados e dois com staging/exclusões preexistentes (`README.md` MD e `render.yaml` AD). Staging SHA-256 `e61564af8fd0d99f734abbd15631be1d1babe8cad59697258374d7163504446b` intacto. Incremento desta etapa: **13 arquivos novos, oito modificados, nenhum removido**. `git diff --check` sem erros. Os 222 caminhos de entrada estão preservados; não foram executados commit, push, deploy, reset ou limpeza destrutiva.

| Estado | Arquivo |
|---|---|
| Criado | [.github/workflows/ci.yml](../.github/workflows/ci.yml) |
| Criado | [deploy/compose.env.example](../deploy/compose.env.example) |
| Criado | [deploy/compose.sqlserver.yml](../deploy/compose.sqlserver.yml) |
| Criado | [docs/ARQUITETURA_MOBILE_API_BANCO.md](../docs/ARQUITETURA_MOBILE_API_BANCO.md) |
| Criado | [docs/DIAGRAMA_IMPLANTACAO.md](../docs/DIAGRAMA_IMPLANTACAO.md) |
| Criado | [docs/ETAPA_8A_INFRAESTRUTURA_DOCKER_NUVEM.md](../docs/ETAPA_8A_INFRAESTRUTURA_DOCKER_NUVEM.md) |
| Criado | [scripts/create-demo-secrets.py](../scripts/create-demo-secrets.py) |
| Criado | [scripts/published-host-smoke.py](../scripts/published-host-smoke.py) |
| Criado | [scripts/validate-infrastructure.py](../scripts/validate-infrastructure.py) |
| Criado | [src/MglConnect.Tests/HostingInfrastructureTests.cs](../src/MglConnect.Tests/HostingInfrastructureTests.cs) |
| Criado | [src/MglConnect.Web/Controllers/ContainerHealthController.cs](../src/MglConnect.Web/Controllers/ContainerHealthController.cs) |
| Criado | [src/MglConnect.Web/Operations/ContainerHealthCommand.cs](../src/MglConnect.Web/Operations/ContainerHealthCommand.cs) |
| Criado | [src/MglConnect.Web/Operations/HostingConfiguration.cs](../src/MglConnect.Web/Operations/HostingConfiguration.cs) |
| Modificado | [.dockerignore](../.dockerignore) |
| Modificado | [.gitignore](../.gitignore) |
| Modificado | [Dockerfile](../Dockerfile) |
| Modificado | [docker-compose.yml](../docker-compose.yml) |
| Modificado | [docs/EXECUCAO_CONTINUA.md](../docs/EXECUCAO_CONTINUA.md) |
| Modificado | [scripts/test-postgres-local.sh](../scripts/test-postgres-local.sh) |
| Modificado | [src/MglConnect.Tests/SecurityHttpFixture.cs](../src/MglConnect.Tests/SecurityHttpFixture.cs) |
| Modificado | [src/MglConnect.Web/Program.cs](../src/MglConnect.Web/Program.cs) |

## Continuidade

O material técnico 8A está pronto para apoiar a revisão acadêmica 8B no escopo local demonstrado. Não declarar homologação operacional dos containers até executar imagem/Compose e repetir persistência, PDF, health, SIGTERM e restauração no ambiente correspondente. Não declarar SQL Server validado, aplicativo Flutter existente, HTTPS público, infraestrutura contratada ou deploy publicado. Retomar o ensaio Docker quando daemon/Compose estiverem disponíveis por procedimento autorizado; manter desenvolvimento nativo independente. Etapa 7B.2 não iniciada.
