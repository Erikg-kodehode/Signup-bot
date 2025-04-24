using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MorningSignInBot.Configuration;
using MorningSignInBot.Services;
using Moq;
using Moq.Protected;
using Xunit;
using Xunit.Abstractions;

namespace MorningSignInBot.Tests
{
    public class NotificationServiceTests
    {
        private readonly Mock<ILogger<NotificationService>> _loggerMock;
        private readonly Mock<DiscordSocketClient> _clientMock;
        private readonly Mock<IOptions<DiscordSettings>> _settingsMock;
        private readonly Mock<ITextChannel> _channelMock;
        private readonly Mock<IMessage> _messageMock;
        private readonly DiscordSettings _settings;

        public NotificationServiceTests()
        {
            _loggerMock = new Mock<ILogger<NotificationService>>();
            _clientMock = new Mock<DiscordSocketClient>();
            _settings = new DiscordSettings
            {
                TargetChannelId = "123456789",
                SignInHour = 8,
                SignInMinute = 30
            };
            _settingsMock = new Mock<IOptions<DiscordSettings>>();
            _settingsMock.Setup(x => x.Value).Returns(_settings);
            
            _channelMock = new Mock<ITextChannel>();
            _messageMock = new Mock<IMessage>();

            // Setup channel mock
            _channelMock.Setup(c => c.Id).Returns(123456789);
            _clientMock.Setup(c => c.GetChannelAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                      .ReturnsAsync(_channelMock.Object);
        }

        [Fact]
        public async Task SendDailySignInAsync_OnWeekend_DoesNotSendMessage()
        {
            // Arrange
            var service = new NotificationService(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object
            );

            // Set current time to a Saturday
            var saturday = new DateTime(2025, 4, 26);

            // Act
            await service.SendDailySignInAsync();

            // Assert
            _channelMock.Verify(
                x => x.SendMessageAsync(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<Embed>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageReference>(),
                    It.IsAny<MessageComponent>(),
                    It.IsAny<ISticker[]>(),
                    It.IsAny<Embed[]>(),
                    It.IsAny<MessageFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task SendDailySignInAsync_OnWeekday_SendsMessageWithButtons()
        {
            // Arrange
            var service = new NotificationService(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object
            );

            _channelMock.Setup(c => c.SendMessageAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<Embed>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(),
                It.Is<MessageComponent>(mc => 
                    mc.Components.Count == 1 && // One action row
                    mc.Components[0].Components.Count == 2), // Two buttons
                It.IsAny<ISticker[]>(),
                It.IsAny<Embed[]>(),
                It.IsAny<MessageFlags>()))
            .ReturnsAsync(_messageMock.Object);

            // Act
            await service.SendDailySignInAsync();

            // Assert
            _channelMock.Verify(
                x => x.SendMessageAsync(
                    It.Is<string>(msg => msg.Contains("God morgen!")),
                    It.IsAny<bool>(),
                    It.IsAny<Embed>(),
                    It.IsAny<RequestOptions>(),
                    It.IsAny<AllowedMentions>(),
                    It.IsAny<MessageReference>(),
                    It.IsAny<MessageComponent>(),
                    It.IsAny<ISticker[]>(),
                    It.IsAny<Embed[]>(),
                    It.IsAny<MessageFlags>()),
                Times.Once);
        }

        [Fact]
        public async Task SendDailySignInAsync_DeletesPreviousMessage()
        {
            // Arrange
            var service = new NotificationService(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object
            );

            // Setup for previous message
            _messageMock.Setup(m => m.Id).Returns(987654321);
            _channelMock.Setup(c => c.GetMessageAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                       .ReturnsAsync(_messageMock.Object);

            // Act
            await service.SendDailySignInAsync();

            // Assert
            _channelMock.Verify(
                x => x.DeleteMessageAsync(It.IsAny<ulong>(), It.IsAny<RequestOptions>()),
                Times.Once);
        }

        [Fact]
        public async Task SendDailySignInAsync_WhenChannelNotFound_LogsError()
        {
            // Arrange
            _clientMock.Setup(c => c.GetChannelAsync(It.IsAny<ulong>(), It.IsAny<CacheMode>(), It.IsAny<RequestOptions>()))
                      .ReturnsAsync((IChannel)null);

            var service = new NotificationService(
                _loggerMock.Object,
                _clientMock.Object,
                _settingsMock.Object
            );

            // Act
            await service.SendDailySignInAsync();

            // Assert
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("not found")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }
    }
}

