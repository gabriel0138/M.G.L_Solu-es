# Respostas viárias capturadas — regressão, não serviço atual

Capturas de 2026-11-05 UTC (2026-11-04 em São Paulo), com marcos públicos MASP e Praça da Sé. A jornada E2E da Entrega 4 consulta uma instância **local real**, não estas respostas.

- `central-sp-20261105.response.json`: OSRM 26.7.0, perfil `car.lua`/`driving`, MLD; 134 vértices, 3107,7 m, 357,9 s, duas paradas localizadas. `alternatives=true` solicitado, nenhuma alternativa retornada neste par.
- `central-sp-20261105.dataset.json`: quatro URLs da API OSM, área, instante de coleta e SHA256 do XML usado pelo OSRM. Apenas vias `highway`, seus nós referenciados e relações completas de restrição disponíveis no recorte. A borda é incompleta.
- `valhalla-truck-central-sp-20261105.response.json`: Valhalla 3.9.1, `truck`; mesmas entidades OSM, ordenadas antes da construção do grafo. 3,388 km e 299,223 s. Perfil fictício: 4,2 m × 2,6 m × 18,6 m, 28,5 t, 8 t/eixo, cinco eixos, sem carga perigosa. **Não é veículo validado.**

Origem declarada longitude/latitude: `-46.6559,-23.5614`; destino `-46.6333,-23.5505`. A resposta conserva os pontos localizados pelo motor separadamente dos declarados; não são posições GPS.

Dados © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright), [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/). Nenhuma captura comprova inventário completo de restrições, legalidade de circulação ou homologação para caminhões. A fixture histórica anterior continua em `../route-trust-public-landmarks.json`, sem ser promovida a resposta atual.
