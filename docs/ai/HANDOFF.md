# Handoff — M.G.L Connect

**Atualizado em:** 2026-11-04
**Estado atual:** consolidação na pasta oficial `mgl-connect`, branch `integration/mgl-unificado-2026-11`, baseada em `6966b11e2e12ecfd477732649bee78e0dcedc455`. Backup novo criptografado verificado antes da criação da worktree. Publicação condicionada à validação e SHA remoto na entrega; merge em main depende de aprovação final. Revisão independente pendente. Consulte [CONSOLIDACAO](./CONSOLIDACAO.md) para seleção, preservação e limites. O contrato decimal abaixo permanece vigente; relatos anteriores são históricos.

## Consolidação — evidência local

- Base decimal preservada; 22 arquivos de infraestrutura/ferramentas recuperados seletivamente; CI apenas manual. Classificação, backup e limites em [CONSOLIDACAO](./CONSOLIDACAO.md).
- Validação desta nova árvore: restore/build Debug 0 warnings/erros; C# **618 aprovados, 0 falhos, 9 ignorados de 627**; JS **107/0/0**; 12 checks estáticos e sintaxe dos scripts aprovados; Gitleaks da árvore sem ocorrências. Os nove externos não foram executados.
- Seis worktrees anteriores preservadas, inclusive pendências e índice original. Binários acadêmicos retirados só do índice oficial; arquivos continuam locais e no backup. Não é expurgo do histórico público.
- Revisão Copilot e aprovação final do merge são etapas distintas; nenhuma autoavaliação vale por revisão independente. PR deve analisar apenas diff, sem execução externa/fix automático.
- Commits desta consolidação: consultar `git log --oneline 6966b11..HEAD`; SHAs e confirmação remota ficam registrados na entrega/PR. Arquivos exatos em [vs main](./ARQUIVOS_VS_MAIN.txt) e [vs snapshot](./ARQUIVOS_VS_PUBLICADO.txt).

## Missão Codex 02 — contrato decimal P0

### Preservação e commits

- Entrada confirmada: worktree solicitada limpa, branch `review/mgl-http500-p1-codex`, HEAD `a888d56414fb54f3aefec86914fcef34c5622a74`.
- Criada `fix/mgl-decimal-contract-p0` nessa mesma worktree, sem colisão. A referência de revisão e a branch diagnóstica continuam em `a888d56`; a branch de publicação continua em `5fe505a09a76a42843b181cf972d583d9030013b`. Nenhum outro checkout ou repositório Fleetbase foi alterado.
- Código/testes: `6c19dfeb7ecc55f1fffe892896ff32f994ff91f8` (`fix: enforce exact invariant decimal forms and axle loads`). Complemento de binding de string: `c43406b88499941c20b32821dd186633db20d59a` (`fix: preserve malformed axle load input for validation`).
- Documentação: commit separado que contém esta seção, BACKLOG e REVISOES. Os hashes finais são informados na conversa e recuperáveis com `git log --oneline a888d56..HEAD`; nenhum commit é reescrito para inserir seu próprio hash.

### Causas corrigidas e contrato definitivo deste escopo

`ParseLimitsInInvariantCulture=true`, que corrige os limites de `RangeAttribute` e os HTTP 500 anteriores, permanece. O defeito residual era a combinação de heurística de ponto/vírgula, `CurrentCulture` e `NumberStyles.Number`, que aceita agrupamento. A lista por eixo usava outro parsing permissivo. Além disso, `decimal.TryParse` pode retornar sucesso com arredondamento perto de sua precisão máxima; sucesso do parser isoladamente não prova exatidão.

