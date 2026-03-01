/************************
 * Rental Turn Manager
 * FunctionCancellationTests.cs
 *
 * Unit tests for cancellation email idempotency in the main Lambda
 * function. Verifies that:
 *   1. A cancellation whose booking JSON is already marked IsCancelled
 *      is skipped without re-sending emails.
 *   2. A new cancellation marks the S3 booking JSON as IsCancelled and
 *      sets CancellationProcessedAt after processing.
 *
 * Author: Brent Foster
 ***********************/

using Amazon.Lambda.Core;
using Amazon.Lambda.Model;
using Amazon.Lambda.TestUtilities;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Lambda;
using RentalTurnManager.Models;
using Xunit;

namespace RentalTurnManager.Tests;

public class FunctionCancellationTests
{
    private readonly Mock<ISecretsService> _mockSecretsService;
    private readonly Mock<IEmailScannerService> _mockEmailScanner;
    private readonly Mock<IBookingParserService> _mockBookingParser;
    private readonly Mock<IStepFunctionService> _mockStepFunction;
    private readonly Mock<IBookingStateService> _mockBookingStateService;
    private readonly Mock<Amazon.Lambda.IAmazonLambda> _mockLambdaClient;
    private readonly Function _function;

    // A cancellation email returned by the second ScanForBookingEmailsAsync call
    private readonly EmailMessage _cancelEmail = new()
    {
        Subject = "Booking canceled by traveler: Aug 8, 2026 - Aug 15, 2026 (Property ID 4906384) Reservation HA-Z2T89B",
        From = "do-not-reply@vrbo.com"
    };

    // Minimal booking as parsed from the cancellation email
    private readonly Booking _parsedCancellation = new()
    {
        Platform = "vrbo",
        BookingReference = "HA-Z2T89B",
        PropertyId = "4906384"
    };

    // The corresponding booking that lives in S3
    private readonly Booking _storedBooking = new()
    {
        Platform = "vrbo",
        BookingReference = "HA-Z2T89B",
        PropertyId = "4906384",
        CheckInDate = new DateTime(2026, 8, 8, 14, 0, 0, DateTimeKind.Utc),
        CheckOutDate = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
        GuestName = "Jane Doe",
        AssignedCleanerName = null, // not yet assigned – keeps ProcessBookingCancellationAsync minimal
        IsCancelled = false
    };

