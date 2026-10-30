using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class SyncQueueTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly string _tempDir;
        private readonly SessionStorage _storage;
        private readonly SyncQueue _queue;

        public SyncQueueTests()
        {
            _loggerMock = new Mock<ILogger>();
            _tempDir = Path.Combine(Path.GetTempPath(), $"ygp_queue_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            _storage = new SessionStorage(_loggerMock.Object, _tempDir);
            _queue = new SyncQueue(_loggerMock.Object, _storage);
        }

        [Fact]
        public void Enqueue_AddsSessionToQueue()
        {
            // Arrange
            var session = CreateTestSession("game-1");
            
            // Act
            _queue.Enqueue(session);
            
            // Assert
            _queue.Count.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Enqueue_DuplicateSession_IgnoresDuplicate()
        {
            // Arrange
            var session = CreateTestSession("game-2");
            
            // Act
            _queue.Enqueue(session);
            _queue.Enqueue(session);
            
            // Assert - should not throw, second enqueue is ignored
            _queue.Count.Should().Be(1);
        }

        [Fact]
        public void Enqueue_SyncedSession_Ignores()
        {
            // Arrange
            var session = CreateTestSession("game-3");
            session.SyncStatus = SyncStatus.Synced;
            
            // Act
            _queue.Enqueue(session);
            
            // Assert
            _queue.Count.Should().Be(0);
        }

        [Fact]
        public void Enqueue_NullSession_Throws()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => _queue.Enqueue(null));
        }

        [Fact]
        public void Enqueue_EmptyId_Throws()
        {
            // Arrange
            var session = CreateTestSession("game-4");
            session.Id = null;
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _queue.Enqueue(session));
        }

        [Fact]
        public async Task ProcessQueue_WithMockProvider_SyncsSessions()
        {
            // Arrange
            var session = CreateTestSession("game-5");
            _storage.SaveSessionAsync(session).Wait();
            _queue.Enqueue(session);

            var mockProvider = new MockGameProfileProvider(shouldSucceed: true);
            
            // Act
            var result = await _queue.ProcessQueueAsync(mockProvider);
            
            // Assert
            result.SuccessfulCount.Should().Be(1);
            result.FailedCount.Should().Be(0);
        }

        [Fact]
        public async Task ProcessQueue_WithFailingProvider_MarksAsFailed()
        {
            // Arrange
            var session = CreateTestSession("game-6");
            _storage.SaveSessionAsync(session).Wait();
            _queue.Enqueue(session);

            var mockProvider = new MockGameProfileProvider(shouldSucceed: false);
            
            // Act
            var result = await _queue.ProcessQueueAsync(mockProvider);
            
            // Assert
            result.SuccessfulCount.Should().Be(0);
            result.FailedCount.Should().Be(1);
        }

        [Fact]
        public async Task ProcessQueue_AuthError_DoesNotRetry()
        {
            // Arrange
            var session = CreateTestSession("game-7");
            session.LastError = "401 Unauthorized";
            _storage.SaveSessionAsync(session).Wait();
            _queue.Enqueue(session);

            var mockProvider = new MockGameProfileProvider(shouldSucceed: true);
            
            // Act
            var result = await _queue.ProcessQueueAsync(mockProvider);
            
            // Assert - should be marked as failed due to auth error
            result.FailedCount.Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public void PendingCount_ReturnsCorrectCount()
        {
            // Arrange
            var session1 = CreateTestSession("game-8");
            var session2 = CreateTestSession("game-9");
            _storage.SaveSessionAsync(session1).Wait();
            _storage.SaveSessionAsync(session2).Wait();
            
            // Act
            var count = _queue.PendingCount;
            
            // Assert
            count.Should().Be(2);
        }

        private GameSession CreateTestSession(string gameId)
        {
            return new GameSession
            {
                Id = GameSession.GenerateIdempotencyKey(gameId, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30)),
                PlayniteGameId = gameId,
                GameName = $"Test Game {gameId}",
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