- Gramática completa do texto: `-?[0-9]+(?:\.[0-9]{1,2})?`, dígitos ASCII, cultura invariável. Sem espaços, vírgulas, milhares, formatos mistos, expoentes, moeda, sinal `+` ou sinal final. `1.234` e até `1.230` são rejeitados pela escala lexical. Zeros iniciais são permitidos; não mudam o valor. `-0.00` representa zero, sujeito à regra do campo.
- Sinal negativo é reconhecido pela gramática e recusado pelas faixas positivas/não negativas destes campos. Não há arredondamento ou truncamento na aceitação. O parser compara a representação fixa exata ao texto canônico antes de retornar o valor.
- Um único valor por campo decimal. Parâmetros decimais repetidos geram erro, em vez de escolher silenciosamente o primeiro.
- Frota: campos decimais obrigatórios com `BindRequired`; ausente/vazio é inválido. Mantidas as faixas e a preservação explícita de zeros legados na edição. Criação continua exigindo valor maior que zero.
- Perfil: medidas anuláveis; ausente/vazio permanece desconhecido. Payload admite zero; demais medidas informadas exigem mínimo 0,01. Mantidas coerência de massas, carga por eixo e vínculo à viagem.
- Cargas por eixo: campo ausente/vazio representa lista desconhecida/vazia; cada item segue a mesma gramática, separado somente por `;`. Não eliminar itens vazios nem aparar espaços. A conversão MVC de string em branco para null foi desabilitada nesse campo para permitir rejeitar `" "`. Valores informados continuam sujeitos à validação de domínio (positividade, quantidade e soma).
- Erros permanecem em ModelState; não são substituídos por números válidos. As views usam ponto na saída e preservam o valor tentado no HTML de resposta. Um navegador pode sanitizar um `value` inválido de `type=number`; não se promete exibição visual de uma vírgula inválida dentro do controle nativo.
- Não há normalização heurística de vírgula no servidor. O contrato HTTP passa a rejeitar explicitamente `18,60`, antes aceito pelo binder. A apresentação regional do controle nativo não altera o contrato de transporte.

| Campos | Unidade / escala | Limites preservados |
|---|---|---|
| CapacidadeCargaKg | kg / 2 casas | 0–99.999.999,99; criação > 0 e coerência com massa cadastrada |
| AlturaMetros, LarguraMetros, ComprimentoMetros | m / 2 casas | 0–999,99; criação > 0 |
| PesoToneladas | t / 2 casas | 0–9.999,99; criação > 0; não é PBT/PBTC comprovado |
| Height, Width, Length | m / 2 casas | nullable; 0,01–1.000.000 quando informado |
| Tare, Pbt, Pbtc | kg / 2 casas | nullable; 0,01–1.000.000 quando informado |
| Payload | kg / 2 casas | nullable; 0–1.000.000 |
| AxleLoadsKg | kg / 2 casas por item | positivo quando informado; regras de quantidade/soma mantidas |
| QtdEixos / Axles | eixos / inteiro | 1–20 / nullable 1–30; não adotar escala decimal |

Os limites são técnicos existentes, não aprovação física/legal de caminhões. A verificação de escala foi acrescentada ao domínio do perfil sem reescrever históricos. Um perfil legado com mais de duas casas pode exigir reconciliação explícita antes de voltar a ser considerado válido; nenhuma base operacional foi lida ou saneada.

### Arquivos de código e testes

- [DecimalFormContract](../../src/MglConnect.Web/ModelBinding/DecimalFormContract.cs) e [HtmlDecimalModelBinder](../../src/MglConnect.Web/ModelBinding/HtmlDecimalModelBinder.cs): gramática, exatidão e erros de binding.
- [Frota](../../src/MglConnect.Web/ViewModels/FleetManagementViewModels.cs), [perfil de viagem](../../src/MglConnect.Web/ViewModels/RouteAssessmentViewModel.cs) e [domínio](../../src/MglConnect.Web/Services/RouteTrust/RouteContracts.cs): presença, escala e lista por eixo.
- [Formulário de veículo](../../src/MglConnect.Web/Views/Frota/VeiculoForm.cshtml) e [avaliação de rota](../../src/MglConnect.Web/Views/RouteAssessment/Index.cshtml): saída invariável e recuperação do valor tentado.
- [DecimalFormContractTests](../../src/MglConnect.Tests/DecimalFormContractTests.cs), [DecimalContractHttpTests](../../src/MglConnect.Tests/DecimalContractHttpTests.cs) e [DecimalRangeCultureTests](../../src/MglConnect.Tests/DecimalRangeCultureTests.cs): regressões. O caso antigo que aceitava vírgula foi substituído por rejeição explícita nas três culturas; não foi mantida uma expectativa incompatível com o contrato novo.
- Nenhum controller, autorização, filtro tenant, esquema, pacote ou configuração de produção foi alterado.

