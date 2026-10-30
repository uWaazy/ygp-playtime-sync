using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Playnite.SDK;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class SessionStorageTests : IDisposable
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly string _tempDir;
        private readonly SessionStorage _storage;

        public SessionStorageTests()
        {
            _loggerMock = new Mock<ILogger>();
            _tempDir = Path.Combine(Path.GetTempPath(), $"ygp_storage_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            _storage = new SessionStorage(_loggerMock.Object, _tempDir);
        }

        [Fact]
        public async Task SaveSession_PersistsSession()
        {
            // Arrange
            var session = CreateTestSession("game-1", "Test Game 1");
            
            // Act
            await _storage.SaveSessionAsync(session);
            
            // Assert
            var loaded = _storage.GetSession(session.Id);
            loaded.Should().NotBeNull();
            loaded.GameName.Should().Be("Test Game 1");
        }

        [Fact]
        public void SaveSession_EmptyId_Throws()
        {
            // Arrange
            var session = CreateTestSession("game-2", "Test Game 2");
            session.Id = null;
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveSessionAsync(session));
        }

        [Fact]
        public async Task UpdateSessionStatus_UpdatesCorrectly()
        {
            // Arrange
            var session = CreateTestSession("game-3", "Test Game 3");
            await _storage.SaveSessionAsync(session);
            
            // Act
            session.SyncStatus = SyncStatus.Syncing;
            session.RetryCount = 2;
            session.LastError = "Test error";
            await _storage.UpdateSessionStatusAsync(session);
            
            // Assert
            var loaded = _storage.GetSession(session.Id);
            loaded.SyncStatus.Should().Be(SyncStatus.Syncing);
            loaded.RetryCount.Should().Be(2);
            loaded.LastError.Should().Be("Test error");
        }

        [Fact]
        public async Task MarkAsSynced_MovesToCompleted()
        {
            // Arrange
            var session = CreateTestSession("game-4", "Test Game 4");
            await _storage.SaveSessionAsync(session);
            var pendingBefore = _storage.PendingCount;
            
            // Act
            await _storage.MarkAsSyncedAsync(session);
            
            // Assert
            var loaded = _storage.GetSession(session.Id);
            loaded.SyncStatus.Should().Be(SyncStatus.Synced);
            _storage.PendingCount.Should().Be(pendingBefore - 1);
        }

        [Fact]
        public async Task GetPendingSessions_ReturnsOnlyPending()
        {
            // Arrange
            var pendingSession = CreateTestSession("pending-1", "Pending Game");
            var syncedSession = CreateTestSession("synced-1", "Synced Game");
            syncedSession.SyncStatus = SyncStatus.Synced;
            
            await _storage.SaveSessionAsync(pendingSession);
            await _storage.SaveSessionAsync(syncedSession);
            
            // Act
            var pendingSessions = _storage.GetPendingSessions();
            
            // Assert
            pendingSessions.Should().ContainSingle();
            pendingSessions.First().GameName.Should().Be("Pending Game");
        }

        [Fact]
        public async Task GetPendingSessions_RespectsMaxRetry()
        {
            // Arrange
            var session = CreateTestSession("retry-1", "Retry Game");
            session.RetryCount = 10; // At max
            await _storage.SaveSessionAsync(session);
            
            // Act
            var pendingSessions = _storage.GetPendingSessions();
            
            // Assert - should not include sessions that exceeded max retries
            pendingSessions.Should().BeEmpty();
        }

        [Fact]
        public void GetPendingSessions_EmptyStorage_ReturnsEmpty()
        {
            // Act
            var pendingSessions = _storage.GetPendingSessions();
            
            // Assert
            pendingSessions.Should().BeEmpty();
        }

        [Fact]
        public async Task ClearAllData_RemovesAllSessions()
        {
            // Arrange
            await _storage.SaveSessionAsync(CreateTestSession("clear-1", "Clear 1"));
            await _storage.SaveSessionAsync(CreateTestSession("clear-2", "Clear 2"));
            
            // Act
            await _storage.ClearAllDataAsync();
            
            // Assert
            _storage.PendingCount.Should().Be(0);
            _storage.SyncedCount.Should().Be(0);
            _storage.GetPendingSessions().Should().BeEmpty();
        }

        [Fact]
        public async Task SaveState_SurvivesRestart()
        {
            // Arrange
            var session = CreateTestSession("restart-1", "Restart Game");
            await _storage.SaveSessionAsync(session);
            
            // Act - simulate restart by creating new storage instance
            var newStorage = new SessionStorage(_loggerMock.Object, _tempDir);
            
            // Assert
            var loaded = newStorage.GetSession(session.Id);
            loaded.Should().NotBeNull();
            loaded.GameName.Should().Be("Restart Game");
        }

        [Fact]
        public void LoadCorruptedFile_SkipsGracefully()
        {
            // Arrange - create corrupted file
            var corruptFile = Path.Combine(_tempDir, "sessions", "corrupt.json");
            Directory.CreateDirectory(Path.GetDirectoryName(corruptFile));
            File.WriteAllText(corruptFile, "not valid json {{");
            
            // Act - should not throw
            var storage = new SessionStorage(_loggerMock.Object, _tempDir);
            
            // Assert - should initialize without error
            storage.Should().NotBeNull();
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

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }
    }
}
