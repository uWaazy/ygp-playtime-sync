using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class GameIdentityResolverTests
    {
        private readonly string _tempDir;
        private readonly GameIdentityResolver _resolver;

        public GameIdentityResolverTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"ygp_identity_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_tempDir);
            _resolver = new GameIdentityResolver(null, _tempDir);
        }

        [Fact]
        public async Task Resolve_NoMatch_ReturnsUnmapped()
        {
            // Arrange
            var game = new PlayniteGameInfo
            {
                Id = "unknown-game",
                Name = "Unknown Game",
                Source = "Unknown",
                SourceId = "",
                SourceGuid = Guid.NewGuid()
            };
            
            // Act
            var result = await _resolver.ResolveAsync(game);
            
            // Assert
            result.Status.Should().Be(IdentityStatus.Unmapped);
            result.Confidence.Should().Be(IdentityConfidence.None);
            result.RequiresManualMapping.Should().BeTrue();
        }

        [Fact]
        public async Task CreateManualMapping_SavesMapping()
        {
            // Arrange
            var playniteId = "playnite-123";
            var ygpId = "ygp-456";
            var ygpName = "Mapped Game";
            
            // Act
            await _resolver.CreateManualMappingAsync(playniteId, ygpId, ygpName);
            
            // Assert
            var mappings = await _resolver.GetAllMappingsAsync();
            mappings.Should().NotBeEmpty();
            var mapping = mappings.First();
            mapping.PlayniteGameId.Should().Be(playniteId);
            mapping.YgpGameId.Should().Be(ygpId);
            mapping.YgpGameName.Should().Be(ygpName);
        }

        [Fact]
        public async Task Resolve_WithManualMapping_ReturnsMapped()
        {
            // Arrange
            await _resolver.CreateManualMappingAsync("playnite-789", "ygp-012", "Mapped");
            
            var game = new PlayniteGameInfo
            {
                Id = "playnite-789",
                Name = "Test Game",
                Source = "Steam",
                SourceId = "999",
                SourceGuid = Guid.Parse("00000000-0000-0000-0000-000000000001")
            };
            
            // Act
            var result = await _resolver.ResolveAsync(game);
            
            // Assert
            result.Status.Should().Be(IdentityStatus.Mapped);
            result.Confidence.Should().Be(IdentityConfidence.Manual);
            result.YgpGameId.Should().Be("ygp-012");
        }

        [Fact]
        public async Task RemoveMapping_RemovesMapping()
        {
            // Arrange
            await _resolver.CreateManualMappingAsync("remove-me", "ygp-111", "To Remove");
            await _resolver.RemoveMappingAsync("remove-me");
            
            // Act
            var mappings = await _resolver.GetAllMappingsAsync();
            
            // Assert
            mappings.Should().NotContain(m => m.PlayniteGameId == "remove-me");
        }

        [Fact]
        public async Task ClearAllMappings_RemovesAll()
        {
            // Arrange
            await _resolver.CreateManualMappingAsync("one", "ygp-1", "One");
            await _resolver.CreateManualMappingAsync("two", "ygp-2", "Two");
            
            // Act
            await _resolver.ClearAllMappingsAsync();
            
            // Assert
            var mappings = await _resolver.GetAllMappingsAsync();
            mappings.Should().BeEmpty();
        }
    }

    /// <summary>
    /// Mock Playnite game info for testing.
    /// </summary>
    public class PlayniteGameInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string SourceId { get; set; }
        public Guid SourceGuid { get; set; }
        public string InstallDirectory { get; set; }
        public string ExecutablePath { get; set; }
        public string ImagePath { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsPlayed { get; set; }
        public int PlayTime { get; set; }
        public string Notes { get; set; }
        public int Rank { get; set; }
        public int Favorite { get; set; }
    }
}
