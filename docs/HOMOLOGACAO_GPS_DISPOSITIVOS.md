# Ensaio físico de GPS — Android e iOS

Estado: **não executado em aparelhos físicos**. Chromium/CDP valida código, transporte e IndexedDB com posições sintéticas; não comprova sensores, bateria ou funcionamento em segundo plano de Android/iOS.

## Preparação

Usar organização, motorista, veículo, pedido e turno exclusivos de homologação, sem dados privados reais. Publicar somente quando autorizado um ambiente de ensaio HTTPS acessível ao aparelho; `localhost` do notebook não é o localhost do celular. Registrar aparelho/OS/navegador/versão, modo navegador/PWA, economia de energia, rede e versões do servidor. Geolocalização exige [contexto seguro e permissão](https://developer.mozilla.org/en-US/docs/Web/API/Geolocation_API).

Não registrar senhas/tokens em vídeos ou evidências. Manter UUIDs e relógios UTC de captura/recebimento, precisão informada e referência de correlação. Precisão informada pelo dispositivo não é medição independente de erro. Comparar em local conhecido somente com método de referência adequado; não usar animação ou mapa como prova.

| Ensaio | Procedimento e critério observável |
|---|---|
| Permissão | Negar, conceder e revogar localização. Exibir orientação correta; sem inventar posição ao negar. |
| Primeiro sinal/qualidade | Página aberta, imóvel e depois percurso controlado com passageiro operando. Medir tempo até sinal, quantidade e precisão; pontos >50 m não produzem navegação/desvio, >100 m ficam apenas na auditoria. |
| Bateria | Medir carga inicial/final e duração em condições registradas, com e sem GPS; repetir em navegador e PWA. Não extrapolar um único ensaio para toda a frota. |
| Rede | Cortar Wi-Fi/dados; registrar pendentes, recarregar, reabrir e reconectar. Mesmo UUID/horário confirmado uma vez; nenhum pendente removido silenciosamente. |
| Tela bloqueada/segundo plano | Bloquear, alternar aplicativo e aguardar intervalos registrados; conferir lacunas e retomada. Pausas são possíveis: **não é garantia de captura contínua**. |
| Reinício do aparelho | Com pendentes, reiniciar e autenticar novamente o mesmo proprietário. Conferir IndexedDB, recibos e preservação de rejeitados/expirados. Evitar modo privado/limpeza de dados durante o ensaio. |
| Turno encerrado | Capturar desconectado, encerrar turno no servidor e reconectar. Somente capturas dentro do intervalo registrado e com autorização vigente entram como histórico; nunca como posição ao vivo. |
| Troca/revogação | Trocar usuário/organização, reatribuir recurso e inativar conta. A fila anterior não pode vazar nem ser reenviada com a nova identidade. |
| Janela/capacidade | Janela máxima de sete dias, cadência adaptativa e limite global 2.000. Expirados preservados, cópia JSON e limpeza explicitamente confirmada. Teste real de sete dias deve durar sete dias; testes com relógios sintéticos recebem essa identificação. |

## Registro de evidência

Para cada caso: data UTC, versões, duração real, estado inicial/final, amostras/UUIDs, pendentes/rejeitados/expirados, captura/recebimento, consumo de bateria, resultado esperado/observado e referência de logs sem coordenadas desnecessárias. Conferir cliente/admin/cockpit e PDF após entrega. Pausas não podem aparecer como movimento contínuo; registrar lacunas sem supor percurso.

Aprovação requer execução real em ao menos Android e iOS nas versões suportadas, recuperação sem duplicação/perda silenciosa, privacidade e limites explícitos. Qualquer promessa de rastreamento contínuo com tela bloqueada exige outra solução e homologação própria; instalar PWA não fornece essa garantia.
