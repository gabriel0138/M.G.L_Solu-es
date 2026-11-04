# Continuação B01/B02 — rastreio público e autorização de telemetria

Data: 28/10/2026. Branch `main`; commit-base `d8a54d3`.

## Resultado

O rastreio público passou a expor primeiro nome do destinatário, cidades/UF, status e datas. Endereço completo, documentos, recebedor, assinatura, foto, número/dados do pedido, motorista e veículo não integram essa projeção. Página, API e notificações públicas de checkpoint/POD utilizam a mesma política de minimização.

Regressão final: **165 aprovados, 0 falhos, 4 ignorados, total 169**. Os quatro ignorados exigem SQL Server externo; não foram declarados aprovados. Restore e build aprovados, com **0 warnings e 0 errors** no build.

## Ponto de retomada

O [relatório anterior](./FASE_1B_SEGURANCA_ISOLAMENTO.md) documentava B01 com 130 testes aprovados e quatro ignorados. O arquivo [PublicTrackingTests](../src/MglConnect.Tests/PublicTrackingTests.cs), preexistente e posterior àquele relatório, já continha três casos de B02 sem implementação correspondente.

Ao retomar, foram acrescentados cenários restantes de B01. A primeira regressão completa reproduziu as três falhas públicas: **158 aprovados, 3 falhos e 4 ignorados**, total 165, incluindo os 28 casos novos de B01. A continuação implementou as correções de B02 e acrescentou quatro casos públicos. Os três testes preexistentes de B02 permaneceram inalterados.

## Correções do rastreio público

- [PublicTrackingProjection](../src/MglConnect.Web/Services/PublicTrackingProjection.cs) define contratos públicos próprios. O [serviço de rastreio](../src/MglConnect.Web/Services/TrackingService.cs) consulta apenas os campos necessários, sem materializar o grafo privado de entrega, pedido, cliente, motorista e veículo para a consulta pública.
- Eventos públicos são reconstruídos a partir de status reconhecido e data. Título, descrição, localidade e ícone livres do histórico não são copiados. A localidade genérica não atribui ao evento uma posição geográfica que não foi validada. O texto original permanece no banco e na consulta privada autorizada.
- A [API pública](../src/MglConnect.Web/Controllers/Api/RastreioApiController.cs) e a [página pública](../src/MglConnect.Web/Views/Rastreio/Index.cshtml) não exibem POD ou endereço completo. A página encaminha o usuário à área autenticada; seu conteúdo ao vivo é criado com `textContent`, sem interpolação de eventos em HTML.
- Códigos decimais, GUID e prefixados existentes continuam consultáveis, inclusive quando um GUID está persistido em minúsculas. A mesma normalização é aplicada à consulta e aos grupos do [hub público](../src/MglConnect.Web/Hubs/TrackingHub.cs). Códigos inválidos ou ausentes recebem mensagem genérica, sem ecoar a entrada.
- Novos códigos usam dez bytes criptograficamente aleatórios, representados em 20 caracteres hexadecimais: 80 bits de aleatoriedade dentro do limite atual da entidade. Não houve regeneração de códigos antigos ou mudança de schema.
- [PublicTrackingRateLimiter](../src/MglConnect.Web/Services/PublicTrackingRateLimiter.cs) e o [middleware público](../src/MglConnect.Web/Middleware/PublicTrackingMiddleware.cs) compartilham o orçamento por IP remoto entre página, API, estabelecimento de conexão pública e inscrições no hub. O padrão é 30 operações por 60 segundos, sem fila, por instância. HTTP retorna 429 com `Retry-After`; invocações excedentes no hub recebem erro explícito. Respostas públicas recebem `Cache-Control: no-store`.

Configuração opcional: `PublicTracking:PermitLimit` e `PublicTracking:WindowSeconds`, ambos positivos. O orçamento inclui negociação/conexão do hub; não equivale a 30 pesquisas completas com conexão por minuto. Cabeçalhos `X-Forwarded-For` enviados diretamente pelo cliente não mudam a partição.

## Evidência acrescentada

