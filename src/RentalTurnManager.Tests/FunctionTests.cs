using Xunit;
using Moq;
using FluentAssertions;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Lambda;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests;

public class FunctionTests
{
    private readonly Mock<ISecretsService> _mockSecretsService;
    private readonly Mock<IEmailScannerService> _mockEmailScanner;
    private readonly Mock<IBookingParserService> _mockBookingParser;
    private readonly Mock<IPropertyConfigService> _mockPropertyConfig;
    private readonly Mock<IStepFunctionService> _mockStepFunction;
    private readonly IServiceProvider _serviceProvider;
    private readonly Function _function;

    public FunctionTests()
    {
        _mockSecretsService = new Mock<ISecretsService>();
        _mockEmailScanner = new Mock<IEmailScannerService>();
        _mockBookingParser = new Mock<IBookingParserService>();
        _mockPropertyConfig = new Mock<IPropertyConfigService>();
        _mockStepFunction = new Mock<IStepFunctionService>();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_mockSecretsService.Object);
        services.AddSingleton(_mockEmailScanner.Object);
        services.AddSingleton(_mockBookingParser.Object);
        services.AddSingleton(_mockPropertyConfig.Object);
        services.AddSingleton(_mockStepFunction.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _function = new Function(_serviceProvider, configuration);
    }

    [Fact]
    public async Task FunctionHandler_NoEmails_ReturnsSuccessWithZeroBookings()
    {
        // Arrange
        var credentials = new EmailCredentials();
        _mockSecretsService
            .Setup(x => x.GetEmailCredentialsAsync())
            .ReturnsAsync(credentials);

        _mockEmailScanner
            .Setup(x => x.ScanForBookingEmailsAsync(It.IsAny<EmailCredentials>()))
            .ReturnsAsync(new List<EmailMessage>());

        var context = new TestLambdaContext
        {
            AwsRequestId = "test-request-id"
        };

        var request = new LambdaRequest();

        // Act
        var result = await _function.FunctionHandler(request, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(0);
        result.WorkflowsStarted.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task FunctionHandler_OneValidBooking_StartsWorkflow()
    {
        // Arrange
        var credentials = new EmailCredentials();
        var email = new EmailMessage
        {
            Subject = "Booking confirmation",
            From = "automated@airbnb.com"
        };
        var booking = new Booking
        {
            Platform = "airbnb",
            BookingReference = "TEST123",
            PropertyId = "AIRBNB_001",
            CheckInDate = DateTime.UtcNow.AddDays(7)
        };
        var property = new PropertyConfiguration
        {
            PropertyId = "property-001",
            Cleaners = new List<CleanerContact>
            {
                new CleanerContact { Name = "Test Cleaner", Email = "cleaner@test.com", Rank = 1 }
            }
        };

        _mockSecretsService
            .Setup(x => x.GetEmailCredentialsAsync())
            .ReturnsAsync(credentials);

        _mockEmailScanner
            .Setup(x => x.ScanForBookingEmailsAsync(It.IsAny<EmailCredentials>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        _mockBookingParser
            .Setup(x => x.ParseBooking(It.IsAny<EmailMessage>()))
            .Returns(booking);

        _mockPropertyConfig
            .Setup(x => x.FindPropertyByPlatformId("airbnb", "AIRBNB_001"))
            .Returns(property);

        _mockStepFunction
            .Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .ReturnsAsync("arn:aws:states:us-east-1:123456789012:execution:test:exec-1");

        var context = new TestLambdaContext();
        var request = new LambdaRequest();

        // Act
        var result = await _function.FunctionHandler(request, context);

        // Assert
        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(1);
        result.WorkflowsStarted.Should().Be(1);
        result.Errors.Should().BeEmpty();

        _mockStepFunction.Verify(x => x.StartCleanerWorkflowAsync(
            It.Is<CleanerWorkflowInput>(w => 
                w.Booking.BookingReference == "TEST123" &&
                w.Property.PropertyId == "property-001")),
            Times.Once);
    }

    [Fact]
    public async Task FunctionHandler_BookingWithoutMatchingProperty_AddsError()
    {
        // Arrange
        var credentials = new EmailCredentials();
        var email = new EmailMessage
        {
            Subject = "Booking confirmation",
            From = "automated@airbnb.com"
        };
        var booking = new Booking
        {
            Platform = "airbnb",
            PropertyId = "UNKNOWN_PROPERTY",
            BookingReference = "TEST123"
        };

        _mockSecretsService
            .Setup(x => x.GetEmailCredentialsAsync())
            .ReturnsAsync(credentials);

        _mockEmailScanner
            .Setup(x => x.ScanForBookingEmailsAsync(It.IsAny<EmailCredentials>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        _mockBookingParser
            .Setup(x => x.ParseBooking(It.IsAny<EmailMessage>()))
            .Returns(booking);

        _mockPropertyConfig
            .Setup(x => x.FindPropertyByPlatformId("airbnb", "UNKNOWN_PROPERTY"))
            .Returns((PropertyConfiguration?)null);

        var context = new TestLambdaContext();
        var request = new LambdaRequest();

        // Act
        var result = await _function.FunctionHandler(request, context);

        // Assert
        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(1);
        result.WorkflowsStarted.Should().Be(0);
        result.Errors.Should().ContainSingle();
        result.Errors[0].Should().Contain("No property configuration found");
    }

    [Fact]
    public async Task FunctionHandler_SecretsManagerError_ReturnsFailed()
    {
        // Arrange
        _mockSecretsService
            .Setup(x => x.GetEmailCredentialsAsync())
            .ThrowsAsync(new Exception("Secrets Manager error"));

        var context = new TestLambdaContext();
        var request = new LambdaRequest();

        // Act
        var result = await _function.FunctionHandler(request, context);

        // Assert
        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Should().Contain("Fatal error");
    }
}