### Matriz de regressão e evidências locais

| Grupo | Evidência |
|---|---|
| Formato/cultura | `pt-BR`, `en-US`, invariante; ponto, vírgula, milhares, mistos, espaços, Unicode, sinais, expoentes, moeda e texto malformado |
| Precisão/limites | 0, mínimo/máximo de cada Range decimal, negativos, excesso de escala, overflow e arredondamento próximo de Decimal.MaxValue |
| Binding | decimal/decimal?, ausente, vazio, valores repetidos, erros e valor tentado de ModelState; obrigatoriedade por HTTP |
| Eixos | lista válida e exata, tokens vazios/espaços/mistos inválidos, valores não positivos, soma e quantidade; quantidade fracionária rejeitada |
| HTTP/persistência | SQLite sintético + Kestrel loopback, culturas de processo `pt_BR.UTF-8`, `en_US.UTF-8`, `C`; saída invariável, revisão JSON exata, nullable/zero, ausência de inserções/revisões/auditoria para entradas inválidas |
| Isolamento | frota GET/POST válido de outro tenant mantém 404; rota GET/POST inclusive malformado de outro tenant mantém 404. Frota POST malformado permanece 400 antes da consulta do recurso, conforme fluxo preexistente; não foi alterada essa ordem |

Ambiente: Kali Linux x64, SDK isolado .NET 10.0.403/runtime 10.0.14, Node v24.21.0, Gitleaks pacote 8.26.0-1+b1. Variáveis `MGLCONNECT_SQLSERVER_TEST_CONNECTION` e `MGLCONNECT_POSTGRES_TEST_CONNECTION` removidas somente dos processos de validação. Fixtures criam arquivos SQLite/chaves sintéticas e processos próprios em loopback; evidências preservadas fora do repositório em `$HOME/.local/share/mglconnect/tests/decimal-p0.oPRAj3`. Nenhum Docker, API externa, banco operacional, migration de produção ou serviço PostgreSQL/SQL Server foi executado.

| Validação | Resultado observado |
|---|---|
| Restore | exit 0; projetos atualizados |
| Build Debug final | exit 0; 0 warnings, 0 errors |
| Direcionados decimal/frota/perfil/rota | 191 aprovados, 0 falhos, 0 ignorados; `focused-final.trx` |
| Complemento HTTP após preservação de string em branco | 6 aprovados, 0 falhos, 0 ignorados; `http-final.trx` |
| C# completa | 627 casos: 618 aprovados, 0 falhos, 9 ignorados; cinco PostgreSQL e quatro SQL Server sem conexão autorizada |
| Novos testes dentro da completa | 141 casos de contrato + 6 HTTP; não somar novamente ao total |
| JavaScript | 107 aprovados, 0 falhos, 0 ignorados; `javascript.log` |
| Diff | `git diff --check` e `git diff --cached --check` aprovados antes dos commits |
| Segredos | Gitleaks da árvore e diff staged de código sem ocorrências; varreduras finais acompanham a entrega |

Histórico desta execução: o primeiro build falhou com CS0266 no tipo de retorno de um helper novo de teste (`DefaultModelBindingContext`/`ModelBindingContext`); corrigido sem alteração de expectativa funcional. Builds posteriores passaram. Primeiro lote dirigido 188/0/0; ampliação para zero assinado/presença obrigatória 191/0/0. A asserção final da localidade do servidor está coberta pela suíte completa. Uma suíte completa anterior ao complemento de string também passou com 618/0/9. Não ocultar os nove ignorados nem usar esses resultados como homologação dos provedores externos.

