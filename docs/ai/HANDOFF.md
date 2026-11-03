# Handoff — M.G.L Connect

**Atualizado em:** 2026-11-03
**Estado:** snapshot de desenvolvimento reconstruído e validado por build/testes iniciais; publicação autorizada, sujeita à auditoria final. Não é homologado nem pronto para produção.

## Branch e commits

- Branch: `publication/mgl-snapshot-2026-11-public`.
- Base: `origin/main` no commit `d8a54d3`.
- A branch foi reconstruída sobre a base pública a partir dos patches revisados, sem alterar branches anteriores. Contatos demonstrativos e caminhos absolutos herdados foram corrigidos antes do primeiro commit novo.
- Commits locais preparados:
  - `1247259` — aplicação, contatos demonstrativos e sanitização de caminhos herdados.
  - `78e296c` — testes de regressão.
  - `043dcff` — script de esquema SQL Server.
  - `d6dcce0` — documentação técnica revisada e sanitizada.
  - O commit atual adiciona coordenação de agentes, conformidade e registro final da publicação.
- Ainda não publicado; o push da branch foi autorizado após aprovação das verificações finais.

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
- Gitleaks não encontrou segredos nas verificações da árvore e do histórico dos cinco commits da branch. O diff contém 222 arquivos (163 adicionados, 59 modificados), sem PDFs/DOCX/ZIP, bancos, backups, chaves ou binários privados.

## Validação desta árvore

- SDK isolado usado nesta branch: .NET 10.0.403/runtime 10.0.14; Node v24.21.0.
- Restore executado nesta branch: aprovado nos dois projetos.
- Build Debug executado nesta branch: aprovado, 0 warnings e 0 errors.
- `TenantIsolationTests` executado nesta branch: 5 aprovados, 0 falhos, 0 ignorados.
- `signature.test.cjs` executado nesta branch: 1 aprovado, 0 falhos, 0 ignorados.
- Não executados: restante da suíte, bancos operacionais, migrations, containers, deploy ou serviços externos. Esses resultados pertencem ao worktree desta branch, não à branch de estabilização.

## Próximos passos

1. Repetir e registrar Gitleaks e verificação de caminhos/artefatos sobre o commit documental final.
2. Executar somente o push autorizado da branch de publicação; não alterar nem fazer merge em `main`.
3. Ampliar depois os testes unitários seguros, limitando-os a fixtures locais/InMemory/SQLite e adaptadores simulados.
4. Fazer revisão técnica cruzada em worktree independente e analisar licenças de terceiros.
5. Decidir individualmente sobre arquivos de infraestrutura e scripts excluídos.
