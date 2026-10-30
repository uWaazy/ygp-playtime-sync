# Solução de problemas

Logs do Playnite: **Menu → Configurações → Geral → Abrir pasta de logs** (`extensions.log`). Procure linhas que começam com `[YGP]`.

## O plugin não carrega ("Falha ao carregar extensão")

- O mesmo plugin (mesmo `Id`) não pode estar instalado como `.pext` **e** ativo em *Configurações → Para desenvolvedores → Extensões externas*. Deixe só um.
- Feche o Playnite antes de instalar ou atualizar o `.pext`.
- Se o erro citar `Newtonsoft.Json`, gere o `.pext` com `build.bat` (ele inclui a DLL) e não com o Toolbox.

## O login não conclui ou o Google bloqueia

Use **Vincular colando cookie (alternativa)**. Entre no YGP no seu navegador, abra F12 → Application → Cookies → `https://yourgamerprofile.com` e copie o valor de `__Secure-better-auth.session_token`.

## Sessão expirada

O envio é suspenso até você vincular a conta de novo. As sessões continuam na fila e são enviadas depois.

## Jogos "não encontrados"

O plugin compara o título com estrita igualdade (ignorando edições como "Remastered" ou "Tom Clancy's"). Sequelas e versões Mobile/Demo são recusadas de propósito. Você pode criar um mapeamento manual.

## Erro 429 (muitas requisições)

O site limitou a velocidade. Espere alguns minutos. Evite o botão **Diagnosticar site** a não ser que seja necessário.

## Horas não aumentam

O plugin só reenvia se o tempo **aumentou** e nunca diminui as horas do site. Para forçar, use o menu de contexto do jogo (*YGP: Configurações* / *YGP: Settings*) e ative o reenvio.

## Apareceu "rota não encontrada" depois de uma atualização do site

As rotas do site não são documentadas e podem mudar. Rode **Diagnosticar site**, anexe o `extensions.log` num issue e revise o `ygp-diagnostico.txt` antes de enviá-lo.
