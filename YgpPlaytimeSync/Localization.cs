using System;
using System.Collections.Generic;
using System.Globalization;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Textos do plugin em português do Brasil, inglês e espanhol.
    /// Uso: L.T("chave") ou L.T("chave", arg0, arg1). A ordem de cada linha é: pt-BR, en, es.
    /// </summary>
    public static class L
    {
        public const string Auto = "auto";

        private const int Pt = 0, En = 1, Es = 2;
        private static int _lang = En;
        private static string _code = "en";

        /// <summary>Código do idioma em uso: "pt", "en" ou "es".</summary>
        public static string Code { get { return _code; } }

        /// <summary>
        /// Define o idioma. <paramref name="preference"/> é "auto", "pt", "en" ou "es".
        /// Em "auto" usa o idioma do Playnite (ex.: pt_BR, en_US, es_ES) e, se ele não vier, o idioma do Windows.
        /// </summary>
        public static void Configure(string preference, string playniteLanguage)
        {
            var wanted = (preference ?? Auto).Trim().ToLowerInvariant();
            if (wanted == Auto || wanted.Length == 0)
            {
                wanted = (playniteLanguage ?? "").Trim().ToLowerInvariant();
                if (wanted.Length == 0) wanted = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }

            if (wanted.StartsWith("pt", StringComparison.Ordinal)) { _lang = Pt; _code = "pt"; }
            else if (wanted.StartsWith("es", StringComparison.Ordinal)) { _lang = Es; _code = "es"; }
            else { _lang = En; _code = "en"; }
        }

        public static string T(string key, params object[] args)
        {
            string[] row;
            if (!Table.TryGetValue(key, out row)) return key;

            var text = row[_lang];
            if (args == null || args.Length == 0) return text;

            try { return string.Format(CultureInfo.CurrentCulture, text, args); }
            catch (FormatException) { return text; }
        }

        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            ["app.name"] = new[] { "Your Gamer Profile Sync", "Your Gamer Profile Sync", "Your Gamer Profile Sync" },
            ["beta"] = new[] { "BETA", "BETA", "BETA" },
            ["subtitle"] = new[] { "Sincronize seu tempo de jogo do Playnite com o seu perfil no yourgamerprofile.com", "Sync your Playnite playtime with your profile on yourgamerprofile.com", "Sincroniza tu tiempo de juego de Playnite con tu perfil en yourgamerprofile.com" },
            ["lang.label"] = new[] { "Idioma:", "Language:", "Idioma:" },
            ["lang.auto"] = new[] { "Automático (idioma do Playnite)", "Automatic (Playnite language)", "Automático (idioma de Playnite)" },
            ["profile.view"] = new[] { "Ver e seguir no YGP", "View and follow on YGP", "Ver y seguir en YGP" },
            ["profile.creator"] = new[] { "Perfil do Criador", "Creator Profile", "Perfil del Creador" },
            ["profile.lastGame"] = new[] { "Último jogo jogado:", "Last game played:", "Último juego jugado:" },
            ["profile.lastGameLoading"] = new[] { "carregando...", "loading...", "cargando..." },
            ["profile.lastGameNone"] = new[] { "indisponível no momento", "unavailable right now", "no disponible por ahora" },
            ["sec.account"] = new[] { "Conta do Your Gamer Profile", "Your Gamer Profile account", "Cuenta de Your Gamer Profile" },
            ["status.label"] = new[] { "Status: ", "Status: ", "Estado: " },
            ["status.notConnected"] = new[] { "Não conectado", "Not connected", "No conectado" },
            ["status.connected"] = new[] { "Conectado", "Connected", "Conectado" },
            ["status.connectedAs"] = new[] { "Conectado como: {0}", "Connected as: {0}", "Conectado como: {0}" },
            ["acct.hint"] = new[] { "Clique em 'Vincular conta' para abrir o navegador interno do Playnite e entrar no Your Gamer Profile.", "Click 'Link account' to open Playnite's built-in browser and sign in to Your Gamer Profile.", "Haz clic en 'Vincular cuenta' para abrir el navegador interno de Playnite e iniciar sesión en Your Gamer Profile." },
            ["acct.lastSync"] = new[] { "Última sincronização com sucesso: {0}", "Last successful sync: {0}", "Última sincronización correcta: {0}" },
            ["acct.neverSynced"] = new[] { "Ainda não sincronizado", "Not synced yet", "Aún no sincronizado" },
            ["btn.link"] = new[] { "Vincular conta (fazer login)", "Link account (sign in)", "Vincular cuenta (iniciar sesión)" },
            ["btn.linkCookie"] = new[] { "Vincular colando cookie (alternativa)", "Link by pasting a cookie (alternative)", "Vincular pegando una cookie (alternativa)" },
            ["btn.test"] = new[] { "Testar conexão", "Test connection", "Probar conexión" },
            ["btn.disconnect"] = new[] { "Desconectar", "Disconnect", "Desconectar" },
            ["msg.linked"] = new[] { "Conta vinculada com sucesso!\nUsuário: {0}", "Account linked successfully!\nUser: {0}", "¡Cuenta vinculada con éxito!\nUsuario: {0}" },
            ["msg.loginIncomplete"] = new[] { "O login não foi concluído ou a janela foi fechada sem autenticação.", "Sign-in was not completed or the window was closed without authenticating.", "El inicio de sesión no se completó o la ventana se cerró sin autenticar." },
            ["msg.connOk"] = new[] { "Conexão ativa e validada!\n{0}\nÚltimo jogo rastreado: {1}", "Connection active and verified!\n{0}\nLast tracked game: {1}", "¡Conexión activa y validada!\n{0}\nÚltimo juego registrado: {1}" },
            ["msg.connFail"] = new[] { "Não foi possível validar a sessão com o site.\n{0}", "Could not verify the session with the site.\n{0}", "No se pudo validar la sesión con el sitio.\n{0}" },
            ["msg.tryLogin"] = new[] { "Tente fazer login novamente.", "Try signing in again.", "Intenta iniciar sesión de nuevo." },
            ["title.connDiag"] = new[] { "Diagnóstico de conexão", "Connection diagnostics", "Diagnóstico de conexión" },
            ["msg.disconnected"] = new[] { "Conta desconectada do plugin.", "Account disconnected from the plugin.", "Cuenta desconectada del plugin." },
            ["lastGame.none"] = new[] { "Nenhum jogo registrado nesta sessão", "No game recorded in this session", "Ningún juego registrado en esta sesión" },
            ["sec.sync"] = new[] { "Sincronização de tempo de jogo", "Playtime synchronization", "Sincronización del tiempo de juego" },
            ["opt.autoSync"] = new[] { "Sincronizar automaticamente em segundo plano", "Sync automatically in the background", "Sincronizar automáticamente en segundo plano" },
            ["opt.autoSync.desc"] = new[] { "Envia as horas jogadas para o Your Gamer Profile automaticamente.", "Sends your played hours to Your Gamer Profile automatically.", "Envía las horas jugadas a Your Gamer Profile automáticamente." },
            ["opt.syncOnStop"] = new[] { "Sincronizar ao fechar o jogo", "Sync when a game closes", "Sincronizar al cerrar el juego" },
            ["opt.syncOnStop.desc"] = new[] { "Quando você parar de jogar, o plugin sincroniza imediatamente as horas acumuladas.", "When you stop playing, the plugin immediately syncs the accumulated hours.", "Cuando dejas de jugar, el plugin sincroniza de inmediato las horas acumuladas." },
            ["opt.manualEdits"] = new[] { "Detectar edições manuais de horas no Playnite (ex.: FriendlySetTime / Propriedades)", "Detect manual playtime edits in Playnite (e.g. FriendlySetTime / Properties)", "Detectar ediciones manuales de horas en Playnite (p. ej., FriendlySetTime / Propiedades)" },
            ["opt.manualEdits.desc"] = new[] { "Se você ajustar o tempo de jogo manualmente no Playnite, o plugin detecta e atualiza o site.", "If you adjust a game's playtime manually in Playnite, the plugin detects it and updates the site.", "Si ajustas el tiempo de juego manualmente en Playnite, el plugin lo detecta y actualiza el sitio." },
            ["opt.sendStart"] = new[] { "Enviar a data de início (primeiro dia jogado) por padrão", "Send the start date (first day played) by default", "Enviar la fecha de inicio (primer día jugado) por defecto" },
            ["opt.sendStart.desc"] = new[] { "Usa o histórico do GameActivity (se instalado) e o registro do próprio plugin. Cada jogo pode mudar isso: botão direito no jogo > YGP: Configurações.", "Uses the GameActivity history (if installed) and the plugin's own log. Each game can override this: right-click the game > YGP: Settings.", "Usa el historial de GameActivity (si está instalado) y el registro del propio plugin. Cada juego puede cambiarlo: clic derecho en el juego > YGP: Configuración." },
            ["opt.notify"] = new[] { "Mostrar aviso do Playnite quando um jogo for sincronizado em segundo plano", "Show a Playnite notification when a game is synced in the background", "Mostrar aviso de Playnite cuando un juego se sincronice en segundo plano" },
            ["opt.notify.desc"] = new[] { "Mostra o jogo, as horas que subiram, o total do jogo e o total geral.", "Shows the game, the hours added, the game total and the overall total.", "Muestra el juego, las horas añadidas, el total del juego y el total general." },
            ["opt.total"] = new[] { "Sincronizar sempre o tempo total acumulado do jogo", "Always sync the game's total accumulated time", "Sincronizar siempre el tiempo total acumulado del juego" },
            ["opt.total.desc"] = new[] { "Garante que o total de horas no Your Gamer Profile fique idêntico ao total registrado no Playnite.", "Keeps the total hours on Your Gamer Profile identical to the total recorded in Playnite.", "Garantiza que el total de horas en Your Gamer Profile sea idéntico al registrado en Playnite." },
            ["opt.interval"] = new[] { "Intervalo de sincronização periódica (minutos)", "Periodic sync interval (minutes)", "Intervalo de sincronización periódica (minutos)" },
            ["opt.interval.desc"] = new[] { "Frequência com que a fila de pendências verifica e reenvia dados.", "How often the pending queue is checked and resent.", "Frecuencia con la que la cola pendiente se revisa y se reenvía." },
            ["unit.min"] = new[] { "min", "min", "min" },
            ["unit.days"] = new[] { "dias", "days", "días" },
            ["sec.autoComplete"] = new[] { "Conclusão automática", "Automatic completion", "Finalización automática" },
            ["opt.autoComplete"] = new[] { "Marcar a data de conclusão quando eu ficar um tempo sem jogar", "Set the completion date when I haven't played a game for a while", "Marcar la fecha de finalización cuando pase un tiempo sin jugar" },
            ["opt.autoComplete.desc"] = new[] { "Se você ficar o número de dias abaixo sem abrir um jogo, o plugin envia ao YGP a data da última vez que você jogou como data de conclusão. Desativado: defina a data manualmente no site ou com o botão direito no jogo (YGP: Configurações).", "If you go the number of days below without opening a game, the plugin sends the date you last played it to YGP as the completion date. Disabled: set the date manually on the site or by right-clicking the game (YGP: Settings).", "Si pasas el número de días indicado sin abrir un juego, el plugin envía a YGP la fecha de la última vez que jugaste como fecha de finalización. Desactivado: define la fecha manualmente en el sitio o con clic derecho en el juego (YGP: Configuración)." },
            ["opt.autoCompleteDays"] = new[] { "Dias sem jogar (de 7 a 30)", "Days without playing (7 to 30)", "Días sin jugar (de 7 a 30)" },
            ["opt.autoCompleteDays.desc"] = new[] { "Exemplo: com 7 dias, um jogo que você parou de jogar no dia 22 é marcado como concluído em 22 quando chegar o dia 29.", "Example: with 7 days, a game you last played on the 22nd is marked as completed on the 22nd once the 29th arrives.", "Ejemplo: con 7 días, un juego que dejaste de jugar el día 22 se marca como finalizado el 22 cuando llegue el día 29." },
            ["notify.autoComplete"] = new[] { "{0} jogo(s) marcado(s) como concluído(s) (sem jogar há {1} dias ou mais).", "{0} game(s) marked as completed (not played for {1} days or more).", "{0} juego(s) marcado(s) como finalizado(s) (sin jugar desde hace {1} días o más)." },
            ["sec.queue"] = new[] { "Fila de sincronização local", "Local sync queue", "Cola de sincronización local" },
            ["q.pending"] = new[] { "Sessões aguardando envio: {0}", "Sessions waiting to be sent: {0}", "Sesiones esperando envío: {0}" },
            ["q.failed"] = new[] { "Sessões com falha (aguardando reenvio manual): {0}", "Failed sessions (waiting for manual resend): {0}", "Sesiones fallidas (esperando reenvío manual): {0}" },
            ["q.synced"] = new[] { "Sessões já enviadas: {0}", "Sessions already sent: {0}", "Sesiones ya enviadas: {0}" },
            ["q.lastRun"] = new[] { "Última execução: {0}", "Last run: {0}", "Última ejecución: {0}" },
            ["q.noRun"] = new[] { "Nenhuma sincronização executada nesta sessão do Playnite.", "No sync has run in this Playnite session.", "No se ha ejecutado ninguna sincronización en esta sesión de Playnite." },
            ["btn.syncNow"] = new[] { "Sincronizar fila agora", "Sync queue now", "Sincronizar cola ahora" },
            ["btn.syncLibrary"] = new[] { "Sincronizar biblioteca inteira (jogos antigos)", "Sync entire library (older games)", "Sincronizar toda la biblioteca (juegos antiguos)" },
            ["btn.clearQueue"] = new[] { "Limpar fila pendente", "Clear pending queue", "Vaciar cola pendiente" },
            ["btn.clearData"] = new[] { "Limpar dados locais", "Clear local data", "Borrar datos locales" },
            ["msg.alreadyRunning"] = new[] { "Já existe uma sincronização em andamento. Aguarde terminar e tente de novo.", "A sync is already in progress. Wait for it to finish and try again.", "Ya hay una sincronización en curso. Espera a que termine e inténtalo de nuevo." },
            ["msg.queueEmpty"] = new[] { "Nenhuma sessão pendente na fila para sincronizar.", "No pending sessions in the queue to sync.", "No hay sesiones pendientes en la cola para sincronizar." },
            ["msg.syncSummary"] = new[] { "Jogos enviados: {0}\nJá estavam no perfil do site: {1}\nFalhas: {2}", "Games sent: {0}\nAlready on the site profile: {1}\nFailures: {2}", "Juegos enviados: {0}\nYa estaban en el perfil del sitio: {1}\nFallos: {2}" },
            ["title.sync"] = new[] { "Sincronização Your Gamer Profile", "Your Gamer Profile sync", "Sincronización de Your Gamer Profile" },
            ["msg.clearQueueAsk"] = new[] { "Deseja ignorar todas as sessões pendentes na fila?", "Do you want to ignore all pending sessions in the queue?", "¿Quieres ignorar todas las sesiones pendientes de la cola?" },
            ["title.clearQueue"] = new[] { "Limpar fila", "Clear queue", "Vaciar cola" },
            ["msg.clearDataAsk"] = new[] { "Deseja apagar o histórico de sessões e mapeamentos salvos no computador?", "Do you want to delete the session history and mappings saved on this computer?", "¿Quieres borrar el historial de sesiones y los mapeos guardados en este equipo?" },
            ["title.clearData"] = new[] { "Limpar dados locais", "Clear local data", "Borrar datos locales" },
            ["msg.dataCleared"] = new[] { "Dados locais removidos.", "Local data removed.", "Datos locales eliminados." },
            ["sec.advanced"] = new[] { "Avançado", "Advanced", "Avanzado" },
            ["adv.procedures"] = new[] { "Procedimentos do site usados para ATUALIZAR um jogo que já está no perfil (separados por vírgula). Só altere se o log mostrar que o site recusou a atualização.", "Site procedures used to UPDATE a game that is already on the profile (comma-separated). Only change this if the log shows the site refused the update.", "Procedimientos del sitio usados para ACTUALIZAR un juego que ya está en el perfil (separados por comas). Cámbialos solo si el registro muestra que el sitio rechazó la actualización." },
            ["adv.ignoreSoftware"] = new[] { "Ignorar aplicativos/softwares (gêneros como Utilities, Software, Design & Illustration)", "Ignore apps/software (genres such as Utilities, Software, Design & Illustration)", "Ignorar aplicaciones/software (géneros como Utilities, Software, Design & Illustration)" },
            ["adv.ignoredNames"] = new[] { "Jogos/aplicativos que nunca devem ser enviados (nomes separados por vírgula):", "Games/apps that must never be sent (names separated by commas):", "Juegos/aplicaciones que nunca deben enviarse (nombres separados por comas):" },
            ["btn.learn"] = new[] { "Aprender rotas do site (modo captura)", "Learn site routes (capture mode)", "Aprender rutas del sitio (modo captura)" },
            ["btn.diag"] = new[] { "Diagnosticar site (descobrir rotas, leva 1-2 min)", "Diagnose site (discover routes, takes 1-2 min)", "Diagnosticar sitio (descubrir rutas, tarda 1-2 min)" },
            ["btn.openFolder"] = new[] { "Abrir pasta do diagnóstico", "Open diagnostics folder", "Abrir carpeta de diagnóstico" },
            ["msg.openFolderFail"] = new[] { "Não foi possível abrir a pasta: {0}", "Could not open the folder: {0}", "No se pudo abrir la carpeta: {0}" },
            ["learn.confirm"] = new[] { "Vai abrir o site numa janela. Faça isto:\n\n1) Use a busca de ADICIONAR JOGO: digite o nome de um jogo (ex.: Hades) e espere a lista aparecer.\n2) Edite as HORAS de um jogo qualquer do seu perfil e salve (depois pode corrigir de volta).\n3) Espere uns 3 segundos e feche a janela.\n\nO plugin guarda as rotas que o próprio site usou. Continuar?", "The site will open in a window. Do this:\n\n1) Use the ADD GAME search: type a game name (e.g. Hades) and wait for the list to appear.\n2) Edit the HOURS of any game on your profile and save (you can change it back afterwards).\n3) Wait about 3 seconds and close the window.\n\nThe plugin stores the routes the site itself used. Continue?", "Se abrirá el sitio en una ventana. Haz esto:\n\n1) Usa la búsqueda de AÑADIR JUEGO: escribe el nombre de un juego (p. ej., Hades) y espera a que aparezca la lista.\n2) Edita las HORAS de cualquier juego de tu perfil y guarda (luego puedes volver a corregirlo).\n3) Espera unos 3 segundos y cierra la ventana.\n\nEl plugin guarda las rutas que usó el propio sitio. ¿Continuar?" },
            ["title.learn"] = new[] { "Modo captura", "Capture mode", "Modo captura" },
            ["title.diag"] = new[] { "Diagnóstico do Your Gamer Profile", "Your Gamer Profile diagnostics", "Diagnóstico de Your Gamer Profile" },
            ["menu.startDate"] = new[] { "Enviar data de início (primeiro dia jogado)", "Send start date (first day played)", "Enviar fecha de inicio (primer día jugado)" },
            ["menu.completeDate"] = new[] { "Enviar data de conclusão (último dia jogado)", "Send completion date (last day played)", "Enviar fecha de finalización (último día jugado)" },
            ["menu.section"] = new[] { "YGP: Configurações", "YGP: Settings", "YGP: Configuración" },
            ["menu.syncGame"] = new[] { "Sincronizar tempo desse jogo com YGP", "Sync this game's playtime with YGP", "Sincronizar tiempo de este juego con YGP" },
            ["menu.syncLibrary"] = new[] { "Sincronizar biblioteca inteira com o Your Gamer Profile", "Sync entire library with Your Gamer Profile", "Sincronizar toda la biblioteca con Your Gamer Profile" },
            ["opt.startOn"] = new[] { "Data de início ATIVADA", "Start date ENABLED", "Fecha de inicio ACTIVADA" },
            ["opt.startOff"] = new[] { "Data de início DESATIVADA", "Start date DISABLED", "Fecha de inicio DESACTIVADA" },
            ["opt.completeOn"] = new[] { "Data de conclusão ATIVADA", "Completion date ENABLED", "Fecha de finalización ACTIVADA" },
            ["opt.completeOff"] = new[] { "Data de conclusão DESATIVADA", "Completion date DISABLED", "Fecha de finalización DESACTIVADA" },
            ["notify.optionChanged"] = new[] { "{0} para {1} jogo(s).", "{0} for {1} game(s).", "{0} para {1} juego(s)." },
            ["notify.sendingNow"] = new[] { " Enviando agora em segundo plano.", " Sending now in the background.", " Enviando ahora en segundo plano." },
            ["notify.nextSync"] = new[] { " Vale na próxima sincronização.", " Takes effect on the next sync.", " Se aplicará en la próxima sincronización." },
            ["notify.single"] = new[] { "{0} sincronizado: +{1}. Total do jogo: {2}. Total geral: {3}.", "{0} synced: +{1}. Game total: {2}. Overall total: {3}.", "{0} sincronizado: +{1}. Total del juego: {2}. Total general: {3}." },
            ["notify.multi"] = new[] { "{0} jogos sincronizados: +{1}. Total geral: {2}.", "{0} games synced: +{1}. Overall total: {2}.", "{0} juegos sincronizados: +{1}. Total general: {2}." },
            ["skip.invalid"] = new[] { "jogo inválido", "invalid game", "juego no válido" },
            ["skip.under1min"] = new[] { "menos de 1 minuto", "less than 1 minute", "menos de 1 minuto" },
            ["skip.ignoredList"] = new[] { "na lista de ignorados", "on the ignore list", "en la lista de ignorados" },
            ["skip.software"] = new[] { "aplicativo/software", "app/software", "aplicación/software" },
            ["skip.noIncrease"] = new[] { "tempo não aumentou", "playtime did not increase", "el tiempo no aumentó" },
            ["skip.notInLibrary"] = new[] { "jogo não está mais na biblioteca", "game is no longer in the library", "el juego ya no está en la biblioteca" },
            ["skip.duplicate"] = new[] { "cópia duplicada do mesmo jogo", "duplicate copy of the same game", "copia duplicada del mismo juego" },
            ["res.notRun"] = new[] { "Sincronização não executada.", "Sync did not run.", "La sincronización no se ejecutó." },
            ["res.noChanges"] = new[] { "Nenhuma alteração pendente para sincronizar.", "No pending changes to sync.", "No hay cambios pendientes que sincronizar." },
            ["res.sent"] = new[] { "Jogos enviados com sucesso: {0}", "Games sent successfully: {0}", "Juegos enviados con éxito: {0}" },
            ["res.added"] = new[] { "Horas enviadas nesta rodada: +{0}", "Hours sent in this round: +{0}", "Horas enviadas en esta ronda: +{0}" },
            ["res.dups"] = new[] { "Duplicados ignorados (mesmo jogo e plataforma): {0}", "Duplicates skipped (same game and platform): {0}", "Duplicados ignorados (mismo juego y plataforma): {0}" },
            ["res.already"] = new[] { "Já estavam no seu perfil (o site já tinha o jogo nessa plataforma): {0}", "Already on your profile (the site already had the game on that platform): {0}", "Ya estaban en tu perfil (el sitio ya tenía el juego en esa plataforma): {0}" },
            ["res.alreadyHint"] = new[] { "(o site já tinha as mesmas horas ou mais, ou recusou a atualização: veja o log)", "(the site already had the same hours or more, or refused the update: see the log)", "(el sitio ya tenía las mismas horas o más, o rechazó la actualización: consulta el registro)" },
            ["res.failures"] = new[] { "Falhas: {0}", "Failures: {0}", "Fallos: {0}" },
            ["res.moreErrors"] = new[] { "... e mais {0} erro(s). Veja o log do Playnite.", "... and {0} more error(s). See the Playnite log.", "... y {0} error(es) más. Consulta el registro de Playnite." },
            ["res.cancelled"] = new[] { "Cancelado pelo usuário.", "Cancelled by the user.", "Cancelado por el usuario." },
            ["res.summary"] = new[] { "{0} - enviados: {1}, já no perfil: {2}, falhas: {3}", "{0} - sent: {1}, already on profile: {2}, failures: {3}", "{0} - enviados: {1}, ya en el perfil: {2}, fallos: {3}" },
            ["err.notInit"] = new[] { "O plugin não foi inicializado corretamente. Veja o log do Playnite.", "The plugin did not initialize correctly. See the Playnite log.", "El plugin no se inicializó correctamente. Consulta el registro de Playnite." },
            ["lib.needLink"] = new[] { "Vincule sua conta do Your Gamer Profile nas configurações do plugin antes de sincronizar.", "Link your Your Gamer Profile account in the plugin settings before syncing.", "Vincula tu cuenta de Your Gamer Profile en los ajustes del plugin antes de sincronizar." },
            ["lib.nothing"] = new[] { "Nada novo para enviar: nenhum jogo teve o tempo aumentado desde o último envio.", "Nothing new to send: no game's playtime has increased since the last upload.", "Nada nuevo que enviar: ningún juego ha aumentado su tiempo desde el último envío." },
            ["lib.ignored"] = new[] { "Ignorados: {0}", "Ignored: {0}", "Ignorados: {0}" },
            ["lib.confirm"] = new[] { "Serão enviados {0} jogos. Pode levar alguns minutos (cerca de 1 a 2 segundos por jogo). Continuar?", "{0} games will be sent. This may take a few minutes (about 1 to 2 seconds per game). Continue?", "Se enviarán {0} juegos. Puede tardar unos minutos (alrededor de 1 a 2 segundos por juego). ¿Continuar?" },
            ["lib.progress"] = new[] { "Enviando tempo de jogo para o Your Gamer Profile...", "Sending playtime to Your Gamer Profile...", "Enviando tiempo de juego a Your Gamer Profile..." },
            ["lib.progressTitle"] = new[] { "Sincronizando com o Your Gamer Profile...", "Syncing with Your Gamer Profile...", "Sincronizando con Your Gamer Profile..." },
            ["lib.general"] = new[] { "Total geral de horas jogadas: {0}", "Overall hours played: {0}", "Total general de horas jugadas: {0}" },
            ["cookie.prompt"] = new[] { "1) Faça login no yourgamerprofile.com no seu navegador normal (Chrome/Edge).\n2) Aperte F12 > aba Application (Aplicativo) > Cookies > https://yourgamerprofile.com\n3) Copie o VALOR do cookie '__Secure-better-auth.session_token' e cole abaixo.\n(Opcional: vários cookies no formato nome=valor; nome2=valor2)", "1) Sign in to yourgamerprofile.com in your regular browser (Chrome/Edge).\n2) Press F12 > Application tab > Cookies > https://yourgamerprofile.com\n3) Copy the VALUE of the cookie '__Secure-better-auth.session_token' and paste it below.\n(Optional: several cookies in the format name=value; name2=value2)", "1) Inicia sesión en yourgamerprofile.com en tu navegador habitual (Chrome/Edge).\n2) Pulsa F12 > pestaña Application (Aplicación) > Cookies > https://yourgamerprofile.com\n3) Copia el VALOR de la cookie '__Secure-better-auth.session_token' y pégalo abajo.\n(Opcional: varias cookies con el formato nombre=valor; nombre2=valor2)" },
            ["cookie.title"] = new[] { "Vincular por cookie de sessão", "Link with a session cookie", "Vincular con cookie de sesión" },
            ["cookie.invalid"] = new[] { "O texto colado não parece ser um cookie de sessão.", "The pasted text does not look like a session cookie.", "El texto pegado no parece una cookie de sesión." },
            ["api.noBrowser"] = new[] { "Não foi possível criar o navegador interno do Playnite.", "Could not create Playnite's built-in browser.", "No se pudo crear el navegador interno de Playnite." },
            ["api.timeout"] = new[] { "Tempo limite ao abrir yourgamerprofile.com (verifique sua internet ou se o site está no ar).", "Timed out opening yourgamerprofile.com (check your internet connection or whether the site is up).", "Tiempo de espera agotado al abrir yourgamerprofile.com (revisa tu conexión o si el sitio está disponible)." },
            ["api.cloudflare"] = new[] { "O site está exibindo a verificação do Cloudflare e ela não foi concluída. Tente novamente em alguns minutos.", "The site is showing the Cloudflare check and it was not completed. Try again in a few minutes.", "El sitio muestra la verificación de Cloudflare y no se completó. Inténtalo de nuevo en unos minutos." },
            ["api.cloudflareBlocked"] = new[] { "Bloqueado pela verificação do Cloudflare.", "Blocked by the Cloudflare check.", "Bloqueado por la verificación de Cloudflare." },
            ["api.linkFirstDiag"] = new[] { "Vincule a conta antes de rodar o diagnóstico.", "Link your account before running the diagnostics.", "Vincula la cuenta antes de ejecutar el diagnóstico." },
            ["api.linkFirstLearn"] = new[] { "Vincule a conta antes de usar o modo captura.", "Link your account before using capture mode.", "Vincula la cuenta antes de usar el modo captura." },
            ["api.respTimeout"] = new[] { "Tempo limite aguardando resposta do site.", "Timed out waiting for the site's response.", "Tiempo de espera agotado esperando la respuesta del sitio." },
            ["api.browserError"] = new[] { "Erro no navegador interno: {0}", "Internal browser error: {0}", "Error en el navegador interno: {0}" },
            ["api.unexpectedResp"] = new[] { "Resposta inesperada do site (HTTP {0}): {1}", "Unexpected response from the site (HTTP {0}): {1}", "Respuesta inesperada del sitio (HTTP {0}): {1}" },
            ["api.parseFail"] = new[] { "Falha ao interpretar a resposta do site: {0}", "Failed to parse the site's response: {0}", "Error al interpretar la respuesta del sitio: {0}" },
            ["api.siteRefused"] = new[] { "O site recusou o envio.", "The site refused the submission.", "El sitio rechazó el envío." },
            ["test.noAccount"] = new[] { "Nenhuma conta vinculada.", "No account linked.", "Ninguna cuenta vinculada." },
            ["api.diagError"] = new[] { "Erro no diagnóstico: {0}", "Diagnostics error: {0}", "Error en el diagnóstico: {0}" },
            ["diag.routes"] = new[] { "Rotas candidatas encontradas no site: {0}", "Candidate routes found on the site: {0}", "Rutas candidatas encontradas en el sitio: {0}" },
            ["diag.searchRoute"] = new[] { "Rota de busca em uso: {0}", "Search route in use: {0}", "Ruta de búsqueda en uso: {0}" },
            ["diag.none"] = new[] { "(nenhuma funcionou)", "(none worked)", "(ninguna funcionó)" },
            ["diag.testErr"] = new[] { "Busca de teste: {0}", "Test search: {0}", "Búsqueda de prueba: {0}" },
            ["diag.testOk"] = new[] { "Busca de teste (\"Hades\"): {0} resultado(s)", "Test search (\"Hades\"): {0} result(s)", "Búsqueda de prueba (\"Hades\"): {0} resultado(s)" },
            ["diag.noId"] = new[] { "sem id", "no id", "sin id" },
            ["diag.file"] = new[] { "Arquivo: {0}", "File: {0}", "Archivo: {0}" },
            ["learn.calls"] = new[] { "Chamadas registradas: {0}", "Calls recorded: {0}", "Llamadas registradas: {0}" },
            ["learn.searchOk"] = new[] { "Busca no catálogo aprendida: {0}", "Catalog search learned: {0}", "Búsqueda en el catálogo aprendida: {0}" },
            ["learn.searchNone"] = new[] { "Busca no catálogo: não vi nenhuma (digite o nome de um jogo na busca de adicionar jogo e espere a lista aparecer).", "Catalog search: none seen (type a game name in the add-game search and wait for the list to appear).", "Búsqueda en el catálogo: no vi ninguna (escribe el nombre de un juego en la búsqueda de añadir juego y espera a que aparezca la lista)." },
            ["learn.editOk"] = new[] { "Edição de horas aprendida: {0}", "Hours edit learned: {0}", "Edición de horas aprendida: {0}" },
            ["learn.editNone"] = new[] { "Edição de horas: não identifiquei (edite as horas de um jogo na janela e espere ~3 segundos antes de fechar).", "Hours edit: not identified (edit a game's hours in the window and wait ~3 seconds before closing).", "Edición de horas: no la identifiqué (edita las horas de un juego en la ventana y espera ~3 segundos antes de cerrar)." },
            ["learn.seenWrites"] = new[] { " Escritas vistas: {0}", " Writes seen: {0}", " Escrituras vistas: {0}" },
            ["learn.details"] = new[] { "Detalhes: {0}", "Details: {0}", "Detalles: {0}" },
            ["test.cantQuery"] = new[] { "Não foi possível consultar a sessão no site.", "Could not query the session on the site.", "No se pudo consultar la sesión en el sitio." },
            ["test.active"] = new[] { "Sessão ativa no Your Gamer Profile.", "Session active on Your Gamer Profile.", "Sesión activa en Your Gamer Profile." },
            ["test.activeAs"] = new[] { "Sessão ativa como {0}.", "Session active as {0}.", "Sesión activa como {0}." },
            ["test.expired"] = new[] { "A sessão expirou. Clique em 'Vincular conta' para entrar de novo.", "The session expired. Click 'Link account' to sign in again.", "La sesión expiró. Haz clic en 'Vincular cuenta' para entrar de nuevo." },
            ["test.unconfirmed"] = new[] { "O site abriu, mas não foi possível confirmar a sessão. O envio será tentado normalmente.", "The site opened, but the session could not be confirmed. Sending will be attempted as usual.", "El sitio se abrió, pero no se pudo confirmar la sesión. El envío se intentará con normalidad." },
            ["test.error"] = new[] { "Erro ao testar conexão: {0}", "Error testing the connection: {0}", "Error al probar la conexión: {0}" },
            ["err.authExpired"] = new[] { "Sessão expirada ou inválida no Your Gamer Profile. Vincule a conta novamente nas configurações do plugin.", "Session expired or invalid on Your Gamer Profile. Link your account again in the plugin settings.", "Sesión caducada o no válida en Your Gamer Profile. Vincula la cuenta de nuevo en los ajustes del plugin." },
            ["err.authShort"] = new[] { "Sessão expirada. Faça login novamente.", "Session expired. Sign in again.", "Sesión caducada. Inicia sesión de nuevo." },
            ["err.queueAuth"] = new[] { "Sessão do Your Gamer Profile expirada ou conta não vinculada. Vincule a conta nas configurações do plugin.", "Your Gamer Profile session expired or account not linked. Link the account in the plugin settings.", "Sesión de Your Gamer Profile caducada o cuenta no vinculada. Vincula la cuenta en los ajustes del plugin." },
            ["err.syncRunning"] = new[] { "Já existe uma sincronização em andamento.", "A sync is already in progress.", "Ya hay una sincronización en curso." },
            ["err.existsRefused"] = new[] { "O site já tem este jogo nesta plataforma e recusou a atualização das horas (veja o ygp-diagnostico.txt).", "The site already has this game on this platform and refused the hours update (see ygp-diagnostico.txt).", "El sitio ya tiene este juego en esta plataforma y rechazó la actualización de horas (consulta ygp-diagnostico.txt)." },
            ["err.existsHours"] = new[] { "O site já tem {0}m (igual ou maior que {1}m); nada a atualizar.", "The site already has {0}m (equal to or greater than {1}m); nothing to update.", "El sitio ya tiene {0}m (igual o mayor que {1}m); nada que actualizar." },
            ["err.dup"] = new[] { "Outra cópia do mesmo jogo/plataforma já foi enviada com mais horas.", "Another copy of the same game/platform was already sent with more hours.", "Otra copia del mismo juego/plataforma ya se envió con más horas." },
            ["err.noSearchRoute"] = new[] { "Não consegui descobrir nenhuma rota de busca do Your Gamer Profile. Use 'Diagnosticar site' nas configurações e envie o arquivo ygp-diagnostico.txt.", "Could not discover any Your Gamer Profile search route. Use 'Diagnose site' in the settings and send the ygp-diagnostico.txt file.", "No pude descubrir ninguna ruta de búsqueda de Your Gamer Profile. Usa 'Diagnosticar sitio' en los ajustes y envía el archivo ygp-diagnostico.txt." },
            ["err.noGameId"] = new[] { "A busca do site achou '{0}', mas não devolveu o ID do jogo (formato diferente do esperado). Veja o arquivo ygp-diagnostico.txt.", "The site search found '{0}' but did not return the game ID (unexpected format). See the ygp-diagnostico.txt file.", "La búsqueda del sitio encontró '{0}', pero no devolvió el ID del juego (formato inesperado). Consulta el archivo ygp-diagnostico.txt." },
            ["err.searchFailed"] = new[] { "Falha na busca do site: {0}", "Site search failed: {0}", "Falló la búsqueda del sitio: {0}" },
            ["err.notFound"] = new[] { "Jogo '{0}' não encontrado no catálogo do Your Gamer Profile", "Game '{0}' not found in the Your Gamer Profile catalog", "Juego '{0}' no encontrado en el catálogo de Your Gamer Profile" },
            ["err.notFoundSimilar"] = new[] { " (resultados parecidos: {0}).", " (similar results: {0}).", " (resultados parecidos: {0})." },
            ["user.default"] = new[] { "Usuário", "User", "Usuario" },
            ["status.disconnectedHint"] = new[] { "Desconectado (clique em Vincular conta para entrar)", "Disconnected (click Link account to sign in)", "Desconectado (haz clic en Vincular cuenta para entrar)" },
            ["status.connecting"] = new[] { "Conectando ao Your Gamer Profile...", "Connecting to Your Gamer Profile...", "Conectando con Your Gamer Profile..." },
            ["status.unknown"] = new[] { "Status desconhecido", "Unknown status", "Estado desconocido" },
            ["err.unknown"] = new[] { "Erro desconhecido", "Unknown error", "Error desconocido" },
        };
    }
}
