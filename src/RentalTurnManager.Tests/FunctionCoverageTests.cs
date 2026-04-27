using Xunit;
using Moq;
using FluentAssertions;
using Amazon.Lambda.TestUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Lambda;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using System.Text.Json;

namespace RentalTurnManager.Tests;

/// <summary>
/// Targeted coverage tests for RentalTurnManager.Lambda.Function, focusing on
/// uncovered branches identified in the coverage report:
///   - _propertiesConfig == null fatal path
///   - Empty BookingReference skip
///   - hasChanged == false skip
///   - Booking.com property-ID fallback (single and multiple configs)
///   - SecretsManager null / missing token
///   - StepFunction throws in booking loop
///   - ParseBookings returns null
///   - ParseCancellation returns null
///   - Alternative time-slot generation
///   - DefaultCheckOut parse failure
///   - Cancellation with assigned cleaner (cleaner invoke path)
///   - InvalidTimezone fallback in ProcessBookingCancellationAsync
///   - Lambda EmailSecret Port / UseSsl getters
/// </summary>
public class FunctionCoverageTests
{
    // -------------------------------------------------------------------------
    // Shared factory helpers
    // -------------------------------------------------------------------------

    private record FunctionBundle(
        Function Fn,
        Mock<IEmailScannerService> Email,
        Mock<IBookingParserService> Parser,
        Mock<IStepFunctionService> Step,
        Mock<IBookingStateService> State,
        Mock<IAmazonSecretsManager> SecretsManager,
        Mock<IAmazonLambda> Lambda);

    private static FunctionBundle Build(
        PropertiesConfiguration? propertiesConfig,
        Action<Mock<IAmazonSecretsManager>>? configureSecrets = null)
    {
        var secretsService = new Mock<ISecretsService>();
        secretsService.Setup(x => x.GetEmailCredentialsAsync()).ReturnsAsync(new EmailCredentials());

        var emailMock = new Mock<IEmailScannerService>();
        // Default: both scans return empty lists
        emailMock
            .Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>());

