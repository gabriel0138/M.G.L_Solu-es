# Requisitos — pedidos e experiência mobile do motorista

## Decisões confirmadas

- O motorista autentica com sua própria conta individual. Depois do login, escolhe somente uma viagem e um veículo já atribuídos a ele e confirma o início do turno com um código temporário gerado pela administração.
- O código é adicional à autenticação individual; placa/número do veículo e código não substituem a conta do motorista.
- A primeira versão do perfil mostra dados pessoais básicos do próprio motorista. Jornada, folha e salário ficam para uma fase posterior, depois de definidas as regras e a fonte oficial dos dados.
- O botão de emergência envia um alerta privado à operação com a posição disponível, identifica a viagem e o veículo e permite confirmação de recebimento. A interface orienta a ligar para os serviços de emergência; não promete atendimento ou envio quando estiver sem rede.
- O acesso do motorista é tratado como uma interface mobile dedicada, com somente as informações necessárias para conduzir e concluir a viagem.

## Requisitos funcionais

Prioridades: **MVP** = necessário nesta primeira versão; **P1** = próxima fase; **Fora do escopo atual** = não implementar como se já existisse.

| ID | Prioridade | Requisito |
|---|---|---|
| FR-01 | MVP | O portal do cliente deve abrir e validar o formulário de novo pedido em cultura pt-BR, apresentar erros de validação sem erro HTTP 500 e preservar os dados digitados em caso de erro. |
| FR-02 | MVP | O pedido deve associar-se ao cliente autenticado, nunca a um cliente escolhido no navegador. Deve aceitar descrição, tipo, peso, valor declarado, dimensões e endereços de coleta/destino que já integram o fluxo atual. |
| FR-03 | P1 | Complementar o pedido com contato de coleta e entrega, quantidade de volumes, janela de coleta/entrega, instruções de manuseio e classificação de cargas que exijam cuidado especial. Definir campos obrigatórios com a operação antes de alterar o esquema. |
| FR-04 | MVP | Cada motorista deve autenticar com credenciais individuais. Após login, o sistema deve encaminhá-lo à área mobile, não ao backoffice nem a listagens administrativas. |
| FR-05 | MVP | A seleção do motorista deve listar somente veículos/viagens ativos atribuídos àquela conta; exibir identificador visual do veículo (placa/número de frota) e não oferecer consulta a cargas ou viagens de outros motoristas. |
| FR-06 | MVP | Para iniciar uma viagem, o motorista autenticado informa um código temporário emitido pela administração para a atribuição. Código expirado, usado, inválido ou pertencente a outra viagem deve ser recusado; nunca armazenar o código em texto aberto. |
| FR-07 | MVP | Ao iniciar a viagem, registrar horário e associar telemetria, rota e eventos somente àquela viagem. Repetir a requisição ou abrir uma rota não atribuída não pode iniciar/alterar outra viagem. |
| FR-08 | MVP | O cockpit mostra mapa, instrução de navegação, próxima manobra, distância e estado do GPS. O mapa acompanha a posição válida; a posição simulada nunca é identificada como GPS real. |
| FR-09 | MVP | O cockpit não mostra cliente, destinatário, descrição, tipo/valor da carga, conteúdo do pedido ou documentos que não sejam necessários para navegação/POD. |
| FR-10 | MVP | O motorista tem uma ação de emergência acessível durante a viagem. O alerta registra motorista, viagem, veículo, instante, coordenadas e precisão quando disponíveis, e informa claramente se foi enviado ou se falhou. |
| FR-11 | MVP | A operação visualiza alertas ativos, pode reconhecê-los e o sistema registra quem reconheceu e quando. O motorista recebe confirmação apenas depois do registro no servidor. |
| FR-12 | MVP | Se não houver conectividade, o cockpit não exibe sucesso falso: informa que o alerta não foi entregue e orienta contato telefônico com os serviços de emergência. |
| FR-13 | MVP | O perfil do motorista mostra apenas dados do servidor do próprio titular (nome, data de nascimento, telefone e CPF mascarado). Edição de cadastro permanece com a administração autorizada. |
| FR-14 | P1 | Registrar escala, jornada, horas, salário e comprovantes de pagamento somente após definir origem dos dados, regras de cálculo, retenção e permissões de RH. Não inferir salário nem jornada a partir do GPS. |
| FR-15 | P1 | Alertas de trânsito devem distinguir dados ao vivo de estimativas sem tráfego; mostrar fonte e horário da última consulta. |
| FR-16 | Fora do escopo atual | Não declarar rota homologada para caminhão, tráfego visual estilo Waze, navegação 3D ou otimização de múltiplas paradas sem provedor/dados e validação próprios. |

