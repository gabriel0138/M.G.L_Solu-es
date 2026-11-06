# Restauração no Windows 11

Este procedimento foi preparado em Kali Linux em 6/11/2026. **Windows, WSL2 e Docker Desktop não foram executados nesta migração.** As validações locais e o estado remoto estão no [relatório de consolidação](./CONSOLIDACAO_FINAL.md). Não formate antes de conferir o arquivo criptografado e a chave em destinos externos separados.

## Código público e ferramentas

Instale Git, VS Code e SDK .NET 10 (a execução Kali usa 10.0.403). Para os harnesses, use Node 24 compatível com WebSocket nativo e Chromium/Brave. Prefira WSL2 Ubuntu para reutilizar Bash/Python e os scripts existentes: no PowerShell administrativo, `wsl --install -d Ubuntu`. Reinicie quando solicitado. Instale ferramentas dentro do Ubuntu; o .NET instalado no Windows não substitui o SDK Linux. Mantenha o checkout no filesystem Linux, por exemplo `~/projetos/mgl-connect`, evitando `/mnt/c` para as jornadas.

```bash
mkdir -p ~/projetos
cd ~/projetos
git clone https://github.com/gabriel0138/M.G.L_Solu-es.git mgl-connect
cd mgl-connect
git branch --show-current
git log -1 --oneline
# Compare com o SHA remoto indicado no recibo final da migração.
dotnet --info
node --version
dotnet restore MglConnect.sln
dotnet build MglConnect.sln --no-restore
mkdir -p ~/.local/share/mglconnect/tmp
export TMPDIR="$HOME/.local/share/mglconnect/tmp"
export DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
MGL_LOCAL_PORT=5290 bash scripts/dev-local.sh
```

Abra `http://127.0.0.1:5290`. O script cria SQLite fictício exclusivo, em `~/.local/share/mglconnect/local-preview`, e mostra contas Development na página de login. Não importar banco operacional. Não abrir portas no roteador/rede. O script executa build e nunca deve apontar para uma pasta com banco preexistente sem marcador de prévia.

A solução está em `MglConnect.sln`: aplicação em `src/MglConnect.Web`, testes em `src/MglConnect.Tests`, ferramentas em `scripts`. ASP.NET MVC/Razor, EF Core, Bootstrap, Leaflet, Photon, OSRM, SignalR e Valhalla experimental continuam preservados.

## Recuperação do arquivo privado

Copie o arquivo `.tar.gz.gpg` e seu `.sha256` do armazenamento externo para uma pasta privada. Recupere a **chave em destino separado**. A chave gerada é um arquivo de senha aleatória, não uma chave de identidade GPG. Não a envie ao GitHub nem a imprima no terminal.

```bash
sha256sum --check MGL_CONNECT_MIGRACAO_*.tar.gz.gpg.sha256
umask 077
mkdir -p ~/mgl-restauracao
# Substitua os nomes pelos arquivos efetivamente recebidos.
# pipefail detecta falha da autenticação GPG, mesmo se tar tiver extraído arquivos.
set -o pipefail
gpg --batch --pinentry-mode loopback --passphrase-file /CAMINHO/PRIVADO/recuperacao.key \
  --decrypt MGL_CONNECT_MIGRACAO_DATA.tar.gz.gpg | tar -xzf - -C ~/mgl-restauracao
cd ~/mgl-restauracao
sha256sum --check SHA256SUMS
```

O nome com `DATA` é exemplo: use o nome exato do recibo. Nunca execute comandos/scripts recuperados sem revisar. A pasta `git/` contém bundle de todas as refs e metadados privados de recuperação. Para restaurar offline:

```bash
git clone --mirror git/all-refs.bundle ~/mgl-historico.git
git --git-dir="$HOME/mgl-historico.git" fsck --full
git clone --branch feat/mgl-functional-gps-mobile ~/mgl-historico.git ~/projetos/mgl-recuperado
```

O mirror é **privado**: inclui branches históricas, refs de sessão e conteúdos que não devem ser publicados. **Nunca usar push --mirror/--all nesse mirror para GitHub público.** O bundle restaura os commits; não depende dos caminhos absolutos das antigas worktrees. O diretório forense `git/common-private` preserva também objetos/reflogs/índices para recuperação avançada; não o coloque diretamente como `.git` do novo checkout.

Em `worktrees/NN`, consulte caminho, HEAD, status, patches e arquivos copiados. Para recuperar alterações em uma pasta separada baseada naquele HEAD, primeiro aplique `staged.patch` com `git apply --index`, depois `unstaged.patch` com `git apply`. Recupere os arquivos novos de `files/`, inspecionando nomes/conteúdo antes de sobrepor. Não aplique patches históricos automaticamente na main. A pasta `snapshot/` inclui a preservação anterior à consolidação e o resíduo de terminal que bloqueou o script.

## Dados locais opcionais

