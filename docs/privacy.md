# Privacidade

O plugin não tem telemetria, analytics nem relatórios de falha. A única comunicação externa é com `https://yourgamerprofile.com`, usando HTTPS e a sua conta.

## O que é enviado ao YGP

Para cada jogo sincronizado: nome, plataforma, tempo total jogado (em minutos) e, se habilitado, as datas de início e de conclusão. Também são enviados os dados que a edição de jogo do site exige (status e nota são preservados como estão no site).

## O que fica no seu computador

Na pasta de dados do plugin (`ExtensionsData\<id do plugin>`):

| Arquivo | Conteúdo |
| --- | --- |
| `sessions/`, `completed/` | Sessões de jogo pendentes e concluídas (id do jogo, nome, plataforma, início/fim, duração) |
| `synced-playtime.json` | Último tempo enviado por jogo |
| `game_mappings.json` | Relação jogo do Playnite ↔ jogo do YGP |
| `ygp-game-options.json`, `ygp-play-dates.json` | Opções por jogo e datas jogadas |
| `ygp-diagnostico.txt` | Só existe se você usar o diagnóstico. Contém respostas brutas do site |

Nas configurações do plugin ficam também o nome de usuário, o id e o avatar do YGP e os **cookies de sessão** (`AuthCookiesJson`), em texto, sem criptografia.

## Cookies de sessão

O cookie de sessão dá acesso à sua conta no YGP. Quem copiar esse arquivo consegue agir como você no site até a sessão expirar. Por isso:

- Não publique nem compartilhe a pasta `ExtensionsData` do plugin nem o arquivo de configurações do Playnite.
- Revise o `ygp-diagnostico.txt` antes de anexá-lo em um issue.
- Use **Desconectar** nas configurações para apagar os cookies guardados.

## Leitura de outros plugins

Se o plugin GameActivity estiver instalado, a extensão lê o histórico dele (somente leitura) para descobrir o primeiro e o último dia jogado.

## Apagar os dados

Use **Desconectar** e **Limpar dados locais de sincronização** nas configurações, ou apague a pasta de dados do plugin com o Playnite fechado.

## Contato

Abra um issue no repositório.