## Requisitos não funcionais

| ID | Prioridade | Critério de aceite |
|---|---|---|
| NFR-01 | MVP | O fluxo do motorista deve funcionar em larguras de 360, 390 e 430 CSS px, em orientação retrato, sem rolagem horizontal, sobreposição dos controles do mapa ou quebra do logotipo. |
| NFR-02 | MVP | O cockpit deve priorizar o mapa; botões primários de GPS/emergência devem ter alvo de toque de pelo menos 44 × 44 CSS px e contraste legível ao sol/no modo escuro. |
| NFR-03 | MVP | Rotas e comandos protegidos validam identidade, papel, atribuição, tenant e estado no servidor, mesmo se forem chamados diretamente sem passar pela tela. |
| NFR-04 | MVP | Códigos temporários são aleatórios, armazenados somente como hash, expiram, são de uso único, têm tentativas limitadas e são invalidados quando a atribuição muda ou termina. |
| NFR-05 | MVP | Mensagens de erro não revelam endereço, coordenadas, segredo, hash de código ou conteúdo da carga. Não registrar coordenadas exatas em logs de rotina. |
| NFR-06 | MVP | O GPS só é coletado durante uma viagem ativa, exige permissão e contexto seguro (HTTPS em produção), e exibe estados de permissão, precisão, perda de sinal e última atualização. |
| NFR-07 | MVP | O serviço nunca afirma que um alerta foi entregue antes da persistência; deve suportar auditoria e tratar duplicidade/reenvio de forma idempotente. |
| NFR-08 | MVP | Atribuições concorrentes e início de turno devem ter garantias no banco, não somente verificações prévias na camada web. |
| NFR-09 | MVP | O site e o formulário permanecem usáveis com teclado/leitor de tela, labels associados, foco visível e erros ligados ao campo correspondente. |
| NFR-10 | P1 | Definir retenção e acesso a GPS/alertas, informação ao motorista, acesso de emergência, auditoria e processo de exportação/remoção conforme política de privacidade e legislação aplicável. |
| NFR-11 | MVP | Provedores públicos de geocodificação, rotas e tiles são substituíveis por configuração; não enviar endereços de clientes nem permitir uso sem limite em produção. |

## O que não deve ser incluído no MVP

- Exibir dados da carga, cliente ou destinatário no celular do motorista, salvo o mínimo estritamente necessário para uma etapa operacional autorizada.
- Uma senha fixa compartilhada por caminhão como autenticação autônoma, rota acessível só por conhecer um número de placa ou credencial permanente de turno.
- Calcular folha/salário, medir jornada legal por GPS ou tratar tempo de deslocamento como ponto eletrônico sem definição de regras e validação jurídica/operacional.
- Chamar autoridades automaticamente, prometer monitoramento 24/7, enviar SMS/push sem provedor e contrato configurados, ou marcar alerta como recebido sem confirmação do servidor.
- Recalcular automaticamente rotas pesadas com perfil genérico de automóvel, anunciar segurança rodoviária, simular condições de tráfego ou usar uma animação como posição real.
- Baixar previamente tiles em massa ou usar demos públicas como infraestrutura ilimitada.

## Plano de validação