`sqlite/preview-deliveryN/` contém backups consistentes das oito prévias, marcador e chaves de Data Protection associadas. Não são bancos de produção nem contêm validação de GPS físico. Escolha uma única prévia, copie seu conteúdo para uma pasta privada no filesystem WSL e execute `MGL_LOCAL_DATA_DIR=/pasta/privada MGL_LOCAL_PORT=5290 bash scripts/dev-local.sh`. Verifique `PRAGMA integrity_check` e `foreign_key_check` antes. Restaurar o banco não reativa conexões SignalR nem comprovantes voláteis de aprovação: gere nova prévia/aprovação. Assinaturas/arquivos, quando externos ao banco, ficam na mesma pasta preservada. Não troque chaves entre prévias.

Configurações privadas preservadas são apenas para inspeção/restauração local; não transportar URLs operacionais para testes. O SDK e pacotes NuGet não estão no arquivo; `dotnet restore` requer internet/cache adequado. Backups anteriores preservados podem exigir suas próprias senhas antigas, que não são substituídas pela chave do arquivo final.

## OSRM e mapas

Instale Docker Desktop, habilite backend WSL2 e integração Ubuntu. Valide `docker info` dentro do WSL. O arquivo preserva `routing/osrm-delivery4` com o recorte OSM, manifesto de proveniência e arquivos MLD. Restaure essa pasta em `~/.local/share/mglconnect/osrm-delivery4`. A imagem oficial fixada por digest é obtida pelo script; rootfs e camadas locais foram excluídos por serem reconstruíveis.

```bash
bash scripts/local-osrm.sh serve
# Outro terminal:
bash scripts/local-osrm.sh probe
MGL_LOCAL_PORT=5290 MGL_DEMO_ROUTES=true \
 MGL_OSRM_BASE_URL=http://127.0.0.1:5007 MGL_LOCAL_ROAD_ENGINE=true \
 MGL_OSRM_ENGINE_VERSION=26.7.0 \
 bash scripts/dev-local.sh
```

Configure também `MGL_OSRM_DATASET_VERSION` e `MGL_OSRM_DATASET_REFERENCE` a partir do manifesto preservado e da [Entrega 4](../qa/ENTREGA_4.md), sem inventar proveniência. Para reconstruir MLD usando o mesmo OSM, use a imagem/digest e os comandos extract/partition/customize de `scripts/local-osrm.sh`. `prepare` pode consultar a fonte: não substitua o extrato histórico sem intenção explícita. Novos dados externos não garantem reprodução byte a byte da malha histórica.

O adaptador Photon pode ser habilitado com `MGL_PHOTON_ENABLED=true`; o endpoint público é externo e não recebe dados privados. Jornadas usam fixture local. Sem motor, o estado correto é rota indisponível. Tiles dependem da rede/provedor, mesmo com OSRM local. Dados OSM exigem atribuição OpenStreetMap/ODbL. A malha é pequena, central São Paulo; OSRM `car.lua` é genérico, sem homologação de caminhão. Valhalla permanece experimental: dados/config/tiles preservados em `routing/valhalla-delivery4`; consulte a Entrega 4 antes de reconstruir o motor, sem prometer equivalência com OSRM.

## Testes e diferenças de ambiente

```bash
# Sem conexões operacionais herdadas:
env -u MGLCONNECT_SQLSERVER_TEST_CONNECTION -u MGLCONNECT_POSTGRES_TEST_CONNECTION \
 dotnet test MglConnect.sln --no-build --no-restore
node --test src/MglConnect.Tests/JavaScript/*.test.cjs
```

Os scripts de browser ainda procuram o SDK Linux em `~/.local/share/mglconnect-dotnet10/dotnet`. Após instalar o SDK dentro do WSL, crie **somente se o caminho não existir** um link local para a instalação real em `~/.local/share/mglconnect-dotnet10` (descubra a raiz com `readlink -f "$(command -v dotnet)"`). Configure `MGL_CHROMIUM_PATH` com um Chromium/Brave Linux real; executar .exe Windows por esse harness não foi validado. Testes Linux em WSL não equivalem a Windows nativo.

Com OSRM loopback ativo, repetir o comando completo da [Entrega 9](../qa/ENTREGA_9.md) com `TMPDIR` no disco. Faça Chromium, Brave (`MGL_CHROMIUM_PATH`, `MGL_REQUESTER=broker`) e offline **sequencialmente**. Cada jornada cria aplicação/SQLite de teste próprios. PostgreSQL é opcional: instale servidor/ferramentas Linux e informe `MGL_POSTGRES_BIN` e `MGL_DOTNET_BIN` a `scripts/test-postgres-local.sh`; o caminho Kali `/usr/lib/postgresql/18/bin` pode variar. O script usa cluster/socket isolados e encerra-o ao final. SQL Server é opcional e seus quatro testes continuam pendentes; não usar conexão operacional.

GPS físico, câmera, suspensão Android e Windows permanecem não testados. Viewport e geolocalização emulada não comprovam hardware. Navegador/PWA não garante coleta com tela bloqueada. GPS sintético não pode alimentar dados de localização física; geometria OSRM e projeção de progresso são informações separadas da posição observada. Proibida orientação pesada homologada sem validação de restrições.

## Antes de formatar

Verifique no destino externo o **tamanho e SHA256** do arquivo, e execute descriptografia, manifesto e restauração Git/SQLite usando a chave recuperada de outro destino. Confirme também os documentos pessoais fora do escopo deste backup. Uma cópia no notebook, uma promessa de upload ou apenas o GitHub não bastam.
