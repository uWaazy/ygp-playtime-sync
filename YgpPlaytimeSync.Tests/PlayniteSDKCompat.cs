using System;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace YgpPlaytimeSync.Tests
{
    /// <summary>
    /// Test implementation of ISettings for unit tests.
    /// Implements the ISettings interface from Playnite.SDK.Plugins.
    /// </summary>
    public class TestSettings : ISettings
    {
        private string _lastError;
        private string _installPath = string.Empty;
        private string _providerId = string.Empty;
        
        public bool AutoSyncEnabled { get; set; } = true;
        public bool SyncExistingPlaytimeEnabled { get; set; } = false;
        public bool SyncOnlyFutureSessions { get; set; } = true;
        public int SyncIntervalMinutes { get; set; } = 5;
        public bool SyncOnGameStop { get; set; } = true;
        public bool RetryFailedSessions { get; set; } = true;
        public string YgpAccountId { get; set; }
        public bool IsConnected { get; set; } = false;
        public int BatchSize { get; set; } = 10;
        public int MaxRetryCount { get; set; } = 10;
        public bool UploadSessionData { get; set; } = true;
        public bool UploadTotalPlaytime { get; set; } = true;
        public ConflictStrategy ConflictStrategy { get; set; } = ConflictStrategy.PreferYgp;
        public bool StoreSessionTimestampsLocally { get; set; } = true;
        public bool StoreGameNamesLocally { get; set; } = true;
        public bool DiagnosticLogging { get; set; } = false;
        public bool AllowClearLocalData { get; set; } = true;
        public bool UseMockProvider { get; set; } = false;
        public bool DebugLogging { get; set; } = false;
        public string ApiEndpoint { get; set; } = "";
        
        // ISettings properties
        public string LastError { get => _lastError; set => _lastError = value; }
        public string InstallPath { get => _installPath; set => _installPath = value; }
        public string ProviderId { get => _providerId; set => _providerId = value; }
    }
}
