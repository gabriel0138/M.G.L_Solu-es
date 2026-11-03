# Registro de revisões cruzadas Copilot ↔ Codex

## Protocolo

Cada registro deve identificar commit-base, branch/worktree, agente autor, agente revisor, escopo/arquivos, comando ou evidência consultada, achados concretos, impacto, recomendação e resolução. Não incluir segredos nem dados pessoais. Uma aprovação sem evidências não conta como revisão.

Copilot e Codex podem revisar o trabalho um do outro. Nenhum dos dois modifica simultaneamente o mesmo checkout; tarefas concorrentes usam worktrees/branches separados. Revisores não fazem merge ou push.

## Registros

### 2026-11-03 — preparação do protocolo multiagente

- Tipo: criação documental; não é revisão de código por dois agentes.
- Commit-base: `931ca7a`.
- Evidência local: inspeção do estado Git e ausência prévia de arquivos compartilhados de instruções.
- Resultado: regras de papéis e revisão cruzada propostas em `AGENTS.md`, `.github/copilot-instructions.md` e `docs/ai/HANDOFF.md`.
- Revisão por Codex: ainda não realizada.

### 2026-11-03 — preparação metodológica de conformidade

- Tipo: pesquisa preliminar de fontes; não é parecer jurídico.
- Fontes consultadas: textos LGPD, CTB e Lei 11.442 no commit `c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6` do repositório externo `ctresb/leis`, além de índices oficiais ANTT/CONTRAN.
- Limitação: as páginas oficiais do Planalto/ANPD e alguns atos regulatórios não puderam ser recuperados nesta sessão; a Lei 11.442 no texto consultado já referencia alteração de 2026. A conferência em fonte primária e da vigência permanece pendente.
- Resultado: metodologia e referências candidatas registradas em `docs/compliance/PLANO_AUDITORIA_LEGAL.md`, sem afirmar conformidade.
- Revisão jurídica/Codex: ainda não realizada.

### 2026-11-03 — preparação do snapshot de publicação

- Tipo: revisão de escopo, privacidade e validação; não é revisão independente por Codex.
- Branch/worktree: `publication/mgl-snapshot-2026-11`, criada sobre `origin/main` em `d8a54d3`.
- Escopo: aplicação, testes, SQL de estrutura e documentos selecionados, sem cherry-pick da branch de estabilização.
- Evidências: Gitleaks no conteúdo candidato; inspeção de caminhos absolutos e marcadores de dados pessoais; restore/build e testes selecionados.
- Achados e resolução: documentos técnicos tiveram caminhos locais removidos; dados identificadores de seed demonstrativo foram substituídos por valores sintéticos; documento de continuidade acadêmica excluído. A varredura nos blobs dos novos commits não encontrou caminhos locais; dois relatórios com caminhos locais continuam inalterados na base pública.
- Auditoria anterior: Gitleaks não encontrou segredos no histórico candidato nem na árvore. Indicadores numéricos restantes foram classificados como fixtures sintéticas, placeholders zerados e IDs de OSM. Dois relatórios herdados da base pública continham caminhos absolutos e foram sanitizados no snapshot, sem reescrever histórico remoto.
- Resultado da validação: restore/build aprovados; 5 testes .NET de isolamento e 1 teste JavaScript de assinatura aprovados. Não equivale a homologação ou prontidão de produção.
- Limitação: revisão técnica cruzada por Codex e suíte ampliada ainda não realizadas; nenhuma publicação autorizada.

### 2026-11-03 — sanitização dos contatos para publicação

- Tipo: sanitização de dados de contato e reconstrução de commits; não é revisão independente por Codex.
- Branch/worktree: `publication/mgl-snapshot-2026-11-public`, criada sobre `origin/main` em `d8a54d3`.
- Classificação: as ocorrências do domínio empresarial anterior eram endereços nos campos de contato do Swagger e da política de privacidade; não eram URLs de serviço nem identificadores técnicos.
- Alteração: substituídos por `operacoes@example.invalid` e `privacidade@example.invalid`, respectivamente, antes do primeiro commit novo da branch. Não foram encontradas outras ocorrências no conteúdo candidato.
- Sanitização adicional: caminhos absolutos nos relatórios históricos da raiz foram convertidos em caminhos de repositório relativos e comandos relativos à raiz.
- Preservação: os seis commits e a branch previamente preparada permanecem intactos; os commits desta branch foram reconstruídos e revisados.
- Validação desta branch: restore e build aprovados (0 warnings/erros); `TenantIsolationTests` 5/5 e `signature.test.cjs` 1/1. A suíte restante não foi executada.
- Revisão técnica cruzada por Codex: pendente.

