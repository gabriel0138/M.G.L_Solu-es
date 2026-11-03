# Backlog de estabilização e colaboração

Estimativas relativas, a refinar após validação. Nenhum item abaixo autoriza sozinho instalação, teste com efeitos colaterais, alteração de código, commit, merge ou push.

| ID | Prioridade | Tarefa | Impacto | Risco | Esforço | Dependências e critério de aceite |
|---|---|---|---|---|---|---|
| B1 | P0 | Instalar/verificar SDK .NET 10 isolado | Alto | Baixo | Baixo | Autorização específica; pacote oficial e SHA-512 verificado; `dotnet --info` mostra SDK 10 sem alterar instalação global. |
| B2 | P0 | Restore, build e testes locais seguros | Alto | Médio | Médio | B1 e autorização separada; registrar comandos, ambiente, resultados e itens ignorados; nenhuma conexão operacional. |
| B3 | P1 | Resolver estados staged/unstaged de README e render | Alto | Alto | Médio | Comparar versões do HEAD, índice e worktree; preservar todo o conteúdo; obter decisão explícita antes de staging/commit. |
| B4 | P1 | Diagnosticar infraestrutura e connection string SQL Server | Alto | Médio | Baixo | Revisão isolada do gerador; teste sem serviço real ou credencial; revisão do diff e de secret scanning. |
| B5 | P1 | Classificar arquivos não rastreados e material acadêmico | Médio | Médio | Médio | Identificar finalidade, metadados e dados pessoais; documentar decisão de incluir/excluir antes da publicação. |
| B6 | P1 | Preparar prontidão de PR/push | Alto | Alto | Médio | B2–B5; diff/commits revisados, segredo e privacidade rechecados, remoto sincronizado; autorização explícita antes de push/merge. |
| B7 | P2 | Trabalho Copilot/Codex em worktrees separados | Médio | Baixo | Baixo | Definir responsável, worktree/branch, arquivos, aceite, testes e revisor por tarefa; impedir checkout simultâneo. |
| B8 | P2 | Preencher matriz de auditoria jurídica | Alto | Alto | Médio/alto | Fontes oficiais atuais, análise de aplicabilidade por operação real e parecer jurídico quando necessário; nenhuma alegação de conformidade sem evidências. |

## Situação observada em 2026-11-03

- B1 concluído: SDK .NET 10.0.403/runtime 10.0.14 isolado e validado.
- B2 inicial concluído nesta branch de publicação: restore e build aprovados; `TenantIsolationTests` 5/5 e `signature.test.cjs` 1/1. A suíte restante não foi executada.
- B3 pendente: README/render permaneceram na base pública e não foram alterados nesta branch. Resolver separadamente no workspace de origem.
- B4 pendente: scripts e arquivos de infraestrutura do workspace original não foram copiados. SQL Server real não foi usado.
- B5 parcial: conteúdo acadêmico com metadados pessoais foi deliberadamente excluído; os demais scripts, deploys e arquivos não rastreados aguardam decisão individual.
- B6 em verificação final: branch nova baseada em `origin/main`, contatos de exibição substituídos por `.invalid` antes do primeiro commit novo, relatórios com caminhos absolutos sanitizados no snapshot, push autorizado após auditoria final.
- B7: esta reconstrução usa worktree separado. Revisões cruzadas futuras também devem usar worktrees independentes.
- B8: plano metodológico criado; confirmação jurídica de aplicabilidade e revisão especializada continuam pendentes.

## Sequência proposta

1. Completar auditoria final e publicar somente a branch de snapshot autorizada.
2. Solicitar autorização separada para testes unitários seguros restantes, excluindo PostgreSQL, SQL Server, Docker, browsers e integrações externas.
3. Solicitar revisão técnica cruzada em worktree independente.
4. Decidir sobre infraestrutura, deploy, scripts e licença/atribuição de terceiros.
5. Resolver README/render e integração à `main` somente em etapa posterior autorizada.