    public FunctionCancellationTests()
    {
        _mockSecretsService = new Mock<ISecretsService>();
        _mockEmailScanner = new Mock<IEmailScannerService>();
        _mockBookingParser = new Mock<IBookingParserService>();
        _mockStepFunction = new Mock<IStepFunctionService>();
        _mockBookingStateService = new Mock<IBookingStateService>();
        _mockLambdaClient = new Mock<Amazon.Lambda.IAmazonLambda>();

        var propertiesConfig = new PropertiesConfiguration
        {
            EmailFilters = new EmailFilterConfiguration
            {
                BookingPlatformFromAddresses = new List<string> { "vrbo.com" },
                SubjectPatterns = new List<string> { "Reservation confirmed" },
                CancellationSubjectPatterns = new List<string> { "canceled by traveler" }
            },
            Properties = new List<PropertyConfiguration>
            {
                new PropertyConfiguration
                {
                    PropertyId = "test-property-1",
                    PlatformIds = new Dictionary<string, string> { { "vrbo", "4906384" } },
                    Address = "123 Test St",
                    Cleaners = new List<CleanerContact>
                    {
                        new CleanerContact { Name = "Test Cleaner", Email = "cleaner@test.com", Phone = "+1-555-0100", Rank = 1 }
                    }
                }
            }
        };

        _mockSecretsService
            .Setup(x => x.GetEmailCredentialsAsync())
            .ReturnsAsync(new EmailCredentials());

        // First ScanForBookingEmailsAsync call = regular booking scan → empty
        // Second call = cancellation scan → the cancel email
        _mockEmailScanner
            .SetupSequence(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(),
                It.IsAny<bool>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>())   // booking emails
            .ReturnsAsync(new List<EmailMessage> { _cancelEmail }); // cancellation emails

        _mockBookingParser
            .Setup(x => x.ParseCancellation(_cancelEmail))
            .Returns(_parsedCancellation);

        _mockBookingStateService
            .Setup(x => x.HasBookingChangedAsync(It.IsAny<Booking>()))
            .ReturnsAsync(false);

        // Default: SaveBookingAsync is a no-op
        _mockBookingStateService
            .Setup(x => x.SaveBookingAsync(It.IsAny<Booking>()))
            .Returns(Task.CompletedTask);

        _mockLambdaClient
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse { StatusCode = 200 });

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton(_mockSecretsService.Object);
        services.AddSingleton(_mockEmailScanner.Object);
        services.AddSingleton(_mockBookingParser.Object);
        services.AddSingleton(_mockStepFunction.Object);
        services.AddSingleton(_mockBookingStateService.Object);
        services.AddSingleton(_mockLambdaClient.Object);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var serviceProvider = services.BuildServiceProvider();
        _function = new Function(serviceProvider, configuration, propertiesConfig);
    }

    /// <summary>
    /// When the booking JSON in S3 already has IsCancelled = true the handler must
    /// skip re-processing and never call SaveBookingAsync a second time.
    /// </summary>
    [Fact]
    public async Task FunctionHandler_CancellationAlreadyProcessed_SkipsWithoutResaving()
    {
        // Arrange – return a booking that was already cancelled
        var alreadyCancelled = CloneBooking(_storedBooking);
        alreadyCancelled.IsCancelled = true;
        alreadyCancelled.CancellationProcessedAt = DateTime.UtcNow.AddHours(-1);

        _mockBookingStateService
            .Setup(x => x.GetBookingAsync(_parsedCancellation.Platform, _parsedCancellation.BookingReference))
            .ReturnsAsync(alreadyCancelled);

        var context = new TestLambdaContext { AwsRequestId = "test-already-cancelled" };

        // Act
        var result = await _function.FunctionHandler(new LambdaRequest(), context);

        // Assert – handler succeeds but the cancellation counter stays at zero
        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(0);

        // SaveBookingAsync must NOT be called – the booking state should not be mutated
        _mockBookingStateService.Verify(
            x => x.SaveBookingAsync(It.IsAny<Booking>()),
            Times.Never,
            "Booking must not be re-saved when the cancellation was already processed");

        // The email should still be marked processed so it is not seen again
        _mockEmailScanner.Verify(
            x => x.MarkEmailAsProcessedAsync(It.IsAny<EmailCredentials>(), _cancelEmail),
            Times.Once,
            "Email must be marked as processed even when skipped");
    }

    /// <summary>
    /// When the booking in S3 has IsCancelled = false the handler must process the
    /// cancellation and then save the booking back with IsCancelled = true and a
    /// non-null CancellationProcessedAt timestamp.
    /// </summary>
    [Fact]
    public async Task FunctionHandler_NewCancellation_SavesBookingWithIsCancelledTrue()
    {
        // Arrange – booking is NOT yet cancelled
        _mockBookingStateService
            .Setup(x => x.GetBookingAsync(_parsedCancellation.Platform, _parsedCancellation.BookingReference))
            .ReturnsAsync(CloneBooking(_storedBooking));

        Booking? savedBooking = null;
        _mockBookingStateService
            .Setup(x => x.SaveBookingAsync(It.IsAny<Booking>()))
            .Callback<Booking>(b => savedBooking = b)
            .Returns(Task.CompletedTask);

        var context = new TestLambdaContext { AwsRequestId = "test-new-cancellation" };

        // Act
        var result = await _function.FunctionHandler(new LambdaRequest(), context);

        // Assert
        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(1);

        _mockBookingStateService.Verify(
            x => x.SaveBookingAsync(It.IsAny<Booking>()),
            Times.Once,
            "Booking must be saved back to S3 after processing a cancellation");

        savedBooking.Should().NotBeNull();
        savedBooking!.IsCancelled.Should().BeTrue("IsCancelled must be set after processing");
        savedBooking.CancellationProcessedAt.Should().NotBeNull("CancellationProcessedAt must be stamped");
        savedBooking.CancellationProcessedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        _mockEmailScanner.Verify(
            x => x.MarkEmailAsProcessedAsync(It.IsAny<EmailCredentials>(), _cancelEmail),
            Times.Once);
    }

    /// <summary>
    /// When the owner notification lambda invoke fails, ProcessBookingCancellationAsync returns false.
    /// The booking must NOT be saved as IsCancelled and the email must NOT be marked processed,
    /// so the next Lambda run retries the cancellation.
    /// </summary>
    [Fact]
    public async Task FunctionHandler_CancellationOwnerEmailFails_DoesNotMarkBookingCancelled()
    {
        // Arrange – return a booking that has not yet been cancelled
        _mockBookingStateService
            .Setup(x => x.GetBookingAsync(_parsedCancellation.Platform, _parsedCancellation.BookingReference))
            .ReturnsAsync(CloneBooking(_storedBooking));

        // Force the calendar lambda invoke to throw (simulates IAM denial / timeout)
        _mockLambdaClient
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ThrowsAsync(new Amazon.Lambda.AmazonLambdaException("not authorized to perform: lambda:InvokeFunction"));

        var context = new TestLambdaContext { AwsRequestId = "test-owner-email-fail" };

        // Act
        var result = await _function.FunctionHandler(new LambdaRequest(), context);

        // Assert – overall handler succeeds but the cancellation was not counted
        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(0,
            "cancellation should not be counted when the owner notification failed");

        // The booking must NOT be saved as cancelled so the next run retries
        _mockBookingStateService.Verify(
            x => x.SaveBookingAsync(It.IsAny<Booking>()),
            Times.Never,
            "Booking must not be marked IsCancelled when email delivery failed");

        // The email must NOT be marked processed — it must be retried next run
        _mockEmailScanner.Verify(
            x => x.MarkEmailAsProcessedAsync(It.IsAny<EmailCredentials>(), _cancelEmail),
            Times.Never,
            "Cancellation email must not be marked processed when owner notification failed");
    }

    // Simple deep-copy helper to avoid mutation across tests
    private static Booking CloneBooking(Booking b) => new()
    {
        Platform = b.Platform,
        BookingReference = b.BookingReference,
        PropertyId = b.PropertyId,
        CheckInDate = b.CheckInDate,
        CheckOutDate = b.CheckOutDate,
        GuestName = b.GuestName,
        AssignedCleanerName = b.AssignedCleanerName,
        AssignedCleanerEmail = b.AssignedCleanerEmail,
        AssignedCleanerPhone = b.AssignedCleanerPhone,
        ScheduledCleaningTime = b.ScheduledCleaningTime,
        IsCancelled = b.IsCancelled,
        CancellationProcessedAt = b.CancellationProcessedAt
    };
}
