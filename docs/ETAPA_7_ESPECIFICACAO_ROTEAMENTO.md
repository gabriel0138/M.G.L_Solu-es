# Especificação preparatória — Etapa 7 (não implementada)

## Estado efetivamente encontrado

- `ITruckRoutingService`/`TruckRoutingService`: geocodificação Google configurável ou Photon opcional; coordenada de depósito e cache em memória. OSRM HTTPS configurável, `/route/v1/driving`, GeoJSON e instruções; timeouts/retorno indisponível em falhas. [O perfil car do OSRM](https://github.com/Project-OSRM/osrm-backend/blob/master/profiles/car.lua) não deve ser apresentado como homologação da combinação brasileira de veículo/carga/via.
- Leaflet/tiles configuráveis apresentam a geometria; não validam restrições. SignalR/`GpsTelemetryPublisher`/`RouteDeviationEngine` usam posições aceitas; baixa precisão não deve gerar desvio ou recálculo. Cache e estado de desvio em memória não substituem persistência GPS.
- Modelo Veiculo contém capacidade, altura, largura, comprimento, peso declarado, eixos e tipo de carga. Entrega replica parte desses dados; pedido possui peso/dimensões da carga. A semântica do peso declarado não prova PBT/PBTC real nem carga por eixo; configurações padrão não são medição. Faltam composição completa, peso por eixo, distribuição, autorizações especiais e conjunto regulatório verificável.
- ETA/distância são estimativas. Parte do tempo para perfil pesado usa heurística de velocidade média, não trânsito observado. Não há base homologada de pedágios, alturas livres, pontes/túneis, restrições por horário, áreas proibidas ou risco. Os avisos condicionais existentes não são legislação universal. Helpers legados `ObterRotaFallback*` permanecem sem chamadas; não reativá-los para inventar geometria/ruas.
- Traçado indisponível resulta em ausência de rota, distância/ETA de navegação indisponíveis e orientação para a operação; os testes cobrem rede, timeout, HTTP 503 e JSON inválido de geocodificação/roteamento.

## Contrato futuro

| Classificação | Evidência necessária | Comunicação permitida |
|---|---|---|
| Estimada | Provedor/configuração/data do cálculo e geometria válida; restrições não comprovadas | “Rota estimada; verificar restrições.” |
| Validada | Veículo+carga completos, regras aplicadas, fonte geográfica versionada, cobertura conhecida e validação das restrições relevantes | Descrever exatamente o que foi validado e o que ficou sem dados. |
| Homologada | Processo operacional aprovado para território/categoria definidos, ensaios independentes, responsáveis, versões, cobertura e critérios explícitos | Somente dentro desse escopo; nunca garantia geral de segurança/legalidade. |

Manter compatibilidade de contratos atuais e introduzir, apenas na Etapa 7, um resultado com provedor/perfil, versão dos dados/regras, classe de confiança, restrições verificadas/desconhecidas, idade dos dados, motivos de bloqueio, geometria/steps e origem de ETA/distância. Ausência de dado obrigatório deve impedir promoção de classe. Não transformar um booleano ou aceite manual em homologação.

## Dados e decisões obrigatórios

1. Envelope real: altura total, largura, comprimento/composição, tara, carga, PBT/PBTC, quantidade/carga por eixo, tipo/periculosidade, autorizações e destino/acessos de carga. Validar unidades e origem desses dados, sem confundir TenantId e ClienteId.
2. Fonte de restrições: altura/peso/comprimento/largura/eixos, pontes/túneis, acesso e horário, cargas perigosas e interdições. Registrar órgão/fonte/versionamento/cobertura; conferir regras jurídicas em fontes oficiais na implementação. Ausência de atributo OSM não significa ausência de restrição.
3. Comparação de provedores com documentação/licença/cotas/custo/cobertura brasileira/privacidade. Não contratar nem enviar dados privados sem autorização. Nenhum provedor foi integrado nesta etapa.
4. Recálculo condicionado a precisão/idade da posição, confiança da rota, debounce, limite/backoff, cancelamento de cálculo obsoleto, isolamento e publicação após confirmar atribuição. Não projetar marcador observado sobre a linha como se fosse medição.
5. Sem rota validável: não sugerir uma via supostamente segura, não desenhar linha substituta como percurso e não gerar ETA de navegação. Permitir contato operacional e apresentar motivos/limites; emergência/POD continuam disponíveis.

## Critérios para iniciar e aceitar

Iniciar somente após revisar a evidência da Etapa 6, disponibilizar dados obrigatórios e escolher escopo/provedor com licença/custo compatíveis. A homologação SQL Server, backups fora da máquina e testes Android/iOS físicos continuam pendências operacionais; não liberar produção com base em SQLite/Chromium.

Aceitar incrementos determinísticos com fixtures sem APIs externas e casos negativos de restrição, ausência de dados, cobertura, timeout, falha, recálculo concorrente, troca de motorista/veículo, autorização e reconexão. Validar percurso conhecido por fonte independente antes de qualquer afirmação de homologação. Não iniciar migração de mapas ou motor pesado apenas para melhorar aparência.
