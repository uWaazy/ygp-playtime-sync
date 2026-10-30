using System;
using System.Threading.Tasks;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Provedor de integração com o Your Gamer Profile.
    /// Gerencia a autenticação via WebView do Playnite e o envio do tempo de jogo.
    /// </summary>
    public class YgpProvider : IGameProfileProvider, ISyncBatchAware
    {
        private readonly IPlayniteAPI _playniteApi;
        private readonly PluginSettings _settings;
        private readonly ILogger _logger;
        private readonly YgpApiClient _apiClient;
        private readonly GameIdentityResolver _identityResolver;
        private ProviderConnectionStatus _connectionStatus = ProviderConnectionStatus.NotConfigured;

        /// <summary>
        /// Devolve o tempo total ATUAL (em segundos) de um jogo do Playnite, lido da biblioteca no momento do envio.
        /// Evita enviar totais antigos quando há várias sessões na fila.
        /// </summary>
        public Func<string, ulong?> PlaytimeResolver { get; set; }

        public Action SettingsChanged
        {
            get { return _apiClient.SettingsChanged; }
            set { _apiClient.SettingsChanged = value; }
        }

        public string LastConnectionMessage => _apiClient.LastTestMessage;

        /// <summary>Registro do último tempo enviado por jogo (só reenvia quando o tempo sobe).</summary>
        public SyncedPlaytimeStore PlaytimeStore { get; set; }

        /// <summary>Devolve as datas (início/conclusão) a enviar para um jogo do Playnite.</summary>
        public Func<string, YgpPlayDates> DatesResolver { get; set; }

        /// <summary>Chamado depois que um jogo foi enviado com sucesso (limpa o "reenviar" do jogo).</summary>
        public Action<string> OnGameSynced { get; set; }

        public Task<string> LearnFromSiteAsync()
        {
            return _apiClient.LearnFromSiteAsync();
        }

        public string DiagnosticsDirectory
        {
            get { return _apiClient.DiagnosticsDirectory; }
            set { _apiClient.DiagnosticsDirectory = value; }
        }

        public Task<string> RunDiagnosticsAsync()
        {
            return _apiClient.RunDiagnosticsAsync();
        }

        public YgpProvider(IPlayniteAPI playniteApi, PluginSettings settings, ILogger logger, GameIdentityResolver identityResolver)
        {
            _playniteApi = playniteApi;
            _settings = settings;
            _logger = logger;
            _identityResolver = identityResolver;
            _apiClient = new YgpApiClient(playniteApi, settings, logger);

            UpdateInitialStatus();
        }

        private void UpdateInitialStatus()
        {
            _connectionStatus = _settings.IsConnected && !string.IsNullOrWhiteSpace(_settings.AuthCookiesJson)
                ? ProviderConnectionStatus.Connected
                : ProviderConnectionStatus.Disconnected;
        }

        public string ProviderName => "Your Gamer Profile";

        public ProviderConnectionStatus ConnectionStatus
        {
            get { return _connectionStatus; }
            private set
            {
                if (_connectionStatus != value)
                {
                    _connectionStatus = value;
                    OnConnectionStatusChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<ProviderConnectionStatus> OnConnectionStatusChanged;

        public async Task<bool> TestConnectionAsync()
        {
            _logger.Info("[YGP] Iniciando teste de conexão...");
            ConnectionStatus = ProviderConnectionStatus.Connecting;

            var result = await _apiClient.TestConnectionAsync();
            ConnectionStatus = result ? ProviderConnectionStatus.Connected : ProviderConnectionStatus.Disconnected;
            return result;
        }

        public async Task<bool> AuthenticateAsync()
        {
            _logger.Info("[YGP] Solicitando autenticação interativa...");
            ConnectionStatus = ProviderConnectionStatus.Connecting;

            var success = await _apiClient.LoginViaWebViewAsync();
            ConnectionStatus = success ? ProviderConnectionStatus.Connected : ProviderConnectionStatus.Disconnected;
            return success;
        }

        public bool ImportCookies(string text)
        {
            var ok = _apiClient.ImportCookieText(text);
            ConnectionStatus = ok ? ProviderConnectionStatus.Connected : ProviderConnectionStatus.Disconnected;
            return ok;
        }

        public Task<ProviderProfile> GetProfileAsync()
        {
            return Task.FromResult(new ProviderProfile
            {
                Username = string.IsNullOrWhiteSpace(_settings.Username) ? L.T("status.connected") : _settings.Username,
                UserId = _settings.UserId ?? "",
                DisplayName = _settings.Username ?? L.T("user.default"),
                AvatarUrl = _settings.AvatarUrl ?? "",
                ProfileLastUpdatedUtc = _settings.LastSyncUtc ?? DateTime.UtcNow
            });
        }

        public Task<GameIdentity> FindGameAsync(string gameName, string platform = null)
        {
            return Task.FromResult(new GameIdentity
            {
                YgpGameId = gameName,
                YgpGameName = gameName,
                Platform = platform ?? "PC"
            });
        }

        public async Task<bool> SyncSessionAsync(GameSession session)
        {
            if (session == null) return false;

            var totalSeconds = session.TotalPlaytimeSeconds;
            try
            {
                var live = PlaytimeResolver?.Invoke(session.PlayniteGameId);
                if (live.HasValue && live.Value > 0)
                {
                    totalSeconds = (int)Math.Min(live.Value, (ulong)int.MaxValue);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Não foi possível ler o tempo atual do jogo: {ex.Message}");
            }

            if (totalSeconds <= 0) totalSeconds = session.DurationSeconds;

            if (totalSeconds / 60 < 1)
            {
                _logger.Debug($"[YGP] '{session.GameName}' com menos de 1 minuto; ignorado.");
                session.LastError = "[SKIP]";
                return true;
            }

            var store = PlaytimeStore;
            if (!session.ForceResend && store != null && !store.HasIncreased(session.PlayniteGameId, totalSeconds))
            {
                _logger.Debug($"[YGP] '{session.GameName}': o tempo não aumentou desde o último envio; ignorado.");
                session.LastError = "[SKIP]";
                return true;
            }

            string knownGameId = null;
            try
            {
                var mapping = await _identityResolver.GetMappingAsync(session.PlayniteGameId);
                if (mapping != null && !string.IsNullOrEmpty(mapping.YgpGameId))
                {
                    // Mapeamento antigo que não combina mais com o nome (ex.: "Dead by Daylight" -> "...Mobile"): descarta.
                    if (!string.IsNullOrEmpty(mapping.YgpGameName) && !YgpApiClient.TitlesMatch(session.GameName, mapping.YgpGameName))
                    {
                        _logger.Warn($"[YGP] Mapeamento antigo descartado: '{session.GameName}' -> '{mapping.YgpGameName}'.");
                    }
                    else
                    {
                        knownGameId = mapping.YgpGameId;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"[YGP] Erro ao ler mapeamento: {ex.Message}");
            }

            var platformKey = YgpApiClient.MapPlatform(session.Platform);
            bool forced = session.ForceResend;
            Func<string, bool> allowWrite = ygpId => forced || store == null || !store.Covers("ygp:" + ygpId + "|" + platformKey, totalSeconds);

            YgpPlayDates dates = null;
            try { dates = DatesResolver?.Invoke(session.PlayniteGameId); }
            catch (Exception ex) { _logger.Debug($"[YGP] Não foi possível calcular as datas do jogo: {ex.Message}"); }
            if (dates == null) dates = new YgpPlayDates();
            dates.Force = forced;

            var previousMinutes = store != null ? store.GetMinutes(session.PlayniteGameId) : 0;
            var result = await _apiClient.SyncGameAsync(session.GameName, knownGameId, totalSeconds, session.Platform, allowWrite, dates);

            if (result.Success)
            {
                session.TotalPlaytimeSeconds = totalSeconds;
                ConnectionStatus = ProviderConnectionStatus.Connected;

                session.SyncedTotalMinutes = totalSeconds / 60;
                var minutesBefore = result.SiteMinutesBefore ?? previousMinutes;
                session.SyncedAddedMinutes = Math.Max(0, totalSeconds / 60 - minutesBefore);
                session.ForceResend = false;
                try { OnGameSynced?.Invoke(session.PlayniteGameId); } catch { }

                // Se o site recusou a atualização, NÃO registra: assim o jogo é tentado de novo na próxima sincronização.
                if (store != null && !result.UpdateFailed)
                {
                    store.Record(session.PlayniteGameId, totalSeconds);
                    if (!string.IsNullOrEmpty(result.YgpGameId)) store.Record("ygp:" + result.YgpGameId + "|" + platformKey, totalSeconds);
                }

                if (string.IsNullOrEmpty(knownGameId) && !string.IsNullOrEmpty(result.YgpGameId))
                {
                    try
                    {
                        await _identityResolver.CreateManualMappingAsync(session.PlayniteGameId, result.YgpGameId, result.YgpGameTitle ?? session.GameName);
                        _logger.Info($"[YGP] Mapeamento salvo: '{session.GameName}' -> {result.YgpGameId}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug($"[YGP] Não foi possível salvar o mapeamento: {ex.Message}");
                    }
                }

                // Jogo que o site já possuía: conta como concluído, mas o SyncQueue o contabiliza à parte.
                if (result.Skipped) session.LastError = "[DUP] " + L.T("err.dup");
                else session.LastError = result.AlreadyOnProfile ? result.ErrorMessage : null;
                return true;
            }

            session.LastError = result.ErrorMessage;
            if (IsAuthError(result.ErrorMessage)) ConnectionStatus = ProviderConnectionStatus.AuthExpired;
            return false;
        }

        public async Task<bool> SyncPlaytimeAsync(string gameId, string ygpGameId, int totalMinutes, string gameName)
        {
            var result = await _apiClient.SyncGameAsync(gameName, ygpGameId, totalMinutes * 60, "PC");
            return result.Success;
        }

        public Task DisconnectAsync()
        {
            _apiClient.Disconnect();
            ConnectionStatus = ProviderConnectionStatus.Disconnected;
            return Task.CompletedTask;
        }

        public Task EndBatchAsync()
        {
            return _apiClient.EndBatchAsync();
        }

        public bool IsAuthError(string errorMessage)
        {
            return !string.IsNullOrEmpty(errorMessage) && errorMessage.StartsWith("[AUTH]", StringComparison.Ordinal);
        }

        public string GetStatusMessage()
        {
            switch (ConnectionStatus)
            {
                case ProviderConnectionStatus.NotConfigured:
                case ProviderConnectionStatus.Disconnected:
                    return L.T("status.disconnectedHint");
                case ProviderConnectionStatus.Connecting:
                    return L.T("status.connecting");
                case ProviderConnectionStatus.Connected:
                    return string.IsNullOrWhiteSpace(_settings.Username)
                        ? L.T("status.connected")
                        : L.T("status.connectedAs", _settings.Username);
                case ProviderConnectionStatus.AuthExpired:
                    return L.T("err.authShort");
                default:
                    return L.T("status.unknown");
            }
        }
    }
}
