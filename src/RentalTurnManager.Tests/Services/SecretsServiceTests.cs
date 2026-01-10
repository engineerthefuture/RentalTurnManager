using Xunit;
using Moq;
using FluentAssertions;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;
using System.Text.Json;

namespace RentalTurnManager.Tests.Services;

public class SecretsServiceTests
{
    private readonly Mock<IAmazonSecretsManager> _mockSecretsManager;
    private readonly Mock<ILogger<SecretsService>> _mockLogger;
    private readonly SecretsService _service;

    public SecretsServiceTests()
    {
        _mockSecretsManager = new Mock<IAmazonSecretsManager>();
        _mockLogger = new Mock<ILogger<SecretsService>>();
        _service = new SecretsService(_mockSecretsManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetEmailCredentialsAsync_ValidSecret_ReturnsCredentials()
    {
        // Arrange
        var expectedCredentials = new EmailCredentials
        {
            Host = "imap.test.com",
            Port = 993,
            Username = "test@example.com",
            Password = "password123",
            UseSsl = true
        };

        var secretJson = JsonSerializer.Serialize(expectedCredentials);
        var response = new GetSecretValueResponse
        {
            SecretString = secretJson
        };

        _mockSecretsManager
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetEmailCredentialsAsync();

        // Assert
        result.Should().NotBeNull();
        result.Host.Should().Be(expectedCredentials.Host);
        result.Port.Should().Be(expectedCredentials.Port);
        result.Username.Should().Be(expectedCredentials.Username);
        result.Password.Should().Be(expectedCredentials.Password);
        result.UseSsl.Should().Be(expectedCredentials.UseSsl);
    }

    [Fact]
    public async Task GetEmailCredentialsAsync_SecretNotFound_ThrowsException()
    {
        // Arrange
        _mockSecretsManager
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ThrowsAsync(new ResourceNotFoundException("Secret not found"));

        // Act & Assert
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => _service.GetEmailCredentialsAsync());
    }

    [Fact]
    public async Task GetEmailCredentialsAsync_InvalidJson_ThrowsException()
    {
        // Arrange
        var response = new GetSecretValueResponse
        {
            SecretString = "invalid json"
        };

        _mockSecretsManager
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(response);

        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(() => _service.GetEmailCredentialsAsync());
    }
}
