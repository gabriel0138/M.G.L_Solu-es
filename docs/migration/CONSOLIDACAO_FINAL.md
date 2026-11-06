# Consolidação para migração — 6/11/2026

Base funcional: `ea3844f16ae76d34ab5d051e4a94607e08bcb64b`, branch `feat/mgl-functional-gps-mobile`. A missão preserva as Entregas 1–9, sem nova funcionalidade, mudança de arquitetura ou edição das worktrees históricas. O marco final é a tag `migration/windows11-2026-11-06`; confira seu SHA e as duas branches no recibo remoto da operação.

## Preservação e diagnóstico

A única pendência no checkout oficial era o arquivo não rastreado `exit`, saída de terminal com escapes ANSI e listagem de commits/branches, sem código de implementação. Foi copiado/conferido por SHA256 no snapshot privado e movido para lá, sem descarte ou publicação. Isso removia a causa da interrupção do script de migração. Não houve alteração staged ou de código após a Entrega 9.

Sete worktrees foram inventariadas. A estabilização tem mudanças staged/unstaged e arquivos novos de documentação acadêmica/infraestrutura; o snapshot público tem três documentos modificados. Os estados permanecem nas origens e no snapshot privado. Índices, patches separados, arquivos novos e configurações relevantes entram na recuperação; nunca foram adicionados automaticamente à branch funcional. As demais estavam limpas.

[Inventário de branches](./BRANCHES.md): diagnóstico, revisão decimal e integração são ancestrais da funcional. Snapshots antigos/reconstituídos são preservados por bundle completo. A comparação com o snapshot público confirma sanitizações e código anterior; não se encontrou correção ausente que justifique merge. Não foi integrado código extra de outras branches. O arquivo forense Git conserva objetos/reflogs privados adicionais; o bundle é o caminho normal de recuperação de todas as refs.

Os dois scripts Downloads foram inspecionados, não executados cegamente. O mais antigo faz `git add -A` e commit automático antes do snapshot; ambos deixam lacunas de preservação/restauração e dependem de senha interativa. Foi usado procedimento privado controlado, com snapshot anterior, hashes, criptografia e restauração real. Nenhum reset, clean, force push, rebase ou limpeza global foi feito.

## Validação desta operação

Temporários no disco, processos sequenciais, SDK Linux .NET 10.0.403; Node/Chromium/Brave instalados. Os bancos são fictícios/descartáveis; OSRM local em loopback usa a pequena malha preservada, não um circuito desenhado ou consulta contínua ao servidor público.

| Etapa | Resultado atual | Evidência privada |
|---|---|---|
| Build .NET completo, no-restore | 0 erros e 0 warnings | build.log |
| C# completo, no-build/no-restore | 691 aprovados, 11 ignorados, 0 falhas | csharp.log |
| JavaScript | 120 aprovados, 0 falhas/ignorados | javascript.log |
| PostgreSQL isolado | 7 aprovados, 0 falhas/ignorados | postgres-retry.log |
| Chromium, planejador profissional/jornada completa | Aprovado | chromium-retry.log |
| Brave, intermediador/jornada completa | Aprovado | brave.log |
| Offline, persistência e replay | Aprovado | offline.log |
| Gitleaks, histórico funcional integral | Sem ocorrências | gitleaks-history.log/json |
| Diff whitespace e integridade Git | Aprovados | git-fsck.log |

Dos 11 ignorados padrão, os sete PostgreSQL passaram separadamente; quatro SQL Server permanecem **não executados**. Windows, Android físico e homologação de caminhões não foram testados. Não há revisão independente nova. Logs, screenshots e comandos estão sob a raiz privada de evidências de migração, incluída no arquivo criptografado.

As duas primeiras falhas foram ambientais, com diagnóstico reproduzido: PostgreSQL recusou socket Unix acima de 107 bytes; Chromium abortou com `Socket path too long`. Caminhos dedicados mais curtos no mesmo disco resolveram ambos. Os logs falhos foram mantidos. Nenhuma espera artificial, supressão de asserção ou alteração de código funcional foi usada.

## Publicação autorizada e limites

O usuário autorizou nesta missão push normal da funcional, avanço seguro da main por fast-forward e criação da tag. A main anterior `d8a54d3` é ancestral, sem proteção indicada pelo GitHub. A atualização usa push normal, com nova consulta às refs e verificação remota; nunca force. As branches antigas, dados privados e refs de sessões **não são publicados**. O recibo final privado registra os SHAs efetivamente observados. Não se atribui aprovação ao CI remoto sem execução. Nenhum comando de merge/edição do PR #1 é necessário para o fast-forward; o GitHub pode atualizar o estado do PR automaticamente quando seu conteúdo passa a integrar a main.

Somente README e os três documentos desta pasta são novos nesta consolidação; código e testes são os da Entrega 9. O Gitleaks também deve aprovar o commit documental antes do push. Material acadêmico já presente no histórico público antigo não foi expurgado: não houve reescrita de histórico, e este trabalho não declara removida exposição histórica anterior.

## Backup e recuperação

Backup preventivo privado de todas as refs e pendências criado e descriptografado antes da consolidação estrutural. O arquivo final único inclui bundle completo, fonte final, metadados forenses Git, estados/índices/patches de sete worktrees, evidências selecionadas, oito prévias SQLite consistentes e suas chaves de Data Protection, recortes/dados/manifestações OSRM e Valhalla, scripts locais e backups anteriores do projeto. Credenciais do GitHub/SSH e arquivos pessoais fora do escopo não são copiados. Builds, perfis temporários, SDK/NuGet e rootfs/camadas de containers reconstruíveis são excluídos; exclusões constam no manifesto privado.

GPG AES256 com senha aleatória em arquivo privado separado; nenhum segredo é impresso ou commitado. O arquivo final inclui SHA256 interno por arquivo e teste de descriptografia/extracão, clone mirror do bundle, comparação de refs, fsck e integridade/FKs das oito bases. Tamanho/hash/caminhos finais e resultado medido ficam no recibo privado, evitando publicar o inventário privado neste repositório. Backups antigos podem continuar exigindo suas próprias senhas, sem afetar a recuperação atual.

[Guia Windows 11](./WINDOWS_11.md): caminho recomendado pelo procedimento é WSL2 com SDK/Node Linux e Docker opcional para OSRM. **Procedimento preparado, não validação Windows.** Banco e aprovações de rota/GPS têm semânticas separadas; OSRM genérico não é navegação homologada para caminhões.

**Não é seguro formatar apenas com o arquivo local.** Não há upload privado comprovado nesta sessão. A transferência do arquivo/hash e da chave para destinos externos separados, seguida de conferência e restauração no destino, permanece obrigatória. O recibo final discrimina essa pendência; GitHub não substitui o backup privado.
