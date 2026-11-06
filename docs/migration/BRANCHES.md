# Inventário de branches antes da consolidação

| Branch | HEAD | Relação com funcional | Preservação |
|---|---|---|---|
| `diagnostic/mgl-http500-p0` | `a888d56414fb` | Ancestral; código já incluído | Bundle completo privado |
| `feat/mgl-functional-gps-mobile` | `ea3844f16ae7` | Ancestral; código já incluído | Bundle completo privado |
| `fix/mgl-decimal-contract-p0` | `6966b11e2e12` | Ancestral; código já incluído | Bundle completo privado |
| `integration/mgl-unificado-2026-11` | `01bcd8299a2c` | Ancestral; código já incluído | Bundle completo privado |
| `main` | `d8a54d38342a` | Ancestral; código já incluído | Bundle completo privado |
| `publication/mgl-snapshot-2026-11` | `bc41aeb7ba1a` | Histórica; versão anterior/reconstituída, não mesclar | Bundle completo privado |
| `publication/mgl-snapshot-2026-11-public` | `5fe505a09a76` | Ancestral; código já incluído | Bundle completo privado |
| `publication/mgl-snapshot-2026-11-sanitized` | `e4a685aa0e94` | Histórica; versão anterior/reconstituída, não mesclar | Bundle completo privado |
| `review/mgl-http500-p1-codex` | `a888d56414fb` | Ancestral; código já incluído | Bundle completo privado |
| `stabilization/mgl-connect-2026-11` | `823ac5353ae4` | Histórica; versão anterior/reconstituída, não mesclar | Bundle completo privado |
| `origin` | `d8a54d38342a` | Ancestral; código já incluído | Bundle completo privado |
| `origin/feat/mgl-functional-gps-mobile` | `ea3844f16ae7` | Ancestral; código já incluído | Bundle completo privado |
| `origin/integration/mgl-unificado-2026-11` | `01bcd8299a2c` | Ancestral; código já incluído | Bundle completo privado |
| `origin/main` | `d8a54d38342a` | Ancestral; código já incluído | Bundle completo privado |
| `origin/publication/mgl-snapshot-2026-11-public` | `5fe505a09a76` | Ancestral; código já incluído | Bundle completo privado |

As branches antigas de publicação e estabilização não são ancestrais diretas porque os snapshots foram reconstituídos e sanitizados. A comparação confirma que as diferenças operacionais são anteriores às correções já presentes; as sanitizações foram preservadas, não revertidas. Entre os snapshots sanitized e public diferem somente dois registros documentais. A estabilização contém harnesses antigos recuperados seletivamente na integração, seed anterior à sanitização e pendências acadêmicas/infraestrutura; essas pendências permanecem privadas, sem promover código não validado.

Não foi encontrada correção útil de código ausente que justifique um merge nesta missão. As refs privadas de sessões de agentes também ficam no bundle; nunca publicá-las indiscriminadamente.
