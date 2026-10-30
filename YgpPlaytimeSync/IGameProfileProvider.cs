using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YgpPlaytimeSync
{
    /// <summary>
    /// Interface for game profile providers (YGP, etc.).
    /// This abstraction allows the sync engine to work with any provider
    /// without depending directly on YGP implementation.
    /// </summary>
    public interface IGameProfileProvider
    {
        /// <summary>
        /// Gets the provider name/display name.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Gets the current connection status.
        /// </summary>
        ProviderConnectionStatus ConnectionStatus { get; }

        /// <summary>
        /// Tests if the provider is available and connected.
        /// </summary>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Authenticates with the provider.
        /// </summary>
        Task<bool> AuthenticateAsync();

        /// <summary>
        /// Gets the user's profile information.
        /// </summary>
        Task<ProviderProfile> GetProfileAsync();

        /// <summary>
        /// Searches for a game on the provider.
        /// </summary>
        Task<GameIdentity> FindGameAsync(string gameName, string platform = null);

        /// <summary>
        /// Synchronizes a single game session.
        /// </summary>
        Task<bool> SyncSessionAsync(GameSession session);

        /// <summary>
        /// Synchronizes total playtime for a game.
        /// This is used for initial sync of existing playtime.
        /// </summary>
        Task<bool> SyncPlaytimeAsync(string gameId, string ygpGameId, int totalMinutes, string gameName);

        /// <summary>
        /// Disconnects from the provider.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Checks if an error indicates authentication failure.
        /// </summary>
        bool IsAuthError(string errorMessage);
    }

    /// <summary>
    /// Implemented by providers that keep resources (e.g. a browser) alive while a batch is processed.
    /// The sync queue calls EndBatchAsync when the batch finishes.
    /// </summary>
    public interface ISyncBatchAware
    {
        System.Threading.Tasks.Task EndBatchAsync();
    }

    /// <summary>
    /// Connection status for a provider.
    /// </summary>
    public enum ProviderConnectionStatus
    {
        /// <summary>Provider is not configured.</summary>
        NotConfigured,

        /// <summary>Provider is disconnected.</summary>
        Disconnected,

        /// <summary>Provider is connecting.</summary>
        Connecting,

        /// <summary>Provider is connected.</summary>
        Connected,

        /// <summary>Provider authentication expired.</summary>
        AuthExpired,

        /// <summary>Provider API is unavailable.</summary>
        Unavailable
    }

    /// <summary>
    /// User profile information from a provider.
    /// </summary>
    public class ProviderProfile
    {
        public string UserId { get; set; }
        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public int TotalGamesPlayed { get; set; }
        public int TotalPlaytimeMinutes { get; set; }
        public int TotalAchievements { get; set; }
        public DateTime ProfileLastUpdatedUtc { get; set; }
    }
}
