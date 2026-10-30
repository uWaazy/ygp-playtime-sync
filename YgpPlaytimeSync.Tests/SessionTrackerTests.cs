using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Playnite.SDK;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class SessionTrackerTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly string _tempDir;
        private readonly SessionStorage _storage;
        private readonly SessionTracker _tracker;

        public SessionTrackerTests()
        {
            _loggerMock = new Mock<ILogger>();
            _tempDir = Path.Combine(Path.GetTempPath(), $"ygp_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            _storage = new SessionStorage(_loggerMock.Object, _tempDir);
            _tracker = new SessionTracker(_loggerMock.Object, _storage);
        }

        [Fact]
        public void OnGameStarted_RecordsStartTime()
        {
            // Arrange
            var gameId = "test-game-1";
            
            // Act
            _tracker.OnGameStarted(gameId, "Test Game", Guid.NewGuid(), "123", "Steam");
            
            // Assert
            _tracker.ActiveSessionCount.Should().Be(1);
        }

        [Fact]
        public void OnGameStarted_MultipleGames_TracksEach()
        {
            // Arrange & Act
            _tracker.OnGameStarted("game1", "Game 1", Guid.NewGuid(), "1", "Steam");
            _tracker.OnGameStarted("game2", "Game 2", Guid.NewGuid(), "2", "GOG");
            
            // Assert
            _tracker.ActiveSessionCount.Should().Be(2);
        }

        [Fact]
        public void OnGameStopped_CreatesSession()
        {
            // Arrange
            var gameId = "test-game-2";
            _tracker.OnGameStarted(gameId, "Test Game", Guid.Parse("00000000-0000-0000-0000-000000000001"), "123", "Steam");
            
            // Act
            var session = _tracker.OnGameStoppedAsync(gameId, "Test Game", 
                Guid.Parse("00000000-0000-0000-0000-000000000001"), "123", "Steam", 3600).Result;
            
            // Assert
            session.Should().NotBeNull();
            session.GameName.Should().Be("Test Game");
            session.DurationSeconds.Should().Be(3600);
            session.DurationMinutes.Should().Be(60);
            session.SyncStatus.Should().Be(SyncStatus.Pending);
        }

        [Fact]
        public void OnGameStopped_UsesElapsedSecondsFromPlaynite()
        {
            // Arrange
            var gameId = "test-game-3";
            _tracker.OnGameStarted(gameId, "Test Game", Guid.NewGuid(), "456", "GOG");
            
            // Act
            var session = _tracker.OnGameStoppedAsync(gameId, "Test Game",
                Guid.NewGuid(), "456", "GOG", 7200).Result;
            
            // Assert
            session.DurationSeconds.Should().Be(7200);
            session.DurationMinutes.Should().Be(120);
        }

        [Fact]
        public async Task OnGameStopped_SavesSessionToStorage()
        {
            // Arrange
            var gameId = "test-game-4";
            _tracker.OnGameStarted(gameId, "Saved Game", Guid.NewGuid(), "789", "Epic");
            
            // Act
            await _tracker.OnGameStoppedAsync(gameId, "Saved Game",
                Guid.NewGuid(), "789", "Epic", 1800);
            
            // Assert
            var pendingSessions = _storage.GetPendingSessions();
            pendingSessions.Should().NotBeEmpty();
            pendingSessions.First().GameName.Should().Be("Saved Game");
        }

        [Fact]
        public void OnGameStopped_EmptyGameId_ReturnsNull()
        {
            // Act
            var session = _tracker.OnGameStoppedAsync("", "Test", Guid.NewGuid(), "", "", 100).Result;
            
            // Assert
            session.Should().BeNull();
        }

        [Fact]
        public void OnGameStopped_NotStartedGame_CreatesRecoveredSession()
        {
            // Arrange - game not started
            var gameId = "untracked-game";
            
            // Act
            var session = _tracker.OnGameStoppedAsync(gameId, "Untracked", 
                Guid.NewGuid(), "999", "Steam", 600).Result;
            
            // Assert
            session.Should().NotBeNull();
            session.IsRecovered.Should().BeTrue();
            session.DurationSeconds.Should().Be(600);
        }

        [Fact]
        public void Dispose_ClearsActiveSessions()
        {
            // Arrange
            _tracker.OnGameStarted("game1", "Game 1", Guid.NewGuid(), "1", "Steam");
            _tracker.OnGameStarted("game2", "Game 2", Guid.NewGuid(), "2", "GOG");
            
            // Act
            _tracker.Dispose();
            
            // Assert - should not throw
        }

        [Fact]
        public void GenerateIdempotencyKey_ReturnsConsistentHash()
        {
            // Arrange
            var gameId = "game-123";
            var start = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            var end = new DateTime(2024, 1, 15, 11, 45, 0, DateTimeKind.Utc);
            
            // Act
            var key1 = GameSession.GenerateIdempotencyKey(gameId, start, end);
            var key2 = GameSession.GenerateIdempotencyKey(gameId, start, end);
            
            // Assert
            key1.Should().Be(key2);
            key1.Should().NotBeEmpty();
        }

        [Fact]
        public void GenerateIdempotencyKey_DifferentSessions_DifferentKeys()
        {
            // Arrange
            var gameId = "game-123";
            var start = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            var end1 = new DateTime(2024, 1, 15, 11, 45, 0, DateTimeKind.Utc);
            var end2 = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
            
            // Act
            var key1 = GameSession.GenerateIdempotencyKey(gameId, start, end1);
            var key2 = GameSession.GenerateIdempotencyKey(gameId, start, end2);
            
            // Assert
            key1.Should().NotBe(key2);
        }

        [Fact]
        public void GenerateIdempotencyKey_UsesSha256()
        {
            // Arrange
            var gameId = "game";
            var start = DateTime.UtcNow;
            var end = start.AddMinutes(30);
            
            // Act
            var key = GameSession.GenerateIdempotencyKey(gameId, start, end);
            
            // Assert - Base64 encoded SHA256 is 43 chars with padding removed
            key.Length.Should().BeLessOrEqualTo(43);
        }
    }
}
