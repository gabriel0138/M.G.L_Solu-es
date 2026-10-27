# 🚛 M.G.L Connect — Plataforma Logística Integrada 3PL & Carga Pesada

> **Projeto Integrado Multidisciplinar IV (PIM IV)**  
> **Curso Superior de Tecnologia em Análise e Desenvolvimento de Sistemas — UNIP**  
> **Empresa Fictícia:** M.G.L Logística Integrada S.A.  
> **Tema:** Desenvolvimento de Solução Tecnológica Corporativa com Gestão Web Administrativa, Portal do Cliente, planejamento de rotas, Proof of Delivery (POD), SignalR em Tempo Real e Banco de Dados Híbrido (SQLite / PostgreSQL).

---

## 🎨 1. Design System & Identidade Visual Oficial M.G.L

O projeto adota uma identidade visual moderna inspirada em SaaS logísticos de alto padrão:

| Token de Cor | Código Hex | Aplicação no Sistema |
| :--- | :--- | :--- |
| **Dark Primary** | `#0A1B2B` | Headers, navegação e superfícies operacionais |
| **Accent Principal** | `#12334A` & `#1D5B75` | Botões, cards e contrastes secundários |
| **Vibrant Highlight** | `#00C9B7` | **Teal Logístico**: percurso, progresso e ações principais |
| **Avisos / Trânsito** | `#FFB020` | **Amber**: avisos e estados que exigem atenção |
| **Sucesso / Concluído** | `#12C982` | **Emerald**: entregas concluídas e confirmações |
| **Fundo de Aplicação**| `#F4F6F9` | **Light Slate Neutro**: Superfície limpa com cards flutuantes (`box-shadow`) |

---

## 🚀 2. Módulos e Funcionalidades Principais

### 1. Painel Administrativo de Expedição (Backoffice)
- **Dashboard de Operações:** Indicadores em tempo real (KPIs de clientes, pedidos, frotas em trânsito e faturamento).
- **Gestão de Clientes:** Cadastro e edição com integração com a **API ViaCEP** para preenchimento automático.
- **Gestão de Pedidos:** Controle de ordens de serviço, pesos, cubagem e volumes expedidos.
- **Despacho & Atribuição de Frota:** Geração automática de **Código de Rastreio exclusivo** (`MGL-XXXXXXXXBR`), designação de motorista e caminhão pesado.

### 2. Área Exclusiva do Cliente (`/Cliente`)
- **Dashboard do Cliente Corporativo:** Visão consolidada de todas as cargas em trânsito, histórico de entregas e investimentos.
- **Meus Pedidos:** Tabela interativa com filtros dinâmicos por status e botão de cópia instantânea do código de rastreamento.
- **Detalhes da Carga:** Visualização transparente com Stepper de 5 estágios e conexão **WebSocket SignalR** para atualizações sem necessidade de recarregar a página (F5).

### 3. Comprovante Digital de Entrega (Proof of Delivery — POD)
- **Assinatura Digital Biométrica:** Canvas interativo integrado com suporte a mouse e touch screen para coleta da assinatura de quem recebeu o pacote.
- **Certificação de Recebimento:** Registro de nome completo do recebedor, documento (CPF/RG), data e hora exata de entrega.
- **Visualização Pública e Corporativa:** Exibição do carimbo de entrega digital no rastreamento e download/arquivamento do comprovante.

### 4. Planejamento e navegação (`/Roteamento`)
- Em desenvolvimento, Photon (demo pública) geocodifica endereços sem chave quando habilitado; em produção, prefira um serviço próprio ou configure um provedor contratado. Google Geocoding continua opcional.
- O OSRM é selecionado por `Routing:OsrmBaseUrl`; `appsettings.Development.json` aponta para a demo pública, limitada a uma solicitação por segundo e sem garantia de disponibilidade.
- O mapa de ruas apresenta nomes de vias e passos de navegação; a posição do motorista vem do GPS do dispositivo e os desvios confirmados pelo servidor solicitam recálculo.
- O OSRM público usa um perfil rodoviário genérico, não considera altura, peso, eixos, carga perigosa ou regras municipais e não é um roteador homologado para caminhões. A aplicação exibe esse aviso e não apresenta rotas fictícias quando o cálculo falha.
- O tráfego ao vivo é opcional: sem uma chave e faturamento habilitados para Google Routes API, a tela informa que não há dados de trânsito em tempo real.

