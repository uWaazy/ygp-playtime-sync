using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class SyncQueueIntegrationTests
    {
        [Fact]
        public async Task FullPipeline_SessionFlowsFromStartToSync()
        {
            // This is a complete integration test simulating the full pipeline
            // Mock Logger
            var logger = new Mock<Playnite.SDK.ILogger>();
            
            // Create temp directory for storage
            var tempDir = Path.Combine(Path.GetTempPath(), $"ygp_integration_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // 1. Create storage and tracker
                var storage = new SessionStorage(logger.Object, tempDir);
                var tracker = new SessionTracker(logger.Object, storage);
                
                // 2. Simulate game start
                var gameId = "integration-test-game";
                var gameName = "Integration Test Game";
                tracker.OnGameStarted(gameId, gameName, Guid.NewGuid(), "999", "Steam");
                
                // 3. Simulate game stop with elapsed time
                var elapsedSeconds = 3600; // 1 hour
                var session = await tracker.OnGameStoppedAsync(
                    gameId, gameName, Guid.NewGuid(), "999", "Steam", elapsedSeconds);
                
                // Assert session was created
                session.Should().NotBeNull();
                session.GameName.Should().Be(gameName);
                session.DurationSeconds.Should().Be(elapsedSeconds);
                
                // 4. Create sync queue and mock provider
                var syncQueue = new SyncQueue(logger.Object, storage);
                var mockProvider = new MockGameProfileProvider(shouldSucceed: true, simulatedDelayMs: 10);
                
                // 5. Enqueue session (should already be in storage, but queue gets from storage)
                syncQueue.Enqueue(session);
                
                // 6. Process the queue
                var result = await syncQueue.ProcessQueueAsync(mockProvider);
                
                // Assert sync succeeded
                result.SuccessfulCount.Should().Be(1);
                result.FailedCount.Should().Be(0);
                
                // 7. Verify session is now marked as synced
                var syncedSession = storage.GetSession(session.Id);
                syncedSession.Should().NotBeNull();
                syncedSession.SyncStatus.Should().Be(SyncStatus.Synced);
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task OfflineMode_SessionPersistsAndSyncsLater()
        {
            // Simulate offline scenario
            var logger = new Mock<Playnite.SDK.ILogger>();
            var tempDir = Path.Combine(Path.GetTempPath(), $"ygp_offline_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Phase 1: Online - create and save session
                var storage = new SessionStorage(logger.Object, tempDir);
                var tracker = new SessionTracker(logger.Object, storage);
                
                tracker.OnGameStarted("offline-game", "Offline Game", Guid.NewGuid(), "111", "Steam");
                
                var session = await tracker.OnGameStoppedAsync(
                    "offline-game", "Offline Game", Guid.NewGuid(), "111", "Steam", 1800);
                
                session.Should().NotBeNull();
                
                var syncQueue = new SyncQueue(logger.Object, storage);
                syncQueue.Enqueue(session);
                
                // Phase 2: Provider unavailable - session should remain pending
                var failingProvider = new MockGameProfileProvider(shouldSucceed: false, simulatedDelayMs: 5);
                var result = await syncQueue.ProcessQueueAsync(failingProvider);
                
                result.SuccessfulCount.Should().Be(0);
                result.FailedCount.Should().BeGreaterThan(0);
                
                // Session should still be pending
                var pendingSession = storage.GetSession(session.Id);
                pendingSession.SyncStatus.Should().Be(SyncStatus.Pending);
                
                // Phase 3: Provider available - now sync should succeed
                var workingProvider = new MockGameProfileProvider(shouldSucceed: true, simulatedDelayMs: 10);
                var result2 = await syncQueue.ProcessQueueAsync(workingProvider);
                
                result2.SuccessfulCount.Should().Be(1);
                
                var finalSession = storage.GetSession(session.Id);
                finalSession.SyncStatus.Should().Be(SyncStatus.Synced);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task CrashRecovery_SessionPersistsAfterRestart()
        {
            var logger = new Mock<Playnite.SDK.ILogger>();
            var tempDir = Path.Combine(Path.GetTempPath(), $"ygp_crash_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Phase 1: Create session before crash
                var storage1 = new SessionStorage(logger.Object, tempDir);
                var tracker1 = new SessionTracker(logger.Object, storage1);
                
                tracker1.OnGameStarted("crash-game", "Crash Game", Guid.NewGuid(), "222", "GOG");
                
                var session = await tracker1.OnGameStoppedAsync(
                    "crash-game", "Crash Game", Guid.NewGuid(), "222", "GOG", 900);
                
                session.Should().NotBeNull();
                
                // Simulate crash by disposing without clean shutdown
                tracker1.Dispose();
                
                // Phase 2: Restart - session should still exist
                var storage2 = new SessionStorage(logger.Object, tempDir);
                
                var recovered = storage2.GetSession(session.Id);
                recovered.Should().NotBeNull();
                recovered.GameName.Should().Be("Crash Game");
                recovered.DurationSeconds.Should().Be(900);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Idempotency_DuplicateSessionNotCreated()
        {
            // Test that the same session ID is generated for the same game/times
            var gameId = "idem-game";
            var start = new DateTime(2024, 3, 15, 14, 0, 0, DateTimeKind.Utc);
            var end = new DateTime(2024, 3, 15, 15, 30, 0, DateTimeKind.Utc);
            
            var key1 = GameSession.GenerateIdempotencyKey(gameId, start, end);
            var key2 = GameSession.GenerateIdempotencyKey(gameId, start, end);
            
            key1.Should().Be(key2, "same session should produce same idempotency key");
            
            // Different session should produce different key
            var end2 = new DateTime(2024, 3, 15, 16, 0, 0, DateTimeKind.Utc);
            var key3 = GameSession.GenerateIdempotencyKey(gameId, start, end2);
            
            key1.Should().NotBe(key3, "different session should produce different key");
        }

        [Fact]
        public async Task RetryLogic_SuccessfulAfterFailures()
        {
            var logger = new Mock<Playnite.SDK.ILogger>();
            var tempDir = Path.Combine(Path.GetTempPath(), $"ygp_retry_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            
            try
            {
                var storage = new SessionStorage(logger.Object, tempDir);
                var syncQueue = new SyncQueue(logger.Object, storage);
                
                var session = CreateTestSession("retry-game", "Retry Game");
                await storage.SaveSessionAsync(session);
                syncQueue.Enqueue(session);
                
                // First attempt fails with timeout
                var timeoutProvider = new MockGameProfileProvider(shouldSucceed: false, simulatedDelayMs: 5);
                var result1 = await syncQueue.ProcessQueueAsync(timeoutProvider);
                result1.SuccessfulCount.Should().Be(0);
                
                // Second attempt fails with temporary error
                var tempErrorProvider = new MockGameProfileProvider(shouldSucceed: false, simulatedDelayMs: 5);
                tempErrorProvider.SetupAuthError("503 Service Unavailable");
                var result2 = await syncQueue.ProcessQueueAsync(tempErrorProvider);
                result2.SuccessfulCount.Should().Be(0);
                
                // Third attempt succeeds
                var successProvider = new MockGameProfileProvider(shouldSucceed: true, simulatedDelayMs: 10);
                var result3 = await syncQueue.ProcessQueueAsync(successProvider);
                result3.SuccessfulCount.Should().Be(1);
                
                // Verify session is synced
                var finalSession = storage.GetSession(session.Id);
                finalSession.SyncStatus.Should().Be(SyncStatus.Synced);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        private GameSession CreateTestSession(string gameId, string gameName)
        {
            return new GameSession
            {
                Id = GameSession.GenerateIdempotencyKey(gameId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)),
                PlayniteGameId = gameId,
                GameName = gameName,
                Source = "Steam",
                SourceId = "12345",
                Platform = "Steam",
                StartedAtUtc = DateTime.UtcNow,
                EndedAtUtc = DateTime.UtcNow.AddMinutes(30),
                DurationSeconds = 1800,
                SyncStatus = SyncStatus.Pending
            };
        }
    }
}
