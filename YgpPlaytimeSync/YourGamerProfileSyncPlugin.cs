using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Controls;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Plugin principal do Your Gamer Profile Sync para Playnite.
    /// Registra o tempo jogado ao fechar um jogo, detecta edições manuais de horas e envia tudo
    /// para o Your Gamer Profile em segundo plano (sem travar a interface do Playnite).
    /// </summary>
    public class YourGamerProfileSyncPlugin : GenericPlugin
    {
        private ILogger _logger;
        private readonly string _dataPath;

        // GUID exclusivo do plugin no ecossistema Playnite
        public override Guid Id => Guid.Parse("3f5a006a-a508-4be7-b83d-05e30830abe3");

        public YourGamerProfileSyncPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };

            _logger = LogManager.GetLogger("YgpPlaytimeSync");
            _logger.Info("[YGP] === PLUGIN CARREGADO - CONSTRUTOR EXECUTADO ===");
            L.Configure(L.Auto, GetPlayniteLanguage());

            try
            {
                _dataPath = GetPluginUserDataPath();
                CreatorProfile = new CreatorProfileService(api, _logger, _dataPath);
                _logger.Info("[YGP] Inicializando Your Gamer Profile Sync...");
                _settings = LoadSettings();
                _settings.AttachOwner(this);
                ApplyLanguage();
                InitializeComponents();
            }
            catch (Exception ex)
            {
                _logger.Error("[YGP] Falha na inicialização do plugin: " + ex);
                _settings = _settings ?? new PluginSettings();
                _isInitialized = false;
            }
        }

        // Componentes do núcleo
        private SessionTracker _sessionTracker;
        private SessionStorage _sessionStorage;
        private SyncQueue _syncQueue;
        private GameIdentityResolver _identityResolver;
        private YgpProvider _ygpProvider;
        private SyncedPlaytimeStore _playtimeStore;
        private GameOptionsStore _gameOptions;
        private PlayDatesStore _playDates;

        // Configurações e estado
        private PluginSettings _settings;
        private bool _isInitialized;
        private volatile Game _currentGame;
        private DateTime _gameStartedAt;
        private string _lastTrackedGameName = L.T("lastGame.none");
        private int _lastTrackedDurationSeconds;
        private string _lastSyncSummary = "";

        // Timers
        private Timer _syncTimer;
        private Timer _syncKickTimer;
        private Timer _manualEditTimer;
        private readonly ConcurrentDictionary<Guid, byte> _manualEdits = new ConcurrentDictionary<Guid, byte>();

        public override void Dispose()
        {
            try
            {
                _logger.Info("[YGP] Descarregando plugin...");
                StopSyncTimer();

                try { _syncKickTimer?.Dispose(); } catch { }
                try { _manualEditTimer?.Dispose(); } catch { }

                try
                {
                    PlayniteApi.Database.Games.ItemUpdated -= OnDatabaseGamesUpdated;
                    PlayniteApi.Database.Games.ItemCollectionChanged -= OnDatabaseGamesAdded;
                }
                catch { }

                try
                {
                    var task = _ygpProvider?.EndBatchAsync();
                    if (task != null) task.ContinueWith(t => { var ignored = t.Exception; });
                }
                catch { }

                _syncQueue?.Dispose();
                _sessionTracker?.Dispose();
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[YGP] Erro ao descarregar plugin.");
            }

            base.Dispose();
        }

        #region Eventos do Playnite

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            try
            {
                if (args?.Game == null) return;

                _currentGame = args.Game;
                _gameStartedAt = DateTime.UtcNow;
                if (_playDates != null) _playDates.RecordSession(args.Game.Id.ToString(), _gameStartedAt);
                _logger.Info($"[YGP] Jogo iniciado: {args.Game.Name}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro em OnGameStarted: {ex.Message}");
            }
        }

        public override void OnGameStarting(OnGameStartingEventArgs args) { }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            try
            {
                if (args?.Game == null) return;

                var game = args.Game;
                var elapsedSeconds = args.ElapsedSeconds;

                _logger.Info($"[YGP] Jogo finalizado: {game.Name} (sessão: {elapsedSeconds}s | total no Playnite: {game.Playtime}s)");

                if (_sessionStorage == null || _syncQueue == null)
                {
                    _logger.Warn("[YGP] Plugin não inicializado; sessão não registrada.");
                    return;
                }

                if (_playDates != null) _playDates.RecordSession(game.Id.ToString(), DateTime.UtcNow);

                // Sincronização automática: só quando a sessão durou pelo menos 1 minuto.
                if (elapsedSeconds < 60)
                {
                    _logger.Info($"[YGP] '{game.Name}': sessão de menos de 1 minuto; não sincroniza.");
                    _currentGame = null;
                    return;
                }

                var skipReason = SkipReason(game);
                if (skipReason != null)
                {
                    _logger.Info($"[YGP] '{game.Name}' não será enviado: {skipReason}.");
                    _currentGame = null;
                    return;
                }

                var session = CreateSessionFromGameStop(game, elapsedSeconds);
                if (session != null)
                {
                    _lastTrackedGameName = session.GameName;
                    _lastTrackedDurationSeconds = session.DurationSeconds;

                    _sessionStorage.PersistSession(session);
                    _syncQueue.Enqueue(session);

                    if (_settings.SyncOnGameStop && _settings.AutoSyncEnabled && _settings.IsConnected)
                    {
                        ScheduleSync(3000);
                    }
                    else if (!_settings.IsConnected)
                    {
                        _logger.Info("[YGP] Conta não vinculada: sessão guardada e será enviada depois que você vincular a conta.");
                    }
                }

                _currentGame = null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro em OnGameStopped: {ex.Message}");
            }
        }

        /// <summary>
        /// Detecta edições manuais do tempo de jogo (propriedades do jogo, FriendlySetTime, importações).
        /// As alterações são agrupadas (debounce) para não gerar uma rajada de envios.
        /// </summary>
        private void OnDatabaseGamesUpdated(object sender, ItemUpdatedEventArgs<Game> args)
        {
            if (_settings == null || !_settings.SyncManualPlaytimeEdits || !_settings.IsConnected) return;

            try
            {
                foreach (var update in args.UpdatedItems)
                {
                    var oldGame = update.OldData;
                    var newGame = update.NewData;
                    if (oldGame == null || newGame == null) continue;
                    if (oldGame.Playtime == newGame.Playtime) continue;

                    // Enquanto o jogo roda, o OnGameStopped cuida do envio.
                    var running = _currentGame;
                    if (running != null && running.Id == newGame.Id) continue;

                    _manualEdits[newGame.Id] = 0;
                }

                if (!_manualEdits.IsEmpty)
                {
                    _manualEditTimer?.Change(8000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao processar atualização de biblioteca: {ex.Message}");
            }
        }

        /// <summary>
        /// Jogo novo na biblioteca (ex.: acabou de instalar/importar da Steam): se já veio com horas, tenta enviar ao YGP.
        /// Usa o mesmo caminho das edições manuais (aguarda alguns segundos para agrupar e checar as regras de envio).
        /// </summary>
        private void OnDatabaseGamesAdded(object sender, ItemCollectionChangedEventArgs<Game> args)
        {
            if (_settings == null || !_settings.IsConnected || args == null || args.AddedItems == null) return;

            try
            {
                foreach (var game in args.AddedItems)
                {
                    if (game == null || game.Playtime < 60) continue;
                    _manualEdits[game.Id] = 0;
                }

                if (!_manualEdits.IsEmpty)
                {
                    _manualEditTimer?.Change(8000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao processar jogo novo na biblioteca: {ex.Message}");
            }
        }

        private void FlushManualEdits()
        {
            try
            {
                var ids = _manualEdits.Keys.ToList();
                int queued = 0;

                foreach (var id in ids)
                {
                    byte ignored;
                    _manualEdits.TryRemove(id, out ignored);

                    var game = PlayniteApi.Database.Games.Get(id);
                    if (game == null || game.Playtime == 0) continue;

                    var skip = SkipReason(game);
                    if (skip != null)
                    {
                        _logger.Debug($"[YGP] Edição manual em '{game.Name}' ignorada: {skip}.");
                        continue;
                    }

                    _logger.Info($"[YGP] Alteração manual de playtime detectada em '{game.Name}': {game.Playtime / 60}m");
                    UpsertSnapshotSession(game, true);
                    queued++;
                }

                if (queued > 0 && _settings.AutoSyncEnabled && _settings.IsConnected)
                {
                    ScheduleSync(1000);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao processar edições manuais: {ex.Message}");
            }
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            try
            {
                _logger.Info("[YGP] Playnite iniciado.");

                // Envia pendências acumuladas, mas só depois que o Playnite terminou de abrir (não atrasa a inicialização).
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(45));
                        _lastAutoCompleteCheckUtc = DateTime.UtcNow;
                        ApplyAutoCompletion();
                        if (_settings != null && _settings.IsConnected && _settings.AutoSyncEnabled &&
                            _syncQueue != null && _syncQueue.PendingCount > 0)
                        {
                            _logger.Info($"[YGP] Enviando {_syncQueue.PendingCount} sessão(ões) pendente(s) acumuladas...");
                            await TrySyncNowAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"[YGP] Erro ao processar pendências iniciais: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro em OnApplicationStarted: {ex.Message}");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _logger.Info("[YGP] Playnite encerrando.");
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var games = args.Games;
            if (games == null || games.Count == 0) yield break;

            var icon = IconPath;
            var first = games[0];
            bool startOn = _gameOptions != null && StartDateEnabled(first);
            bool completedOn = _gameOptions != null && _gameOptions.Get(first.Id.ToString()).SetCompletedAtLastPlayed;

            var section = L.T("menu.section");

            yield return new GameMenuItem
            {
                MenuSection = section,
                Icon = icon,
                Description = L.T("menu.syncGame"),
                Action = a =>
                {
                    if (a.Games == null || !a.Games.Any()) return;
                    SyncLibraryWithProgress(a.Games.ToList());
                }
            };

            yield return new GameMenuItem
            {
                MenuSection = section,
                Icon = icon,
                Description = (startOn ? "[X] " : "[   ] ") + L.T("menu.startDate"),
                Action = a => ToggleStartDate(a.Games)
            };

            yield return new GameMenuItem
            {
                MenuSection = section,
                Icon = icon,
                Description = (completedOn ? "[X] " : "[   ] ") + L.T("menu.completeDate"),
                Action = a => ToggleCompletionDate(a.Games)
            };
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = L.T("menu.syncLibrary"),
                MenuSection = "@Your Gamer Profile Sync",
                Icon = IconPath,
                Action = a => SyncLibraryWithProgress(null)
            };
        }

        #endregion

        #region Datas, opções por jogo e avisos

        private string IconPath
        {
            get
            {
                try
                {
                    var dir = Path.GetDirectoryName(typeof(YourGamerProfileSyncPlugin).Assembly.Location);
                    var path = Path.Combine(dir, "Resources", "pluginicon.png");
                    return File.Exists(path) ? path : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        private bool StartDateEnabled(Game g)
        {
            var o = _gameOptions.Get(g.Id.ToString());
            return o.SendStartDate ?? _settings.SendStartDateByDefault;
        }

        private void ToggleStartDate(IList<Game> games)
        {
            if (_gameOptions == null || games == null || games.Count == 0) return;

            var newValue = !StartDateEnabled(games[0]);
            foreach (var g in games)
            {
                _gameOptions.Update(g.Id.ToString(), o => { o.SendStartDate = newValue; o.ForceResend = true; });
            }

            AfterOptionChange(games, L.T(newValue ? "opt.startOn" : "opt.startOff"));
        }

        private void ToggleCompletionDate(IList<Game> games)
        {
            if (_gameOptions == null || games == null || games.Count == 0) return;

            var newValue = !_gameOptions.Get(games[0].Id.ToString()).SetCompletedAtLastPlayed;
            foreach (var g in games)
            {
                _gameOptions.Update(g.Id.ToString(), o => { o.SetCompletedAtLastPlayed = newValue; o.ForceResend = true; });
            }

            AfterOptionChange(games, L.T(newValue ? "opt.completeOn" : "opt.completeOff"));
        }

        private void AfterOptionChange(IList<Game> games, string text)
        {
            bool auto = _settings.IsConnected && _settings.AutoSyncEnabled && _syncQueue != null;
            NotifyUser(L.T("notify.optionChanged", text, games.Count) + L.T(auto ? "notify.sendingNow" : "notify.nextSync"));

            if (!auto) return;

            foreach (var g in games)
            {
                if (SkipReason(g) == null) UpsertSnapshotSession(g, true);
            }
            ScheduleSync(1500);
        }

        private static DateTime? EarlierOf(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return a.Value <= b.Value ? a : b;
        }

        private static DateTime? LaterOf(DateTime? a, DateTime? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return a.Value >= b.Value ? a : b;
        }

        /// <summary>Primeiro e último dia jogado (GameActivity + registro do plugin + LastActivity do Playnite).</summary>
        private void ComputePlayRange(Guid id, string playniteGameId, out DateTime? first, out DateTime? last)
        {
            first = null;
            last = null;

            DateTime gaFirst, gaLast;
            if (GameActivityReader.TryGetRange(PlayniteApi.Paths.ExtensionsDataPath, id, out gaFirst, out gaLast))
            {
                first = gaFirst;
                last = gaLast;
            }

            var tracked = _playDates != null ? _playDates.Get(playniteGameId) : null;
            if (tracked != null)
            {
                if (tracked.First.HasValue) first = EarlierOf(first, tracked.First.Value.ToLocalTime());
                if (tracked.Last.HasValue) last = LaterOf(last, tracked.Last.Value.ToLocalTime());
            }

            var game = PlayniteApi.Database.Games.Get(id);
            if (game != null && game.LastActivity.HasValue)
            {
                var activity = game.LastActivity.Value;
                if (activity.Kind == DateTimeKind.Utc) activity = activity.ToLocalTime();
                last = LaterOf(last, activity);
            }

            if (first.HasValue && last.HasValue && first.Value > last.Value) first = last;
        }

        /// <summary>Último dia em que o jogo foi jogado (null se não há registro).</summary>
        private DateTime? GetLastPlayed(string playniteGameId)
        {
            try
            {
                Guid id;
                if (!Guid.TryParse(playniteGameId, out id)) return null;
                DateTime? first, last;
                ComputePlayRange(id, playniteGameId, out first, out last);
                return last;
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Erro ao ler o último dia jogado: {ex.Message}");
                return null;
            }
        }

        /// <summary>Atalho barato: se o LastActivity do Playnite é recente, o último dia jogado também é (nunca é anterior a ele).</summary>
        private bool RecentlyPlayed(Game game)
        {
            if (!game.LastActivity.HasValue) return false;
            var activity = game.LastActivity.Value;
            if (activity.Kind == DateTimeKind.Utc) activity = activity.ToLocalTime();
            return !AutoCompleteDue(activity);
        }

        /// <summary>True se a conclusão automática está ligada e o jogo está parado há dias suficientes.</summary>
        private bool AutoCompleteDue(DateTime? lastPlayed)
        {
            if (_settings == null || !_settings.AutoCompleteEnabled || !lastPlayed.HasValue) return false;
            var days = Math.Max(PluginSettings.AutoCompleteMinDays, Math.Min(PluginSettings.AutoCompleteMaxDays, _settings.AutoCompleteDays));
            return (DateTime.Now.Date - lastPlayed.Value.Date).TotalDays >= days;
        }

        /// <summary>
        /// Calcula as datas do jogo: início = primeiro dia jogado; conclusão = último dia jogado
        /// (se o usuário ativou a opção para o jogo ou se a conclusão automática se aplica).
        /// </summary>
        private YgpPlayDates ResolveDates(string playniteGameId)
        {
            var result = new YgpPlayDates();

            try
            {
                Guid id;
                if (!Guid.TryParse(playniteGameId, out id)) return result;

                var options = _gameOptions != null ? _gameOptions.Get(playniteGameId) : new GameOptions();
                bool sendStart = options.SendStartDate ?? _settings.SendStartDateByDefault;
                bool sendCompleted = options.SetCompletedAtLastPlayed;
                if (!sendStart && !sendCompleted && !_settings.AutoCompleteEnabled) return result;

                DateTime? first, last;
                ComputePlayRange(id, playniteGameId, out first, out last);

                if (sendStart) result.Start = first;
                if (sendCompleted || AutoCompleteDue(last)) result.Completed = last;
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Erro ao calcular datas: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Conclusão automática: jogos parados há N dias (configurável de 7 a 30) entram na fila para receber
        /// a data do último dia jogado como data de conclusão. Cada "último dia jogado" é enviado uma única vez.
        /// </summary>
        private int ApplyAutoCompletion()
        {
            if (_settings == null || !_settings.AutoCompleteEnabled || !_settings.IsConnected) return 0;
            if (_gameOptions == null || _sessionStorage == null || _syncQueue == null) return 0;

            int marked = 0;
            try
            {
                foreach (var game in PlayniteApi.Database.Games.ToList())
                {
                    if (game.Playtime < 60) continue;
                    if (IsIgnoredByName(game)) continue;
                    if (RecentlyPlayed(game)) continue;
                    if (_settings.IgnoreSoftware && IsLikelySoftware(game)) continue;

                    var idText = game.Id.ToString();
                    var last = GetLastPlayed(idText);
                    if (!AutoCompleteDue(last)) continue;

                    var already = _gameOptions.Get(idText).AutoCompletedFor;
                    if (already.HasValue && already.Value.Date == last.Value.Date) continue;

                    _gameOptions.Update(idText, o => o.ForceResend = true);
                    UpsertSnapshotSession(game, true);
                    marked++;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro na conclusão automática: {ex.Message}");
            }

            if (marked > 0)
            {
                _logger.Info($"[YGP] Conclusão automática: {marked} jogo(s) parado(s) há {_settings.AutoCompleteDays} dia(s) ou mais.");
                if (_settings.AutoSyncEnabled) ScheduleSync(1500);
            }
            return marked;
        }

        /// <summary>Marca (ForceResend) os jogos que acabaram de passar do limite de dias, para que a sincronização manual os inclua.</summary>
        private void ApplyAutoCompletionFlags()
        {
            if (_settings == null || !_settings.AutoCompleteEnabled || _gameOptions == null) return;
            try
            {
                foreach (var game in PlayniteApi.Database.Games.ToList())
                {
                    if (game.Playtime < 60 || RecentlyPlayed(game)) continue;
                    var idText = game.Id.ToString();
                    var last = GetLastPlayed(idText);
                    if (!AutoCompleteDue(last)) continue;
                    var already = _gameOptions.Get(idText).AutoCompletedFor;
                    if (already.HasValue && already.Value.Date == last.Value.Date) continue;
                    _gameOptions.Update(idText, o => o.ForceResend = true);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Erro ao preparar conclusão automática: {ex.Message}");
            }
        }

        /// <summary>Anota que a conclusão automática foi enviada para o último dia jogado atual do jogo.</summary>
        private void RecordAutoCompleted(string playniteGameId)
        {
            if (_settings == null || !_settings.AutoCompleteEnabled || _gameOptions == null) return;

            var last = GetLastPlayed(playniteGameId);
            if (!AutoCompleteDue(last)) return;

            var day = last.Value.Date;
            _gameOptions.Update(playniteGameId, o => o.AutoCompletedFor = day);
        }

        /// <summary>Idioma configurado no Playnite (ex.: pt_BR). Lido por reflexão para funcionar em qualquer versão do SDK.</summary>
        private string GetPlayniteLanguage()
        {
            try
            {
                var settings = PlayniteApi != null ? PlayniteApi.ApplicationSettings : null;
                if (settings == null) return null;
                var prop = settings.GetType().GetProperty("Language");
                var value = prop != null ? prop.GetValue(settings, null) as string : null;
                return value;
            }
            catch
            {
                return null;
            }
        }

        private void ApplyLanguage()
        {
            L.Configure(_settings != null ? _settings.Language : L.Auto, GetPlayniteLanguage());
        }

        /// <summary>Usado pela tela de configurações quando o usuário troca o idioma.</summary>
        public void ChangeLanguage(string preference)
        {
            if (_settings != null) _settings.Language = string.IsNullOrWhiteSpace(preference) ? L.Auto : preference;
            ApplyLanguage();
            PersistSettingsQuietly();
        }

        private void NotifyUser(string text)
        {
            try
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "ygp-sync-" + Guid.NewGuid().ToString("N"),
                    "Your Gamer Profile Sync: " + text,
                    NotificationType.Info));
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Não foi possível mostrar a notificação: {ex.Message}");
            }
        }

        private static string FormatMinutes(int minutes)
        {
            if (minutes < 60) return minutes + "min";
            return (minutes / 60) + "h " + (minutes % 60).ToString("00") + "min";
        }

        /// <summary>Aviso no Playnite depois de uma sincronização automática: o que subiu e o total geral.</summary>
        private void NotifySynced(SyncResult result)
        {
            if (!_settings.ShowSyncNotifications || result == null || result.Synced.Count == 0) return;

            var general = _playtimeStore != null ? _playtimeStore.TotalMinutes() : 0;

            if (result.Synced.Count == 1)
            {
                var g = result.Synced[0];
                NotifyUser(L.T("notify.single", g.Name, FormatMinutes(g.AddedMinutes), FormatMinutes(g.TotalMinutes), FormatMinutes(general)));
            }
            else
            {
                var added = result.Synced.Sum(x => x.AddedMinutes);
                NotifyUser(L.T("notify.multi", result.Synced.Count, FormatMinutes(added), FormatMinutes(general)));
            }
        }

        #endregion

        #region Regras de envio (o que ignorar)

        private static readonly string[] SoftwareMarkers =
        {
            "software", "utilities", "utility", "application", "applications", "tool", "tools", "design & illustration",
            "animation & modeling", "audio production", "video production", "photo editing", "web publishing",
            "software training", "game development"
        };

        private static bool IsLikelySoftware(Game g)
        {
            try
            {
                var names = new List<string>();
                if (g.Genres != null) names.AddRange(g.Genres.Select(x => x.Name));
                if (g.Tags != null) names.AddRange(g.Tags.Select(x => x.Name));
                if (g.Categories != null) names.AddRange(g.Categories.Select(x => x.Name));

                return names.Any(n => !string.IsNullOrEmpty(n) && SoftwareMarkers.Contains(n.Trim().ToLowerInvariant()));
            }
            catch
            {
                return false;
            }
        }

        // Programas muito comuns que aparecem na biblioteca como "jogo" mas não são jogos (ignorados quando
        // "Ignorar aplicativos/software" está ligado), além da lista de nomes do usuário.
        private static readonly string[] KnownApps =
        {
            "OCCT", "Wallpaper Engine", "MSI Afterburner", "HWiNFO", "CPU-Z", "GPU-Z", "FurMark", "AIDA64",
            "Cinebench", "OBS Studio", "Blender", "Lossless Scaling", "Process Lasso"
        };

        private bool IsKnownApp(Game g)
        {
            if (_settings == null || !_settings.IgnoreSoftware) return false;
            var key = YgpApiClient.DuplicateKey(g.Name);
            return key.Length > 0 && KnownApps.Any(a => YgpApiClient.DuplicateKey(a) == key);
        }

        private bool IsIgnoredByName(Game g)
        {
            if (IsKnownApp(g)) return true;

            var raw = _settings.IgnoredGameNames;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var key = YgpApiClient.DuplicateKey(g.Name);
            foreach (var part in raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var k = YgpApiClient.DuplicateKey(part.Trim());
                if (k.Length > 0 && k == key) return true;
            }
            return false;
        }

        /// <summary>Motivo pelo qual o jogo NÃO deve ser enviado agora (null = pode enviar).</summary>
        private string SkipReason(Game g)
        {
            if (g == null) return L.T("skip.invalid");
            if (g.Playtime < 60) return L.T("skip.under1min");
            if (IsIgnoredByName(g)) return L.T("skip.ignoredList");
            if (_settings.IgnoreSoftware && IsLikelySoftware(g)) return L.T("skip.software");
            var force = _gameOptions != null && _gameOptions.Get(g.Id.ToString()).ForceResend;
            if (!force && _playtimeStore != null && !_playtimeStore.HasIncreased(g.Id.ToString(), ToSeconds(g.Playtime))) return L.T("skip.noIncrease");
            return null;
        }

        /// <summary>Marca como ignoradas as sessões pendentes/falhas de jogos que hoje não devem ser enviados (apps, lista de ignorados, tempo sem mudança).</summary>
        private int PurgeIneligibleSessions()
        {
            if (_sessionStorage == null) return 0;

            int purged = 0;
            var all = _sessionStorage.GetPendingSessions().Concat(_sessionStorage.GetFailedSessions()).ToList();
            foreach (var s in all)
            {
                Guid id;
                Game game = null;
                if (Guid.TryParse(s.PlayniteGameId, out id)) game = PlayniteApi.Database.Games.Get(id);

                var reason = game == null ? L.T("skip.notInLibrary") : SkipReason(game);
                if (reason != null)
                {
                    s.SyncStatus = SyncStatus.Ignored;
                    _sessionStorage.UpdateSessionStatusAsync(s).GetAwaiter().GetResult();
                    purged++;
                }
            }

            if (purged > 0) _logger.Info($"[YGP] {purged} sessão(ões) pendente(s) descartada(s) (aplicativos, ignorados ou sem aumento de tempo).");
            return purged;
        }

        /// <summary>
        /// Jogos repetidos na biblioteca (ex.: Steam + Epic, ou cópias que o DuplicateHider/DuplicateFinder escondem):
        /// mantém só a cópia com mais horas por jogo+plataforma, para não enviar horas diferentes do mesmo jogo.
        /// </summary>
        private List<Game> RemoveDuplicates(List<Game> list, out int removed)
        {
            var best = new Dictionary<string, Game>();
            var order = new List<string>();

            foreach (var g in list)
            {
                var title = YgpApiClient.DuplicateKey(g.Name);
                var key = (title.Length > 0 ? title : g.Id.ToString()) + "|" + GetYgpPlatform(g);

                Game current;
                if (!best.TryGetValue(key, out current))
                {
                    best[key] = g;
                    order.Add(key);
                }
                else if (g.Playtime > current.Playtime)
                {
                    best[key] = g;
                }
            }

            removed = list.Count - best.Count;
            return order.Select(k => best[k]).ToList();
        }

        #endregion

        #region Operações de Sincronização

        private DateTime _lastAutoCompleteCheckUtc = DateTime.MinValue;

        private bool CanSyncInBackground()
        {
            return _isInitialized && _settings != null && _settings.IsConnected && _settings.AutoSyncEnabled &&
                   _syncQueue != null && _ygpProvider != null;
        }

        private void StartSyncTimer(int intervalMinutes)
        {
            StopSyncTimer();

            var interval = TimeSpan.FromMinutes(intervalMinutes);
            _syncTimer = new Timer(_ =>
            {
                try
                {
                    // Só trabalha se houver algo a enviar: nada de abrir navegador à toa.
                    if (CanSyncInBackground() && _settings.AutoCompleteEnabled &&
                        (DateTime.UtcNow - _lastAutoCompleteCheckUtc).TotalMinutes >= 60)
                    {
                        _lastAutoCompleteCheckUtc = DateTime.UtcNow;
                        ApplyAutoCompletion();
                    }

                    if (CanSyncInBackground() && _syncQueue.PendingCount > 0)
                    {
                        Task.Run(() => TrySyncNowAsync());
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"[YGP] Erro no timer de sincronização: {ex.Message}");
                }
            }, null, interval, interval);

            _logger.Info($"[YGP] Verificação de pendências a cada {interval.TotalMinutes} minuto(s).");
        }

        private void StopSyncTimer()
        {
            if (_syncTimer != null)
            {
                _syncTimer.Dispose();
                _syncTimer = null;
            }
        }

        /// <summary>Agenda uma sincronização em segundo plano (várias chamadas seguidas viram uma só).</summary>
        private void ScheduleSync(int delayMs)
        {
            try { _syncKickTimer?.Change(delayMs, Timeout.Infinite); }
            catch (Exception ex) { _logger.Debug($"[YGP] Falha ao agendar sincronização: {ex.Message}"); }
        }

        /// <summary>Sincronização manual (botão "Sincronizar Fila Agora"): reenvia inclusive as que falharam.</summary>
        public async Task<SyncResult> SyncNowAsync()
        {
            _logger.Info("[YGP] Sincronização manual acionada.");

            if (_syncQueue == null || _sessionStorage == null)
            {
                var failed = new SyncResult { Error = L.T("err.notInit") };
                return failed;
            }

            PurgeIneligibleSessions();
            _sessionStorage.RequeueFailed();
            var result = await _syncQueue.ProcessQueueAsync(_ygpProvider, int.MaxValue, null, default(CancellationToken), true);
            RecordResult(result);
            return result;
        }

        private async Task TrySyncNowAsync()
        {
            try
            {
                if (!CanSyncInBackground()) return;

                PurgeIneligibleSessions();
                var result = await _syncQueue.ProcessQueueAsync(_ygpProvider);
                RecordResult(result);
                NotifySynced(result);

                // Havia outra sincronização rodando (jogo novo entrou na fila no meio): tenta de novo logo.
                if (result.AlreadyRunning)
                {
                    ScheduleSync(8000);
                    return;
                }

                // Ainda há jogos na fila e o lote anterior avançou: continua logo em seguida.
                int progressed = result.SuccessfulCount + result.AlreadyOnProfileCount + result.DuplicatesSkipped;
                if (progressed > 0 && !result.SkippedDueToUnavailable && _syncQueue.PendingCount > 0)
                {
                    ScheduleSync(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Falha ao sincronizar: {ex.Message}");
            }
        }

        private void RecordResult(SyncResult result)
        {
            if (result == null || result.AlreadyRunning) return;
            if (result.SuccessfulCount == 0 && result.FailedCount == 0 && result.AlreadyOnProfileCount == 0 && string.IsNullOrEmpty(result.Error)) return;

            _lastSyncSummary = L.T("res.summary", DateTime.Now.ToString("dd/MM HH:mm"), result.SuccessfulCount, result.AlreadyOnProfileCount, result.FailedCount) +
                               (string.IsNullOrEmpty(result.Error) ? "" : $" ({result.Error})");
            _logger.Info("[YGP] " + _lastSyncSummary);
        }

        private static string BuildResultMessage(SyncResult r)
        {
            if (r == null) return L.T("res.notRun");
            if (r.AlreadyRunning) return L.T("msg.alreadyRunning");
            if (r.SuccessfulCount == 0 && r.FailedCount == 0 && r.AlreadyOnProfileCount == 0 && r.DuplicatesSkipped == 0)
            {
                return string.IsNullOrEmpty(r.Error) ? L.T("res.noChanges") : r.Error;
            }

            var sb = new StringBuilder();
            sb.AppendLine(L.T("res.sent", r.SuccessfulCount));
            if (r.Synced.Count > 0) sb.AppendLine(L.T("res.added", FormatMinutes(r.Synced.Sum(x => x.AddedMinutes))));
            if (r.DuplicatesSkipped > 0) sb.AppendLine(L.T("res.dups", r.DuplicatesSkipped));
            if (r.AlreadyOnProfileCount > 0)
            {
                sb.AppendLine(L.T("res.already", r.AlreadyOnProfileCount));
                sb.AppendLine("  " + string.Join(", ", r.AlreadyOnProfileGames.Take(8)) + (r.AlreadyOnProfileGames.Count > 8 ? ", ..." : ""));
                sb.AppendLine("  " + L.T("res.alreadyHint"));
            }
            if (r.FailedCount > 0)
            {
                sb.AppendLine(L.T("res.failures", r.FailedCount));
                foreach (var e in r.Errors.Take(5)) sb.AppendLine("- " + e);
                if (r.Errors.Count > 5) sb.AppendLine(L.T("res.moreErrors", r.Errors.Count - 5));
            }
            if (r.SkippedDueToUnavailable && !string.IsNullOrEmpty(r.Error)) sb.AppendLine(r.Error);
            if (r.Cancelled) sb.AppendLine(L.T("res.cancelled"));
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Guarda o tempo atual do jogo na fila. Se já existir uma sessão pendente do mesmo jogo, apenas atualiza o total.
        /// </summary>
        private GameSession UpsertSnapshotSession(Game game, bool manual)
        {
            var gameId = game.Id.ToString();
            var total = ToSeconds(game.Playtime);
            var existing = _sessionStorage.FindPendingByGame(gameId);
            var forceResend = _gameOptions != null && _gameOptions.Get(gameId).ForceResend;

            if (existing != null)
            {
                existing.ForceResend = existing.ForceResend || forceResend;
                existing.TotalPlaytimeSeconds = total;
                existing.EndedAtUtc = DateTime.UtcNow;
                existing.IsManualEdit = existing.IsManualEdit || manual;
                existing.RetryCount = 0;
                existing.LastAttemptAtUtc = null;
                existing.LastError = null;
                _sessionStorage.PersistSession(existing);
                return existing;
            }

            var now = DateTime.UtcNow;
            var session = new GameSession
            {
                Id = GameSession.GenerateIdempotencyKey(gameId, now, now),
                PlayniteGameId = gameId,
                GameName = game.Name,
                Source = game.Source?.Name ?? "PC",
                SourceId = game.GameId ?? string.Empty,
                Platform = GetYgpPlatform(game),
                StartedAtUtc = now,
                EndedAtUtc = now,
                DurationSeconds = 0,
                TotalPlaytimeSeconds = total,
                IsManualEdit = manual,
                ForceResend = forceResend,
                SyncStatus = SyncStatus.Pending
            };

            _sessionStorage.PersistSession(session);
            _syncQueue.Enqueue(session);
            return session;
        }

        /// <summary>
        /// Envia o tempo de jogo dos jogos informados (ou da biblioteca inteira, quando null) com barra de progresso.
        /// Serve tanto para sincronizar os jogos antigos quanto para o menu de contexto.
        /// </summary>
        public void SyncLibraryWithProgress(IList<Game> games)
        {
            if (_syncQueue == null || _sessionStorage == null || _ygpProvider == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(L.T("err.notInit"), L.T("app.name"));
                return;
            }

            if (!_settings.IsConnected)
            {
                PlayniteApi.Dialogs.ShowMessage(L.T("lib.needLink"), L.T("app.name"));
                return;
            }

            ApplyAutoCompletionFlags();

            IEnumerable<Game> source = games != null ? (IEnumerable<Game>)games : PlayniteApi.Database.Games;

            // Aplica as regras: menos de 1 minuto, aplicativos, lista de ignorados, tempo que não aumentou e duplicados.
            var reasons = new Dictionary<string, int>();
            var eligible = new List<Game>();
            foreach (var g in source)
            {
                if (g.Playtime == 0) continue;

                var reason = SkipReason(g);
                if (reason == null)
                {
                    eligible.Add(g);
                }
                else
                {
                    int count;
                    reasons[reason] = reasons.TryGetValue(reason, out count) ? count + 1 : 1;
                }
            }

            int duplicates;
            var list = RemoveDuplicates(eligible, out duplicates);
            if (duplicates > 0) reasons[L.T("skip.duplicate")] = duplicates;

            var skippedText = reasons.Count == 0
                ? ""
                : "\n" + L.T("lib.ignored", string.Join(", ", reasons.Select(kv => $"{kv.Value} ({kv.Key})")));
            _logger.Info($"[YGP] Biblioteca: {list.Count} jogo(s) a enviar.{skippedText.Replace("\n", " ")}");

            if (list.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(L.T("lib.nothing") + skippedText, L.T("app.name"));
                return;
            }

            if (list.Count > 25)
            {
                var answer = PlayniteApi.Dialogs.ShowMessage(
                    L.T("lib.confirm", list.Count) + skippedText,
                    L.T("app.name"),
                    MessageBoxButton.YesNo);
                if (answer != MessageBoxResult.Yes) return;
            }

            PurgeIneligibleSessions();
            foreach (var g in list)
            {
                UpsertSnapshotSession(g, true);
            }
            _sessionStorage.RequeueFailed();

            SyncResult result = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(a =>
            {
                a.Text = L.T("lib.progress");
                result = Task.Run(() => _syncQueue.ProcessQueueAsync(
                    _ygpProvider,
                    int.MaxValue,
                    (done, total, name) =>
                    {
                        a.ProgressMaxValue = total;
                        a.CurrentProgressValue = done;
                        a.Text = $"({done}/{total}) {name}";
                    },
                    a.CancelToken,
                    true)).GetAwaiter().GetResult();
            }, new GlobalProgressOptions(L.T("lib.progressTitle"), true));

            RecordResult(result);
            var generalText = _playtimeStore != null ? "\n" + L.T("lib.general", FormatMinutes(_playtimeStore.TotalMinutes())) : "";
            PlayniteApi.Dialogs.ShowMessage(BuildResultMessage(result) + generalText + skippedText, L.T("app.name"));
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                var result = await _ygpProvider.TestConnectionAsync();
                _logger.Info($"[YGP] Teste de conectividade: {(result ? "OK" : "FALHOU")}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro no teste de conectividade: {ex.Message}");
            }
        }

        #endregion

        #region Configurações e UI

        public override ISettings GetSettings(bool firstRunSettings)
        {
            if (_settings == null) _settings = LoadSettings();
            _settings.AttachOwner(this);
            return _settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            if (_settings == null) _settings = LoadSettings();
            _settings.AttachOwner(this);
            return new YgpSyncSettingsControl(_settings, this);
        }

        private PluginSettings LoadSettings()
        {
            try
            {
                var loaded = LoadPluginSettings<PluginSettings>();
                return loaded ?? new PluginSettings();
            }
            catch (Exception ex)
            {
                _logger.Warn("[YGP] Erro ao carregar configurações: " + ex.Message);
                return new PluginSettings();
            }
        }

        private void PersistSettingsQuietly()
        {
            try
            {
                if (_settings != null) SavePluginSettings(_settings);
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Não foi possível salvar as configurações: {ex.Message}");
            }
        }

        /// <summary>
        /// Chave de ordenação da fila: quando o jogo foi jogado por último (UTC).
        /// A fila envia do mais antigo para o mais recente; assim os jogos jogados por último são editados por último
        /// e aparecem no topo dos "recentes" do perfil no site.
        /// </summary>
        private DateTime? ResolveLastPlayedForOrdering(string playniteGameId)
        {
            try
            {
                Guid id;
                if (!Guid.TryParse(playniteGameId, out id)) return null;
                var game = PlayniteApi.Database.Games.Get(id);
                if (game != null && game.LastActivity.HasValue)
                {
                    var activity = game.LastActivity.Value;
                    return activity.Kind == DateTimeKind.Utc ? activity : activity.ToUniversalTime();
                }

                var tracked = _playDates != null ? _playDates.Get(playniteGameId) : null;
                if (tracked != null && tracked.Last.HasValue) return tracked.Last.Value.ToUniversalTime();
            }
            catch
            {
            }
            return null;
        }

        private ulong? ResolveCurrentPlaytime(string playniteGameId)
        {
            Guid id;
            if (!Guid.TryParse(playniteGameId, out id)) return null;
            var game = PlayniteApi.Database.Games.Get(id);
            return game?.Playtime;
        }

        private void InitializeComponents()
        {
            if (_sessionStorage != null) return;

            _logger.Info("[YGP] Inicializando banco de sessões e resolvedor...");
            _sessionStorage = new SessionStorage(_logger, _dataPath);
            _sessionTracker = new SessionTracker(_logger, _sessionStorage);
            _syncQueue = new SyncQueue(_logger, _sessionStorage);
            _syncQueue.LastPlayedResolver = ResolveLastPlayedForOrdering;
            _identityResolver = new GameIdentityResolver(_logger, _dataPath);

            _ygpProvider = new YgpProvider(PlayniteApi, _settings, _logger, _identityResolver);
            _playtimeStore = new SyncedPlaytimeStore(_logger, _dataPath);
            if (_playtimeStore.WasEmpty)
            {
                _playtimeStore.SeedFrom(_sessionStorage.GetSyncedSessions());
                _logger.Info($"[YGP] Registro de tempos enviados criado a partir de {_playtimeStore.Count} jogo(s) já sincronizado(s).");
            }
            _ygpProvider.PlaytimeStore = _playtimeStore;

            // Versão 0.4.1: o registro antigo marcava como "tratados" jogos cujas horas NÃO tinham sido atualizadas no site.
            if (_settings.StoreSchemaVersion < 2)
            {
                _playtimeStore.Clear();
                _settings.StoreSchemaVersion = 2;
                PersistSettingsQuietly();
                _logger.Info("[YGP] Registro de tempos enviados reiniciado (correção da atualização de horas). Rode 'Sincronizar Biblioteca Inteira' uma vez.");
            }

            _gameOptions = new GameOptionsStore(_logger, _dataPath);
            _playDates = new PlayDatesStore(_logger, _dataPath);
            _ygpProvider.DatesResolver = ResolveDates;
            _ygpProvider.OnGameSynced = id =>
            {
                try { _gameOptions.Update(id, o => o.ForceResend = false); }
                catch { }
                try { RecordAutoCompleted(id); }
                catch { }
            };
            _ygpProvider.PlaytimeResolver = ResolveCurrentPlaytime;
            _ygpProvider.SettingsChanged = PersistSettingsQuietly;
            _ygpProvider.DiagnosticsDirectory = _dataPath;

            _syncKickTimer = new Timer(_ =>
            {
                try { Task.Run(() => TrySyncNowAsync()); }
                catch (Exception ex) { _logger.Debug($"[YGP] Erro ao iniciar sincronização agendada: {ex.Message}"); }
            }, null, Timeout.Infinite, Timeout.Infinite);

            _manualEditTimer = new Timer(_ => FlushManualEdits(), null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                PlayniteApi.Database.Games.ItemUpdated += OnDatabaseGamesUpdated;
                PlayniteApi.Database.Games.ItemCollectionChanged += OnDatabaseGamesAdded;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[YGP] Não foi possível registrar ItemUpdated: {ex.Message}");
            }

            if (_settings.AutoSyncEnabled && _settings.SyncIntervalMinutes > 0)
            {
                StartSyncTimer(_settings.SyncIntervalMinutes);
            }

            _isInitialized = true;
            _logger.Info("[YGP] Componentes inicializados com sucesso.");
        }

        public async Task SaveSettingsAsync(PluginSettings settings)
        {
            _settings = settings;
            ApplyLanguage();

            if (_isInitialized)
            {
                if (_settings.AutoSyncEnabled && _settings.SyncIntervalMinutes > 0)
                {
                    StartSyncTimer(_settings.SyncIntervalMinutes);
                }
                else
                {
                    StopSyncTimer();
                }
            }

            try
            {
                SavePluginSettings(settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[YGP] Erro ao salvar configurações: " + ex.Message);
                throw;
            }

            if (_isInitialized && _settings.AutoCompleteEnabled)
            {
                Task.Run(() =>
                {
                    var queued = ApplyAutoCompletion();
                    if (queued > 0) NotifyUser(L.T("notify.autoComplete", queued, _settings.AutoCompleteDays));
                });
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Comandos de Ação Pública

        public SessionStorage SessionStorage => _sessionStorage;
        public SyncQueue SyncQueue => _syncQueue;
        public IGameProfileProvider YgpProvider => _ygpProvider;
        public GameIdentityResolver IdentityResolver => _identityResolver;
        public PluginSettings Settings => _settings;
        public CreatorProfileService CreatorProfile { get; private set; }

        public int PendingSessionCount => _sessionStorage?.PendingCount ?? 0;
        public int SyncedSessionCount => _sessionStorage?.SyncedCount ?? 0;
        public int FailedSessionCount => _sessionStorage?.FailedCount ?? 0;
        public string ConnectionStatusText => _ygpProvider?.GetStatusMessage() ?? L.T("status.unknown");
        public string LastConnectionMessage => _ygpProvider?.LastConnectionMessage ?? "";
        public string LastSyncSummary => _lastSyncSummary;
        public string DataDirectory => _dataPath;

        public async Task<string> LearnFromSiteAsync()
        {
            if (_ygpProvider == null) return L.T("err.notInit");
            var text = await _ygpProvider.LearnFromSiteAsync();
            PersistSettingsQuietly();
            return text;
        }

        public async Task<string> RunDiagnosticsAsync()
        {
            if (_ygpProvider == null) return L.T("err.notInit");
            var text = await _ygpProvider.RunDiagnosticsAsync();
            PersistSettingsQuietly();
            return text;
        }
        public string LastTrackedGameName => _lastTrackedGameName;
        public int LastTrackedDurationSeconds => _lastTrackedDurationSeconds;

        private static int ToSeconds(ulong value)
        {
            return (int)Math.Min(value, (ulong)int.MaxValue);
        }

        private GameSession CreateSessionFromGameStop(Game game, ulong elapsedSeconds)
        {
            if (game == null) return null;

            var endedAt = DateTime.UtcNow;
            var startedAt = _gameStartedAt;
            if (startedAt == default(DateTime) || startedAt > endedAt)
            {
                startedAt = endedAt.AddSeconds(-(double)elapsedSeconds);
            }

            var session = new GameSession
            {
                Id = GameSession.GenerateIdempotencyKey(game.Id.ToString(), startedAt, endedAt),
                PlayniteGameId = game.Id.ToString(),
                GameName = game.Name,
                Source = game.Source?.Name ?? "PC",
                SourceId = game.GameId ?? string.Empty,
                Platform = GetYgpPlatform(game),
                StartedAtUtc = startedAt,
                EndedAtUtc = endedAt,
                DurationSeconds = ToSeconds(elapsedSeconds),
                TotalPlaytimeSeconds = ToSeconds(game.Playtime),
                IsManualEdit = false,
                SyncStatus = SyncStatus.Pending
            };

            return session;
        }

        /// <summary>
        /// Determina o nome da plataforma a ser enviado ao Your Gamer Profile para um jogo do Playnite.
        /// Prioridade: campo customizado YGPPlatform (sobrescrita explícita) > campo Emulator/GameSystem
        /// (detecção automática para jogos em emulador) > plataforma declarada no Playnite.
        /// </summary>
        private static string GetYgpPlatform(Game game)
        {
            var customFields = GetCustomFields(game);
            if (customFields != null)
            {
                string explicitPlatform;
                if (customFields.TryGetValue("YGPPlatform", out explicitPlatform) && !string.IsNullOrWhiteSpace(explicitPlatform))
                {
                    var mapped = YgpApiClient.MapPlatform(explicitPlatform.Trim());
                    return mapped == explicitPlatform.Trim() ? explicitPlatform.Trim() : mapped;
                }

                foreach (var key in new[] { "Emulator", "emulator", "GameSystem", "gamesystem" })
                {
                    string emulator;
                    if (customFields.TryGetValue(key, out emulator) && !string.IsNullOrWhiteSpace(emulator))
                    {
                        return YgpApiClient.MapPlatformWithEmulator(emulator);
                    }
                }
            }

            var pName = game.Platforms?.FirstOrDefault()?.Name;
            return string.IsNullOrEmpty(pName) ? "PC (Microsoft Windows)" : YgpApiClient.MapPlatform(pName);
        }

        private static IDictionary<string, string> GetCustomFields(Game game)
        {
            try
            {
                var prop = typeof(Game).GetProperty("CustomFields");
                if (prop != null)
                {
                    var value = prop.GetValue(game);
                    var dict = value as IDictionary<string, string>;
                    if (dict != null) return dict;
                }
            }
            catch { }
            return null;
        }

        public async Task<bool> ConnectCommandAsync()
        {
            try
            {
                _logger.Info("[YGP] Abrindo tela de vinculação de conta...");
                var success = await _ygpProvider.AuthenticateAsync();
                if (!success) return false;

                await SaveSettingsAsync(_settings);

                // Confirma a sessão e preenche nome de usuário/avatar.
                await TestConnectionCommandAsync();
                await SaveSettingsAsync(_settings);

                if (_settings.IsConnected && _syncQueue != null && _syncQueue.PendingCount > 0)
                {
                    ScheduleSync(3000);
                }

                return _settings.IsConnected;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao autenticar: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ImportSessionCookieCommandAsync()
        {
            try
            {
                var message = L.T("cookie.prompt");

                var answer = PlayniteApi.Dialogs.SelectString(message, L.T("cookie.title"), "");
                if (answer == null || !answer.Result || string.IsNullOrWhiteSpace(answer.SelectedString)) return false;

                if (!_ygpProvider.ImportCookies(answer.SelectedString))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(L.T("cookie.invalid"), L.T("app.name"));
                    return false;
                }

                await SaveSettingsAsync(_settings);
                await TestConnectionCommandAsync();
                await SaveSettingsAsync(_settings);

                if (_settings.IsConnected && _syncQueue != null && _syncQueue.PendingCount > 0)
                {
                    ScheduleSync(3000);
                }

                return _settings.IsConnected;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao importar cookie: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectCommandAsync()
        {
            try
            {
                await _ygpProvider.DisconnectAsync();
                await SaveSettingsAsync(_settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro ao desconectar: {ex.Message}");
            }
        }

        public async Task<bool> TestConnectionCommandAsync()
        {
            try
            {
                return await _ygpProvider.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"[YGP] Erro no comando TestConnection: {ex.Message}");
                return false;
            }
        }

        public async Task ClearAllDataAsync()
        {
            if (_playtimeStore != null) _playtimeStore.Clear();
            if (_sessionStorage != null) await _sessionStorage.ClearAllDataAsync();
            if (_identityResolver != null) await _identityResolver.ClearAllMappingsAsync();
        }

        #endregion
    }
}
