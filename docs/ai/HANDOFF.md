# Handoff — M.G.L Connect

**Atualizado em:** 2026-11-03
**Estado:** o snapshot de desenvolvimento está publicado em sua branch remota. A correção P0 está isolada na branch local `diagnostic/mgl-http500-p0` e possui commit local `bbcb189`; aguarda revisão independente do Codex. Não houve push ou merge. Não é homologado nem pronto para produção.

## Branch e commits

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

## Validação desta árvore

- SDK isolado usado nesta branch: .NET 10.0.403/runtime 10.0.14; Node v24.21.0.
- Restore executado nesta branch: aprovado nos dois projetos.
- Build Debug executado nesta branch: aprovado, 0 warnings e 0 errors.
- `TenantIsolationTests` executado nesta branch: 5 aprovados, 0 falhos, 0 ignorados.
- `signature.test.cjs` executado nesta branch: 1 aprovado, 0 falhos, 0 ignorados.
- Não executados: restante da suíte, bancos operacionais, migrations, containers, deploy ou serviços externos. Esses resultados pertencem ao worktree desta branch, não à branch de estabilização.

## Próximos passos

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

## Solicitação pronta para revisão independente pelo Codex

> Faça uma revisão independente, somente de leitura, da correção P0 no commit `bbcb189643a134c1a56811a6748077e79af6e3d8`. Crie uma branch e worktree próprias a partir desse commit (por exemplo, `review/mgl-http500-p0-codex` e `$HOME/projetos/mgl-codex-review-p0`), após confirmar que ambas não existem. Não use nem altere o checkout do Copilot, a branch `diagnostic/mgl-http500-p0`, a branch publicada `publication/mgl-snapshot-2026-11-public` ou `main`; não faça commit, push, merge, rebase ou alterações de código.
>
> Avalie com evidências concretas: (1) a causa original dos 13 HTTP 500 em `RangeAttribute`/`SetupConversion`; (2) `ParseLimitsInInvariantCulture` e os limites decimais em `FleetManagementViewModels.cs` e `RouteAssessmentViewModel.cs`; (3) `HtmlDecimalModelBinder`, incluindo `decimal` e `decimal?`, `ModelState`, Razor/MVC, entradas com ponto/vírgula, negativos, limites, vazio e formatos inválidos; (4) ambiguidade e risco de separadores de milhar serem aceitos como decimal, inclusive valores como `1.234`, `1,234`, `1,234.56` e formatos malformados; (5) precisão e integridade dos valores persistidos; (6) regressões nos fluxos de frota, perfis de viagem e avaliação de rotas; (7) eventual impacto em autorização de administradores/motoristas, isolamento entre tenants, respostas 404 e validação de entrada.
>
> Confira os testes de regressão e, se necessário, execute somente testes locais com fixtures isoladas, sem bancos operacionais, PostgreSQL, SQL Server, Docker, serviços externos ou browser tests. Não enfraqueça assertions. Entregue achados ordenados por severidade, cada um com arquivo e linha exatos, severidade, evidência reproduzível/impacto e sugestão mínima de correção. Se não houver achados, declare o escopo efetivamente revisado, os comandos/testes executados e limitações; não use aprovação genérica sem evidências.