### 2026-11-03 — diagnóstico e correção P0 dos HTTP 500

- Tipo: diagnóstico por reprodução e correção local; não é homologação nem revisão independente.
- Worktree/branch: `diagnostic/mgl-http500-p0`, baseada no commit publicado `5fe505a09a76a42843b181cf972d583d9030013b`; correção commitada localmente em `bbcb189643a134c1a56811a6748077e79af6e3d8`, sem push ou merge.
- Reprodução inicial: `FleetManagementHttpTests` 8 falhas, `TripProfileIsolationTests` 2 e `RouteAssessmentIntegrationTests` 3. As 13 respostas 500 foram reproduzidas com fixtures SQLite temporárias e Kestrel em loopback; nenhuma conexão PostgreSQL/SQL Server ou integração externa foi configurada.
- Evidência segura: a captura opt-in da fixture registrou somente categorias, tipos e frames de stack sanitizados. Todos os 13 traces incluem `System.ArgumentException`/`System.FormatException` e `System.ComponentModel.DataAnnotations.RangeAttribute.SetupConversion`; a pilha também mostra `RangeAttributeAdapter` durante renderização/validação MVC. Nenhuma mensagem, parâmetro, corpo, token, cookie ou connection string foi registrada. A instrumentação foi removida após diagnóstico.
- Causa confirmada dos 500: limites decimal `RangeAttribute` como `"0.01"` eram convertidos pela cultura corrente `pt-BR`, levando a `FormatException` antes da action ou durante a geração dos validadores Razor. A correção habilita `ParseLimitsInInvariantCulture` nas cinco propriedades de frota e sete propriedades de perfil de viagem afetadas.
- Causa adicional descoberta ao reexecutar a frota: depois de eliminar os 500, oito testes HTTP recebiam `400` porque a cultura `pt-BR` interpretava o ponto dos campos HTML `type=number` como agrupamento; `18.60` não permanecia 18,60. Adicionado `HtmlDecimalModelBinder`, limitado a esses dois ViewModels: ponto usa cultura invariável, vírgula usa cultura corrente. Não houve alteração de autenticação, autorização, tenant, status esperados ou consultas.
- Regressões: `DecimalRangeCultureTests` verifica limites em `pt-BR` e entrada `18.60`/`18,60`; `VehicleLifecycleNormalizesPlate...` preserva o valor decimal persistido; `HttpProfileIsExplicitScopedVersioned...` verifica altura 4,2 exata. Nenhuma assertion de segurança foi enfraquecida.
- Testes direcionados após correção: cultura 3/3; `FleetManagementHttpTests` 20/20; `TripProfileIsolationTests` 8/8; `RouteAssessmentIntegrationTests` 13/13.
- Validação completa: `dotnet restore MglConnect.sln` aprovado; build Debug 0 warnings/erros; C# 481 casos (472 aprovados, 0 falhos, 9 ignorados); JavaScript 107 aprovados, 0 falhos/ignorados. Total combinado: 588 casos, 579 executados e aprovados, 9 ignorados. Os 9 ignorados são cinco PostgreSQL e quatro SQL Server sem conexões de teste.
- Segurança/diff: Gitleaks na árvore e nos oito commits do snapshot desde `origin/main` não encontrou segredos; após a correção, novo scan da árvore e do commit staged também não encontrou segredos; `git diff --check` passou. Não há instrumentação diagnóstica nas alterações commitadas. A revisão independente pelo Codex permanece pendente.
- Handoff: o commit `bbcb189` contém somente correção e testes. HANDOFF, BACKLOG e este registro são alterados em commit documental separado. O prompt para revisão em worktree independente está em `docs/ai/HANDOFF.md`.