| Arquivo | Casos | O que foi validado |
|---|---:|---|
| [B01RouteEventAuthorizationTests](../src/MglConnect.Tests/B01RouteEventAuthorizationTests.cs) | 24 novos | Sucesso, ausência de rota e exceção do provedor; sete revogações e controle sem revogação, alterados durante o cálculo pendente. Destinatário revogado sai da inscrição/grupo; controle autorizado recebe o resultado. SQLite e serviço de turnos reais, provedor/engine/clientes SignalR controlados; integração de serviço, sem transporte HTTP/WebSocket. |
| [B01TelemetryReconnectTests](../src/MglConnect.Tests/B01TelemetryReconnectTests.cs) | 4 novos | Cookie/bearer após mudança de perfil ou organização: identidade antiga rejeitada na negociação, login renovado e inscrições permitidas apenas nos recursos atuais. HTTP e WebSocket reais. |
| [PublicTrackingTests](../src/MglConnect.Tests/PublicTrackingTests.cs) | 3 preexistentes | Minimização na página/API, inclusive eventos legados; compatibilidade de códigos e erros genéricos; orçamento compartilhado entre página e API. |
| [PublicTrackingRealtimeTests](../src/MglConnect.Tests/PublicTrackingRealtimeTests.cs) | 4 novos | Checkpoint/POD recebidos anonimamente por WebSocket sem dados privados; projeção consistente com API/página; histórico e PDF autorizado preservados; orçamento compartilhado com invocações no hub e resistência a `X-Forwarded-For` forjado; novo código válido no modelo e consultável. |

A [fixture HTTP](../src/MglConnect.Tests/SecurityHttpFixture.cs) recebeu apenas um leitor de payloads de eventos. Os casos com transporte continuam usando Kestrel, handlers reais de cookie/bearer e SQLite exclusivo por caso. Não foi acrescentado handler de autenticação falso nem dependência nova.

## Comandos e validação

Executados a partir da raiz, com o SDK local:

```bash
DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet restore MglConnect.sln --verbosity minimal

DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet build MglConnect.sln --no-restore --verbosity minimal

env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION \
  DOTNET_ROOT=$HOME/.local/share/mglconnect-dotnet10 \
  $HOME/.local/share/mglconnect-dotnet10/dotnet test MglConnect.sln --no-restore --verbosity minimal
```

Resultados: 28/28 casos novos de B01 aprovados no lote dirigido; 7/7 casos públicos aprovados no lote dirigido; regressão final de 169 casos com 165 aprovados, zero falhos e quatro SQL Server ignorados, aproximadamente 48 segundos. `git diff --check` sem problemas nos arquivos verificados. Razor compilou e as páginas foram renderizadas por HTTP nos testes; não houve inspeção visual em navegador.

## Preservação e limites

O índice Git foi comparado por SHA-256 com o estado inicial e permaneceu igual. As alterações antigas de README/implantação, os arquivos de schema/modelo e os testes da Fase 1A foram preservados. O [relatório anterior de B01](./FASE_1B_SEGURANCA_ISOLAMENTO.md) e os relatórios da Fase 2 não foram reescritos. Em arquivos já alterados, somente o registro/middleware público de inicialização e o leitor de payloads da fixture receberam complementos desta continuação.

Nenhum banco preexistente foi saneado, migrado ou usado nos ensaios. Não houve commit, push, deploy, instalação de pacotes, alteração de infraestrutura ou inicialização de containers. As fixtures encerram seus processos próprios e conservam seus diretórios temporários conforme o comportamento anterior.

Pendências explícitas:

- A matriz no SQL Server, HTTPS/proxy e navegador real permanece pendente. Os eventos privados de recálculo foram testados em integração de serviço; a matriz equivalente de recálculo por WebSocket com provedor controlado continua pendente.
- O orçamento é local à instância e usa o IP remoto efetivo. Proxy/NAT pode agrupar usuários; implantação com proxy confiável ou múltiplas instâncias exige configuração e validação próprias. Não foi ativada confiança genérica em cabeçalhos encaminhados nem prometida proteção contra consulta distribuída.
- Códigos antigos conservam sua entropia e validade anteriores. Não foram revogados ou rotacionados. A política de prazo, rotação e acesso a códigos antigos precisa de decisão operacional.
- Primeiro nome e cidades continuam sendo informações públicas para quem possui um código válido. Esta implementação atende ao contrato dos testes existentes; aprovação da política final de dados públicos e retenção continua pendente.
- A consulta case-insensitive aplica `UPPER` ao código existente. Não foi alterado índice/collation nem realizado teste de carga; otimização exige análise no banco-alvo.
- Eventos públicos agora exibem mensagens genéricas por estado. Etapas detalhadas derivadas de textos livres não são reconstruídas publicamente. O histórico privado mantém esses textos.

