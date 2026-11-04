# Entrega 2 — circuito controlado e cockpit mobile

> Evolução atual de estados, custos e fontes viárias: [Entrega 3](ENTREGA_3.md). Este documento preserva os resultados da Entrega 2.

Base preservada: `32d1057` e `21d14a5`. Implementação: `01a6f74`; regressão de reconexão: `a21844c`, na mesma branch `feat/mgl-functional-gps-mobile`. Sem alterações em main, PR #1, outras worktrees, push ou deploy.

## Abrir a demonstração

A prévia preparada nesta sessão usa loopback em `http://127.0.0.1:5284`, SQLite sintético próprio e viagem 1 com turno iniciado. Entre pela conta de motorista exibida no login de Development, abra a navegação da viagem e pressione **Iniciar GPS sintético**. **Desvio de ensaio** seleciona o circuito alternativo. **Acompanhar** alterna o seguimento da posição. A assinatura e a emergência ficam no painel inferior; diagnósticos permanecem recolhidos.

Para iniciar outra instância descartável, a partir da raiz, usando uma porta livre:

```sh
MGL_LOCAL_DATA_DIR=$(mktemp -d /tmp/mgl-demo-route-XXXXXX) \
MGL_LOCAL_PORT=5285 MGL_DEMO_ROUTES=true bash scripts/dev-local.sh
```

Nesta instância nova, o administrador emite o código de turno da entrega e o motorista inicia o turno pelo formulário existente. O modo padrão, sem `MGL_DEMO_ROUTES=true`, mantém o serviço real e suas verificações. Com provedores desativados, mostra o estado sem rota.

## Implementação e limites do cenário

- [ControlledRouteDemo](../../src/MglConnect.Web/Services/ControlledRouteDemo.cs) fornece circuito autorado com sete vértices, origem/destino, pontos intermediários e alternativa com cinco vértices. **Não é geometria extraída de ruas reais e não geocodifica o endereço do pedido.** O fundo OSM não transforma essa fixture em uma rota viária operacional.
- O opt-in exige simultaneamente Development, DemoData habilitado, SQLite com caminho absoluto e marcador de prévia local. Não modifica `RouteTrustService.NavigationAllowedAsync`: planos sintéticos continuam sem autorização operacional, aprovação ou homologação de caminhões.
- A distância soma os segmentos por Haversine. O ETA do circuito usa modelo nominal de 20 km/h; não usa trânsito ao vivo. Amostras são reproduzidas em cadência acelerada de 1,2 s, com velocidade sintética nominal, não velocidade física medida. Frações de minuto aparecem como **<1 min**, sem anunciar zero antes do destino.
- [ControlledDemoNavigation](../../src/MglConnect.Web/Services/ControlledDemoNavigation.cs) aceita índices de amostras, não coordenadas arbitrárias. Revalida conta, tenant, atribuição e turno; POST exige antiforgery. Reaproveita ingestão/idempotência e grava `Origem=Simulacao` com captura informada.
- Amostras sintéticas **não atualizam o snapshot GPS real** nem entram nas projeções GPS do cliente/público. A operação usa projeção explicitamente marcada como simulação, somente quando não há snapshot real; real e sintético não são fundidos.
- O evento privado `ReceiveDemoState` leva plano e posição aos assinantes autorizados de operação/motorista. O cliente continua recebendo os estados logísticos e o POD autorizado. Recarga/reconexão lê o estado persistido; a alternativa é reconstruída da última amostra do circuito, inclusive após reiniciar o servidor.
- O botão de desvio seleciona uma alternativa pré-definida que parte da posição do cenário. **Não é prova de recálculo de ruas arbitrárias nem relaxa o detector operacional de saltos/desvios.** GPS físico nunca avança o progresso da fixture. Não há map matching automático.

## Defeitos corrigidos

| Antes | Depois / evidência |
|---|---|
| Sem geometria na demonstração anterior | Plano controlado integrado ao serviço existente; veículo percorre o circuito e chega a 100% no navegador. |
| Aviso longo e repetido, mapa pequeno | Cabeçalho curto, métricas compactas, ações lado a lado e diagnósticos recolhidos. Em paisagem, painel lateral e mapa à direita. |
| Botão sintético sem fundo suficientemente legível sobre tiles | Botão primário teal da identidade existente. |
| Mapa vazio antes de qualquer origem GPS/rota | Diagnóstico confirma zero tiles, zero falhas e posição aguardando: Leaflet ainda sem centro, deliberadamente. A mensagem pede rota/GPS; não se inventa centro geográfico. |
| Falha de tiles podia ser apagada pelo total de sucessos antigos | Estado por lote de carregamento, com contadores totais separados. Nove tiles bloqueados no ensaio mantiveram erro visível; rota e marcador permaneceram; redraw recuperou carregamento. |
| ETA arredondava para zero em trecho curto | Mantém fração para cálculo e apresenta `<1 min` antes do destino. |

## Testes executados

Ambiente: Chromium headless real via CDP, Node.js, SDK .NET 10 e Kestrel em loopback; SQLite/perfis/chaves privados e descartáveis. GPS do aparelho nos ensaios usa override sintético do Chromium. Não houve banco operacional, serviços pagos, containers, PostgreSQL/SQL Server ou deploy. Assets públicos e tiles usam as dependências de rede já presentes na aplicação.