Comandos executados a partir da raiz (nos comandos .NET abaixo, `dotnet` foi o executável do SDK isolado; TMPDIR foi a raiz de evidências acima):

```sh
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet restore MglConnect.sln --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet build MglConnect.sln --no-restore -c Debug --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet test MglConnect.sln --no-build --no-restore -c Debug --filter 'FullyQualifiedName~Decimal|FullyQualifiedName~FleetManagementHttpTests|FullyQualifiedName~TripProfileIsolationTests|FullyQualifiedName~RouteAssessmentIntegrationTests' --logger 'trx;LogFileName=focused-final.trx' --results-directory "$TMPDIR/results" --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet test MglConnect.sln --no-build --no-restore -c Debug --filter 'FullyQualifiedName~DecimalContractHttpTests' --logger 'trx;LogFileName=http-final.trx' --results-directory "$TMPDIR/results" --verbosity minimal
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet test MglConnect.sln --no-build --no-restore -c Debug --logger 'trx;LogFileName=full-final.trx' --results-directory "$TMPDIR/results" --verbosity minimal
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
git diff --check
git diff --cached --check
gitleaks detect --source . --no-git --redact --no-banner
git diff --cached | gitleaks detect --pipe --redact --no-banner
```

### Revisão independente pelo Copilot e riscos restantes

Revisor previsto: Copilot; revisão ainda não executada. Examinar os commits de código desde `a888d56`, em worktree/branch próprios ou somente leitura sem edição concorrente. Arquivos previstos: os dez caminhos de código/testes acima e estes três documentos. Aceite: formato invariável único, rejeição sem escrita, precisão perto do limite decimal, limites/escala/nulabilidade e 404 preservados, sem autorização enfraquecida. Conferir TRX/comandos, não confiar apenas neste relato; testes com efeitos colaterais exigem escopo isolado explicitamente autorizado.

Auditar especialmente: round-trip que detecta arredondamento; parâmetros repetidos; `BindRequired` e zero legado; `DisplayFormat` no texto de cargas por eixo; comportamento de ModelState e HTML em pt-BR; coerência das escalas de domínio; diferenças 400/404 preexistentes. Não promover o parser a todos os formulários/APIs sem inventário e requisito específico. Não copiar código Fleetbase.

Pendentes: validação SQL Server/PostgreSQL atuais, navegador real/teclado regional, reconciliação de dados legados eventualmente gravados pelo parser antigo e revisão independente. Não houve saneamento, migração, homologação de rota ou publicação. Próximo P0: revisão cruzada; depois P1, infraestrutura descartável e regressões por provedor com autorização própria. Push/merge continuam não autorizados.

## Histórico até 2026-11-03

## Branch e commits — histórico

- Branch: `publication/mgl-snapshot-2026-11-public`.
- Base: `origin/main` no commit `d8a54d3`.
- Branch local de diagnóstico: `diagnostic/mgl-http500-p0`, criada sobre `5fe505a09a76a42843b181cf972d583d9030013b`; não publicada.
- A branch foi reconstruída sobre a base pública a partir dos patches revisados, sem alterar branches anteriores. Contatos demonstrativos e caminhos absolutos herdados foram corrigidos antes do primeiro commit novo.
- Commits já publicados na branch:
  - `1247259` — aplicação, contatos demonstrativos e sanitização de caminhos herdados.
  - `78e296c` — testes de regressão.
  - `043dcff` — script de esquema SQL Server.
  - `d6dcce0` — documentação técnica revisada e sanitizada.
  - `a7783ba` — coordenação de agentes e metodologia de conformidade.
- `32d7282` — registro final da auditoria.
- `4b5800f` — alinhamento do handoff à auditoria.
- `5fe505a` — conclusão da auditoria do snapshot sanitizado.
- Correção local nesta branch: `bbcb189643a134c1a56811a6748077e79af6e3d8` — parsing decimal compatível com cultura e regressões.
- O snapshot remoto continua em `5fe505a09a76a42843b181cf972d583d9030013b`; o commit de correção e a documentação desta branch não foram publicados.

