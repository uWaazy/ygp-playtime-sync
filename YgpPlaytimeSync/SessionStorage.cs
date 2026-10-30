using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Threading.Tasks;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Local storage for game sessions using JSON files.
    /// Sessions are persisted to survive Playnite restarts and system reboots.
    /// </summary>
    public class SessionStorage
    {
        private readonly ILogger _logger;
        private readonly string _sessionsDir;
        private readonly string _completedDir;
        private readonly ConcurrentDictionary<string, GameSession> _pendingSessions;
        private readonly ConcurrentDictionary<string, GameSession> _syncedSessions;

        public SessionStorage(ILogger logger, string dataPath)
        {
            _logger = logger;
            _sessionsDir = Path.Combine(dataPath, "sessions");
            _completedDir = Path.Combine(dataPath, "completed");
            _pendingSessions = new ConcurrentDictionary<string, GameSession>();
            _syncedSessions = new ConcurrentDictionary<string, GameSession>();

            EnsureDirectories();
            LoadExistingSessions();
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_sessionsDir);
            Directory.CreateDirectory(_completedDir);
        }

        private void LoadExistingSessions()
        {
            try
            {
                // Load pending sessions
                foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var session = JsonConvert.DeserializeObject<GameSession>(json);
                        if (session != null)
                        {
                            // Uma sessão que ficou "Syncing" quando o Playnite fechou volta para a fila.
                            if (session.SyncStatus == SyncStatus.Syncing) session.SyncStatus = SyncStatus.Pending;
                            _pendingSessions[session.Id] = session;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to load session file {file}: {ex.Message}");
                    }
                }

                // Load completed/synced sessions (arquivos com mais de 30 dias são apagados)
                foreach (var file in Directory.GetFiles(_completedDir, "*.json"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-30))
                        {
                            File.Delete(file);
                            continue;
                        }

                        var json = File.ReadAllText(file);
                        var session = JsonConvert.DeserializeObject<GameSession>(json);
                        if (session != null)
                        {
                            _syncedSessions[session.Id] = session;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to load completed session file {file}: {ex.Message}");
                    }
                }

                _logger.Info($"Loaded {_pendingSessions.Count} pending sessions, {_syncedSessions.Count} synced sessions.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to load sessions: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves a session to local storage.
        /// </summary>
        public async Task SaveSessionAsync(GameSession session)
        {
            if (string.IsNullOrEmpty(session.Id))
            {
                throw new ArgumentException("Session ID is required");
            }

            try
            {
                var file = Path.Combine(_sessionsDir, $"{session.Id}.json");
                var json = JsonConvert.SerializeObject(session, Formatting.None);
                
                File.WriteAllText(file, json);
                
                _pendingSessions[session.Id] = session;
                _logger.Info($"Saved session: {session.GameName} ({session.Id.Substring(0, 8)}...)");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save session {session.Id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Marks a session as synced and moves it to completed storage.
        /// </summary>
        public async Task MarkAsSyncedAsync(GameSession session)
        {
            session.SyncStatus = SyncStatus.Synced;
            
            try
            {
                // Update in pending storage
                var pendingFile = Path.Combine(_sessionsDir, $"{session.Id}.json");
                if (File.Exists(pendingFile))
                {
                    var json = JsonConvert.SerializeObject(session, Formatting.None);
                    File.WriteAllText(pendingFile, json);
                }

                // Move to completed
                var completedFile = Path.Combine(_completedDir, $"{session.Id}.json");
                if (File.Exists(pendingFile))
                {
                    if (File.Exists(completedFile)) File.Delete(completedFile);
                    File.Move(pendingFile, completedFile);
                }
                
                _pendingSessions.TryRemove(session.Id, out _);
                _syncedSessions[session.Id] = session;
                
                _logger.Info($"Marked session as synced: {session.GameName}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to mark session as synced {session.Id}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates a session's sync status and error information.
        /// </summary>
        public async Task UpdateSessionStatusAsync(GameSession session)
        {
            try
            {
                if (_pendingSessions.TryGetValue(session.Id, out var existing))
                {
                    existing.SyncStatus = session.SyncStatus;
                    existing.RetryCount = session.RetryCount;
                    existing.LastAttemptAtUtc = session.LastAttemptAtUtc;
                    existing.LastError = session.LastError;
                    
                    var file = Path.Combine(_sessionsDir, $"{session.Id}.json");
                    var json = JsonConvert.SerializeObject(existing, Formatting.None);
                    File.WriteAllText(file, json);
                    
                    _logger.Debug($"Updated session status: {session.Id} -> {session.SyncStatus}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to update session status {session.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all pending sessions that need synchronization.
        /// </summary>
        public IEnumerable<GameSession> GetPendingSessions()
        {
            return _pendingSessions.Values
                .Where(s => s.SyncStatus == SyncStatus.Pending)
                .ToList();
        }

        /// <summary>
        /// Sessões que falharam de forma definitiva (jogo não encontrado ou limite de tentativas).
        /// </summary>
        public IEnumerable<GameSession> GetFailedSessions()
        {
            return _pendingSessions.Values
                .Where(s => s.SyncStatus == SyncStatus.Failed)
                .ToList();
        }

        /// <summary>
        /// Procura uma sessão pendente do mesmo jogo, para atualizá-la em vez de criar outra.
        /// </summary>
        /// <summary>Sessões já enviadas (usado para preencher o registro de tempos enviados).</summary>
        public IEnumerable<GameSession> GetSyncedSessions()
        {
            return _syncedSessions.Values.ToList();
        }

        public GameSession FindPendingByGame(string playniteGameId)
        {
            if (string.IsNullOrEmpty(playniteGameId)) return null;
            return _pendingSessions.Values
                .Where(s => s.SyncStatus == SyncStatus.Pending && s.PlayniteGameId == playniteGameId)
                .OrderByDescending(s => s.CreatedAtUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Recoloca na fila as sessões que falharam (usado no botão de envio manual).
        /// </summary>
        public int RequeueFailed()
        {
            int count = 0;
            foreach (var s in _pendingSessions.Values.Where(x => x.SyncStatus == SyncStatus.Failed).ToList())
            {
                s.SyncStatus = SyncStatus.Pending;
                s.RetryCount = 0;
                s.LastAttemptAtUtc = null;
                UpdateSessionStatusAsync(s).GetAwaiter().GetResult();
                count++;
            }
            return count;
        }

        /// <summary>
        /// Grava novamente uma sessão pendente já existente (ex.: tempo total atualizado).
        /// </summary>
        public void PersistSession(GameSession session)
        {
            try
            {
                var file = Path.Combine(_sessionsDir, $"{session.Id}.json");
                File.WriteAllText(file, JsonConvert.SerializeObject(session, Formatting.None));
                _pendingSessions[session.Id] = session;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to persist session {session.Id}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a session by ID.
        /// </summary>
        public GameSession GetSession(string id)
        {
            if (_pendingSessions.TryGetValue(id, out var session))
                return session;
            
            if (_syncedSessions.TryGetValue(id, out session))
                return session;
            
            return null;
        }

        /// <summary>
        /// Gets pending session count.
        /// </summary>
        public int PendingCount => _pendingSessions.Values
            .Count(s => s.SyncStatus == SyncStatus.Pending || s.SyncStatus == SyncStatus.Syncing);

        /// <summary>
        /// Gets the number of sessions that failed permanently.
        /// </summary>
        public int FailedCount => _pendingSessions.Values.Count(s => s.SyncStatus == SyncStatus.Failed);

        /// <summary>
        /// Gets synced session count.
        /// </summary>
        public int SyncedCount => _syncedSessions.Count;

        /// <summary>
        /// Clears all local synchronization data.
        /// </summary>
        public async Task ClearAllDataAsync()
        {
            try
            {
                // Clear pending sessions
                foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
                {
                    File.Delete(file);
                }
                _pendingSessions.Clear();

                // Clear completed sessions
                foreach (var file in Directory.GetFiles(_completedDir, "*.json"))
                {
                    File.Delete(file);
                }
                _syncedSessions.Clear();

                _logger.Info("Cleared all synchronization data");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to clear data: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Saves the current state for crash recovery.
        /// </summary>
        public async Task SaveStateAsync()
        {
            // All sessions are already saved individually
            // This method exists for future use if we add batch serialization
            _logger.Debug("Session storage state saved");
            await Task.CompletedTask;
        }
    }
}