### 5. Notificações e WebSockets em Tempo Real (SignalR)
- **Hub WebSocket (`/trackingHub`):** Os navegadores dos clientes se conectam ao canal do código de rastreio (`JoinTrackingGroup`).
- Ao adicionar qualquer novo checkpoint (via web ou aplicativo mobile do motorista), o evento é transmitido instantaneamente para a tela do usuário com animação e som de notificação.

### 6. Banco de Dados Híbrido (SQLite Local + PostgreSQL)
- **Ambiente de Desenvolvimento Local:** Executa em **SQLite** (`mglconnect.db`) sem depender da instalação de servidores locais.
- **Produção na Nuvem:** Detecta `POSTGRES_URL`, `DATABASE_URL` ou `POSTGRES_PRISMA_URL` e usa **Npgsql Entity Framework Core** com SSL obrigatório. Em Vercel, PostgreSQL é obrigatório porque o disco do contêiner é efêmero. A integração Neon no Marketplace conecta a um provedor PostgreSQL externo.
- **Proteção de cookies:** Quando PostgreSQL é usado sem `DataProtection:KeysPath`, as chaves do ASP.NET Core Data Protection são compartilhadas pela tabela `DataProtectionKeys`; com SQLite, o Docker Compose usa o volume persistente para mantê-las.
- **Contas demonstrativas:** Os dados e senhas demonstrativos são criados por padrão somente em `Development`. Para habilitá-los em uma implantação, configure `DemoData__Enabled=true` e senhas fortes em `DemoData__AdminPassword`, `DemoData__DriverPassword` e `DemoData__ClientPassword` antes da primeira inicialização. Não publique as senhas de desenvolvimento.
- **Script SQL Server Acadêmico (UNIP PIM IV):** O script `sql/banco_pim_sqlserver.sql` contém a Stored Procedure `sp_ConsultarEntregasMotorista` e a Trigger `trg_ImpedirAlteracaoAposEntregue`.

### Mapas, trânsito e publicação
- Photon e OSRM públicos são demos best-effort, sem SLA. O geocodificador Photon fica desligado por padrão fora de `Development`; não envie endereços reais de clientes para demos públicas em produção.
- O Leaflet é a biblioteca de exibição, não um provedor de mapas. Os tiles públicos OSM exigem a URL oficial, atribuição visível e cache; não oferecem SLA nem uso ilimitado. Para rastreamento em produção, use um provedor apropriado ou hospede os serviços.
- Google Geocoding e Routes exigem APIs habilitadas e faturamento; restrinja a chave no servidor e monitore quotas/custos. A API Routes fornece tempo estimado com tráfego, não uma camada visual de trânsito estilo Waze.
- OpenRouteService é uma alternativa para avaliação de rotas e exige chave de API e respeito aos limites da conta. Não foi selecionado para este teste sem cadastro: confirme as condições atuais do plano e a compatibilidade das restrições de caminhões antes de integrá-lo.

#### Matriz de suporte operacional

| Capacidade | Situação atual | Limite explícito |
| :--- | :--- | :--- |
| Solicitação de pedido pelo cliente | Implementada | O cliente autenticado solicita um pedido com coleta, destino, peso, tipo e dimensões da carga. |
| Despacho de pedido | Implementado com validações | Verifica pedido sem entrega, motorista/veículo ativos, capacidade nominal e atribuições ativas. As verificações de conflito são no nível da aplicação, não uma reserva transacional segura contra duas instâncias concorrentes. |
| Isolamento por empresa | Implementado no contexto de dados | Requer que usuários autenticados recebam o `TenantId` correto. Não substitui testes de autorização nem uma estratégia de migrations versionadas. |
| GPS e mapa de ruas | Parcial | O navegador pode fornecer a posição e Leaflet exibe tiles raster OSM; disponibilidade do GPS, cobertura dos tiles e precisão dependem do dispositivo e dos provedores. |
| Simulação de telemetria | Disponível apenas para Admin | Usa a geometria carregada da rota e é identificada como fictícia; não equivale a uma viagem real nem registra POD. |
| Rota de caminhão homologada | Não disponível | O OSRM configurado usa perfil automotivo genérico. Não valida dimensões, peso por eixo, carga perigosa, restrições locais nem autorização legal para o veículo. |
| Trânsito ao vivo | Parcial e opcional | Google Routes requer chave, APIs habilitadas e faturamento. A aplicação pode consultar tempo com `TRAFFIC_AWARE`, mas não renderiza uma camada colorida de trânsito como o Waze. |
| Rota com várias paradas / intermediadores | Não disponível | O modelo atual suporta uma entrega associada a um pedido e uma origem/destino; não otimiza a sequência de paradas. |
| Visualização 3D | Não disponível | Leaflet com tiles raster não oferece inclinação nem edifícios 3D. A opção 3D da navegação informa a indisponibilidade, em vez de simular esse recurso. |
| Publicação pública | Não realizada | Vercel/Neon, chaves, domínio e serviço remoto precisam ser configurados e validados antes de anunciar uma implantação. |