| Comando na raiz | Resultado |
|---|---|
| `bash scripts/dev-local.sh` pelos harnesses | Restore/build aprovados; Debug sem warnings/erros; seed e instância loopback próprios. |
| `dotnet test MglConnect.sln --no-build --no-restore --verbosity minimal` | **633 aprovados, 0 falhos, 9 ignorados**, de 642; os nove exigem bancos externos. Variáveis de conexão SQL Server/PostgreSQL removidas do processo. Última execução: 1 min 13 s. |
| `dotnet test MglConnect.sln --no-restore --filter FullyQualifiedName~ControlledRouteDemoTests --verbosity minimal` | Sete novos testes C# aprovados: gates, geometria/estimativas, CSRF, isolamento, duplicação, atraso, snapshot real intacto, projeção do cliente, reinício e falhas. Incluídos na suíte completa. |
| `node --test src/MglConnect.Tests/JavaScript/*.test.cjs` | **111 aprovados, 0 falhos**; inclui demo sem confiança operacional, separação de GPS físico, canal dedicado e ETA de trecho curto. |
| `node scripts/logistics-journey-browser-check.cjs` | Jornada sem provedor aprovada; GPS do aparelho continua disponível, sem distância/ETA inventados. |
| `MGL_DEMO_ROUTES=true node scripts/logistics-journey-browser-check.cjs` | Jornada controlada completa aprovada: cliente cria pedido → aprovação/despacho → turno → circuito → GPS/progresso → desvio/retorno/alternativa → 100% → operação → assinatura/POD → recuperação de recibo perdido → PDF/estado final do cliente. |
| `MGL_DEMO_ROUTES=true MGL_DEMO_ROUTE_FAILURE=provider node scripts/logistics-journey-browser-check.cjs` | Falha do provedor aprovada: botões sintéticos desabilitados, sem geometria/ETA/distância; GPS do aparelho ainda recebido. |
| Mesmo comando com `MGL_DEMO_ROUTE_FAILURE=missing` | Ausência de rota aprovada no navegador. |
| `node scripts/gps-offline-browser-check.cjs` | Aprovado: IndexedDB, GPS atrasado, parada/reinício real, offline reload, recibo perdido, UUID/horário preservados e ausência de duplicação. A fila GPS real continua separada da reprodução sintética. |
| `git diff --check`, `git diff --cached --check`, `bash -n scripts/dev-local.sh`, sintaxe JS e Gitleaks do diff staged | Aprovados; nenhuma ocorrência no diff de implementação. |

Os harnesses foram ampliados, sem criar runner E2E paralelo. A jornada monitora HTTP locais >=500 e exceções JS não tratadas: nenhuma nas execuções finais. Falhas de rede injetadas intencionalmente são esperadas.

Cobertura do cockpit controlado: **320, 360, 390, 428, 768 e 1024 px**, **844×390** e **390×500**, claro/escuro; mapa útil, overflow, área de toque, hit testing de GPS/seguir/POD/emergência, resize de container sem resize da janela, duplicação, progresso e retomada. O navegador fecha o WebSocket nativo, exige novo connectionId/reentrada no grupo autorizado e restaura a posição persistida após limpar o estado visual transitório. Viewports curtos podem rolar para manter controles e diagnósticos alcançáveis. Nos estados finais, também 1280/1440 px e verificações de nomes acessíveis/foco. Testes C# cobrem outro cliente/tenant; o percurso de intermediador no navegador permanece pendente.

## Evidências e pendências

Evidências fora do Git: `$HOME/.local/share/mglconnect/evidence/delivery2-20261104/`, com **187 capturas**, logs, diff e manifest. `before/` preserva capturas reais da entrega 1; `demo/driver/` contém circuito/progresso/alternativa/100%/paisagem/tiles bloqueados; `no-route/`, `provider-failure/`, `missing-route/` e `offline/` registram os demais cenários. Nenhum banco, screenshot ou chave entrou no Git.

Durante a ampliação do harness, dois erros do próprio teste foram corrigidos: CSS `w-100 !important` impedia o resize usado pelo teste; retorno circular do Leaflet excedia a serialização CDP. A regressão agora muda a largura efetiva com prioridade explícita e não serializa o objeto de mapa. As assertions de geometria e largura continuam exigidas.

Pendente: aparelho físico/teclado/safe area reais, GPS em estrada, roteamento de ruas reais e restrições pesadas, intermediador no navegador, todos os CRUDs administrativos, indisponibilidade completa dos CDNs, 2560 px e provedores SQL Server/PostgreSQL. Não se promete GPS com tela bloqueada. O controle sintético requer página aberta e pausa no erro; não substitui a fila GPS real offline.

**Recomendação para a entrega 3:** conectar um provedor rodoviário local isolado a uma malha de teste documentada, substituir o circuito autorado por geometria de ruas verificável e exercitar desvio/recálculo sobre essa malha. Manter o bloqueio de orientação pesada sem evidência própria; validar também toque e teclado em aparelho físico.
