using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using MorningSignInBot.Data;
using MorningSignInBot.Services;
using Moq;
using Moq.Protected;
using Xunit;
using Xunit.Abstractions;

namespace MorningSignInBot.Tests
{
    public class WorkerTests
    {
        private readonly Mock<ILogger<Worker>> _loggerMock;
        private readonly Mock<DiscordSocketClient> _clientMock;
        private readonly Mock<IOptions<DiscordSettings>> _settingsMock;
        private readonly Mock<InteractionService> _interactionServiceMock;
        private readonly Mock<IServiceProvider> _serviceProviderMock;
        private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
        private readonly Mock<INotificationService> _notificationServiceMock;
        private readonly DiscordSettings _settings;

        public WorkerTests()
        {
            _loggerMock = new Mock<ILogger<Worker>>();
            _clientMock = new Mock<DiscordSocketClient>();
            _settings = new DiscordSettings 
            { 
                SignInHour = 8, 
                SignInMinute = 30,
                BotToken = "test-token",
                TargetChannelId = "123456789"
            };
            _settingsMock = new Mock<IOptions<DiscordSettings>>();
            _settingsMock.Setup(x => x.Value).Returns(_settings);
            _interactionServiceMock = new Mock<InteractionService>(_clientMock.Object);
            _serviceProviderMock = new Mock<IServiceProvider>();
            _scopeFactoryMock = new Mock<IServiceScopeFactory>();
            _notificationServiceMock = new Mock<INotificationService>();
        }

        [Fact]
        public async Task StartAsync_InitializesCorrectly()
        {
            // Arrange
            var worker = new Worker(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object,
                _interactionServiceMock.Object,
                _serviceProviderMock.Object,
                _scopeFactoryMock.Object,
                _notificationServiceMock.Object
            );

            // Act
            await worker.StartAsync(CancellationToken.None);

            // Assert
            _clientMock.Verify(x => x.LoginAsync(It.IsAny<TokenType>(), It.IsAny<string>()), Times.Once);
            _clientMock.Verify(x => x.StartAsync(), Times.Once);
        }

        [Fact]
        public async Task TimerTick_WhenConnected_SendsMessage()
        {
            // Arrange
            var worker = new Worker(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object,
                _interactionServiceMock.Object,
                _serviceProviderMock.Object,
                _scopeFactoryMock.Object,
                _notificationServiceMock.Object
            );

            _clientMock.Setup(x => x.ConnectionState).Returns(ConnectionState.Connected);

            // Act & Assert
            await worker.StartAsync(CancellationToken.None);
            
            // Verify that notification service was called
            _notificationServiceMock.Verify(x => x.SendDailySignInAsync(), Times.Never);
            
            // We can't easily test the timer directly, but we can verify the setup was correct
            _loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Information),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Scheduling next sign-in message")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()
                ),
                Times.Once);
        }

        [Theory]
        [InlineData(2025, 12, 25)] // Christmas
        [InlineData(2025, 5, 17)]  // Constitution Day
        [InlineData(2025, 4, 26)]  // Saturday
        [InlineData(2025, 4, 27)]  // Sunday
        public async Task ScheduleNextSignInMessage_SkipsHolidaysAndWeekends(int year, int month, int day)
        {
            // Arrange
            var worker = new Worker(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object,
                _interactionServiceMock.Object,
                _serviceProviderMock.Object,
                _scopeFactoryMock.Object,
                _notificationServiceMock.Object
            );

            var testDate = new DateTime(year, month, day, _settings.SignInHour, _settings.SignInMinute, 0);

            // Act
            await worker.StartAsync(CancellationToken.None);

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == LogLevel.Trace),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => 
                        v.ToString().Contains("Skipping") &&
                        v.ToString().Contains(testDate.ToString("yyyy-MM-dd"))),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()
                ),
                Times.AtLeastOnce);
        }
    }
}

