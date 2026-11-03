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
| B9 | P0 | Revisar correção dos HTTP 500 e cultura decimal | Alto | Alto | Médio | Correção local `bbcb189` pronta; revisão independente do Codex em branch/worktree próprios. Confirmar os 13 casos, precisão decimal e respostas 404/400 sem relaxar autorização. |

## Situação observada em 2026-11-03

- B1 concluído: SDK .NET 10.0.403/runtime 10.0.14 isolado e validado.
- B2 concluído: na branch de diagnóstico, restore/build aprovados; C# 472 aprovados, 0 falhos e 9 ignorados em 481 casos; JavaScript 107/107 aprovados. PostgreSQL/SQL Server ficaram sem configuração.
- B3 pendente: README/render permaneceram na base pública e não foram alterados nesta branch. Resolver separadamente no workspace de origem.
- B4 pendente: scripts e arquivos de infraestrutura do workspace original não foram copiados. SQL Server real não foi usado.
- B5 parcial: conteúdo acadêmico com metadados pessoais foi deliberadamente excluído; os demais scripts, deploys e arquivos não rastreados aguardam decisão individual.
- B6 concluído para o snapshot publicado. A nova correção permanece apenas na branch diagnóstica local, sem push autorizado.
- B7: esta reconstrução usa worktree separado. Revisões cruzadas futuras também devem usar worktrees independentes.
- B8: plano metodológico criado; confirmação jurídica de aplicabilidade e revisão especializada continuam pendentes.
- B9: correção local preparada e bateria segura aprovada; revisão cruzada ainda pendente.

## Sequência proposta

1. (B9, P0) Fazer revisão independente do commit `bbcb189` em worktree própria.
2. Avaliar e resolver eventuais achados antes de decidir sobre publicação; nunca publicar na `main` automaticamente.
3. Autorizar e preparar bancos descartáveis para testes PostgreSQL/SQL Server.
4. Definir ambiente/limiares para testes de browser/performance e analisar licenças de terceiros.
5. Resolver infraestrutura, deploy, scripts, README/render e integração à `main` em tarefas separadas e autorizadas.
