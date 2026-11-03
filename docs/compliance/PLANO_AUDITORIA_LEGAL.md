# Plano de auditoria legal e regulatória — M.G.L Connect

**Versão:** metodologia inicial
**Data de pesquisa preliminar:** 2026-11-03
**Status:** não é parecer jurídico, certificação nem declaração de conformidade. Nenhuma correção de software foi implementada por este plano.

## 1. Objetivo e limites

Estabelecer um processo verificável para identificar normas potencialmente aplicáveis às operações que o M.G.L Connect venha a apoiar. A análise deve separar o software acadêmico/protótipo de uma operação real de transporte e não presumir que o produto seja transportadora, embarcador, operador de frota, controlador ou operador de dados sem examinar contratos, finalidade, fluxos, papéis e realidade operacional.

Este plano não conclui que qualquer norma se aplica ao sistema ou a seus usuários. A aplicabilidade e a interpretação jurídica devem ser confirmadas por profissional habilitado e pelos responsáveis pela operação. Não afirmar conformidade total.

## 2. Vocabulário e estados

Classificar separadamente:

- **Obrigação legal candidata:** texto normativo identificado, ainda sujeito à confirmação de vigência, sujeito obrigado, fato gerador e escopo.
- **Boa prática recomendada:** controle de segurança/privacidade que não será rotulado como obrigação legal sem dispositivo e análise.
- **Requisito operacional/contratual:** obrigação decorrente de operação, contrato, política ou SLA, identificada como tal.
- **Funcionalidade implementada:** comportamento demonstrado no código/configuração revisados; não equivale a conformidade.
- **Evidência técnica:** teste, log sanitizado, configuração, documentação ou inspeção reproduzível associada à versão/commit.
- **Lacuna/risco:** evidência ausente, controle incompleto, dependência ou incerteza.

Estado de aplicabilidade por norma: `não avaliada`, `candidata`, `condicional`, `confirmada por parecer`, `não aplicável com justificativa`. Não usar “obrigatória” como conclusão antes da validação jurídica.

## 3. Método de avaliação

1. **Definir cenário real:** protótipo acadêmico, demonstração fictícia ou operação real; identificar pessoa jurídica, contratos, papéis, clientes, transportadores, motoristas, destinatários, regiões e tipos de carga.
2. **Desenhar fluxos:** para cada fluxo registrar ator, finalidade, dados, origem, destinatário, acesso, armazenamento, retenção, exclusão, integração, localização e transferência internacional.
3. **Delimitar aplicabilidade:** documentar fato que ativa ou afasta cada hipótese normativa; não inferir atividade remunerada, relação de emprego, responsabilidade de transportador ou papel de controlador apenas por haver módulo correspondente.
4. **Verificar fonte primária:** consultar texto consolidado e histórico de alterações em fonte oficial (Planalto, Diário Oficial da União, ANPD, ANTT, CONTRAN/SENATRAN). Registrar data, URL, versão, vigência, revogações, alterações e transição. O repositório `ctresb/leis` é índice/cópia de pesquisa, não autoridade operacional ou jurídica.
5. **Mapear sistema:** relacionar dispositivo candidato a funcionalidade e código por caminho, classe/método, configuração, endpoint/hub, entidade e teste. Confirmar no commit avaliado; não extrapolar a existência de um arquivo para afirmar que o controle funciona.
6. **Testar evidência:** identificar teste automatizado/manual, ambiente, versão, resultado e limitações. Separar teste histórico de execução atual; nunca usar sucesso histórico como validação recente.
7. **Avaliar risco:** registrar impacto, probabilidade, exposição, titulares, abrangência, severidade, mitigação, responsável e prioridade. Priorizar risco concreto, não rótulo jurídico genérico.
8. **Revisão humana:** submeter interpretação, papéis legais, bases, retenção, transferências e conclusões de aplicabilidade a assessoria jurídica e, quando pertinente, encarregado, segurança, privacidade, operação de transporte e engenharia.
9. **Acompanhar mudanças:** monitorar alterações normativas e decisões administrativas/judiciais relevantes; versionar a matriz e revalidar após mudança de produto, operação, contrato, base de dados ou norma.

