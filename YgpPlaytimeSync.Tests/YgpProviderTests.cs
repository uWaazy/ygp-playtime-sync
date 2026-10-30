using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace YgpPlaytimeSync.Tests
{
    public class YgpProviderTests
    {
        [Fact]
        public void Constructor_SetsProviderName()
        {
            var provider = CreateTestProvider();
            provider.Should().NotBeNull();
            provider.ProviderName.Should().Be("Your Gamer Profile");
        }

        [Fact]
        public void Constructor_WithConnectedSettings_SetsConnectedStatus()
        {
            var settings = new PluginSettings
            {
                IsConnected = true,
                Username = "tester"
            };
            var provider = CreateTestProvider(settings);
            provider.ConnectionStatus.Should().Be(ProviderConnectionStatus.Connected);
        }

        [Fact]
        public void Constructor_WithDisconnectedSettings_SetsDisconnectedStatus()
        {
            var settings = new PluginSettings
            {
                IsConnected = false
            };
            var provider = CreateTestProvider(settings);
            provider.ConnectionStatus.Should().Be(ProviderConnectionStatus.Disconnected);
        }

        [Fact]
        public void IsAuthError_WithAuthPrefix_ReturnsTrue()
        {
            var provider = CreateTestProvider();
            provider.IsAuthError("[AUTH] Sessão expirada").Should().BeTrue();
        }

        [Fact]
        public void IsAuthError_WithoutAuthPrefix_ReturnsFalse()
        {
            var provider = CreateTestProvider();
            provider.IsAuthError("500 Internal Server Error").Should().BeFalse();
            provider.IsAuthError("Request timeout").Should().BeFalse();
            provider.IsAuthError("[NOTFOUND] Game not found").Should().BeFalse();
            provider.IsAuthError("").Should().BeFalse();
            provider.IsAuthError(null).Should().BeFalse();
        }

        [Fact]
        public void GetStatusMessage_Disconnected_ReturnsConnectPrompt()
        {
            var provider = CreateTestProvider();
            provider.GetStatusMessage().Should().Contain("Vincular conta");
        }

        [Fact]
        public void GetStatusMessage_Connected_ReturnsUsername()
        {
            var settings = new PluginSettings
            {
                IsConnected = true,
                Username = "sam"
            };
            var provider = CreateTestProvider(settings);
            provider.GetStatusMessage().Should().Contain("sam");
        }

        [Fact]
        public void GetStatusMessage_Connected_NoUsername_ReturnsGeneric()
        {
            var settings = new PluginSettings
            {
                IsConnected = true,
                Username = null
            };
            var provider = CreateTestProvider(settings);
            provider.GetStatusMessage().Should().Be("Conectado");
        }

        [Fact]
        public async Task FindGameAsync_ReturnsIdentityFromName()
        {
            var provider = CreateTestProvider();
            var identity = await provider.FindGameAsync("Portal 2", "PC (Microsoft Windows)");
            identity.Should().NotBeNull();
            identity.YgpGameName.Should().Be("Portal 2");
            identity.Platform.Should().Be("PC (Microsoft Windows)");
        }

        [Fact]
        public async Task DisconnectAsync_ClearsConnectionStatus()
        {
            var settings = new PluginSettings { IsConnected = true, Username = "x" };
            var provider = CreateTestProvider(settings);
            await provider.DisconnectAsync();
            provider.ConnectionStatus.Should().Be(ProviderConnectionStatus.Disconnected);
        }

        private YgpProvider CreateTestProvider(PluginSettings settings = null)
        {
            L.Configure("pt", null);
            var logger = new MockLogger();
            var apiMock = new Mock<IPlayniteAPI>();
            var resolver = new GameIdentityResolver(logger, Path.Combine(Path.GetTempPath(), $"ygp_provider_test_{Guid.NewGuid()}"));
            return new YgpProvider(apiMock.Object, settings ?? new PluginSettings(), logger, resolver);
        }
    }

    internal class MockLogger : ILogger
    {
        public void Trace(string message) { }
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Fatal(string message) { }
        public void Fatal(string message, Exception ex) { }
    }
}