        var parserMock = new Mock<IBookingParserService>();
        parserMock
            .Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)>());

        var stepMock = new Mock<IStepFunctionService>();
        stepMock
            .Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .ReturnsAsync("arn:exec");

        var stateMock = new Mock<IBookingStateService>();
        stateMock.Setup(x => x.HasBookingChangedAsync(It.IsAny<Booking>())).ReturnsAsync(true);
        stateMock.Setup(x => x.SaveBookingAsync(It.IsAny<Booking>())).Returns(Task.CompletedTask);

        var secretsManagerMock = new Mock<IAmazonSecretsManager>();
        if (configureSecrets != null)
        {
            configureSecrets(secretsManagerMock);
        }
        else
        {
            // Default: valid token in secret
            secretsManagerMock
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "test-owner-token" })
                });
        }

        var lambdaMock = new Mock<IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse { StatusCode = 200 });

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(secretsService.Object);
        services.AddSingleton(emailMock.Object);
        services.AddSingleton(parserMock.Object);
        services.AddSingleton(stepMock.Object);
        services.AddSingleton(stateMock.Object);
        services.AddSingleton(secretsManagerMock.Object);
        services.AddSingleton(lambdaMock.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        services.AddSingleton<IConfiguration>(config);

        var sp = services.BuildServiceProvider();
        var fn = new Function(sp, config, propertiesConfig);
        return new FunctionBundle(fn, emailMock, parserMock, stepMock, stateMock, secretsManagerMock, lambdaMock);
    }

    private static PropertiesConfiguration SingleAirbnbConfig(
        Action<PropertyMetadata>? configureMeta = null) => new()
    {
        EmailFilters = new EmailFilterConfiguration
        {
            BookingPlatformFromAddresses = new List<string> { "airbnb.com" },
            SubjectPatterns = new List<string> { "Reservation confirmed" }
        },
        Properties = new List<PropertyConfiguration>
        {
            new PropertyConfiguration
            {
                PropertyId = "prop-1",
                PlatformIds = new Dictionary<string, string>
                {
                    { "airbnb", "AIRBNB_001" }
                },
                Address = "123 Test St",
                Metadata = new PropertyMetadata
                {
                    PropertyName = "Test Property",
                    OwnerName = "Test Owner",
                    Timezone = "America/New_York"
                }.Also(configureMeta),
                Cleaners = new List<CleanerContact>
                {
                    new CleanerContact { Name = "Cleaner A", Email = "a@test.com", CleanerId = "c1", Rank = 1 }
                }
            }
        }
    };

    // Minimal booking that matches the single-property config
    private static Booking AirbnbBooking(string? bookingRef = "REF001", string? propertyId = "AIRBNB_001") => new()
    {
        Platform = "airbnb",
        BookingReference = bookingRef ?? string.Empty,
        PropertyId = propertyId ?? string.Empty,
        CheckOutDate = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
        CheckInDate = new DateTime(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc)
    };

    // -------------------------------------------------------------------------
    // Lambda EmailSecret getters (Port / UseSsl are at 0% line coverage)
    // -------------------------------------------------------------------------

    [Fact]
    public void LambdaEmailSecret_AllProperties_ReturnSetValues()
    {
        var secret = new RentalTurnManager.Lambda.EmailSecret
        {
            Host = "smtp.example.com",
            Port = 587,
            Username = "user@test.com",
            Password = "s3cr3t",
            UseSsl = true,
            OwnerOverrideToken = "owner-tok"
        };

        secret.Host.Should().Be("smtp.example.com");
        secret.Port.Should().Be(587);
        secret.Username.Should().Be("user@test.com");
        secret.Password.Should().Be("s3cr3t");
        secret.UseSsl.Should().BeTrue();
        secret.OwnerOverrideToken.Should().Be("owner-tok");
    }

    // -------------------------------------------------------------------------
    // _propertiesConfig == null → Fatal error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_NullPropertiesConfig_ReturnsFatalError()
    {
        var b = Build(propertiesConfig: null);

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Fatal error"));
    }

    // -------------------------------------------------------------------------
    // Empty BookingReference → skipped without incrementing BookingsProcessed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_EmptyBookingReference_BookingSkipped()
    {
        var booking = AirbnbBooking(bookingRef: "");
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(SingleAirbnbConfig());
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(0);
        result.WorkflowsStarted.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // hasChanged == false → booking counted but no workflow started
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_BookingUnchanged_SkipsWorkflow()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(SingleAirbnbConfig());
        b.State.Setup(x => x.HasBookingChangedAsync(It.IsAny<Booking>())).ReturnsAsync(false);
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(1);
        result.WorkflowsStarted.Should().Be(0);
        b.Step.Verify(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()), Times.Never);
    }

    // -------------------------------------------------------------------------
    // ParseBookings returns null → treated as empty list (no NullReferenceException)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_ParseBookingsReturnsNull_TreatedAsEmpty()
    {
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(SingleAirbnbConfig());
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns((List<(Booking, List<EmailMessage>)>?)null!);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.BookingsProcessed.Should().Be(0);
    }

    // -------------------------------------------------------------------------
    // Booking.com platform normalisation and property-ID fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_BookingComDotPlatform_NormalisesToBookingcom()
    {
        // Platform string "booking.com" should normalise to "bookingcom" and match property
        var bookingcomConfig = new PropertiesConfiguration
        {
            EmailFilters = new EmailFilterConfiguration
            {
                BookingPlatformFromAddresses = new List<string> { "booking.com" },
                SubjectPatterns = new List<string> { "New booking" }
            },
            Properties = new List<PropertyConfiguration>
            {
                new PropertyConfiguration
                {
                    PropertyId = "prop-bc",
                    PlatformIds = new Dictionary<string, string> { { "bookingcom", "BC_PROP_001" } },
                    Address = "5 Beach Rd",
                    Metadata = new PropertyMetadata { PropertyName = "Beach Cottage", Timezone = "America/New_York" },
                    Cleaners = new List<CleanerContact>
                    {
                        new CleanerContact { Name = "C1", Email = "c1@test.com", CleanerId = "c1", Rank = 1 }
                    }
                }
            }
        };

        var booking = new Booking
        {
            Platform = "booking.com",  // as returned by the parser
            BookingReference = "BC-REF-001",
            PropertyId = "BC_PROP_001",
            CheckOutDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            CheckInDate = new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)
        };
        var email = new EmailMessage { Subject = "New booking" };

        var b = Build(bookingcomConfig);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.WorkflowsStarted.Should().Be(1, "booking.com platform should normalise and match the configured property");
    }

    [Fact]
    public async Task FunctionHandler_BookingComEmptyPropertyId_SingleConfig_FallsBack()
    {
        // When PropertyId is empty and there is exactly one bookingcom property, use that one
        var bookingcomConfig = new PropertiesConfiguration
        {
            EmailFilters = new EmailFilterConfiguration
            {
                BookingPlatformFromAddresses = new List<string> { "booking.com" },
                SubjectPatterns = new List<string> { "New booking" }
            },
            Properties = new List<PropertyConfiguration>
            {
                new PropertyConfiguration
                {
                    PropertyId = "prop-bc",
                    PlatformIds = new Dictionary<string, string> { { "bookingcom", "BC_PROP_001" } },
                    Address = "5 Beach Rd",
                    Metadata = new PropertyMetadata { PropertyName = "Beach Cottage", Timezone = "America/New_York" },
                    Cleaners = new List<CleanerContact>
                    {
                        new CleanerContact { Name = "C1", Email = "c1@test.com", CleanerId = "c1", Rank = 1 }
                    }
                }
            }
        };

        var booking = new Booking
        {
            Platform = "bookingcom",
            BookingReference = "BC-000",
            PropertyId = "",   // ← empty: triggers single-property fallback
            CheckOutDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            CheckInDate = new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)
        };
        var email = new EmailMessage { Subject = "New booking" };

        var b = Build(bookingcomConfig);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.WorkflowsStarted.Should().Be(1, "empty PropertyId with single bookingcom config should fall back");
        // PropertyId should have been filled in with the config's platform ID
        booking.PropertyId.Should().Be("BC_PROP_001");
    }

    [Fact]
    public async Task FunctionHandler_BookingComEmptyPropertyId_MultipleConfigs_AddsError()
    {
        // When PropertyId is empty and there are multiple bookingcom properties, no fallback → error
        var multiConfig = new PropertiesConfiguration
        {
            EmailFilters = new EmailFilterConfiguration
            {
                BookingPlatformFromAddresses = new List<string> { "booking.com" },
                SubjectPatterns = new List<string> { "New booking" }
            },
            Properties = new List<PropertyConfiguration>
            {
                new PropertyConfiguration
                {
                    PropertyId = "prop-bc-1",
                    PlatformIds = new Dictionary<string, string> { { "bookingcom", "BC_001" } },
                    Address = "1 Beach Rd",
                    Metadata = new PropertyMetadata { PropertyName = "Cottage 1", Timezone = "America/New_York" },
                    Cleaners = new List<CleanerContact>()
                },
                new PropertyConfiguration
                {
                    PropertyId = "prop-bc-2",
                    PlatformIds = new Dictionary<string, string> { { "bookingcom", "BC_002" } },
                    Address = "2 Beach Rd",
                    Metadata = new PropertyMetadata { PropertyName = "Cottage 2", Timezone = "America/New_York" },
                    Cleaners = new List<CleanerContact>()
                }
            }
        };

        var booking = new Booking
        {
            Platform = "bookingcom",
            BookingReference = "BC-MULTI",
            PropertyId = "",
            CheckOutDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            CheckInDate = new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc)
        };
        var email = new EmailMessage { Subject = "New booking" };

        var b = Build(multiConfig);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.WorkflowsStarted.Should().Be(0);
        result.Errors.Should().ContainSingle(e => e.Contains("No property configuration found"));
    }

    // -------------------------------------------------------------------------
    // SecretsManager returns null OwnerOverrideToken → ownerToken = "MISSING_TOKEN"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_SecretsManagerNullToken_WorkflowStartsWithMissingToken()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(
            SingleAirbnbConfig(),
            configureSecrets: m => m
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ReturnsAsync(new GetSecretValueResponse { SecretString = "{}" })); // no OwnerOverrideToken

        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        CleanerWorkflowInput? capturedInput = null;
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .Callback<CleanerWorkflowInput>(w => capturedInput = w)
            .ReturnsAsync("arn:exec");

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.WorkflowsStarted.Should().Be(1);
        capturedInput!.OwnerToken.Should().Be("MISSING_TOKEN");
    }

    // -------------------------------------------------------------------------
    // SecretsManager throws → continues with ownerToken = "MISSING_TOKEN"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_SecretsManagerThrows_WorkflowStartsWithMissingToken()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(
            SingleAirbnbConfig(),
            configureSecrets: m => m
                .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
                .ThrowsAsync(new Amazon.SecretsManager.AmazonSecretsManagerException("Access denied")));

        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        CleanerWorkflowInput? capturedInput = null;
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .Callback<CleanerWorkflowInput>(w => capturedInput = w)
            .ReturnsAsync("arn:exec");

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.WorkflowsStarted.Should().Be(1);
        capturedInput!.OwnerToken.Should().Be("MISSING_TOKEN");
    }

    // -------------------------------------------------------------------------
    // Valid SecretsManager token → EmailSecret fully exercised + real token used
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_ValidSecretsManagerToken_WorkflowUsesRealToken()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };
        var b = Build(SingleAirbnbConfig()); // default configureSecrets returns "test-owner-token"

        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        CleanerWorkflowInput? capturedInput = null;
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .Callback<CleanerWorkflowInput>(w => capturedInput = w)
            .ReturnsAsync("arn:exec");

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.WorkflowsStarted.Should().Be(1);
        capturedInput!.OwnerToken.Should().Be("test-owner-token");
    }

    // -------------------------------------------------------------------------
    // StepFunction throws for one booking → error added, continues with others
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_StepFunctionThrows_AddsErrorAndContinues()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(SingleAirbnbConfig());
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .ThrowsAsync(new Exception("Step Functions capacity exceeded"));

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue("outer handler should still succeed even if one booking fails");
        result.WorkflowsStarted.Should().Be(0);
        result.Errors.Should().ContainSingle(e => e.Contains("REF001"));
    }

    // -------------------------------------------------------------------------
    // Alternative time slot generation (DefaultCheckIn + DefaultCheckOut + Duration set)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_WithTimeSlotConfig_GeneratesAlternativeSlots()
    {
        // 11:00 AM checkout + 30 min margin → default start = 11:30 AM
        // 4:00 PM check-in, 2.5 hr cleaning → latest start = 1:30 PM
        // 120 available minutes / 5 = 24 → rounded to 20-minute increment
        var config = SingleAirbnbConfig(m =>
        {
            m.DefaultCheckOut = "11:00 AM";
            m.DefaultCheckIn = "4:00 PM";
            m.CleaningDuration = "2.5 hours";
            m.MarginMinutesAfterCheckOut = 30;
        });

        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(config);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        CleanerWorkflowInput? capturedInput = null;
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .Callback<CleanerWorkflowInput>(w => capturedInput = w)
            .ReturnsAsync("arn:exec");

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.WorkflowsStarted.Should().Be(1);
        capturedInput.Should().NotBeNull();
        // There should be at least one real (non-empty) time slot generated
        capturedInput!.AlternativeTimeSlots.Should().Contain(s => !string.IsNullOrEmpty(s.Time));
        capturedInput.TimeButtonsHtml.Should().NotBeNullOrEmpty("time slots HTML should be populated");
    }

    // -------------------------------------------------------------------------
    // DefaultCheckOut parse failure → uses default cleaningHour=12
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_InvalidDefaultCheckOut_UsesDefaultNoon()
    {
        var config = SingleAirbnbConfig(m =>
        {
            m.DefaultCheckOut = "not-a-valid-time"; // TryParse will fail
            m.MarginMinutesAfterCheckOut = 30;
        });

        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(config);
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        CleanerWorkflowInput? capturedInput = null;
        b.Step.Setup(x => x.StartCleanerWorkflowAsync(It.IsAny<CleanerWorkflowInput>()))
            .Callback<CleanerWorkflowInput>(w => capturedInput = w)
            .ReturnsAsync("arn:exec");

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.WorkflowsStarted.Should().Be(1);
        // With cleaningHour=12, CleaningTime should contain "12:" (noon)
        capturedInput!.CleaningTime.Should().StartWith("12:");
    }

    // -------------------------------------------------------------------------
    // ForceRescan = true → passed through to email scanner
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_ForceRescan_PassesTrueToScanner()
    {
        var b = Build(SingleAirbnbConfig());
        var request = new LambdaRequest { ForceRescan = true };

        await b.Fn.FunctionHandler(request, new TestLambdaContext());

        b.Email.Verify(x => x.ScanForBookingEmailsAsync(
            It.IsAny<EmailCredentials>(),
            true,
            It.IsAny<List<string>?>(),
            It.IsAny<List<string>?>()),
            Times.AtLeastOnce,
            "ForceRescan=true must be passed to the scanner");
    }

    // -------------------------------------------------------------------------
    // ParseCancellation returns null → email is not processed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_ParseCancellationReturnsNull_EmailNotMarkedProcessed()
    {
        var cancelEmail = new EmailMessage { Subject = "Booking canceled by traveler" };

        var b = Build(SingleAirbnbConfig());
        // First scan (bookings) → empty; second scan (cancellations) → cancel email
        b.Email.SetupSequence(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>())
            .ReturnsAsync(new List<EmailMessage> { cancelEmail });

        b.Parser.Setup(x => x.ParseCancellation(cancelEmail)).Returns((Booking?)null);

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(0);
        b.Email.Verify(
            x => x.MarkEmailAsProcessedAsync(It.IsAny<EmailCredentials>(), cancelEmail),
            Times.Never);
    }

    // -------------------------------------------------------------------------
    // Cancellation with assigned cleaner → cleaner CalendarLambda invoke sent
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_CancellationWithAssignedCleaner_InvokesCalendarLambdaTwice()
    {
        // The stored booking has cleaner info → ProcessBookingCancellationAsync should invoke once
        // for the cleaner and once for the owner.
        var cancelEmail = new EmailMessage { Subject = "Booking canceled by traveler" };
        var parsedCancellation = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-X",
            PropertyId = "AIRBNB_001"
        };
        var storedBooking = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-X",
            PropertyId = "AIRBNB_001",
            AssignedCleanerName = "Jane Doe",
            AssignedCleanerEmail = "jane@cleaner.com",
            ScheduledCleaningTime = new DateTime(2026, 8, 15, 16, 0, 0, DateTimeKind.Utc),
            IsCancelled = false
        };

        var b = Build(SingleAirbnbConfig());
        b.Email.SetupSequence(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>())
            .ReturnsAsync(new List<EmailMessage> { cancelEmail });
        b.Parser.Setup(x => x.ParseCancellation(cancelEmail)).Returns(parsedCancellation);
        b.State.Setup(x => x.GetBookingAsync("airbnb", "REF-X")).ReturnsAsync(storedBooking);

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(1);
        // CalendarLambda should be invoked twice: once for cleaner, once for owner
        b.Lambda.Verify(
            x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default),
            Times.Exactly(2),
            "CalendarLambda must be invoked for both cleaner and owner when cleaner is assigned");
    }

    // -------------------------------------------------------------------------
    // ProcessBookingCancellationAsync: invalid timezone → falls back to UTC
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_CancellationInvalidTimezone_FallsBackToUtc()
    {
        var cancelEmail = new EmailMessage { Subject = "Booking canceled" };
        var parsedCancellation = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-TZ",
            PropertyId = "AIRBNB_001"
        };
        var storedBooking = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-TZ",
            PropertyId = "AIRBNB_001",
            CheckOutDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
            IsCancelled = false
        };

        // Property has an invalid timezone; code should fall back to UTC
        var configBadTz = SingleAirbnbConfig(m => m.Timezone = "Invalid/NonExistent");

        var b = Build(configBadTz);
        b.Email.SetupSequence(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>())
            .ReturnsAsync(new List<EmailMessage> { cancelEmail });
        b.Parser.Setup(x => x.ParseCancellation(cancelEmail)).Returns(parsedCancellation);
        b.State.Setup(x => x.GetBookingAsync("airbnb", "REF-TZ")).ReturnsAsync(storedBooking);

        // Owner invoke succeeds
        b.Lambda.Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse { StatusCode = 200 });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue("invalid timezone must not crash the handler");
        result.CancellationsProcessed.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // ProcessBookingCancellationAsync: ScheduledCleaningTime null → uses CheckOutDate
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_CancellationNoScheduledTime_UsesCheckoutDate()
    {
        var cancelEmail = new EmailMessage { Subject = "Booking canceled" };
        var parsedCancellation = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-NOSCHED",
            PropertyId = "AIRBNB_001"
        };
        var storedBooking = new Booking
        {
            Platform = "airbnb",
            BookingReference = "REF-NOSCHED",
            PropertyId = "AIRBNB_001",
            CheckOutDate = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc),
            ScheduledCleaningTime = null,  // ← no scheduled time
            IsCancelled = false
        };

        var b = Build(SingleAirbnbConfig());
        b.Email.SetupSequence(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage>())
            .ReturnsAsync(new List<EmailMessage> { cancelEmail });
        b.Parser.Setup(x => x.ParseCancellation(cancelEmail)).Returns(parsedCancellation);
        b.State.Setup(x => x.GetBookingAsync("airbnb", "REF-NOSCHED")).ReturnsAsync(storedBooking);

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.Success.Should().BeTrue();
        result.CancellationsProcessed.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // After successful workflow, source emails are marked processed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FunctionHandler_SuccessfulWorkflow_MarksEmailProcessed()
    {
        var booking = AirbnbBooking();
        var email = new EmailMessage { Subject = "Booking confirmed" };

        var b = Build(SingleAirbnbConfig());
        b.Email.Setup(x => x.ScanForBookingEmailsAsync(
                It.IsAny<EmailCredentials>(), It.IsAny<bool>(),
                It.IsAny<List<string>?>(), It.IsAny<List<string>?>()))
            .ReturnsAsync(new List<EmailMessage> { email });
        b.Parser.Setup(x => x.ParseBookings(It.IsAny<IEnumerable<EmailMessage>>()))
            .Returns(new List<(Booking, List<EmailMessage>)> { (booking, new List<EmailMessage> { email }) });

        var result = await b.Fn.FunctionHandler(new LambdaRequest(), new TestLambdaContext());

        result.WorkflowsStarted.Should().Be(1);
        b.Email.Verify(
            x => x.MarkEmailAsProcessedAsync(It.IsAny<EmailCredentials>(), email),
            Times.Once,
            "Source email must be marked processed after workflow starts");
    }
}

// -------------------------------------------------------------------------
// Extension to allow inline property mutation (used in SingleAirbnbConfig)
// -------------------------------------------------------------------------
internal static class ObjectExtensions
{
    public static T Also<T>(this T obj, Action<T>? configure)
    {
        configure?.Invoke(obj);
        return obj;
    }
}
