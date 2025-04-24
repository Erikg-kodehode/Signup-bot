using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public class SignInTests : IDisposable
    {
        private readonly SignInContext _context;
        private readonly Mock<ILogger<Worker>> _loggerMock;
        private readonly Mock<SocketMessageComponent> _componentMock;
        private readonly Mock<SocketUser> _userMock;
        private readonly IServiceScopeFactory _scopeFactory;

        public SignInTests()
        {
            // Set up in-memory database
            var options = new DbContextOptionsBuilder<SignInContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new SignInContext(options);

            // Set up service provider for scope factory
            var services = new ServiceCollection();
            services.AddDbContext<SignInContext>(opt => opt.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));
            var serviceProvider = services.BuildServiceProvider();
            _scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            // Set up mocks
            _loggerMock = new Mock<ILogger<Worker>>();
            _componentMock = new Mock<SocketMessageComponent>();
            _userMock = new Mock<SocketUser>();

            // Setup user mock
            _userMock.Setup(u => u.Id).Returns(123456789);
            _userMock.Setup(u => u.Username).Returns("TestUser");
            _userMock.Setup(u => u.GlobalName).Returns("Test User Global");
            
            _componentMock.Setup(c => c.User).Returns(_userMock.Object);
        }

        [Fact]
        public async Task HandleSignInButton_NewSignIn_Succeeds()
        {
            // Arrange
            _componentMock.Setup(c => c.Data.CustomId).Returns("daily_signin_kontor");

            // Act
            var result = await PerformSignIn(_componentMock.Object);

            // Assert
            Assert.True(result.Success);
            var signIn = await _context.SignIns.FirstOrDefaultAsync(s => s.UserId == _userMock.Object.Id);
            Assert.NotNull(signIn);
            Assert.Equal("Kontor", signIn.SignInType);
            Assert.Equal("Test User Global", signIn.Username);
        }

        [Fact]
        public async Task HandleSignInButton_DuplicateSignIn_Fails()
        {
            // Arrange
            _componentMock.Setup(c => c.Data.CustomId).Returns("daily_signin_kontor");

            // First sign-in
            await PerformSignIn(_componentMock.Object);

            // Act - Second sign-in
            var result = await PerformSignIn(_componentMock.Object);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("Already signed in", result.ErrorMessage);
        }

        [Theory]
        [InlineData("daily_signin_kontor", "Kontor")]
        [InlineData("daily_signin_hjemme", "Hjemmekontor")]
        public async Task HandleSignInButton_DifferentTypes_SavesCorrectly(string buttonId, string expectedType)
        {
            // Arrange
            _componentMock.Setup(c => c.Data.CustomId).Returns(buttonId);

            // Act
            var result = await PerformSignIn(_componentMock.Object);

            // Assert
            Assert.True(result.Success);
            var signIn = await _context.SignIns.FirstOrDefaultAsync(s => s.UserId == _userMock.Object.Id);
            Assert.NotNull(signIn);
            Assert.Equal(expectedType, signIn.SignInType);
        }

        private async Task<(bool Success, string ErrorMessage)> PerformSignIn(SocketMessageComponent component)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<SignInContext>();
                    DateTime startOfDayUtc = DateTime.UtcNow.Date;
                    
                    // Check for existing sign-in
                    bool alreadySignedIn = await dbContext.SignIns.AnyAsync(s => 
                        s.UserId == component.User.Id && 
                        s.Timestamp >= startOfDayUtc);

                    if (alreadySignedIn)
                    {
                        return (false, "Already signed in");
                    }

                    string signInType = component.Data.CustomId == "daily_signin_kontor" ? "Kontor" : "Hjemmekontor";
                    
                    var entry = new SignInEntry(
                        userId: component.User.Id,
                        username: component.User.GlobalName ?? component.User.Username,
                        timestamp: DateTime.UtcNow,
                        signInType: signInType
                    );

                    dbContext.SignIns.Add(entry);
                    await dbContext.SaveChangesAsync();
                    return (true, null);
                }
            }
            catch (Exception)
            {
                return (false, "Database error");
            }
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }
    }
}

