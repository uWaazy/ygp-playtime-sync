using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Represents a game play session tracked by Playnite.
    /// </summary>
    public class GameSession
    {
        /// <summary>Unique identifier for this session (idempotency key).</summary>
        public string Id { get; set; }

        /// <summary>Playnite internal game ID.</summary>
        public string PlayniteGameId { get; set; }

        /// <summary>Display name of the game.</summary>
        public string GameName { get; set; }

        /// <summary>Source/platform identifier (Steam, GOG, etc).</summary>
        public string Source { get; set; }

        /// <summary>Source-specific game ID (e.g., Steam AppID).</summary>
        public string SourceId { get; set; }

        /// <summary>Platform name.</summary>
        public string Platform { get; set; }

        /// <summary>Session start time in UTC.</summary>
        public DateTime StartedAtUtc { get; set; }

        /// <summary>Session end time in UTC.</summary>
        public DateTime EndedAtUtc { get; set; }

        /// <summary>Duration of the session in seconds.</summary>
        public int DurationSeconds { get; set; }

        /// <summary>Duration in minutes (rounded).</summary>
        public int DurationMinutes
        {
            get { return (int)Math.Round(DurationSeconds / 60.0); }
        }

        /// <summary>Total accumulated playtime in Playnite in seconds.</summary>
        public int TotalPlaytimeSeconds { get; set; }

        /// <summary>Total playtime in minutes (rounded).</summary>
        public int TotalPlaytimeMinutes
        {
            get { return (int)Math.Round(TotalPlaytimeSeconds / 60.0); }
        }

        /// <summary>Reenviar mesmo sem aumento de horas (uma opção de datas do jogo mudou).</summary>
        public bool ForceResend { get; set; }

        /// <summary>Preenchido pelo provedor após o envio: minutos adicionados (não é salvo em disco).</summary>
        [JsonIgnore]
        public int SyncedAddedMinutes { get; set; }

        /// <summary>Preenchido pelo provedor após o envio: total do jogo em minutos (não é salvo em disco).</summary>
        [JsonIgnore]
        public int SyncedTotalMinutes { get; set; }

        /// <summary>True if this entry was triggered by a manual edit in Playnite library.</summary>
        public bool IsManualEdit { get; set; }

        /// <summary>Timestamp when this record was created.</summary>
        public DateTime CreatedAtUtc
        {
            get { return _createdAtUtc; }
            set { _createdAtUtc = value; }
        }
        private DateTime _createdAtUtc = DateTime.UtcNow;

        /// <summary>Current sync status of this session.</summary>
        public SyncStatus SyncStatus
        {
            get { return _syncStatus; }
            set { _syncStatus = value; }
        }
        private SyncStatus _syncStatus = SyncStatus.Pending;

        /// <summary>Number of retry attempts made.</summary>
        public int RetryCount { get; set; }

        /// <summary>Last time a sync was attempted.</summary>
        public DateTime? LastAttemptAtUtc { get; set; }

        /// <summary>Last error message if sync failed.</summary>
        public string LastError { get; set; }

        /// <summary>Whether the session was recovered from an abrupt shutdown.</summary>
        public bool IsRecovered { get; set; }

        /// <summary>
        /// Generates an idempotency key for this session.
        /// </summary>
        public static string GenerateIdempotencyKey(string playniteGameId, DateTime startedAtUtc, DateTime endedAtUtc)
        {
            string raw = playniteGameId + "|" + startedAtUtc.ToString("O") + "|" + endedAtUtc.ToString("O");
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash)
                    .Replace("/", "_")
                    .Replace("+", "-")
                    .TrimEnd('=');
            }
        }

        /// <summary>
        /// Creates a new game session from Playnite game stop event.
        /// </summary>
        public static GameSession CreateFromPlayniteEvents(
            string playniteGameId,
            string gameName,
            Guid sourceGuid,
            string sourceId,
            string sourceName,
            DateTime startedAtUtc,
            DateTime endedAtUtc,
            int elapsedSeconds)
        {
            string source = GetSourceName(sourceGuid, sourceName);
            
            GameSession session = new GameSession();
            session.Id = GenerateIdempotencyKey(playniteGameId, startedAtUtc, endedAtUtc);
            session.PlayniteGameId = playniteGameId;
            session.GameName = gameName;
            session.Source = source;
            session.SourceId = sourceId ?? string.Empty;
            session.Platform = source;
            session.StartedAtUtc = startedAtUtc;
            session.EndedAtUtc = endedAtUtc;
            session.DurationSeconds = elapsedSeconds;
            session.CreatedAtUtc = DateTime.UtcNow;
            session.SyncStatus = SyncStatus.Pending;
            
            return session;
        }

        private static string GetSourceName(Guid sourceGuid, string sourceName)
        {
            // Playnite SDK 6.17 exposes BuiltinExtension values differently from a Guid;
            // do not compare a Guid with BuiltinExtension or call a nonexistent ToGuid().
            // Use the source name supplied by Playnite, which also supports third-party sources.
            if (string.IsNullOrWhiteSpace(sourceName))
                return "Unknown";

            string normalized = sourceName.Trim();
            string lower = normalized.ToLowerInvariant();

            if (lower.Contains("steam")) return "Steam";
            if (lower.Contains("gog")) return "GOG";
            if (lower.Contains("epic")) return "Epic Games";
            if (lower.Contains("xbox") || lower.Contains("microsoft")) return "Xbox";
            if (lower.Contains("playstation") || lower == "psn") return "PlayStation";
            if (lower.Contains("nintendo")) return "Nintendo";

            return normalized;
        }
    }

    /// <summary>
    /// Synchronization status for a game session.
    /// </summary>
    public enum SyncStatus
    {
        /// <summary>Session is waiting to be synchronized.</summary>
        Pending,

        /// <summary>Session is currently being synchronized.</summary>
        Syncing,

        /// <summary>Session was successfully synchronized.</summary>
        Synced,

        /// <summary>Session sync failed permanently (e.g., auth error).</summary>
        Failed,

        /// <summary>Session was intentionally ignored/skipped.</summary>
        Ignored
    }
}