1. Regressão do portal: abrir, validar e submeter um pedido sob `pt-BR`, sem erros HTTP 500; testar limites, campos opcionais e cliente inativo.
2. Segurança de turno: motorista A não vê viagem de B; código incorreto/expirado/usado não inicia; troca de motorista/veículo invalida o código; deep link não contorna a verificação.
3. Privacidade: verificar HTML, respostas, logs e telemetria para confirmar que a tela mobile não expõe descrição, cliente, destinatário ou valor da carga.
4. GPS e emergência: testar localização permitida/negada/imprecisa, perda e restauração de rede, confirmação do operador e envio duplicado.
5. Layout: testar larguras 360, 390, 430, 768, 1024 e 1440 px, zoom de 200%, retrato/paisagem e tela cheia; conferir marca, menus, formulários, cards e mapa.
6. Mapas: comparar cobertura, geocodificação e geometria com endereços públicos fictícios e coordenadas de teste em São Paulo; avaliar restrições do veículo separadamente da qualidade visual.

## Etapa 2 — comportamento implementado

### Turno e autorização

- O login individual existente continua sendo a autenticação principal. O middleware `ActiveAccountMiddleware` confere em cada requisição autenticada se a conta está ativa e se o papel da sessão corresponde ao perfil armazenado.
- Um usuário `Admin`/`Administrador` emite um código aleatório para uma entrega com motorista e veículo ativos atribuídos. O segredo é exibido uma vez à operação, armazenado somente como SHA-256, expira em 15 minutos, tem até cinco tentativas, e uma nova emissão ou cancelamento invalida o anterior.
- O código é vinculado ao motorista e à entrega. A operação registra somente eventos, identificador e metadados; o segredo não é persistido nem escrito nos logs.
- O início do turno verifica usuário, papel, atribuição atual, validade do código e existência de outro turno ativo no banco. Um índice único filtrado impede mais de um turno ativo por motorista ou entrega. Reenvio após resposta perdida retorna turno já ativo; um código consumido não inicia outro turno.
- Encerrar o turno mantém a entrega atribuída e pendente de novo código para retomada. O perfil e a lista de viagens próprias não exigem turno. A prévia/navegação, POD, checkpoints e telemetria de motorista exigem turno ativo; o perfil não expõe o perfil de outro motorista.
- A alteração/cancelamento da atribuição invalida códigos pendentes. Não se aceita status enviado pelo navegador como prova de turno.

### Alertas e pedidos

- A emergência requer turno ativo e chave idempotente. O servidor persiste alerta, motorista, entrega, veículo, horário, estado da localização e auditoria. Coordenadas só são associadas se recebidas em par e dentro dos limites; posição com mais de dois minutos é marcada como desatualizada. Reenvio com a mesma chave e mesma viagem devolve o alerta existente.
- A operação consulta os alertas na tela administrativa, reconhece um alerta aberto e pode seguir as transições `Reconhecido → EmAtendimento → Resolvido`, com `FalsoAlarme` permitido a partir dos estados operacionais definidos no serviço. Cada alteração gera auditoria.
- A tela informa que o registro no sistema não é ligação telefônica, não garante monitoramento contínuo e não aciona automaticamente socorro. Não há integração com SMS, push ou central em tempo real nesta etapa.
- O pedido é associado ao cliente/intermediador autenticado, com escopo determinado por `Usuario.ClienteId`; a chave de envio evita duplicação por reenvio. O despacho valida pedido sem entrega, motorista e veículo ativos, capacidade nominal e atribuições ocupadas. As transições permitidas do pedido são `Registrado → EmProcessamento` ou `Cancelado`, `EmProcessamento → Cancelado`, e `Registrado/EmProcessamento → ComEntregaAtribuida` somente no despacho. Conclusão é vinculada ao POD da entrega. A entrega começa `Pendente`; o turno autenticado é que a move para `Em transporte`.
- A conclusão é feita pelo fluxo POD; checkpoints não mudam estado. Conclusão, cancelamento e atribuições usam versão de concorrência e índices únicos no banco para conflitos relevantes. O motorista recebe apenas campos operacionais necessários.

### Matriz de acesso de servidor

