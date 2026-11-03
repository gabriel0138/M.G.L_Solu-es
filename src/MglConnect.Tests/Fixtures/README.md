# Fixture de geometria — Etapa 7A

`route-trust-public-landmarks.json` contém a geometria gravada de uma consulta OSRM de automóvel entre dois marcos públicos de São Paulo (MASP e Praça da Sé), sem endereço de cliente ou posição real de motorista. Capturada em 29/10/2026, com 189 vértices preservados; SHA-256 da resposta original: `b5a32e785d9e6371f30233363d596dc82c1d62ed64c9577131adcadfdf999c99`.

A referência HTTP está no próprio JSON. A captura **não identifica a versão da base OSM e não homologa rodovias/caminhões**. Dados de mapa: [OpenStreetMap contributors / licença e atribuição](https://www.openstreetmap.org/copyright). Serviço/protocolo: [OSRM](https://project-osrm.org/), [política do serviço de demonstração](https://routing.openstreetmap.de/about.html).

As restrições adicionadas pelos testes são SINTÉTICAS, com fontes `fixture://`, validade, território, hash de coordenadas e intervalos de arestas explícitos. Não representam limites reais de pontes/vias de São Paulo. O adaptador de arquivo somente funciona em Development, exige `Synthetic=true` e não libera orientação real, inclusive quando o conjunto sintético é classificado como validado no seu escopo de ensaio. Os testes de serviço podem injetar um adaptador controlado para verificar o ramo de validação; isso não ativa um fornecedor em produção.

Nenhuma geometria é fabricada ligando origem/destino como fallback. Falha de provedor ou associação não comprovada resulta em indisponibilidade/desconhecido. `__FIXTURE_CAPTURE__` é um marcador explícito de fixture: o adaptador Development o substitui pelo hash dos vértices gravados, sem usar proximidade espacial como prova de restrição real.

## Contrato Valhalla — Etapa 7B.1

`valhalla-contract-synthetic.json` é um envelope **sintético** de resposta `/route` nativa (polyline6), construído com a geometria pública OSRM já descrita acima. Não foi capturado de uma instância Valhalla e não comprova o comportamento de um grafo `truck` real. Os testes HTTP controlados validam serialização, unidades, parser, falhas e bloqueio de orientação; não são homologação viária.

O inventário real e separado está em `../../MglConnect.Web/Services/RouteTrust/Data/brazil-pilot-osm.json`. Conserva IDs, versões, timestamps e nós ordenados OSM de 49 vias numa bbox pública do entorno do MASP, com atribuição ODbL. Não é associado automaticamente à geometria OSRM/Valhalla. O importador `scripts/import-routing-coverage.py` só lê XML local, limita tamanho e recusa sobrescrever a saída; não realiza consultas de rede.
