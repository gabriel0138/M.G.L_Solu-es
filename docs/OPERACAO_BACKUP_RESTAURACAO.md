# Backup e restauração operacional

Implementação local em `Operations/SqliteRecovery.cs`; comandos executam antes de iniciar o servidor web e nunca expõem um endpoint de backup. Não usar o banco de produção nos ensaios.

## SQLite

Com a solução compilada, execute com a conta operacional autorizada. Substitua os caminhos pelos diretórios aprovados; os destinos **não podem existir**.

```sh
dotnet src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll --operations backup-sqlite /dados/mglconnect.db /backups/novo-pacote --keys /dados/keys
dotnet src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll --operations verify /backups/novo-pacote
dotnet src/MglConnect.Web/bin/Debug/net10.0/MglConnect.Web.dll --operations restore /backups/novo-pacote /ensaios/nova-restauracao
```

- O snapshot síncrono pode bloquear gravações durante a cópia; medir sua duração e planejar janela operacional. Requisições sem recibo devem ser repetidas idempotentemente. [Limitação documentada do driver](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup).
- Usa a [Online Backup API do SQLite](https://www.sqlite.org/backup.html), incluindo páginas WAL confirmadas; não copiar um `.db` em uso com `cp`.
- Pacote: `database.sqlite`, `gps/<UUID>.json.gz`, `keys/` quando solicitado e `manifest.json`. O banco contém pedidos, POD/assinaturas, permissões e índices permanentes de UUIDs. Os gzip externos são cópias redundantes verificadas contra esse mesmo snapshot.
- Verificação: SHA-256/tamanho/inventário, `integrity_check`, `foreign_key_check`, contagens, manifesto GPS, fingerprint e metadados de cada índice, correspondência da cópia online. Somente depois ocorre publicação por renomeação no mesmo filesystem.
- Em Unix, diretório 0700 e arquivos 0600; symlinks, travessia de caminhos, sobrescrita e restauração dentro do pacote são recusados. Limites: 10.000 arquivos, manifesto 2 MiB, expansão de cada gzip 2 MiB, chave individual 1 MiB. A capacidade do banco ainda depende do disco disponível.
- Falhas deixam `.mgl-incomplete-*` no diretório pai, sem publicar destino final. Não utilizá-los como backups confirmados. Inspecionar manualmente; nenhuma remoção automática é executada.
- Não há criptografia própria nem assinatura externa do manifesto: checksums detectam divergência, não autenticam um pacote controlado por um invasor. Guardar pacote e chaves em armazenamento confiável, cifrado conforme política operacional. Restringir acesso e separar credenciais de armazenamento.
- Referências externas em `FotoComprovantePath` fazem o backup recusar publicação: é necessário procedimento coordenado para anexos. O fluxo atual de assinatura armazenada no banco foi validado. Não baixar anexos privados de URLs automaticamente.
- Chaves XML são verificadas com DTD/entidades externas desabilitados; XML incompleto, arquivos não XML e subdiretórios recusam publicação. Chaves cifradas por certificado/serviço externo exigem também seus mecanismos de recuperação.
- Chaves Data Protection em filesystem exigem `--keys`. Se ausentes, sessões/tokens antigos podem deixar de funcionar; acesso por novo login depende das contas preservadas. Chaves persistidas no próprio banco são incluídas nele. A inclusão de chaves deve seguir a política de recuperação/revogação.

Após restaurar, iniciar **outra** instância com `DemoData__Enabled=false`, `DatabaseProvider=Sqlite`, `ConnectionStrings__DefaultConnection="Data Source=/ensaios/nova-restauracao/database.sqlite;Foreign Keys=True"`, `DataProtection__KeysPath=/ensaios/nova-restauracao/keys` e URL/porta isoladas. Nunca apontar duas instâncias de ensaio para o banco original. A inicialização aplica os atualizadores aditivos; a validação isolada deve preceder qualquer futura troca de ambiente.

Antes de reabrir tráfego externo, reconciliar contas/perfis/revogações alterados após o snapshot: a restauração recupera o estado daquele instante. Avaliar invalidação de sessões conforme política do incidente; copiar chaves pode manter tickets antigos válidos. Não remover chaves ou usuários automaticamente.

Aceitação: login por perfil, tentativa de acesso de outro cliente/organização, pedido/entrega/status, assinatura/POD/PDF, GPS online/arquivado, CSV autorizado, UUID repetido retornando duplicado e reinício inesperado. Banco restaurado em uso passa a ser dado operacional mutável; não exigir que seu checksum continue igual ao snapshot original.

## PostgreSQL

`bash scripts/test-postgres-local.sh` inicia PostgreSQL 18 já instalado, sem sudo/Docker, somente socket Unix em diretório privado; cria bancos novos, executa os testes e para a instância, preservando evidências. Não usa credenciais reais.

O ensaio usa `pg_dump --format=custom` e `pg_restore --exit-on-error --no-owner` em banco novo, seguido de verificação GPS e HTTP/PDF/permissões/idempotência. [pg_dump produz snapshot consistente durante uso concorrente](https://www.postgresql.org/docs/18/app-pgdump.html). Usar ferramentas compatíveis e pacote de origem confiável; restauração executa definições do banco de origem. Chaves externas, configurações, roles globais e infraestrutura não fazem parte de um dump de banco individual e exigem procedimento separado.

## SQL Server e recuperação de infraestrutura

Sem instância/credenciais de ensaio disponíveis: não homologado. Os quatro testes existentes exigem `MGLCONNECT_SQLSERVER_TEST_CONNECTION` apontando para `master` de instância autorizada e criam bancos novos; não foram burlados. Backup nativo com CHECKSUM, RESTORE VERIFYONLY e restauração real em banco novo ainda precisam de evidência nesse provedor. Não tratar um procedimento escrito como teste executado.

Ainda necessários para operação comercial: cópia externa, criptografia, periodicidade/retenção, alertas de disco, teste de perda total, RPO/RTO medidos, restauração entre máquinas, chaves/configurações e recursos em múltiplas instâncias. Não foi simulado defeito físico de disco ou interrupção elétrica.