| Recurso/rota | Acesso | Exigência adicional |
|---|---|---|
| `/Entregas` e `/Entregas/PerfilMotorista` | Motorista autenticado; operações para Admin | lista/perfil limitados ao usuário próprio |
| `/Entregas/Navegar/{id}` | Admin ou motorista atribuído | motorista com turno ativo naquela entrega |
| `POST /Entregas/IniciarTurno`, `/EncerrarTurno` | Motorista autenticado | código/estado persistido para iniciar; encerrar exige turno próprio |
| `POST /Entregas/EmitirCodigoTurno`, `/CancelarCodigoTurno` | Admin | entrega atribuída e operação válida |
| `POST /Entregas/CriarAlertaEmergencia` | Motorista autenticado | turno ativo na entrega; GPS opcional |
| `/Entregas/AlertasEmergencia`, reconhecer/atualizar | Admin | transições verificadas pelo serviço |
| `GET /api/entregas/motorista/{id}` | motorista dono ou Admin | motorista vê lista minimizada; Admin pode consultar para operação |
| `GET /api/entregas/{id}` | usuário autorizado à entrega | motorista exige turno; cliente/intermediador limitado ao `ClienteId` associado |
| status, checkpoint, POD e ETA em `/api/entregas/{id}/...` | usuário autorizado | motorista exige turno; POD é a única conclusão válida; cancelamento é administrativo |
| PDF POD | Admin, motorista atribuído ou cliente do pedido | entrega concluída; escopo validado no servidor |
| relatório ESG | Admin | autenticação e papel administrativo |
| SignalR `/telemetryHub` | usuário autenticado | envio por motorista exige atribuição e turno; clientes só assinam entrega vinculada; simulação apenas Admin |
| SignalR `/trackingHub` | público com código de rastreio | canal existente de consulta pública; não é canal de comando operacional |

O middleware e as verificações de controlador/API/hub são controles complementares. Ocultar um botão na interface não substitui autorização de servidor.

### Persistência, migração e execução

- `DbInitializer` executa `EnsureCreatedAsync` e os atualizadores aditivos de perfil, rota e segurança operacional. `EnsureOperationalSecuritySchemaAsync` é repetível; o teste de SQLite também cobre a atualização de tabelas legadas mínimas e o retropreenchimento de atribuições pendentes.
- São adicionadas colunas `ChaveIdempotencia`/`Versao` a `Pedidos`, `AtribuicaoAtiva`/`Versao` a `Entregas` e as tabelas `CodigosTurnoMotorista`, `TurnosMotorista`, `AlertasEmergencia` e `AuditoriaOperacional`, além de índices únicos/filtrados.
- Os atualizadores têm SQL para SQLite, PostgreSQL e SQL Server. A suíte local valida SQLite; não há banco de PostgreSQL nem SQL Server configurado neste ambiente, então a DDL desses provedores ainda precisa de validação no ambiente de destino.
- Antes da atualização, fazer backup. Se os registros existentes tiverem duplicidades de motorista/veículo atribuídos ativos ou de códigos idempotentes, os índices únicos podem falhar; resolver as duplicidades com a operação antes de iniciar a aplicação. Não há migração destrutiva automática nem deploy realizado.
- Não é necessária configuração de serviço externo para código de turno, persistência ou consulta de alertas. A aplicação requer a connection string/provedor que o projeto já usa e um usuário administrativo ativo para emitir códigos; geração e entrega segura do código ao motorista ainda são tarefas operacionais manuais.

### Fora do que está pronto para operação real

- Não há SMS, push, SignalR de alertas de emergência ou promessa de monitoramento humano contínuo.
- Jornada, horas e salário continuam fora do MVP até existir fonte oficial e regras de RH.
- A rota exibida permanece uma estimativa genérica, sem homologação para caminhões. Não é substituta de navegação profissional nem da validação do despachante.
- O escopo de `Intermediador` usa vínculo único `Usuario.ClienteId`; ainda não existe modelo de múltiplas empresas representadas, delegações ou permissões granulares.

### Verificação executada nesta etapa

