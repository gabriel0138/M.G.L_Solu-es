# M.G.L Connect

Sistema acadêmico de gestão logística em C#/.NET 10, ASP.NET Core MVC/Razor, EF Core e SignalR. Codex é o agente principal; Copilot realiza revisões independentes pontuais. O projeto continua em estabilização e não está homologado para produção ou navegação de caminhões.

## Desenvolvimento diário

Use a pasta oficial `mgl-connect` e mantenha uma sessão principal do Codex. As worktrees anteriores permanecem preservadas como referências; não precisam ser usadas no trabalho diário. GitHub mantém o código publicável e seu histórico, mas não substitui backup criptografado dos arquivos privados e das alterações locais.

Pré-requisitos: SDK .NET 10 e Node.js para os testes JavaScript. Docker é opcional.

```sh
dotnet restore MglConnect.sln
dotnet build MglConnect.sln --no-restore -c Debug
# Sem conexões externas configuradas: cinco PostgreSQL e quatro SQL Server são ignorados.
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION dotnet test MglConnect.sln --no-build --no-restore -c Debug
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
```

Os testes HTTP usam SQLite descartável e processos Kestrel em loopback. Usar uma raiz TMPDIR privada com espaço disponível. Não configurar conexões operacionais para testes.

Para uma prévia local sintética, após revisar os efeitos do script:

```sh
bash scripts/dev-local.sh
```

O script compila e inicia a aplicação em loopback, com banco/chaves de demonstração em diretório próprio. Não foi executado nesta consolidação. Não substitui configuração ou provisionamento de produção.

## Capacidades e limites

| Área | Estado |
|---|---|
| Administrador | Clientes, pedidos, despacho, frota/motoristas, códigos de turno, rastreamento e operações implementados; cobertura depende dos testes e do provedor |
| Cliente/intermediador | Solicitação e consulta de pedidos, com vínculo de cliente resolvido no servidor; intermediador tem vínculo único |
| Motorista | Conta individual, atribuições, turno, GPS, emergência e POD; interface Razor/PWA, não aplicativo Flutter |
| GPS/offline | Filas IndexedDB de GPS/POD, recibos e histórico persistente; não garante coleta com aplicativo fechado ou tela bloqueada |
| POD | Imagem de assinatura e PDF autorizado; não é assinatura criptográfica nem certificação jurídica automática; POD privado não integra a projeção pública |
| Rastreio público | Códigos novos de alta entropia mantêm a projeção mínima e o SignalR; códigos legados de baixa entropia exibem somente status, sem detalhes pessoais ou atualização ao vivo |
| Decimais de frota/perfil | Ponto invariável, até duas casas, sem milhares ou interpretação pela cultura do servidor; lista por eixo com o mesmo contrato |
| Custos/frete/CO2 | Estimativas com parâmetros locais; não são despesas efetivas, piso legal vigente comprovado ou inventário certificado |
| Equipamentos/manutenção | Sem módulo completo equivalente localizado; não acrescentado nesta consolidação |
| Banco | Um provedor por instância: SQLite, SQL Server ou PostgreSQL; ensaio SQLite não homologa os demais |
| Roteamento | OSRM genérico e adaptador Valhalla experimental; restrições/cobertura insuficientes suspendem orientação pesada |

Photon/OSRM públicos são demonstrações sem SLA. Leaflet exibe mapas e não homologa restrições. Não enviar dados privados a demos públicas. Os termos de mapas, dados e dependências exigem revisão conforme a finalidade real.

## Infraestrutura preparada

[Dockerfile](./Dockerfile), [Compose](./docker-compose.yml) e [configurações de demonstração](./deploy/) foram recuperados seletivamente. Uso Docker, SQL Server, scripts de browser e serviços externos exige autorização própria. A imagem, volumes, PDF no container, TLS externo e implantação não foram homologados nesta missão.

O [workflow](./.github/workflows/ci.yml) executa em pull requests e também pode ser iniciado manualmente (`workflow_dispatch`). As ações são fixadas por SHA; a validação inclui restore/build/testes locais .NET, testes JavaScript, verificação do diff e Gitleaks, sem Docker/PostgreSQL/SQL Server. O check remoto só pode ser confirmado após publicar a atualização da branch.

Documentos acadêmicos privados, backups, bancos e chaves ficam fora do conteúdo versionado atual. Arquivos acadêmicos já existentes no histórico público anterior não foram expurgados: esta missão não reescreve histórico. Avaliar eventual exposição anterior separadamente.

## Evidências e continuidade

- [Consolidação e classificação das origens](./docs/ai/CONSOLIDACAO.md).
- [Handoff e contrato decimal](./docs/ai/HANDOFF.md).
- [Backlog](./docs/ai/BACKLOG.md) e [registro de revisões](./docs/ai/REVISOES.md).
- [Arquitetura mobile/API/banco](./docs/ARQUITETURA_MOBILE_API_BANCO.md).
- [Operação de backup/restauração](./docs/OPERACAO_BACKUP_RESTAURACAO.md).

Os relatórios históricos preservam seu contexto e não são aprovação atual. Nenhuma revisão do Codex equivale a revisão independente do Copilot. Push exige autorização e verificações; merge em `main` exige aprovação final específica.
