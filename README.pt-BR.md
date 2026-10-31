<div align="center">

# YGP Playtime Sync

**Envie seu tempo de jogo do Playnite para a sua conta do [Your Gamer Profile](https://yourgamerprofile.com/), automaticamente.**

![Status](https://img.shields.io/badge/status-beta-orange)
![Versão](https://img.shields.io/badge/vers%C3%A3o-0.5.1--beta-blue)
![Playnite](https://img.shields.io/badge/Playnite-10.x-5b8def)
![Idiomas](https://img.shields.io/badge/UI-English%20%7C%20Portugu%C3%AAs%20%7C%20Espa%C3%B1ol-green)

[English](README.md) · Português (Brasil) · [Español](README.es.md)

</div>

> **Beta.** Esta é a primeira versão pública. Ela funciona no dia a dia, mas ainda pode ter arestas. Por favor, reporte os problemas (veja [Como reportar problemas](#como-reportar-problemas)).
>
> **Projeto não oficial.** Não tem relação com o Your Gamer Profile (YGP) nem é apoiado por ele.

## O que ele faz

- **Sincroniza automaticamente ao fechar um jogo** (sessões de 1 minuto ou mais) e confere pendências periodicamente em segundo plano.
- **Sempre envia o tempo total do jogo** e nunca diminui as horas que o site já tem.
- **Adiciona jogos novos e atualiza os que já estão** no seu perfil. A plataforma, o status e a nota que você definiu no site são preservados.
- **Jogos recentes no topo.** A sincronização vai do jogo que você jogou há mais tempo até o que jogou por último, então os mais recentes ficam no topo dos recentes do seu perfil, o mesmo efeito de editar as horas à mão no site.
- **Conclusão automática (opcional).** Escolha de **7 a 30 dias**. Se você ficar esse tempo sem abrir um jogo, o plugin define no YGP a data de conclusão como o **último dia em que você realmente jogou**. Veja [Conclusão automática](#conclusão-automática).
- **Datas de início e de conclusão** a partir do seu histórico (usa o plugin *GameActivity*, se você tiver, e o registro do próprio plugin). Dá para ligar/desligar por jogo.
- **Detecta edições manuais de horas** no Playnite (por exemplo, com o FriendlySetTime) e atualiza o site.
- **Sincronizar biblioteca inteira** com um clique e barra de progresso. Jogos antigos entram.
- **Filtros:** ignora aplicativos/softwares, jogos com menos de 1 minuto, cópias duplicadas do mesmo jogo e uma lista de nomes que você escolher.
- **Fila segura offline:** as sessões ficam salvas no computador, sobrevivem a reinícios e são reenviadas sozinhas.
- **Interface em Português (Brasil), English e Español.** Segue o idioma do Playnite e pode ser trocada nas configurações.

## Requisitos

- Windows com **Playnite 10.x**
- Uma conta no **Your Gamer Profile**

## Instalação

1. Baixe o `.pext` mais recente na página de [**Releases**](https://github.com/uwaazy/ygp-playtime-sync/releases).
2. Feche o Playnite e dê um duplo clique no arquivo `.pext` (ou arrasta arquivo`.pext`para janela aberto do seu Playnite e confirma instalação).
3. Vá em **Extensões → Configurações de extensões → Genérico → Your Gamer Profile Sync**.
4. Clique em **Vincular conta (fazer login)** e entre no YGP na janela que abrir. Se o Google bloquear o login no navegador embutido, use **Vincular colando cookie (alternativa)** e siga as instruções da tela.
5. Clique uma vez em **Sincronizar biblioteca inteira (jogos antigos)**. Leva alguns minutos (cerca de 1 a 2 segundos por jogo).

Depois disso, tudo acontece em segundo plano.

## Configurações

| Opção | O que faz |
| --- | --- |
| Sincronizar automaticamente em segundo plano | Envia suas horas sem você precisar fazer nada |
| Sincronizar ao fechar o jogo | Sincroniza logo após cada sessão |
| Detectar edições manuais de horas | Pega as horas que você muda à mão no Playnite |
| Enviar a data de início por padrão | Envia o primeiro dia jogado (dá para mudar por jogo) |
| Mostrar aviso do Playnite | Mostra o jogo, as horas que subiram, o total do jogo e o total geral |
| Sincronizar sempre o tempo total acumulado | Mantém o total do site igual ao do Playnite |
| Intervalo de sincronização periódica | De quanto em quanto tempo a fila é conferida (1 a 60 min) |
| Conclusão automática + dias | Veja abaixo |
| Ignorar aplicativos/softwares · Nomes ignorados | Mantém o que não é jogo fora do seu perfil |
| Idioma | Automático, English, Português (Brasil) ou Español |

**Por jogo:** clique com o botão direito no jogo → **YGP: Configurações** (um único submenu) para sincronizar o tempo daquele jogo na hora ou ligar/desligar a data de início e a data de conclusão dele.

## Conclusão automática

Ative **Marcar a data de conclusão quando eu ficar um tempo sem jogar** e escolha quantos dias (de 7 a 30).

Quando um jogo ficar esse número de dias sem ser aberto, o plugin envia ao YGP a sua **data da última vez que jogou** como data de conclusão. A verificação acontece ao abrir o Playnite, de hora em hora enquanto ele estiver aberto e quando você salva as configurações.

> Exemplo com 7 dias: você jogou pela última vez no dia 22. No dia 29 o plugin define a data de conclusão como **22** (e não 29).

- Cada "último dia jogado" é enviado uma única vez por jogo. Se você voltar a jogar e parar de novo, a nova data é enviada.
- Só a **data** de conclusão é definida. Nos jogos que já estão no seu perfil, o status e a nota do site não são alterados.
- Se deixar a opção desligada, você ainda pode definir as datas à mão no site, ou por jogo com botão direito → **YGP: Configurações**.

## Como a conexão funciona

- Você entra no site oficial do YGP dentro do navegador embutido do Playnite. O plugin **nunca vê a sua senha**.
- Para agir em seu nome, ele guarda o **cookie de sessão** do site nas configurações do plugin, no seu computador. Veja [Privacidade e segurança](#privacidade-e-segurança).
- Os dados vão só para `https://yourgamerprofile.com`, por HTTPS. **Não há telemetria, analytics nem serviços de terceiros.**
- Os botões **Diagnosticar site** e **Aprender rotas do site** (Avançado) servem para resolver problemas. O diagnóstico faz muitas requisições ao site, então use só quando algo parar de funcionar.

## Privacidade e segurança

O que é enviado ao YGP: nome do jogo, plataforma, tempo total em minutos e, se estiver ativado, as datas de início e de conclusão.

O que fica no seu PC (na pasta `ExtensionsData` do Playnite, no diretório deste plugin): a fila de sessões, o registro do que já foi enviado, os mapeamentos de jogos, as opções por jogo e as datas jogadas.

**O seu cookie de sessão fica salvo em texto puro** nas configurações do plugin, como a maioria das configurações de plugins do Playnite. Quem pegar esse arquivo consegue agir como você no YGP até a sessão expirar. Por isso:

- Não compartilhe a pasta `ExtensionsData` do Playnite nem os arquivos de configuração.
- Revise o `ygp-diagnostico.txt` antes de anexá-lo a um relato de bug; ele tem respostas brutas do site.
- **Desconectar** (nas configurações) apaga o cookie guardado.

## Limitações conhecidas (beta)

- As rotas do site não são documentadas, então uma atualização do YGP pode quebrar a sincronização até o plugin ser atualizado.
- A identificação dos jogos é rígida de propósito (sequências e versões Mobile/Demo não são misturadas). Alguns jogos podem precisar ser adicionados à mão no site.
- A ordem "recentes no topo" depende de como o YGP registra edições; hoje ela equivale ao que acontece quando você edita as horas no site, mas o YGP pode mudar isso.
- O site pode limitar a velocidade (HTTP 429). Espere alguns minutos e sincronize de novo.

## Como reportar problemas

Abra um [issue](https://github.com/uWaazy/ygp-playtime-sync/issues) e inclua:

1. As versões do Playnite e do plugin.
2. O que você fez e o que esperava que acontecesse.
3. O `extensions.log` do Playnite (*Menu → Configurações → Geral → Abrir pasta de logs*). Procure as linhas que começam com `[YGP]`.
4. Se for problema de sincronização, o `ygp-diagnostico.txt` do botão **Abrir pasta do diagnóstico**. Revise antes para tirar dados pessoais.

## Próximos passos

- Mais testes durante o beta
- Correções para o que o YGP mudar no site
- Seu feedback: sugestões são bem-vindas nos issues

## Licença e código-fonte

O plugin é gratuito e de código aberto, sob a [licença MIT](LICENSE). O código-fonte está neste repositório (pastas `YgpPlaytimeSync` e `YgpPlaytimeSync.Tests`).

### Compilar e testar no Playnite (modo desenvolvedor)

1. Instale o Playnite 10.x e o [.NET SDK](https://dotnet.microsoft.com/download) (ou o Visual Studio Build Tools), e tenha acesso à internet (NuGet).
2. Rode o `build.bat`. O plugin é compilado em `bin\Release`.
3. No Playnite, abra **Configurações → Para desenvolvedores → Extensões externas → Adicionar**, escolha a pasta `bin\Release` e reinicie o Playnite.

Se o Playnite não estiver em `%LOCALAPPDATA%\Playnite` (versão portátil), defina a variável de ambiente `PLAYNITE_DIR` com a pasta dele antes de rodar o `build.bat`.

## Créditos

Feito para o [Playnite](https://playnite.link/). Obrigado a todos que estão testando o beta.

