# Próximos incrementos do protótipo

Esta primeira entrega implementa correções e demonstra uma jornada local; não encerra a homologação funcional do sistema inteiro.

1. **P0 — rota viária controlada:** fixture determinística do contrato do provedor no servidor, curva/desvio/retorno e recálculo; comparar geometria serializada, progresso e todos os mapas. Na execução atual o provedor ficou desativado: orientação suspensa é o resultado correto. GPS observado permanece independente da geometria. Medir lentidão antes de otimizar consultas ou algoritmos.
2. **P0 — aparelho móvel:** validar toque, safe area, rotação e teclado reais em 360/390 px; manter evidências de regressão do clique GPS, POD e emergência. Headless não comprova execução no aparelho ou em segundo plano.
3. **P1 — cliente/intermediador:** ampliar os perfis sintéticos do navegador para segundo cliente/tenant e intermediador; verificar links, formulários inválidos e estados vazios. Há cobertura técnica C# de isolamento, mas ela não substitui esse percurso visual.
4. **P1 — administração:** exercitar CRUD de clientes/frota, pedidos inválidos, cancelamento, despesas, alertas e filtros, com ações/persistência e não só abertura. Registrar cada cenário na matriz antes de marcá-lo aprovado.
5. **P2 — histórico e infraestrutura:** ensaios dedicados de GPS arquivado/compactação e restauração; PostgreSQL/SQL Server e containers somente em serviços descartáveis autorizados. Dependências CSS/JS/tiles externas exigem um ensaio de indisponibilidade próprio.

Manter a branch funcional separada do PR #1. Publicação exige apresentação de commits/diff, validação, segredos e estado remoto seguida de autorização explícita. Merge em main continua separado. Não incorporar código Fleetbase nem prometer rota de caminhão homologada.