- `dotnet test src/MglConnect.Tests/MglConnect.Tests.csproj --no-restore --verbosity minimal`: **75 aprovados, 0 falhos, 0 ignorados**.
- `dotnet build src/MglConnect.Web/MglConnect.Web.csproj --no-restore --verbosity minimal`: compilação concluída com **0 avisos e 0 erros**.
- Os testes cobrem código válido/inválido/expirado/reutilizado/cancelado, limite de tentativas, início concorrente, prévia sem turno, autorização de motorista/intermediador, pedidos idempotentes, alerta com localização ausente/desatualizada, reenvio, reconhecimento, falha de persistência, transições e atualização repetida de esquema legado SQLite.
- Não foram executados testes com navegadores reais nem com instâncias PostgreSQL/SQL Server. Essas validações continuam pendentes antes de qualquer piloto operacional.

## Provedores e APIs para ensaio

| Serviço | Como testar | Limite importante |
|---|---|---|
| Photon | [Demo pública](https://photon.komoot.io/) para poucas consultas com locais genéricos; [projeto open source](https://github.com/komoot/photon) pode ser hospedado pela equipe. | A demo pede uso razoável, pode limitar/bloquear e não oferece SLA. Não enviar endereço de cliente. |
| OSRM | [API pública de demonstração](https://router.project-osrm.org/) com coordenadas de teste; backend é [open source](https://github.com/Project-OSRM/osrm-backend). | O perfil é fixado no pré-processamento do grafo. A instância pública é best-effort e o perfil comum é automotivo; não é rota homologada para caminhão. |
| OpenStreetMap | Usar os dados e a atribuição oficial no [mapa de tiles](https://tile.openstreetmap.org/). | Os dados OSM são abertos; o servidor de tiles comunitário não é uma API ilimitada nem tem SLA. Seguir a [política oficial de tiles](https://operations.osmfoundation.org/policies/tiles/). |
| openrouteservice (ORS) | Criar conta na [HeiGIT](https://account.heigit.org/signup); o site do ORS informa que a conta inclui uma chave Standard gratuita. Testar com coordenadas públicas e verificar quota no painel. | No levantamento de 26/10/2026, o site institucional informou indisponibilidade temporária da página interativa, embora signup, painel e serviços/API continuem acessíveis. Confirmar quota e termos na conta. Não presumir que o perfil Standard roteie caminhões com gabarito real. |
| GraphHopper | [Plano e chave de teste](https://www.graphhopper.com/pricing/). A página oficial anuncia plano gratuito sem cartão; conferir os limites exatos no painel ao criar a conta. | Chave e limites do serviço hospedado; confirmar suporte a restrições da frota antes de qualquer piloto. |
| Valhalla | [API e documentação](https://valhalla.github.io/valhalla/api/route/api-reference/) e [demo comunitária](https://valhalla.openstreetmap.de/). | O motor é open source e pode ser hospedado, mas compute, dados, atualização e operação têm custo. A demo não deve ser tratada como serviço de produção. |
| Google Routes | [Preços e limites oficiais](https://developers.google.com/maps/billing-and-pricing/pricing) e [tráfego em polylines](https://developers.google.com/maps/documentation/routes/traffic_on_polylines). | Exige projeto, chave e faturamento; a tabela consultada mostra cotas gratuitas por SKU (por exemplo, Essentials e recursos Pro têm tetos diferentes). Configurar alertas de custo; tráfego visual avançado pode elevar a cobrança. |
| Nominatim público | [Política da instância oficial](https://operations.osmfoundation.org/policies/nominatim/). | Não recomendado para autocomplete contínuo ou geocodificação massiva de aplicativo. Hospedar instância própria se o uso exigir controle. |

### Sequência sugerida de testes

1. Repetir Photon + OSRM + tiles OSM com dados fictícios, uma solicitação por vez, medindo endereço encontrado, rota, latência e erros.
2. Obter chaves de teste de ORS e GraphHopper, comparar endpoint/direções em uma região piloto e verificar os limites no painel da conta. A ausência de cartão deve ser confirmada no processo de cadastro; não fornecer cartão ou credenciais ao código.
3. Medir a mesma rota com o perfil de veículo e restrições reais apenas se o provedor as aceitar explicitamente; comparar com percurso validado por um despachante.
4. Só então decidir se hospeda Valhalla/OSRM ou contrata Google/ORS/GraphHopper. Nenhuma alternativa substitui a validação legal e operacional da rota de caminhão.
