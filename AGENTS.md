# M.G.L Connect — instruções compartilhadas para agentes

Estas instruções são a base comum para Copilot, Codex e colaboradores automatizados. As instruções do Copilot podem complementar este arquivo, mas não podem reduzir suas salvaguardas.

## Identidade e objetivo

M.G.L Connect é um sistema de gestão logística em desenvolvimento. O núcleo permanece em C#/.NET 10, ASP.NET Core MVC/Razor e Entity Framework Core. Preserve a arquitetura existente e implemente somente o escopo solicitado; não migre para outra plataforma nem invente funcionalidades.

## Arquitetura e responsabilidades

- Controllers MVC e APIs tratam HTTP, validação de entrada e autorização, delegando regras de negócio a serviços.
- Services e interfaces concentram regras, integrações e operações compartilhadas; evite duplicar comportamento em controllers ou views.
- Models representam entidades e conceitos do domínio; DTOs e ViewModels definem contratos de transporte e apresentação.
- `MglDbContext`, inicializadores e atualizadores de esquema concentram persistência, configuração relacional e compatibilidade de dados.
- Hubs SignalR publicam telemetria e rastreamento somente para conexões e audiências autorizadas.
- Views Razor, CSS e JavaScript implementam as interfaces administrativa, do cliente e do motorista; preserve os fluxos offline existentes.
- Trate cada provedor de banco, serviço externo e recurso de roteamento conforme sua configuração e evidência atuais. Roteamento experimental ou não homologado não pode ser apresentado como seguro para uso operacional.

## Segurança, autorização e isolamento

- A autorização deve ser validada no servidor em cada operação. Revalide tenant, propriedade e vínculo com o recurso ao consultar ou alterar dados; não confie apenas em identificadores ou filtros fornecidos pelo cliente.
- Não amplie acesso entre tenants. APIs, hubs, GPS/histórico, comprovantes de entrega (POD), exports, diagnósticos e arquivos exigem verificação explícita de escopo.
- Não introduza credenciais, tokens, chaves, dados pessoais reais ou conteúdo de bancos em código, testes, logs, commits ou documentação.
- Não leia configurações sensíveis sem necessidade. `.gitignore` não protege arquivos que já são rastreados.
- Não conecte validações a bancos operacionais. Use fixtures e serviços locais isolados e identifique testes que criem dados ou iniciem serviços.

## Mudanças, versionamento e validação

- Antes de editar, examine o estado Git e o contexto dos arquivos relacionados. Preserve alterações staged, unstaged, não rastreadas e de outros colaboradores.
- Faça mudanças cirúrgicas. Não reverta, descarte, limpe, rebaseie ou reescreva histórico sem autorização explícita.
- Nunca use `git add .` indiscriminadamente. Revise cada conjunto com `git diff --cached` antes de commit.
- Não afirme que build, testes, homologação ou implantação passaram sem evidência recente. Registre comando, ambiente, resultado, falhas e verificações não executadas.
- Execute apenas validações autorizadas; sinalize efeitos colaterais, dependências externas, banco ou containers antes de iniciá-las.
- Antes de qualquer push ou merge, apresente branch, commits, arquivos, auditoria de segredos, sincronização remota, validação e riscos. Push e merge exigem autorização explícita.

## Fleetbase

Fleetbase é uma referência externa e não integra o repositório M.G.L Connect. Não copie nem incorpore seu código. Comparações futuras devem distinguir conceitos e contratos de API de reutilização de código e considerar as obrigações da licença AGPL-3.0.

## Colaboração e transferência de contexto

- A pasta de desenvolvimento diário é `mgl-connect`; as outras worktrees ficam preservadas como referências históricas. Codex conduz planejamento, implementação, testes e manutenção nesta pasta e administra branches quando necessário, sem exigir escolhas rotineiras do usuário.
- Copilot faz revisão independente pontual, preferencialmente do diff/PR e sem editar a pasta oficial. Não atribuir autoavaliação do Codex a uma revisão do Copilot. Não alterar configurações globais para esse fluxo.
- GitHub é a cópia versionada do conteúdo publicável; não substitui backup local criptografado de histórico, arquivos privados e alterações pendentes. Verificar backup antes de consolidações estruturais. Nunca publicar backups, chaves ou documentos acadêmicos pessoais.
- Cada tarefa delegada identifica responsável, branch/worktree, arquivos previstos, objetivo, critérios de aceite, testes necessários e revisor.
- Codex é o agente principal de engenharia. Copilot é revisor quando solicitado; revisão deve informar commit, escopo, evidências, achados e limitações, sem integrar automaticamente.
- Não permita alterações simultâneas no mesmo checkout. Antes de paralelizar, confira os worktrees e use worktrees/branches isolados.
- Nenhum agente faz merge ou push automaticamente.
- Handoffs registram commit-base, arquivos/diffs, evidências, comandos executados, resultados, limitações, pendências e próximo passo autorizado. Trate relatos de outro agente como alegações até que evidências verificáveis os sustentem.
- Revisões cruzadas devem apontar achados concretos com localização, impacto, evidência e recomendação; aprovação genérica não basta. Copilot e Codex podem revisar o trabalho um do outro.