## 4. Registro obrigatório por norma e requisito

Manter uma linha por dispositivo/requisito, não somente uma linha por lei:

| Campo | Conteúdo exigido |
|---|---|
| Norma/dispositivo | Identificação, artigo/parágrafo/inciso somente após confirmação na fonte primária |
| Fonte `ctresb/leis` | Link permanente a arquivo e commit consultados; ou “não localizado” |
| Fonte oficial | Link ao texto oficial/consolidado e, se necessário, ato de alteração |
| Versão e consulta | Data/hora, versão/commit, data de consolidação e cópia consultada |
| Vigência/alterações | Situação, vacatio, alterações, revogações e transições a verificar |
| Aplicabilidade | Fatos assumidos, justificativa, papéis/sujeitos e estado da avaliação |
| Funcionalidade | Fluxo concreto relacionado no M.G.L Connect e cenário operacional |
| Código | Arquivos, classes, métodos, rotas, entidades/configurações e commit examinados |
| Evidências | Testes e outros artefatos, versão, comando, resultado e limitações |
| Lacunas | O que não existe, não foi provado ou depende de informação externa |
| Risco/prioridade | Impacto, probabilidade, prioridade, responsável e mitigação proposta |
| Revisão especializada | Questões jurídicas/operacionais abertas, parecer necessário e data/status |

Não registrar informação pessoal real na matriz. Use identificadores de teste fictícios e evidências redigidas/minimizadas.

## 5. Trilhas prioritárias e mapeamento inicial

Os itens abaixo são **candidatos de pesquisa**, não conclusões sobre aplicabilidade ou obrigações. A pesquisa externa foi pontual e deve ser repetida antes de qualquer parecer ou decisão de produto.

### Cadastro preliminar de normas candidatas

Os registros abaixo refletem somente a consulta de 2026-11-03 ao commit `c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6` do acervo externo e às páginas oficiais acessíveis. “Não avaliada” significa que não há conclusão legal.

