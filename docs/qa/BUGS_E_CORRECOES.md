# Defeitos reproduzidos e correções — 2026-11-04

> Registro da entrega 1. A implementação, validação e limitações atuais de circuito/GPS/cockpit estão na [Entrega 2](ENTREGA_2.md).

Base: `01bcd8299a2c1108ee8de89c6743531ec32a92a5`. Branch: `feat/mgl-functional-gps-mobile`.

| Defeito | Reprodução e causa | Correção | Evidência de regressão |
|---|---|---|---|
| GPS coberto pelo painel inferior | Chromium 360×800 e 390×844: botão centralizado verticalmente ficava sob painel de maior z-index. A captura de 390×844 não mostra o botão. | Grid reserva espaço entre instruções e painel inferior; mapa e controles ocupam a faixa visível, mantendo também o marcador fora dos painéis. | Hit testing com `elementFromPoint`, clique CDP real e confirmação GPS no servidor; oito larguras e dois temas. |
| Sobreposição em paisagem/altura reduzida | 844×390: painel inferior começava em y=-57,47; em 390×500 começava em y=17,02, sobrepondo instruções. | Altura cresce com conteúdo; rolagem permite alcançar GPS, POD e emergência mesmo com detalhes offline expandidos. Coluna `minmax(0,1fr)` evita expansão pela largura intrínseca. | 844×390 e 390×500 com detalhes abertos: controles recebem ponteiro após rolagem. |
| Marcador corta a curva por uma diagonal fictícia | Fixture Leaflet em L: amostras (-23.5,-46.6) e (-23.49,-46.59). Após 450 ms, posição desenhada era (-23.494275,-46.594275), ausente das amostras e fora da linha da fixture. Causa: interpolação linear por 1,8 s. | Marcador recebe a coordenada observada imediatamente, sem encaixe na rua nem coordenadas intermediárias inventadas. Geometria planejada e projeção de progresso permanecem separadas. | Novo teste JS de amostras em curva/desvio; navegador verifica a coordenada desenhada e captura a fixture explicitamente sintética. Movimento passa a ocorrer por amostras, sem animação do marcador. |
| Leaflet não acompanha resize exclusivo do container | No Chromium com o tracker da base 01bcd82, container reduzido de 350 para 280 px: Leaflet continuava informando 350 px. Invalidação dependia do resize da janela; o tracker não observava o container. | `ResizeObserver` invalida tamanho em frame agrupado, sem deslocar o mapa; observer/frame são liberados no encerramento. | Fixture no navegador reduz só o container para 280 px: `map.getSize().x === 280`, sem resize da janela. |

Arquivos de implementação: [view de navegação](../../src/MglConnect.Web/Views/Entregas/Navegar.cshtml), [estilos do cockpit](../../src/MglConnect.Web/wwwroot/css/command-cockpit.css), [tracker](../../src/MglConnect.Web/wwwroot/js/tracker.js).

Regressões: [JavaScript](../../src/MglConnect.Tests/JavaScript/tracker.test.cjs), [navegador motorista](../../scripts/driver-navigation-browser-check.cjs), [jornada integrada](../../scripts/logistics-journey-browser-check.cjs).

## Correções no próprio teste

- O despacho procurava um nome anterior à sanitização do seed. Agora seleciona o motorista fictício vigente, mantendo a exigência de recurso conhecido.
- O estado POD pendente também aparece ao enfileirar, antes da resposta HTTP. O teste aguarda a interceptação efetiva antes de verificar o recibo perdido.
- A fixture de geometria não abre uma assinatura SignalR sem entrega. Seu mapa e posições são exclusivamente locais e sintéticos.
- Durante a ampliação dos testes, corrigidos contexto público ainda em `about:blank` e uso de endpoint bearer em uma verificação de cookie. A verificação final usa a API GPS que aceita explicitamente cookie e testa 401/404, sem alterar autenticação da aplicação.

## Limites

Não foi implementado map matching nem movimento estimado sobre a rua. A remoção da diagonal não comprova a rota viária operacional de ponta a ponta. O provedor de rotas ficou desativado; a tela real manteve orientação suspensa e ausência de estimativas. Não houve homologação para caminhões nem teste de GPS físico, navegador móvel físico ou coleta com tela bloqueada.
