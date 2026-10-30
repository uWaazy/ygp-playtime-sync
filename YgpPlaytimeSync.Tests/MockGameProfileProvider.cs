using System;
using System.Threading.Tasks;

namespace YgpPlaytimeSync.Tests
{
    /// <summary>
    /// Provedor falso usado apenas nos testes. Nunca acessa a rede.
    /// </summary>
    public class MockGameProfileProvider : IGameProfileProvider
    {
        private readonly bool _shouldSucceed;
        private readonly int _simulatedDelayMs;
        private string _authErrorText;

        public MockGameProfileProvider(bool shouldSucceed = true, int simulatedDelayMs = 0)
        {
            _shouldSucceed = shouldSucceed;
            _simulatedDelayMs = simulatedDelayMs;
            ConnectionStatus = ProviderConnectionStatus.Connected;
        }

        public int SyncCalls { get; private set; }

        public string ProviderName => "Mock";

        public ProviderConnectionStatus ConnectionStatus { get; private set; }

        /// <summary>Faz o provedor falhar com esta mensagem (use o prefixo "[AUTH]" para simular erro de login).</summary>
        public void SetupAuthError(string message)
        {
            _authErrorText = message;
        }

        public Task<bool> TestConnectionAsync() => Task.FromResult(_shouldSucceed);

        public Task<bool> AuthenticateAsync() => Task.FromResult(_shouldSucceed);

        public Task<ProviderProfile> GetProfileAsync() =>
            Task.FromResult(new ProviderProfile { UserId = "mock", Username = "mock", DisplayName = "Mock", ProfileLastUpdatedUtc = DateTime.UtcNow });

        public Task<GameIdentity> FindGameAsync(string gameName, string platform = null) =>
            Task.FromResult<GameIdentity>(null);

        public async Task<bool> SyncSessionAsync(GameSession session)
        {
            if (_simulatedDelayMs > 0) await Task.Delay(_simulatedDelayMs);
            SyncCalls++;
            if (_shouldSucceed)
            {
                session.LastError = null;
                return true;
            }
            session.LastError = _authErrorText ?? "Simulated failure";
            return false;
        }

        public Task<bool> SyncPlaytimeAsync(string gameId, string ygpGameId, int totalMinutes, string gameName) =>
            Task.FromResult(_shouldSucceed);

        public Task DisconnectAsync()
        {
            ConnectionStatus = ProviderConnectionStatus.Disconnected;
            return Task.CompletedTask;
        }

        public bool IsAuthError(string errorMessage) =>
            !string.IsNullOrEmpty(errorMessage) && errorMessage.StartsWith("[AUTH]", StringComparison.Ordinal);
    }
}
