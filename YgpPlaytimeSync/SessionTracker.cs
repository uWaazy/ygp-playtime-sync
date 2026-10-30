using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Playnite.SDK;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Tracks game start/stop events from Playnite and creates game sessions.
    /// </summary>
    public class SessionTracker : IDisposable
    {
        private readonly ILogger _logger;
        private readonly SessionStorage _storage;
        private readonly ConcurrentDictionary<string, DateTime> _activeSessions;
        private bool _disposed;

        public SessionTracker(ILogger logger, SessionStorage storage)
        {
            _logger = logger;
            _storage = storage;
            _activeSessions = new ConcurrentDictionary<string, DateTime>();
        }

        /// <summary>
        /// Called when a game is starting.
        /// Records the start time for session tracking.
        /// </summary>
        public void OnGameStarting(string gameId, string gameName, Guid sourceGuid, string sourceId, string sourceName)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                _logger.Warn("OnGameStarting called with empty game ID");
                return;
            }

            _logger.Info($"Game starting: {gameName} ({gameId})");

            var startTime = DateTime.UtcNow;
            _activeSessions[gameId] = startTime;

            // We don't create a session yet - wait for game to actually start/stop
            // This is just tracking the intended start
        }

        /// <summary>
        /// Called when a game has started.
        /// Records the actual start time.
        /// </summary>
        public void OnGameStarted(string gameId, string gameName, Guid sourceGuid, string sourceId, string sourceName)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                _logger.Warn("OnGameStarted called with empty game ID");
                return;
            }

            _logger.Info($"Game started: {gameName} ({gameId})");

            var startTime = DateTime.UtcNow;
            
            // Update or add the session start time
            if (!_activeSessions.TryAdd(gameId, startTime))
            {
                _activeSessions[gameId] = startTime;
            }
        }

        /// <summary>
        /// Called when a game has stopped.
        /// Creates a session record and saves it to storage.
        /// </summary>
        public async Task<GameSession> OnGameStoppedAsync(
            string gameId,
            string gameName,
            Guid sourceGuid,
            string sourceId,
            string sourceName,
            int elapsedSeconds)
        {
            if (string.IsNullOrEmpty(gameId))
            {
                _logger.Warn("OnGameStopped called with empty game ID");
                return null;
            }

            if (!_activeSessions.TryRemove(gameId, out var startTime))
            {
                // Game wasn't tracked - might be a recovered session
                _logger.Warn($"Game stopped but wasn't tracked: {gameName} ({gameId})");
                
                // Create a recovered session with unknown start time
                startTime = DateTime.UtcNow.AddSeconds(-elapsedSeconds);
            }

            var endedAt = DateTime.UtcNow;
            var session = GameSession.CreateFromPlayniteEvents(
                gameId,
                gameName,
                sourceGuid,
                sourceId,
                sourceName,
                startTime,
                endedAt,
                elapsedSeconds);

            _logger.Info($"Game stopped: {gameName} - Duration: {session.DurationMinutes} min");

            // Save session to storage
            await _storage.SaveSessionAsync(session);
            
            return session;
        }

        /// <summary>
        /// Checks for sessions that might have been lost due to crash/abrupt shutdown.
        /// This is called on application start to recover incomplete sessions.
        /// </summary>
        public IEnumerable<GameSession> RecoverLostSessions()
        {
            var recovered = new List<GameSession>();
            
            // Get all pending sessions that were not properly ended
            foreach (var session in _storage.GetPendingSessions())
            {
                // Sessions that have been pending for more than 1 hour might be from a crash
                var age = DateTime.UtcNow - session.CreatedAtUtc;
                if (age.TotalHours > 1 && session.IsRecovered == false)
                {
                    // We can't determine the actual duration, mark as recovered
                    session.IsRecovered = true;
                    _logger.Warn($"Potentially lost session recovered: {session.GameName} (created {age.TotalMinutes:F0} min ago)");
                    recovered.Add(session);
                }
            }

            return recovered;
        }

        /// <summary>
        /// Gets active session count.
        /// </summary>
        public int ActiveSessionCount => _activeSessions.Count;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Save any remaining active sessions info if needed
                    _logger.Info($"Disposing SessionTracker with {_activeSessions.Count} active sessions");
                    _activeSessions.Clear();
                }
                _disposed = true;
            }
        }
    }
}
