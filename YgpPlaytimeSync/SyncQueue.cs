using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Fila de sincronização. As sessões pendentes ficam no SessionStorage (disco);
    /// aqui elas são agrupadas por jogo (só o total mais recente importa) e enviadas em lote.
    /// </summary>
    public class SyncQueue
    {
        private readonly ILogger _logger;
        private readonly SessionStorage _storage;
        private readonly ConcurrentDictionary<string, byte> _queued = new ConcurrentDictionary<string, byte>();
        private int _processing;

        // Espera entre tentativas, em segundos, conforme o número de falhas já ocorridas.
        private static readonly int[] RetryDelays = { 0, 30, 120, 300, 900, 1800 };

        private const int MaxRetries = 10;

        /// <summary>
        /// Informa quando o jogo foi jogado por último. A fila envia do mais antigo para o mais recente,
        /// então os jogos jogados por último são editados por último e ficam no topo dos "recentes" do perfil.
        /// </summary>
        public Func<string, DateTime?> LastPlayedResolver { get; set; }

        public SyncQueue(ILogger logger, SessionStorage storage)
        {
            _logger = logger;
            _storage = storage;
        }

        /// <summary>Marca uma sessão como pendente de envio.</summary>
        public void Enqueue(GameSession session)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (string.IsNullOrEmpty(session.Id))
                throw new ArgumentException("Session must have an ID");

            if (session.SyncStatus == SyncStatus.Synced)
            {
                _logger.Debug($"Session already synced, skipping: {session.Id}");
                return;
            }

            session.SyncStatus = SyncStatus.Pending;
            if (_queued.TryAdd(session.Id, 0))
            {
                _logger.Info($"Enqueued session for sync: {session.GameName}");
            }
        }

        private static bool IsDueForRetry(GameSession s, DateTime nowUtc)
        {
            if (!s.LastAttemptAtUtc.HasValue) return true;
            var index = Math.Min(Math.Max(s.RetryCount, 0), RetryDelays.Length - 1);
            return nowUtc >= s.LastAttemptAtUtc.Value.AddSeconds(RetryDelays[index]);
        }

        /// <summary>
        /// Envia as sessões pendentes. Várias sessões do mesmo jogo viram UM envio (o tempo total atual).
        /// </summary>
        /// <param name="maxBatchSize">Quantidade máxima de JOGOS por execução.</param>
        /// <param name="progress">Callback (concluídos, total, nome do jogo).</param>
        /// <param name="ignoreBackoff">True nas execuções manuais: reenvia mesmo as que falharam há pouco.</param>
        public async Task<SyncResult> ProcessQueueAsync(
            IGameProfileProvider provider,
            int maxBatchSize = 10,
            Action<int, int, string> progress = null,
            CancellationToken cancellationToken = default(CancellationToken),
            bool ignoreBackoff = false)
        {
            var result = new SyncResult();

            if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
            {
                _logger.Debug("Sync already in progress, skipping");
                result.AlreadyRunning = true;
                result.Error = L.T("err.syncRunning");
                return result;
            }

            try
            {
                var now = DateTime.UtcNow;
                var pending = _storage.GetPendingSessions().ToList();
                if (!ignoreBackoff)
                {
                    pending = pending.Where(s => IsDueForRetry(s, now)).ToList();
                }

                if (pending.Count == 0)
                {
                    _logger.Debug("No pending sessions to sync");
                    return result;
                }

                var resolver = LastPlayedResolver;
                Func<List<GameSession>, DateTime> lastPlayedOf = g =>
                {
                    DateTime? value = null;
                    try { value = resolver != null ? resolver(g[0].PlayniteGameId) : null; }
                    catch { }
                    return value ?? g.Max(x => x.EndedAtUtc);
                };

                // Do jogo jogado há mais tempo para o jogado mais recentemente (o último a ser editado fica no topo dos recentes do site).
                var groups = pending
                    .GroupBy(s => s.PlayniteGameId ?? s.GameName)
                    .Select(g => g.OrderByDescending(s => s.EndedAtUtc).ThenByDescending(s => s.CreatedAtUtc).ToList())
                    .OrderBy(g => lastPlayedOf(g))
                    .ThenBy(g => g.Max(s => s.TotalPlaytimeSeconds))
                    .Take(Math.Max(1, maxBatchSize))
                    .ToList();

                _logger.Info($"Sincronizando {groups.Count} jogo(s) ({pending.Count} sessão(ões) pendente(s)).");

                int done = 0;
                foreach (var group in groups)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    var representative = group[0];
                    progress?.Invoke(done, groups.Count, representative.GameName);

                    representative.TotalPlaytimeSeconds = Math.Max(representative.TotalPlaytimeSeconds, group.Max(s => s.TotalPlaytimeSeconds));

                    var abort = await ProcessGroupAsync(provider, group, result);
                    done++;
                    progress?.Invoke(done, groups.Count, representative.GameName);

                    if (abort) break;
                }

                _logger.Info($"Sync cycle completed: {result.SuccessfulCount} succeeded, {result.FailedCount} failed");
            }
            catch (Exception ex)
            {
                _logger.Error($"Sync queue processing failed: {ex}");
                result.Error = ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);

                try
                {
                    var batchAware = provider as ISyncBatchAware;
                    if (batchAware != null) await batchAware.EndBatchAsync();
                }
                catch (Exception ex)
                {
                    _logger.Debug($"Erro ao encerrar lote: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>Processa todas as sessões de um jogo. Retorna true se o lote inteiro deve ser interrompido.</summary>
        private async Task<bool> ProcessGroupAsync(IGameProfileProvider provider, List<GameSession> group, SyncResult result)
        {
            var representative = group[0];
            var attemptTime = DateTime.UtcNow;

            foreach (var s in group)
            {
                s.SyncStatus = SyncStatus.Syncing;
                s.LastAttemptAtUtc = attemptTime;
                await _storage.UpdateSessionStatusAsync(s);
            }

            bool success;
            string error = null;

            try
            {
                success = await provider.SyncSessionAsync(representative);
                error = representative.LastError;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex.Message;
                _logger.Error($"Exceção ao sincronizar {representative.GameName}: {ex}");
            }

            if (success)
            {
                bool existed = !string.IsNullOrEmpty(error) && error.StartsWith("[EXISTS]", StringComparison.Ordinal);
                bool skipped = !string.IsNullOrEmpty(error) && error.StartsWith("[SKIP]", StringComparison.Ordinal);

                foreach (var s in group)
                {
                    s.TotalPlaytimeSeconds = representative.TotalPlaytimeSeconds;
                    s.LastError = null;
                    await _storage.MarkAsSyncedAsync(s);
                    byte ignored;
                    _queued.TryRemove(s.Id, out ignored);
                }

                bool duplicate = !string.IsNullOrEmpty(error) && error.StartsWith("[DUP]", StringComparison.Ordinal);

                if (skipped)
                {
                    _logger.Debug($"Nada a enviar (sem aumento de tempo ou menos de 1 minuto): {representative.GameName}");
                }
                else if (duplicate)
                {
                    result.DuplicatesSkipped++;
                    _logger.Info($"Duplicado ignorado (mesmo jogo/plataforma já tratado com mais horas): {representative.GameName}");
                }
                else if (existed)
                {
                    result.AlreadyOnProfileCount++;
                    result.AlreadyOnProfileGames.Add(representative.GameName);
                    _logger.Info($"Já estava no perfil do site (tempo não sobrescrito): {representative.GameName}");
                }
                else
                {
                    result.SuccessfulCount++;
                    result.Synced.Add(new SyncedGameInfo
                    {
                        Name = representative.GameName,
                        AddedMinutes = representative.SyncedAddedMinutes,
                        TotalMinutes = representative.SyncedTotalMinutes
                    });
                    _logger.Info($"Session synced: {representative.GameName}");
                }
                return false;
            }

            result.FailedCount++;
            error = string.IsNullOrEmpty(error) ? L.T("err.unknown") : error;
            result.Errors.Add($"{representative.GameName}: {error}");
            if (string.IsNullOrEmpty(result.Error)) result.Error = error;

            var isAuth = provider.IsAuthError(error);
            if (isAuth || error.StartsWith("[ABORT]", StringComparison.Ordinal))
            {
                // Falha de login ou de rota do site não é culpa da sessão: volta para a fila sem contar tentativa.
                foreach (var s in group)
                {
                    s.SyncStatus = SyncStatus.Pending;
                    s.LastError = error;
                    s.LastAttemptAtUtc = null;
                    await _storage.UpdateSessionStatusAsync(s);
                }

                result.SkippedDueToUnavailable = true;
                result.Error = isAuth
                    ? L.T("err.queueAuth")
                    : error;
                _logger.Warn("Falha sistêmica (login ou rota do site); interrompendo o lote sem contar tentativas.");
                return true;
            }

            foreach (var s in group)
            {
                HandleSyncFailure(s, error);
                await _storage.UpdateSessionStatusAsync(s);
            }

            return false;
        }

        private void HandleSyncFailure(GameSession session, string error)
        {
            session.RetryCount++;
            session.LastError = error;

            if (error.StartsWith("[NOTFOUND]", StringComparison.Ordinal))
            {
                session.SyncStatus = SyncStatus.Failed;
                _logger.Warn($"Jogo não encontrado no YGP, não será reenviado automaticamente: {session.GameName}");
            }
            else if (session.RetryCount >= MaxRetries)
            {
                session.SyncStatus = SyncStatus.Failed;
                _logger.Warn($"Limite de tentativas atingido para {session.GameName}");
            }
            else
            {
                session.SyncStatus = SyncStatus.Pending;
                _logger.Warn($"Falha ao sincronizar {session.GameName} (tentativa {session.RetryCount}): {error}");
            }
        }

        public int Count => _queued.Count;

        public int PendingCount => _storage.PendingCount;

        public void Dispose()
        {
        }
    }

    public class SyncedGameInfo
    {
        public string Name { get; set; }
        public int AddedMinutes { get; set; }
        public int TotalMinutes { get; set; }
    }

    /// <summary>
    /// Result of a sync operation.
    /// </summary>
    public class SyncResult
    {
        public int SuccessfulCount { get; set; }
        public int FailedCount { get; set; }
        public int AlreadyOnProfileCount { get; set; }
        public int DuplicatesSkipped { get; set; }
        public List<SyncedGameInfo> Synced { get; } = new List<SyncedGameInfo>();
        public List<string> AlreadyOnProfileGames { get; } = new List<string>();
        public string Error { get; set; }
        public bool SkippedDueToUnavailable { get; set; }
        public bool AlreadyRunning { get; set; }
        public bool Cancelled { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public DateTime CompletedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