## Escopo incluído e excluído

Incluídos: aplicação ASP.NET Core MVC, testes C#/JavaScript, script SQL de estrutura, 17 documentos técnicos revisados, instruções Copilot/Codex e plano metodológico de conformidade.

Excluídos: documentação de continuidade acadêmica com identificadores de estudante/orientador; todo `docs/academico/` e seus PDF/DOCX/ZIP; arquivos de infraestrutura ainda não rastreados ou modificados no workspace de origem; scripts auxiliares não rastreados; histórico SQL anterior; README e render; bancos, binários, artefatos de build, credenciais e backups. Nenhum conteúdo de Fleetbase ou `ctresb/leis` foi copiado.

## Sanitização e revisão

- Os caminhos absolutos de ambiente local presentes nos documentos técnicos selecionados foram substituídos por referências genéricas ou omitidos. Nenhum caminho pessoal absoluto permaneceu nos documentos candidatos.
- As duas ocorrências de contato no domínio empresarial anterior eram valores em campos de contato (Swagger e política de privacidade), não URLs ou identificadores técnicos. Foram substituídas por `operacoes@example.invalid` e `privacidade@example.invalid` antes do primeiro commit novo. Não foram encontradas outras referências no conteúdo candidato.
- Dois relatórios herdados da base pública continham caminhos absolutos locais; seus links e comandos foram convertidos para referências relativas nesta branch. O histórico público anterior não foi reescrito.
- O arquivo `docs/EXECUCAO_CONTINUA.md` foi excluído por misturar resultados de produção documental acadêmica e referências pessoais.
- Dados de contato/identificação do seed demonstrativo foram substituídos por valores sintéticos; as contas de demonstração e seus identificadores funcionais foram preservados.
- Os alertas numéricos de CPF/telefone foram contextualizados: o seed usa somente valores zerados, testes usam fixtures sintéticas e números do JSON OSM são identificadores de nós/ways. Não foi identificado dado pessoal real nesses casos.
- As senhas demonstrativas padrão permanecem exclusivamente para Development; sua exibição na tela de login também está condicionada a Development. Não são segredos de produção e não devem ser reutilizadas em outros ambientes.
- Swagger e política de privacidade usam endereços `.invalid`, reservados para demonstrações e não destinados a entrega real de mensagens.
- `appsettings*.json` não foram modificados em relação à base pública; a connection string é SQLite relativa e não contém usuário/senha.
- O dado de cobertura derivado de OSM contém origem e atribuição ODbL. Não foi encontrado arquivo LICENSE de nível raiz; revisar as licenças de terceiros antes de redistribuição, sem confundir essa pendência com segredo ou afirmar licença própria.
- Gitleaks não encontrou segredos na árvore e no histórico dos commits da branch. O diff contém 222 arquivos (163 adicionados, 59 modificados), sem PDFs/DOCX/ZIP, bancos, backups, chaves ou binários privados.

## Validação histórica do snapshot

- SDK isolado usado nesta branch: .NET 10.0.403/runtime 10.0.14; Node v24.21.0.
- Restore executado nesta branch: aprovado nos dois projetos.
- Build Debug executado nesta branch: aprovado, 0 warnings e 0 errors.
- `TenantIsolationTests` executado nesta branch: 5 aprovados, 0 falhos, 0 ignorados.
- `signature.test.cjs` executado nesta branch: 1 aprovado, 0 falhos, 0 ignorados.
- Não executados: restante da suíte, bancos operacionais, migrations, containers, deploy ou serviços externos. Esses resultados pertencem ao worktree desta branch, não à branch de estabilização.

## Próximos passos — histórico

1. Solicitar revisão técnica independente do Codex em nova worktree baseada no commit local `bbcb189`.
2. Corrigir achados apenas após decisão explícita; não alterar a branch publicada nem fazer merge em `main`.
3. Após revisão, decidir separadamente sobre publicação da branch de correção; nenhum push está autorizado por este handoff.
4. Executar posteriormente as suítes PostgreSQL/SQL Server apenas em bancos descartáveis e autorizados.
5. Analisar licenças de terceiros e decidir individualmente sobre infraestrutura e scripts excluídos.