#### Comparação dos motores de rota

| Opção | O que oferece | Decisão para este projeto |
| :--- | :--- | :--- |
| OSRM | API de rotas eficiente; o perfil é aplicado no pré-processamento do grafo, não escolhido dinamicamente por cada pedido. | Já usado apenas para demonstração com perfil genérico. A demo pública não tem SLA e não deve orientar caminhões. Um perfil próprio exigiria dados, processamento, hospedagem e validação independente. |
| Valhalla | Motor open source que pode ser hospedado pela operação e configurado por modelos de custo e opções de rota. | Candidato para uma prova de conceito futura; ainda não foi instalado, avaliado com dados brasileiros ou aprovado para restrições da frota. |
| openrouteservice | API de direções com opções e modelos de roteamento documentados; uso hospedado depende de chave, quotas e condições atuais da conta. | Não integrado. Antes de escolher, confirmar modalidade de veículo, restrições HGV, limites, termos e cobertura necessários para a frota. |
| GraphHopper | API de roteamento hospedada e configuração de rotas conforme a oferta disponível. | Não integrado nem cotado. É necessário verificar perfil de veículo, limites e custos atuais; não presumir que uma quota gratuita atenda a operação. |
| Google Routes | Rotas e estimativas com tráfego; a documentação classifica recursos de tráfego avançado em níveis de cobrança próprios. | Integração opcional somente para estimativa de tráfego. Não é uma camada de mapa estilo Waze e não foi validada como roteador pesado homologado. |

Nenhuma linha desta comparação representa aprovação de segurança rodoviária. Para escolher um motor pesado, testar dimensões e restrições reais da frota em uma região piloto, conferir a qualidade das tags OSM locais e obter validação operacional/legal antes de usar rotas em produção. Não envie coordenadas ou endereços reais a endpoints públicos de demonstração.

#### Visual 3D e dados de trânsito

O mapa atual usa Leaflet e tiles raster. Inclinar a câmera ou desenhar edifícios 3D requer uma biblioteca compatível com dados vetoriais, estilo e fonte de edifícios configurados; MapLibre oferece controles de câmera, mas não fornece esses dados por si só. O modo 3D permanece desativado até essa infraestrutura ser escolhida. Do mesmo modo, receber uma duração com tráfego não equivale a possuir uma camada visual de congestionamento; os trechos coloridos dependem de dados e permissões específicos do provedor.