Esta continuação resolve os cenários reproduzidos e amplia a evidência de B01/B02; não declara encerrada toda a auditoria nem inicia B03, migração SaaS, saneamento de dados ou publicação.

## Atualização de segurança — 2026-11-04

Esta seção complementa o estado histórico acima após auditoria independente do PR #1.

- A classificação de baixa entropia cobre códigos decimais de oito dígitos e `MGL-########BR`. Eles continuam resolvendo para o registro correto, mas a página e a API expõem somente o status genérico: nome, origem/destino, datas e linha do tempo ficam vazios. Esses códigos não podem entrar em grupos públicos SignalR nem recebem broadcasts. Códigos aleatórios novos e GUIDs mantêm a projeção pública anterior.
- O orçamento global de códigos legados foi removido para que tentativas, inclusive contra códigos inexistentes, não bloqueiem coletivamente todos os destinatários. Página, API e ingresso no hub compartilham apenas o limite por endereço IP (padrão: 30 operações por 60 segundos). Não há contador nem cache indexado por código fornecido pelo cliente, evitando estado por código e consumo de memória controlável por entradas arbitrárias. Esse limite local **não impede enumeração distribuída** por múltiplos endereços e não é compartilhado entre instâncias; deployments com réplicas precisam de rate limiting agregado no gateway.
- Política escolhida: preservar temporariamente o acesso legado somente ao status, sem rotação ou revogação em massa, e oferecer migração gradual por rotação individual autenticada. Códigos de baixa entropia ainda permitem distinguir códigos válidos pela resposta e podem ser enumerados de forma distribuída; a projeção reduzida limita os dados expostos, mas não torna esses códigos equivalentes a segredos fortes.
- Nenhum código existente é rotacionado automaticamente. Um administrador pode rotacionar uma entrega individual pela tela de detalhes. A ação cria um código aleatório novo, registra a operação sem armazenar os códigos nos detalhes de auditoria e invalida o anterior. Para preservar acesso legítimo: confirmar a identidade/autoridade de quem solicita, usar canal de contato previamente verificado para transmitir o novo código após a rotação e confirmar que o destinatário recebeu o acesso. Não fazer rotação em lote nem usar contatos não verificados. Não há migração de schema ou operação automática em banco operacional.
- Testes cobrindo códigos MGL/números legados, códigos fortes, consultas inválidas, dados pessoais ocultos, hub bloqueado, limite compartilhado por IP e isolamento entre endereços e rotação individual estão em [PublicTrackingTests](../src/MglConnect.Tests/PublicTrackingTests.cs), [PublicTrackingRateLimiterTests](../src/MglConnect.Tests/PublicTrackingRateLimiterTests.cs) e [PublicTrackingRealtimeTests](../src/MglConnect.Tests/PublicTrackingRealtimeTests.cs).
- [Workflow CI](../.github/workflows/ci.yml) agora executa em pull requests, com permissão `contents: read`, fixtures locais, .NET 10, JavaScript, whitespace e Gitleaks. PostgreSQL, SQL Server, Docker, deploy e serviços externos continuam fora do workflow. O scanner é baixado de release fixada e validado por SHA-256; relatórios usam redaction integral. Resultado remoto do PR deve ser confirmado separadamente, após o push.

### Procedimento de rotação individual

1. Verificar a solicitação com a organização/destinatário por um contato já cadastrado e confiável. A aplicação não valida destinatários nem envia o código por e-mail.
2. Na tela autenticada da entrega, um administrador seleciona **Rotacionar código público** e confirma que o código anterior será invalidado.
3. Comunicar o novo código por canal autorizado e verificar seu recebimento. Aguardar essa comunicação antes de promover rotação de outros registros.
4. Confirmar que o novo código retorna status e a projeção completa permitida; confirmar que o anterior retorna 404. Registrar a ocorrência sem copiar códigos em tickets/logs.

Se não houver meio confiável de contatar o destinatário, não rotacionar só para remover o formato legado: ele permanece acessível com status limitado até que haja solicitação/risco individualizado. Nenhum conteúdo histórico é apagado por esse procedimento.