## Diagnóstico P0 — branch local

- Worktree/branch: worktree independente `diagnostic/mgl-http500-p0`, baseada no commit publicado `5fe505a09a76a42843b181cf972d583d9030013b`. A correção tem o commit local `bbcb189643a134c1a56811a6748077e79af6e3d8`; documentação de handoff será registrada em commit separado.
- Causa dos 13 HTTP 500: `RangeAttribute.SetupConversion` convertia limites decimais escritos com ponto usando a cultura corrente `pt-BR`, gerando `ArgumentException` interna com `FormatException` antes da action, tanto na validação MVC quanto na geração dos validadores Razor.
- Correção: limites `RangeAttribute` decimal de frota e perfil de viagem usam `ParseLimitsInInvariantCulture=true`. Os formulários HTML afetados usam `HtmlDecimalModelBinder`, que interpreta o ponto de `type=number` como separador decimal invariável e mantém entradas com vírgula na cultura corrente. A autorização e o escopo por tenant não foram modificados.
- Regressões: teste de cultura `pt-BR` cobre conversão dos limites e valores `18.60`/`18,60`; os testes HTTP verificam o valor decimal persistido de frota e perfil.
- Validação no worktree de diagnóstico: restore aprovado; build Debug com 0 warnings/erros; C# 481 total (472 aprovados, 0 falhos, 9 ignorados); JavaScript 107/107 aprovados. Os nove ignorados são PostgreSQL/SQL Server sem conexões configuradas. Gitleaks não encontrou ocorrências na árvore nem nos oito commits do snapshot desde `origin/main`.
- A revisão independente pelo Codex é pendente. A correção foi commitada localmente em `bbcb189`; nenhuma publicação, integração ou alteração da `main` ocorreu.

## Solicitação histórica para revisão independente pelo Codex

> Faça uma revisão independente, somente de leitura, da correção P0 no commit `bbcb189643a134c1a56811a6748077e79af6e3d8`. Crie uma branch e worktree próprias a partir desse commit (por exemplo, `review/mgl-http500-p0-codex` e `$HOME/projetos/mgl-codex-review-p0`), após confirmar que ambas não existem. Não use nem altere o checkout do Copilot, a branch `diagnostic/mgl-http500-p0`, a branch publicada `publication/mgl-snapshot-2026-11-public` ou `main`; não faça commit, push, merge, rebase ou alterações de código.
>
> Avalie com evidências concretas: (1) a causa original dos 13 HTTP 500 em `RangeAttribute`/`SetupConversion`; (2) `ParseLimitsInInvariantCulture` e os limites decimais em `FleetManagementViewModels.cs` e `RouteAssessmentViewModel.cs`; (3) `HtmlDecimalModelBinder`, incluindo `decimal` e `decimal?`, `ModelState`, Razor/MVC, entradas com ponto/vírgula, negativos, limites, vazio e formatos inválidos; (4) ambiguidade e risco de separadores de milhar serem aceitos como decimal, inclusive valores como `1.234`, `1,234`, `1,234.56` e formatos malformados; (5) precisão e integridade dos valores persistidos; (6) regressões nos fluxos de frota, perfis de viagem e avaliação de rotas; (7) eventual impacto em autorização de administradores/motoristas, isolamento entre tenants, respostas 404 e validação de entrada.
>
> Confira os testes de regressão e, se necessário, execute somente testes locais com fixtures isoladas, sem bancos operacionais, PostgreSQL, SQL Server, Docker, serviços externos ou browser tests. Não enfraqueça assertions. Entregue achados ordenados por severidade, cada um com arquivo e linha exatos, severidade, evidência reproduzível/impacto e sugestão mínima de correção. Se não houver achados, declare o escopo efetivamente revisado, os comandos/testes executados e limitações; não use aprovação genérica sem evidências.