| Norma/dispositivo candidato | Versão/link `ctresb/leis` | Fonte primária e vigência | Aplicabilidade e justificativa preliminar | Código/evidência/testes | Lacuna, risco e prioridade | Revisão especializada |
|---|---|---|---|---|---|---|
| Lei nº 13.709/2018 (LGPD); verificar arts. 5–7, 18, 37, 46 e 48 conforme cada operação; conferir art. 4º, II, na análise do contexto acadêmico | [LGPD, arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/2018/lei-13709-2018-08-14.md). Metadados dizem “compilada” e baixada em 2026-10-13. | [Planalto](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm); conferir alterações, texto vigente e regulamentação ANPD na data de cada avaliação. Planalto não carregou nesta sessão. | Candidata se houver tratamento de dados pessoais em operação abrangida. A exclusão para certos fins acadêmicos não é automaticamente atribuída ao produto, demonstração ou futura operação; titularidade, papéis, finalidade e contexto são desconhecidos. **Estado: não avaliada.** | Candidatos: entidades `Usuario`, `Cliente`, `Pedido`, `Entrega`, `PosicaoGps`; serviços de GPS/POD e controllers/hubs relacionados. Testes existentes: `TenantIsolationTests`, `GpsPublisherAuthorizationTests`, `GpsHistoryHttpTests`, `PodReceiptTests`. Nenhum executado nesta etapa. | Definir papéis, finalidades/base por fluxo, transparência, retenção, direitos, compartilhamento, segurança e incidentes. **Prioridade inicial: alta**, sem afirmar infração. | Sim: assessoria de privacidade/jurídica deve determinar incidência, papéis, bases e deveres aplicáveis. |
| Lei nº 9.503/1997 (CTB); art. 1º para escopo do Código; dispositivos específicos de veículo, circulação, dimensões, pesos e restrições ainda a identificar | [CTB, arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/1997/lei-9503-1997-09-23.md). Metadados dizem “compilada” e baixada em 2026-10-13. | [Planalto — texto compilado](https://www.planalto.gov.br/ccivil_03/leis/l9503compilado.htm); confirmar atualizações e atos complementares. Planalto não carregou nesta sessão. | Candidata para operação real de tráfego/roteamento e configurações de veículos; aplicação a software que apenas apresenta rotas depende da função, promessa, uso e operação concreta. **Estado: não avaliada.** | `Models/Veiculo.cs`, `Services/RouteTrust/RestrictionEngine.cs`, `TripProfiles.cs`, `RouteTrustService.cs`, `Controllers/RouteAssessmentController.cs` e testes de rota. Testes não executados nesta etapa. | Não há correspondência validada entre perfis/cobertura do sistema e os requisitos jurídicos atuais de cada veículo/via. **Prioridade alta** antes de qualquer alegação de rota legal/homologada. | Sim: especialista em trânsito/transporte e jurídico; validação operacional por rota/veículo. |
| Lei nº 11.442/2007; art. 1º e art. 2º como pontos de delimitação de transporte rodoviário de cargas por conta de terceiros e mediante remuneração | [Lei 11.442, arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/2007/lei-11442-2007-01-05.md). Metadados dizem “compilada” e baixada em 2026-10-13, mas o texto inclui alteração atribuída à Lei 15.485/2026. | [Planalto — Lei 11.442](https://www.planalto.gov.br/ccivil_03/_ato2007-2010/2007/lei/l11442.htm); conferir Lei 15.485/2026 e regulamentação ANTT em fonte oficial. Planalto não carregou nesta sessão. | Condicional à exploração real de TRC por conta de terceiros e remunerado e ao papel da pessoa/entidade. Não aplicar automaticamente a protótipo acadêmico, fornecedor de software ou demonstração. **Estado: condicional/não avaliada.** | Módulos de pedido, entrega, motorista, veículo, frota e despacho. Código comprova funcionalidades existentes, não qualidade de transportador nem cumprimento de requisitos de registro. | Confirmar operação, contratos, remuneração, categoria e relação com RNTRC; versão consultada contém alteração recente a conferir. **Prioridade alta** se houver operação remunerada. | Sim: advogado de transporte e equipe responsável pela operação; confirmar registros e papéis. |
| Resolução CD/ANPD nº 15, de 24/04/2024, como candidata para incidentes de segurança | Arquivo individual em `ctresb/leis` não localizado nesta pesquisa; [repositório externo](https://github.com/ctresb/leis) consultado por HTTPS, sem clone. | [Referência ao DOU](https://www.in.gov.br/en/web/dou/-/resolucao-cd/anpd-n-15-de-24-de-abril-de-2024-556243024); conferir texto integral, alterações e orientações vigentes da [ANPD](https://www.gov.br/anpd/pt-br/). DOU/ANPD não carregaram nesta sessão. | Candidata se a entidade e o incidente estiverem no âmbito regulatório aplicável; sujeito, risco/dano relevante, critérios e prazo não foram analisados nesta pesquisa. **Estado: candidata, pendente de validação.** | `Operations/RecoveryCommand.cs`, middleware de diagnóstico, logging e configuração de hospedagem como locais para mapear. Nenhuma evidência de processo organizacional de incidentes foi validada. | Identificar plano de resposta, classificação, escalonamento, registros e responsáveis, sem inventar prazo/obrigação. **Prioridade alta** para preparar operação real. | Sim: jurídico/encarregado e segurança; validar gatilhos, prazos e comunicação oficiais. |

**ANTT e CONTRAN:** ainda não há nesta fase um registro individual de resolução por número/artigo. A consulta ao repositório externo não localizou arquivos específicos de resolução ANTT/CONTRAN, e o acesso a páginas oficiais específicas da ANTT falhou. Até localizar e validar os textos e suas alterações, esses tópicos permanecem na fila de pesquisa abaixo, não como requisito legal confirmado. Não inventar link de versão externa, artigo ou estado de vigência.

### 5.1 Proteção de dados e privacidade

**Norma candidata:** Lei nº 13.709/2018 (LGPD), conforme registro preliminar acima. O texto consultado contém a exceção do art. 4º, II, para certos fins acadêmicos; não presumir que o produto, dados de demonstração ou uma futura operação se enquadrem nela.

- Referências candidatas a conferir na fonte oficial: conceitos e dados pessoais; princípios/finalidades; hipóteses de tratamento; direitos dos titulares; registro das operações; segurança e incidentes. Os artigos exatos só serão fixados na matriz depois de validação da redação atual e do cenário.
- **M.G.L relacionado:** contas/perfis, cliente e destinatário, endereços, GPS, histórico de rota, POD/assinatura, PDFs/exports, logs, cookies/armazenamento offline, integrações e backups.
- **Arquivos iniciais:** `src/MglConnect.Web/Models/Usuario.cs`, `Cliente.cs`, `PosicaoGps.cs`, `Entrega.cs`, `Pedido.cs`; `Services/GpsIngestionService.cs`, `GpsHistoryAccess.cs`, `PodContent.cs`, `PodPdfService.cs`; `Controllers/Api/GpsController.cs`, `Controllers/GpsHistoricoController.cs`, `Controllers/EntregasController.cs`; `Data/MglDbContext.cs`.
- **Evidência/testes a localizar e executar:** inventário de dados e retenção; `TenantIsolationTests`, `GpsPublisherAuthorizationTests`, `GpsHistoryHttpTests`, `PodReceiptTests`; exportação e apagamento/anonimização, controle de acesso e backup. A mera existência dos testes não demonstra aprovação; seus resultados atuais não foram executados nesta etapa.
- **Questões:** controlador(es)/operador(es), finalidade e base por fluxo, transparência, retenção, direitos, compartilhamento, dados de crianças (se houver), transferência internacional e resposta a incidente. Avaliação jurídica e de privacidade necessária.

### 5.2 Localização e monitoramento de motoristas

Não presumir que localização seja automaticamente dado pessoal sensível em toda situação; localização vinculada ou vinculável a pessoa pode ser dado pessoal e o contexto pode aumentar o risco. Determinar necessidade, frequência, precisão, finalidade, visibilidade, período ativo, acesso de empregador/cliente, retenção e uso fora do turno. Avaliar também relação contratual/trabalhista e regras de transparência com especialista.

- **Arquivos iniciais:** `Controllers/Api/GpsController.cs`, `Controllers/GpsHistoricoController.cs`, `Services/GpsIngestionService.cs`, `GpsHistoryAccess.cs`, `GpsHistoryReader.cs`, `Hubs/TelemetryHub.cs`, `Hubs/TrackingHub.cs`, `Models/PosicaoGps.cs`, `Models/GpsArquivo.cs`, cliente offline `wwwroot/js/driver-gps.js` e `driver-gps-queue.js`.
- **Testes iniciais:** `GpsPublisherAuthorizationTests`, `GpsHistoryHttpTests`, `GpsOperationalHistoryTests`, `TelemetryHubAuthorizationTests`, `GpsRetentionTests`.
- **Lacunas a provar:** consentimento não deve ser assumido como base universal; documentar base/finalidade por cenário, minimização, pausa/fim do turno, separação de tenants, perfis de acesso, retenção e trilha de auditoria. Avaliação jurídica especializada necessária.

### 5.3 Dados de cliente, destinatário e POD/assinatura

Mapear nome, documento, endereço, telefone, assinatura, foto, localização e evidência de entrega, incluindo conteúdo embutido em PDF, fila offline, telemetria, logs e backups. Determinar se cada campo é necessário e qual papel/usuário pode consultar, exportar, corrigir ou excluir.

- **Arquivos iniciais:** `Models/Entrega.cs`, `Services/PodContent.cs`, `Services/PodPdfService.cs`, `Controllers/EntregasController.cs`, `Controllers/Api/EntregasApiController.cs`, `wwwroot/js/driver-pod-queue.js`, `pod-signature.js`.
- **Testes iniciais:** `PodReceiptTests`, `JavaScript/pod-queue.test.cjs`, `JavaScript/signature.test.cjs`, testes de autorização de entregas.
- **Lacunas:** validade jurídica da assinatura/evidência, integridade/autoria/data, retenção e força probatória dependem do método concreto, contrato e contexto; não declarar equivalência a assinatura qualificada nem suficiência jurídica sem parecer.

### 5.4 Acesso, isolamento por tenant, APIs e incidentes

Avaliar autenticação/autorização por endpoint e hub, checagem de tenant em leitura e escrita, mass assignment, enumeração de IDs, tokens, rate limit, logs sem excesso de dados, proteção contra abuso, gestão de sessão, segredo/chave, backup e resposta a incidentes.

- **Arquivos iniciais:** `Models/ITenantOwned.cs`, `Data/MglDbContext.cs`, `Program.cs`, `Services/ApiBearerAuthenticationHandler.cs`, controllers em `Controllers/Api/`, `Hubs/TelemetryHub.cs`, `Hubs/TrackingHub.cs`, middleware e `Operations/RecoveryCommand.cs`.
- **Testes iniciais:** `TenantIsolationTests`, `B01HttpAuthorizationTests`, `B01RouteEventAuthorizationTests`, `TelemetryHubAuthorizationTests`, `HostingInfrastructureTests`.
- **Lacunas:** não inferir cobertura total por filtros globais ou por testes existentes; criar matriz de rotas/atores/tenant e testar negações e concorrência. Mapear incidentes técnicos e fluxo organizacional com a regulamentação ANPD vigente. Especialista de segurança/privacidade deve revisar.

### 5.5 CTB, CONTRAN e restrições de veículos pesados

**Norma candidata:** Lei nº 9.503/1997 (CTB), conforme registro preliminar acima. A trilha deverá selecionar dispositivos apenas após definir tipos de veículo, combinação, massa/dimensões, via, rota e função do software.

**Resoluções CONTRAN:** não foi possível identificar nesta consulta a resolução atualmente aplicável a cada regra de massa, dimensão, sinalização, autorização especial ou veículo/composição. A página oficial de resoluções apresenta atos de 2026, portanto números antigos não devem ser tratados como vigentes sem conferir consolidação, alterações e revogações. Mapear restrição de rota e roteamento pesado como apoio à decisão, não garantia de legalidade ou segurança da rota.

- **M.G.L relacionado:** `Models/Veiculo.cs`, `Services/RouteTrust/RestrictionEngine.cs`, `TripProfiles.cs`, `RouteTrustService.cs`, `Controllers/RouteAssessmentController.cs`, respectivos testes de restrição/perfil.
- **Lacunas:** fonte autoritativa atual de cada limite, cobertura geográfica, qualidade/data dos dados, restrições temporárias, vias municipais/estaduais, sinalização e exceções. Validação por operador/autoridade e especialista de transporte é necessária; roteamento experimental permanece não homologado.

### 5.6 Transporte remunerado de cargas e ANTT

**Norma candidata:** Lei nº 11.442/2007, conforme registro preliminar acima. O escopo do art. 1º descrito no texto consultado é transporte rodoviário de cargas por conta de terceiros e mediante remuneração. O texto registra alteração de 2026 posterior à data de download indicada nos metadados; confirmar em fonte oficial. O escopo não deve ser atribuído a protótipo acadêmico ou software isolado sem comprovar operação e papel jurídico.

**ANTT/RNTRC:** identificar e validar a resolução vigente e suas alterações somente após definir se a entidade operadora é TAC, ETC, CTC, embarcador, plataforma/intermediário ou prestador de software. A página/versão individual de ato ANTT não foi recuperada nesta consulta; número e artigos ficam pendentes. Não assumir que sistema SaaS tenha obrigação de registro de transportador.

- **M.G.L relacionado:** entidades `Pedido`, `Entrega`, `Motorista`, `Veiculo`, fluxos de despacho, cadastro/gestão de frota e eventual integração documental.
- **Lacunas:** contratos, remuneração, quem contrata/transporta, RNTRC, seguros, documentos e tipo de carga. Parecer jurídico/regulatório especializado obrigatório antes de converter hipóteses em requisito do produto.

### 5.7 Limites de responsabilidade e homologação

Separar responsabilidade do fornecedor de software, operador de transporte, embarcador, transportador, motorista, provedores de mapa/rota e cliente por contrato e operação. Documentar limitações, dependências de dados externos, modo degradado, decisões automatizadas/assistidas, homologação por veículo/rota e comunicação de risco. Esta trilha é contratual, operacional e jurídica; não se presume resolvida por disclaimer de interface.

## 6. Fontes consultadas e rastreabilidade

Consulta preliminar realizada em 2026-11-03. Para congelar referência externa, links de GitHub abaixo fixam o commit `c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6` do repositório `ctresb/leis`; ainda assim, a fonte oficial prevalece.

| Documento | Texto de pesquisa em `ctresb/leis` | Fonte primária a conferir | Resultado desta consulta |
|---|---|---|---|
| Lei nº 13.709/2018 — LGPD | [arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/2018/lei-13709-2018-08-14.md) | [Planalto — Lei 13.709](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm); [ANPD](https://www.gov.br/anpd/pt-br/) | Markdown/metadata consultados; Planalto não foi recuperado pelo navegador nesta sessão. Artigos e vigência atuais pendentes de validação oficial. |
| Lei nº 9.503/1997 — CTB | [arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/1997/lei-9503-1997-09-23.md) | [Planalto — CTB compilado](https://www.planalto.gov.br/ccivil_03/leis/l9503compilado.htm); [índice oficial CONTRAN/SENATRAN](https://www.gov.br/transportes/pt-br/assuntos/transito/conteudo-Senatran/resolucoes-contran) | Texto/metadata consultados; Planalto não foi recuperado. Índice CONTRAN carregou, com atos de 2026; resolução específica aplicável ainda pendente. |
| Lei nº 11.442/2007 — TRC remunerado | [arquivo no commit consultado](https://github.com/ctresb/leis/blob/c9f21b4ab11aa0d7ca6fe861d75028e94219c2d6/federal/leis-ordinarias/2007/lei-11442-2007-01-05.md) | [Planalto — Lei 11.442](https://www.planalto.gov.br/ccivil_03/_ato2007-2010/2007/lei/l11442.htm); [ANTT](https://www.gov.br/antt/) | Art. 1º e metadados consultados; texto indica alteração de 2026. Confirmar fonte oficial e escopo atual. |
| Resolução CD/ANPD nº 15, de 24/04/2024 — incidente de segurança | Arquivo específico em `ctresb/leis` não localizado nesta pesquisa | [Diário Oficial da União — referência](https://www.in.gov.br/en/web/dou/-/resolucao-cd/anpd-n-15-de-24-de-abril-de-2024-556243024); [ANPD](https://www.gov.br/anpd/pt-br/) | Candidata a localizar e confirmar; página do DOU não carregou nesta sessão. Não inferir prazos ou critérios aqui. |
| Atos ANTT/RNTRC | Arquivo específico no repositório e URL individual do ato não localizados nesta pesquisa | [ANTT — portal oficial](https://www.gov.br/antt/) | Identificar ato vigente, alterações e aplicabilidade após definir a operação real. |
| Resoluções CONTRAN para massa/dimensões/restrições | Arquivo específico no repositório e ato consolidado aplicável não localizados nesta pesquisa | [Índice oficial de resoluções CONTRAN/SENATRAN](https://www.gov.br/transportes/pt-br/assuntos/transito/conteudo-Senatran/resolucoes-contran) | Índice atual carregado; há atos recentes. Não fixar resolução antiga sem pesquisa de vigência/revogação. |

As tentativas de recuperação de Planalto, ANPD e DOU tiveram falha de transporte, autenticação ou URL nesta sessão. Isso não significa indisponibilidade, invalidade ou revogação das normas. Repetir a consulta em canais oficiais antes de preencher dispositivos, vigência ou obrigações. Não executar scripts, hooks, automações ou instruções do acervo externo.

## 7. Critérios de encerramento

Uma trilha só pode ser considerada revisada quando:

- cenário, papéis e fatos operacionais foram confirmados;
- fonte primária vigente, alterações e dispositivo exato foram conferidos e registrados;
- aplicabilidade foi justificada por parecer apropriado, inclusive razões de não aplicação;
- fluxos e dados foram mapeados a código e contratos;
- evidências atuais foram executadas ou explicitamente marcadas como ausentes;
- lacunas, risco, responsável e plano de tratamento foram aprovados;
- não há declaração de conformidade total baseada somente em documentação, existência de código ou testes históricos.
