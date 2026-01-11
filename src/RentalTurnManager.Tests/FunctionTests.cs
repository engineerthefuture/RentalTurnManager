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
    private readonly Mock<IStepFunctionService> _mockStepFunction;
    private readonly Mock<IBookingStateService> _mockBookingStateService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Function _function;
    private readonly PropertiesConfiguration _propertiesConfig;

    public FunctionTests()
    {
        _mockSecretsService = new Mock<ISecretsService>();
        _mockEmailScanner = new Mock<IEmailScannerService>();
        _mockBookingParser = new Mock<IBookingParserService>();
        _mockStepFunction = new Mock<IStepFunctionService>();
        _mockBookingStateService = new Mock<IBookingStateService>();

        // Setup properties configuration
        _propertiesConfig = new PropertiesConfiguration
        {
            EmailFilters = new EmailFilterConfiguration
            {
                BookingPlatformFromAddresses = new List<string> { "airbnb.com", "vrbo.com", "booking.com" },
                SubjectPatterns = new List<string> { "Reservation confirmed", "Instant Booking from", "booking confirmation" }
            },
            Properties = new List<PropertyConfiguration>
            {
                new PropertyConfiguration
                {
                    PropertyId = "test-property-1",
                    PlatformIds = new Dictionary<string, string>
                    {
                        { "airbnb", "AIRBNB_001" },
                        { "vrbo", "VRBO_001" },
                        { "bookingcom", "BOOKING_001" }
                    },
                    Address = "123 Test St",
                    Cleaners = new List<CleanerContact>
                    {
                        new CleanerContact { Name = "Test Cleaner", Email = "test@example.com", Phone = "+1-555-0100", Rank = 1 }
                    }
                }
            }
        };

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_mockSecretsService.Object);
        services.AddSingleton(_mockEmailScanner.Object);
        services.AddSingleton(_mockBookingParser.Object);
        services.AddSingleton(_mockStepFunction.Object);
        services.AddSingleton(_mockBookingStateService.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        _serviceProvider = services.BuildServiceProvider();
        _function = new Function(_serviceProvider, configuration, _propertiesConfig);
        
        // Setup default behavior for booking state service
        _mockBookingStateService.Setup(x => x.HasBookingChangedAsync(It.IsAny<Booking>()))
            .ReturnsAsync(true); // By default, treat all bookings as new/changed
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
            .Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), 
                It.IsAny<bool>(), 
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>()))
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
            .Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), 
                It.IsAny<bool>(), 
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        _mockBookingParser
            .Setup(x => x.ParseBooking(It.IsAny<EmailMessage>()))
            .Returns(booking);

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
                w.Property.PropertyId == "test-property-1")),
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
            .Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), 
                It.IsAny<bool>(), 
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        _mockBookingParser
            .Setup(x => x.ParseBooking(It.IsAny<EmailMessage>()))
            .Returns(booking);

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