- Referências: [OSRM API](https://project-osrm.org/docs/v5.24.0/api/), [perfis OSRM](https://github.com/Project-OSRM/osrm-backend/blob/master/docs/profiles.md), [Valhalla Route API](https://valhalla.github.io/valhalla/api/route/api-reference/), [openrouteservice Directions](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/), [GraphHopper Routing API](https://docs.graphhopper.com/openapi/routing), [Google Routes — polylines com trânsito](https://developers.google.com/maps/documentation/routes/traffic_on_polylines) e [MapLibre Map API](https://maplibre.org/maplibre-gl-js/docs/API/classes/Map/).

- Vercel aceita imagens Docker como Functions, mas isso não torna o banco parte da Vercel: use PostgreSQL externo (por exemplo, Neon) e valide limites do plano, WebSockets/SignalR, Data Protection, uploads e coordenação de estado antes do deploy.
- O projeto usa .NET 10 LTS. O Dockerfile usa o runtime .NET 10.
- O serviço de rotas não deve ser considerado seguro ou homologado para caminhões até integrar um provedor que aceite dimensões e restrições da frota.
- Vercel Container Runtime executa o `Dockerfile` da raiz do projeto e encaminha tráfego para a porta `PORT` (80 por padrão). A plataforma e o Neon precisam ser configurados na conta antes do deploy.
- A Vercel oferece WebSockets em beta. As notificações SignalR usam estado local da instância e não têm backplane; múltiplas instâncias não garantem broadcast entre si. Para uso consistente em escala, configure um backplane SignalR ou hospede a aplicação em um serviço com conexões persistentes.

O `DatabaseSchemaUpdater` aplica apenas a atualização aditiva necessária aos novos campos de endereço/dimensões do pedido em bancos existentes e preenche `TipoCarga` nulo com `Carga Geral Seca` para compatibilidade dos registros antigos. Ele não substitui migrations versionadas; mudanças futuras de esquema devem ser planejadas e validadas por provedor de banco.

---

## 🔑 3. Credenciais de Acesso de Demonstração

Estas credenciais e contas são exclusivas do ambiente local `Development`; elas não
são criadas nem exibidas em produção. Para um ambiente demonstrativo publicado, defina
senhas fortes nas variáveis `DemoData__*` descritas na seção de publicação.

| Perfil | Usuário | Senha | Acesso e Visão |
| :--- | :--- | :--- | :--- |
| **Administrador** | `admin` | `admin123` | Backoffice total: Dashboard, Clientes, Pedidos, Despachos e POD |
| **Motorista** | `carlos.motorista` | `motorista123` | Rotas atribuídas e confirmação de entrega |
| **Cliente Corporativo** | `cliente.demo` | `cliente123` | Portal do Cliente (`/Cliente`) focado nas cargas da Alpha Tech |

---

## 📦 4. Códigos de Rastreio para Teste (Estilo Mercado Livre)

Acesse a página inicial ou `/Rastreio`:
- **`MGL-BR100201`** — Carga em trânsito com 4 checkpoints e caminhão Scania R450 6x2 vinculado.
- **`MGL-BR200302`** — Encomenda entregue com **Comprovante Digital de Entrega (POD)** assinado por *Maria Oliveira Santos* e conferência de documento.

---

## ⚡ 5. Como Executar a Aplicação

```bash
# Requer o SDK .NET 10
# 1. Compilar a solução
dotnet build

# 2. Executar os testes automatizados
dotnet test

# 3. Executar o servidor web
dotnet run --project src/MglConnect.Web

# 3. Acessar os módulos no navegador:
# - Página Inicial:       http://localhost:5000/
# - Rastreio Estilo ML:   http://localhost:5000/Rastreio
# - Roteamento Pesado:    http://localhost:5000/Roteamento
# - Portal do Cliente:    http://localhost:5000/Cliente
# - Painel Administrativo: http://localhost:5000/Dashboard
# - Documentação API:     http://localhost:5000/swagger
```

---

## 🐳 6. Execução via Docker Container

```bash
docker-compose up -d --build
```
A plataforma estará acessível na porta `5000`.

O SQLite é inicializado sem apagar os dados existentes. Os dados demonstrativos são
inseridos somente quando todas as tabelas estão vazias. O projeto de testes faz parte
da solução e é executado por `dotnet test`.

Para publicar na Vercel, conecte o projeto a um repositório Git isolado, selecione a
pasta `projetos` como diretório raiz e conecte um banco Neon. Configure `DATABASE_URL`
e, se precisar testar as contas demonstrativas, as variáveis `DemoData__*` descritas
acima com senhas fortes. Sem a URL PostgreSQL, a aplicação recusa iniciar na Vercel
para evitar perder dados no armazenamento efêmero.

Para a API móvel, obtenha o bearer token em `POST /api/auth/login` e envie-o no
cabeçalho `Authorization: Bearer <token>`; o token tem validade de oito horas.
No Docker, as chaves de proteção desses tokens ficam no volume persistente `mgl_data`.
