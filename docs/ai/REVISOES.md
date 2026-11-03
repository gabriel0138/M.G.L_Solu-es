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
