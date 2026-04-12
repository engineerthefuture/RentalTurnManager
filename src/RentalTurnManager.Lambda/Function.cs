/************************
 * Rental Turn Manager
 * Function.cs
 * 
 * Main AWS Lambda handler that scans emails for new rental bookings
 * from Airbnb, VRBO, and Booking.com. Parses booking details, tracks
 * state in S3, and triggers Step Functions workflows for cleaner coordination.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SimpleEmail;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;
using System.Net;
using System.Text.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.Lambda;

/// <summary>
/// Lambda function handler for scanning emails and triggering cleaner workflows
/// </summary>
public class Function
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Function> _logger;
    private readonly IConfiguration _configuration;
    private readonly PropertiesConfiguration? _propertiesConfig;

        // Compute a sensible increment (minutes) to target ~5 slots given available minutes.
        // Made internal for unit testing.
        public static int ComputeIncrementMinutes(double availableMinutes, int originalIncrement)
        {
            var incrementMinutes = originalIncrement;

            if (availableMinutes > 0)
            {
                var slotsAtOriginal = (int)Math.Floor(availableMinutes / (double)originalIncrement);

                if (slotsAtOriginal == 5)
                {
                    incrementMinutes = originalIncrement;
                }
                else
                {
                    var desired = (int)Math.Floor(availableMinutes / 5.0);
                    if (desired < 5) desired = 5;

                    // Round desired down to nearest multiple of 5
                    desired = (desired / 5) * 5;
                    if (desired < 5) desired = 5;

                    incrementMinutes = desired;
                }
            }

            return incrementMinutes;
        }

    public Function()
    {
        // Build configuration
        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddEnvironmentVariables();
        
        // Load properties configuration from environment variable
        var propertiesJson = Environment.GetEnvironmentVariable("PROPERTIES_CONFIG");
        if (!string.IsNullOrEmpty(propertiesJson))
        {
            try
            {
                _propertiesConfig = JsonSerializer.Deserialize<PropertiesConfiguration>(propertiesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                // Log later when logger is available
                Console.WriteLine($"Error deserializing PROPERTIES_CONFIG: {ex.Message}");
                Console.WriteLine($"PROPERTIES_CONFIG value (first 500 chars): {(propertiesJson.Length > 500 ? propertiesJson.Substring(0, 500) : propertiesJson)}");
            }
        }
        else
        {
            Console.WriteLine("PROPERTIES_CONFIG environment variable is null or empty");
        }
        
        // Load message templates from environment variable if present
        var templatesJson = Environment.GetEnvironmentVariable("MESSAGE_TEMPLATES");
        if (!string.IsNullOrEmpty(templatesJson))
        {
            var templatesStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(templatesJson));
            configBuilder.AddJsonStream(templatesStream);
        }
        
        _configuration = configBuilder.Build();

        // Setup dependency injection
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection, _propertiesConfig);
        _serviceProvider = serviceCollection.BuildServiceProvider();

        _logger = _serviceProvider.GetRequiredService<ILogger<Function>>();
        
        // Now log configuration details
        if (_propertiesConfig != null)
        {
            _logger.LogInformation($"Loaded {_propertiesConfig.Properties?.Count ?? 0} property configurations");
        }
        else
        {
            _logger.LogWarning("No PROPERTIES_CONFIG environment variable found or failed to parse");
        }
    }

    /// <summary>
    /// Test constructor for dependency injection
    /// </summary>
    public Function(IServiceProvider serviceProvider, IConfiguration configuration, PropertiesConfiguration? propertiesConfig = null)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _propertiesConfig = propertiesConfig;
        _logger = _serviceProvider.GetRequiredService<ILogger<Function>>();
    }

    private void ConfigureServices(IServiceCollection services, PropertiesConfiguration? propertiesConfig)
    {
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        // Add configuration
        services.AddSingleton(_configuration);
        
        // Add properties configuration as singleton
        if (propertiesConfig != null)
        {
            services.AddSingleton(propertiesConfig);
        }

        // Add AWS services
        services.AddAWSService<IAmazonSecretsManager>();
        services.AddAWSService<IAmazonStepFunctions>();
        services.AddAWSService<IAmazonSimpleEmailService>();
        services.AddAWSService<Amazon.S3.IAmazonS3>();
        services.AddAWSService<Amazon.Lambda.IAmazonLambda>();

        // Add application services
        services.AddSingleton<ISecretsService, SecretsService>();
        services.AddSingleton<IEmailScannerService, EmailScannerService>();
        services.AddSingleton<IBookingParserService, BookingParserService>();
        services.AddSingleton<IPropertyConfigService, PropertyConfigService>();
        services.AddSingleton<IStepFunctionService, StepFunctionService>();
        
        // Add BookingStateService
        services.AddSingleton<IBookingStateService>(sp =>
        {
            var s3Client = sp.GetRequiredService<Amazon.S3.IAmazonS3>();
            var logger = sp.GetRequiredService<ILogger<BookingStateService>>();
            var bucketName = Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET") ?? 
                           $"{Environment.GetEnvironmentVariable("NAMESPACE_PREFIX") ?? "bf"}-{Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "dev"}-s3-{(Environment.GetEnvironmentVariable("APP_NAME") ?? "RentalTurnManager").ToLower()}-bookings";
            return new BookingStateService(s3Client, logger, bucketName);
        });
    }

    /// <summary>
    /// Lambda function handler - scans emails for new bookings
    /// </summary>
    public async Task<LambdaResponse> FunctionHandler(LambdaRequest input, ILambdaContext context)
    {
        _logger.LogInformation("Starting RentalTurnManager email scan");
        _logger.LogInformation($"Request ID: {context.AwsRequestId}");

        var response = new LambdaResponse
        {
            RequestId = context.AwsRequestId,
            Timestamp = DateTime.UtcNow,
            BookingsProcessed = 0,
            WorkflowsStarted = 0,
            Errors = new List<string>()
        };

        try
        {
            // Get services
            var secretsService = _serviceProvider.GetRequiredService<ISecretsService>();
            var emailScanner = _serviceProvider.GetRequiredService<IEmailScannerService>();
            var bookingParser = _serviceProvider.GetRequiredService<IBookingParserService>();
            var stepFunctionService = _serviceProvider.GetRequiredService<IStepFunctionService>();
            var bookingStateService = _serviceProvider.GetRequiredService<IBookingStateService>();

            if (_propertiesConfig == null)
            {
                throw new InvalidOperationException("PROPERTIES_CONFIG environment variable not set or invalid");
            }
            
            _logger.LogInformation($"Using property config with {_propertiesConfig.Properties?.Count ?? 0} properties");

            // Retrieve email credentials from Secrets Manager
            var emailCredentials = await secretsService.GetEmailCredentialsAsync();
            
            // Get configured from addresses for booking platforms
            var fromAddresses = _propertiesConfig.EmailFilters?.BookingPlatformFromAddresses ?? new List<string> { "airbnb.com", "vrbo.com", "booking.com" };
            _logger.LogInformation($"Using from addresses: {string.Join(", ", fromAddresses)}");
            
            // Get configured subject patterns and always include the Booking.com-specific ones.
            // These are merged rather than replaced so existing configs that pre-date Booking.com
            // support continue to work without requiring a manual secret update.
            var configuredPatterns = _propertiesConfig.EmailFilters?.SubjectPatterns
                ?? new List<string> { "Reservation confirmed", "Instant Booking from", "booking confirmation" };
            var requiredPatterns = new List<string> { "New booking request", "Booking.com - New booking" };
            var subjectPatterns = configuredPatterns
                .Concat(requiredPatterns.Where(r =>
                    !configuredPatterns.Any(c => c.Contains(r, StringComparison.OrdinalIgnoreCase))))
                .ToList();
            _logger.LogInformation($"Using subject patterns: {string.Join(", ", subjectPatterns)}");
            
            // Scan emails for new bookings
            _logger.LogInformation($"Scanning emails for new bookings (ForceRescan: {input.ForceRescan})");
            var emails = await emailScanner.ScanForBookingEmailsAsync(emailCredentials, input.ForceRescan, fromAddresses, subjectPatterns);
            _logger.LogInformation($"Found {emails.Count} potential booking emails");

            var bookingPairs = bookingParser.ParseBookings(emails) ?? new List<(Booking Booking, List<EmailMessage> SourceEmails)>();
            _logger.LogInformation($"Parsed {bookingPairs.Count} complete booking(s) from emails");

            foreach (var (booking, sourceEmails) in bookingPairs)
            {
                try
                {
                    // Validate booking has required fields
                    if (string.IsNullOrEmpty(booking.BookingReference))
                    {
                        _logger.LogWarning($"Booking missing reference ID, skipping");
                        continue;
                    }

                    response.BookingsProcessed++;
                    _logger.LogInformation($"Parsed booking: {booking.Platform} - {booking.BookingReference}");

                    // Check if booking has changed or is new
                    bool hasChanged = await bookingStateService.HasBookingChangedAsync(booking);
                    if (!hasChanged)
                    {  
                        _logger.LogInformation($"Booking unchanged, skipping workflow: {booking.Platform} - {booking.BookingReference}");
                        continue;
                    }
                    
                    _logger.LogInformation($"Processing new or updated booking: {booking.Platform} - {booking.BookingReference}");

                    // Find matching property configuration
                    var normalizedPlatform = booking.Platform.ToLower() switch
                    {
                        "airbnb" => "airbnb",
                        "vrbo" => "vrbo",
                        "bookingcom" or "booking.com" => "bookingcom",
                        _ => booking.Platform.ToLower()
                    };
                    
                    var property = _propertiesConfig.Properties?.FirstOrDefault(p =>
                        p.PlatformIds.TryGetValue(normalizedPlatform, out var id) && 
                        id.Equals(booking.PropertyId, StringComparison.OrdinalIgnoreCase)
                    );

                    // Booking.com host emails don't embed the host's property ID, so when
                    // PropertyId is empty fall back to the single configured bookingcom property.
                    if (property == null && string.IsNullOrEmpty(booking.PropertyId) && normalizedPlatform == "bookingcom")
                    {
                        var bookingComProperties = _propertiesConfig.Properties
                            ?.Where(p => p.PlatformIds.ContainsKey("bookingcom"))
                            .ToList();
                        if (bookingComProperties?.Count == 1)
                        {
                            property = bookingComProperties[0];
                            booking.PropertyId = property.PlatformIds["bookingcom"];
                            _logger.LogInformation(
                                $"Booking.com email contained no property ID; matched to sole configured property '{property.PropertyId}' (bookingcom ID: {booking.PropertyId})");
                        }
                    }
                    
                    if (property == null)
                    {
                        var error = $"No property configuration found for {booking.Platform} property {booking.PropertyId}";
                        _logger.LogError(error);
                        _logger.LogError("Available properties: {PropertyIds}", string.Join(", ", _propertiesConfig.Properties?.Select(p => $"{p.PropertyId}: {string.Join(", ", p.PlatformIds.Select(kv => $"{kv.Key}={kv.Value}"))}") ?? Array.Empty<string>()));
                        response.Errors.Add(error);
                        continue;
                    }

                    // Calculate cleaning time (on checkout date at defaultCheckOut + 1 hour)
                    var cleaningDate = booking.CheckOutDate;
                    var easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
                    
                    // Parse defaultCheckOut time (e.g., "11:00 AM") and add margin minutes
                    int cleaningHour = 12; // Default to 12:00 PM if parsing fails
                    int cleaningMinute = 0;
                    if (!string.IsNullOrEmpty(property.Metadata.DefaultCheckOut))
                    {
                        if (DateTime.TryParse(property.Metadata.DefaultCheckOut, out var checkOutTime))
                        {
                            // Add configured margin minutes to check-out time
                            var cleaningTime = checkOutTime.AddMinutes(property.Metadata.MarginMinutesAfterCheckOut);
                            cleaningHour = cleaningTime.Hour;
                            cleaningMinute = cleaningTime.Minute;
                        }
                    }
                    
                    // Create DateTime at calculated time on checkout date in Eastern Time
                    var cleaningDateTimeEastern = new DateTime(
                        cleaningDate.Year, 
                        cleaningDate.Month, 
                        cleaningDate.Day, 
                        cleaningHour, 
                        cleaningMinute, 
                        0, 
                        DateTimeKind.Unspecified
                    );
                    
                    // Convert to UTC for storage and transmission
                    var cleaningDateTimeUtc = TimeZoneInfo.ConvertTimeToUtc(cleaningDateTimeEastern, easternZone);

                    // Calculate alternative time slots (30-minute increments)
                    var alternativeTimeSlots = new List<TimeSlot>();
                    if (!string.IsNullOrEmpty(property.Metadata.DefaultCheckIn) && 
                        !string.IsNullOrEmpty(property.Metadata.DefaultCheckOut) &&
                        !string.IsNullOrEmpty(property.Metadata.CleaningDuration))
                    {
                        if (DateTime.TryParse(property.Metadata.DefaultCheckIn, out var checkInTime) &&
                            DateTime.TryParse(property.Metadata.DefaultCheckOut, out var checkOutTime))
                        {
                            // Parse cleaning duration (e.g., "2.5 hours")
                            var durationMatch = System.Text.RegularExpressions.Regex.Match(
                                property.Metadata.CleaningDuration, 
                                @"([0-9.]+)\s*hours?");
                            if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, out var durationHours))
                            {
                                // Start time is default cleaning time (checkOut + marginMinutesAfterCheckOut)
                                var startTime = new DateTime(
                                    cleaningDate.Year,
                                    cleaningDate.Month,
                                    cleaningDate.Day,
                                    checkOutTime.Hour,
                                    checkOutTime.Minute,
                                    0,
                                    DateTimeKind.Unspecified
                                ).AddMinutes(property.Metadata.MarginMinutesAfterCheckOut);
                                
                                // Latest possible start time is checkIn - cleaningDuration
                                var latestStartEastern = new DateTime(
                                    cleaningDate.Year,
                                    cleaningDate.Month,
                                    cleaningDate.Day,
                                    checkInTime.Hour,
                                    checkInTime.Minute,
                                    0,
                                    DateTimeKind.Unspecified
                                ).AddHours(-durationHours);
                                
                                // Choose a sensible increment so we end up with approximately 5 slots
                                var originalIncrement = property.Metadata.AlternateTimeIncrementMinutes > 0
                                    ? property.Metadata.AlternateTimeIncrementMinutes
                                    : 30;

                                var availableMinutes = (latestStartEastern - startTime).TotalMinutes;
                                var incrementMinutes = originalIncrement;

                                if (availableMinutes > 0)
                                {
                                    // How many slots would we get at the original increment?
                                    var slotsAtOriginal = (int)Math.Floor(availableMinutes / (double)originalIncrement);

                                    _logger.LogInformation("Alternate slots: availableMinutes={availableMinutes}, originalIncrement={originalIncrement}, slotsAtOriginal={slotsAtOriginal}", availableMinutes, originalIncrement, slotsAtOriginal);

                                    if (slotsAtOriginal == 5)
                                    {
                                        // original increment is perfect
                                        incrementMinutes = originalIncrement;
                                    }
                                    else
                                    {
                                        // Derive an increment that yields ~5 slots: availableMinutes / 5
                                        var desired = (int)Math.Floor(availableMinutes / 5.0);
                                        if (desired < 5) desired = 5; // never below 5 minutes

                                        // Round desired down to nearest multiple of 5 for consistency
                                        desired = (desired / 5) * 5;
                                        if (desired < 5) desired = 5;

                                        incrementMinutes = desired;
                                    }

                                    _logger.LogInformation("Chosen alternate increment: {incrementMinutes} minutes (desired calculation)", incrementMinutes);
                                }

                                // Generate time slots at the (possibly adjusted) interval
                                if (availableMinutes > 0 && incrementMinutes > 0)
                                {
                                    var currentSlot = startTime.AddMinutes(incrementMinutes); // Start from increment after default
                                    while (currentSlot <= latestStartEastern)
                                    {
                                        var slotUtc = TimeZoneInfo.ConvertTimeToUtc(currentSlot, easternZone);
                                        alternativeTimeSlots.Add(new TimeSlot
                                        {
                                            Time = currentSlot.ToString("h:mm tt"),
                                            IsoDateTime = slotUtc.ToString("o")
                                        });
                                        currentSlot = currentSlot.AddMinutes(incrementMinutes);
                                    }
                                }
                            }
                        }
                    }

                    // Get owner email from environment variable
                    var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL");
                    if (string.IsNullOrEmpty(ownerEmail))
                    {
                        _logger.LogWarning("OWNER_EMAIL environment variable not set, using default");
                        ownerEmail = "support@example.com";
                    }
                    _logger.LogInformation($"Using owner email: {ownerEmail}");

                    // Get callback API URL from environment variable
                    var callbackApiUrl = Environment.GetEnvironmentVariable("CALLBACK_API_URL");
                    if (string.IsNullOrEmpty(callbackApiUrl))
                    {
                        _logger.LogWarning("CALLBACK_API_URL environment variable not set");
                        callbackApiUrl = "";
                    }

                    // Generate HTML for alternative time slots (non-clickable due to Step Functions limitations)
                    var timeButtonsHtml = string.Empty;
                    if (alternativeTimeSlots.Count > 0)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.Append("<p style=\"margin: 20px 0; padding: 15px; background-color: #f8f9fa; border-radius: 5px;\">");
                        sb.Append("<strong style=\"display: block; margin-bottom: 10px;\">If the default time doesn't work, alternative times are available:</strong>");
                        sb.Append("<ul style=\"margin: 10px 0; padding-left: 20px;\">");
                        foreach (var slot in alternativeTimeSlots)
                        {
                            sb.Append($"<li style=\"margin: 5px 0;\">{slot.Time}</li>");
                        }
                        sb.Append("</ul>");
                        sb.Append("<em style=\"font-size: 0.9em; color: #6c757d;\">Please reply to this email or contact the owner to request an alternative time.</em>");
                        sb.Append("</p>");
                        timeButtonsHtml = sb.ToString();
                    }

                    // Ensure ownerName has a default value if missing
                    if (string.IsNullOrEmpty(property.Metadata.OwnerName))
                    {
                        property.Metadata.OwnerName = "Property Management";
                    }
                    
                    // Get booking state bucket name from environment variable
                    var bookingStateBucket = Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET") ?? 
                                           $"{Environment.GetEnvironmentVariable("NAMESPACE_PREFIX") ?? "bf"}-{Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "dev"}-s3-{(Environment.GetEnvironmentVariable("APP_NAME") ?? "RentalTurnManager").ToLower()}-bookings";

                    // Get owner token for escalation email
                    var emailSecretName = Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
                    if (string.IsNullOrEmpty(emailSecretName))
                    {
                        _logger.LogWarning("EMAIL_SECRET_NAME environment variable not set");
                        emailSecretName = "RentalTurnManager-EmailCredentials";
                    }

                    string ownerToken;
                    try
                    {
                        var secretsManager = _serviceProvider.GetRequiredService<IAmazonSecretsManager>();
                        _logger.LogInformation("Retrieving owner token from Secrets Manager.");
                        var secretResponse = await secretsManager.GetSecretValueAsync(new Amazon.SecretsManager.Model.GetSecretValueRequest
                        {
                            SecretId = emailSecretName
                        });
                        
                        _logger.LogInformation("Secret retrieved successfully, deserializing...");
                        var emailSecret = JsonSerializer.Deserialize<EmailSecret>(secretResponse.SecretString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        
                        if (emailSecret?.OwnerOverrideToken == null)
                        {
                            _logger.LogWarning("OwnerOverrideToken field is null or missing in secret");
                            ownerToken = "MISSING_TOKEN";
                        }
                        else
                        {
                            ownerToken = emailSecret.OwnerOverrideToken;
                            _logger.LogInformation($"Owner token retrieved successfully (length: {ownerToken.Length})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to retrieve owner token from Secrets Manager: {ex.Message}");
                        ownerToken = "MISSING_TOKEN";
                    }

                    // Generate escalation email HTML with dynamic cleaner list
                    var escalationEmailHtml = GenerateEscalationEmailHtml(
                        property,
                        booking,
                        cleaningDateTimeUtc,
                        ownerToken,
                        callbackApiUrl
                    );

                    // Build button HTML for actual time slots only (no empty buttons)
                    var alternativeButtons = new List<string>();
                    foreach (var slot in alternativeTimeSlots)
                    {
                        alternativeButtons.Add($"<a href=\"{{0}}/respond?token={{{{1}}}}&response=yes&time={{2}}\" style=\"display: inline-block; background-color: #007bff; color: white; padding: 8px 20px; text-decoration: none; border-radius: 5px; margin: 5px;\">{slot.Time}</a>");
                    }
                    
                    // Pad arrays to 5 elements for workflow (empty strings for unused slots)
                    while (alternativeTimeSlots.Count < 5)
                    {
                        alternativeTimeSlots.Add(new TimeSlot { Time = "", IsoDateTime = "" });
                    }
                    while (alternativeButtons.Count < 5)
                    {
                        alternativeButtons.Add("");
                    }

                    // Start Step Functions workflow
                    var workflowInput = new CleanerWorkflowInput
                    {
                        Booking = booking,
                        Property = property,
                        CleaningDateTime = cleaningDateTimeUtc,
                        CleaningTime = cleaningDateTimeEastern.ToString("h:mm tt"),
                        AlternativeTimeSlots = alternativeTimeSlots,
                        TimeButtonsHtml = timeButtonsHtml,
                        CurrentCleanerIndex = 0,
                        AttemptCount = 0,
                        OwnerEmail = ownerEmail,
                        CallbackApiUrl = callbackApiUrl,
                        BookingStateBucket = bookingStateBucket,
                        EscalationEmailHtml = escalationEmailHtml,
                        OwnerToken = ownerToken
                    };

                    var executionArn = await stepFunctionService.StartCleanerWorkflowAsync(workflowInput);
                    response.WorkflowsStarted++;
                    _logger.LogInformation($"Started workflow: {executionArn}");

                    // Save booking state
                    await bookingStateService.SaveBookingAsync(booking);
                    _logger.LogInformation($"Saved booking state: {booking.Platform} - {booking.BookingReference}");

                    // Mark all source emails as processed
                    foreach (var sourceEmail in sourceEmails)
                        await emailScanner.MarkEmailAsProcessedAsync(emailCredentials, sourceEmail);
                }
                catch (Exception ex)
                {
                    var error = $"Error processing booking '{booking.BookingReference}' ({booking.Platform}): {ex.Message}";
                    _logger.LogError(ex, error);
                    response.Errors.Add(error);
                }
            }

            // Scan for and process booking cancellation emails
            var cancellationSubjectPatterns = _propertiesConfig.EmailFilters?.CancellationSubjectPatterns
                ?? new List<string> { "canceled by traveler", "Booking canceled", "Reservation canceled", "Booking cancelled", "Reservation cancelled" };
            _logger.LogInformation($"Scanning for cancellation emails with patterns: {string.Join(", ", cancellationSubjectPatterns)}");

            var cancellationFromAddresses = _propertiesConfig.EmailFilters?.CancellationFromAddresses ?? fromAddresses;

            var cancellationEmails = await emailScanner.ScanForBookingEmailsAsync(emailCredentials, input.ForceRescan, cancellationFromAddresses, cancellationSubjectPatterns);
            _logger.LogInformation($"Found {cancellationEmails.Count} cancellation emails");

            foreach (var cancelEmail in cancellationEmails)
            {
                try
                {
                    var cancellation = bookingParser.ParseCancellation(cancelEmail);
                    if (cancellation == null)
                    {
                        _logger.LogWarning($"Could not parse cancellation from email: {cancelEmail.Subject}");
                        continue;
                    }

                    _logger.LogInformation($"Processing cancellation: {cancellation.Platform} - {cancellation.BookingReference}");

                    // Look up the existing booking in S3
                    var existingBooking = await bookingStateService.GetBookingAsync(cancellation.Platform, cancellation.BookingReference);
                    if (existingBooking == null)
                    {
                        _logger.LogWarning($"Booking not found in S3 for cancellation: {cancellation.Platform}/{cancellation.BookingReference}. Leaving email unprocessed so it can be retried later.");
                        continue;
                    }

                    // Skip if this cancellation has already been processed (idempotency guard)
                    if (existingBooking.IsCancelled)
                    {
                        _logger.LogInformation($"Cancellation already processed for booking {cancellation.Platform}/{cancellation.BookingReference} at {existingBooking.CancellationProcessedAt:u}. Skipping.");
                        await emailScanner.MarkEmailAsProcessedAsync(emailCredentials, cancelEmail);
                        continue;
                    }

                    // Find the property config using the platform-specific ID
                    var normalizedCancelPlatform = cancellation.Platform.ToLower() switch
                    {
                        "airbnb" => "airbnb",
                        "vrbo" => "vrbo",
                        "bookingcom" or "booking.com" => "bookingcom",
                        _ => cancellation.Platform.ToLower()
                    };

                    var cancelProperty = _propertiesConfig.Properties?.FirstOrDefault(p =>
                        p.PlatformIds.TryGetValue(normalizedCancelPlatform, out var id) &&
                        id.Equals(existingBooking.PropertyId, StringComparison.OrdinalIgnoreCase));

                    var ownerEmailForCancel = Environment.GetEnvironmentVariable("OWNER_EMAIL") ?? "support@example.com";
                    var calendarLambdaName = Environment.GetEnvironmentVariable("CALENDAR_LAMBDA_NAME") ?? "RentalTurnManager-CalendarLambda";
                    var lambdaClient = _serviceProvider.GetRequiredService<Amazon.Lambda.IAmazonLambda>();

                    var cancellationSucceeded = await ProcessBookingCancellationAsync(
                        existingBooking,
                        cancelProperty,
                        ownerEmailForCancel,
                        calendarLambdaName,
                        lambdaClient);

                    if (cancellationSucceeded)
                    {
                        // Mark the booking as cancelled in S3 so future runs skip it
                        existingBooking.IsCancelled = true;
                        existingBooking.CancellationProcessedAt = DateTime.UtcNow;
                        await bookingStateService.SaveBookingAsync(existingBooking);
                        response.CancellationsProcessed++;
                        await emailScanner.MarkEmailAsProcessedAsync(emailCredentials, cancelEmail);
                    }
                    else
                    {
                        _logger.LogWarning($"Cancellation notifications failed for {cancellation.Platform}/{cancellation.BookingReference}. Booking will not be marked as cancelled so it retries on the next run.");
                    }
                    _logger.LogInformation($"Processed cancellation for booking: {cancellation.Platform} - {cancellation.BookingReference}");
                }
                catch (Exception ex)
                {
                    var error = $"Error processing cancellation email '{cancelEmail.Subject}': {ex.Message}";
                    _logger.LogError(ex, error);
                    response.Errors.Add(error);
                }
            }

            _logger.LogInformation($"Email scan complete. Processed {response.BookingsProcessed} bookings, started {response.WorkflowsStarted} workflows, processed {response.CancellationsProcessed} cancellations");
            response.Success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in email scanning");
            response.Success = false;
            response.Errors.Add($"Fatal error: {ex.Message}");
        }

        return response;
    }

    /// <summary>
    /// Sends cancellation emails (with calendar CANCEL) to the cleaner and owner when a booking cancellation is received.
    /// Returns true when the owner notification was sent successfully; false otherwise.
    /// The cleaner notification is best-effort and does not affect the return value.
    /// </summary>
    private async Task<bool> ProcessBookingCancellationAsync(
        Booking booking,
        PropertyConfiguration? property,
        string ownerEmail,
        string calendarLambdaName,
        Amazon.Lambda.IAmazonLambda lambdaClient)
    {
        var propertyName = booking.WorkflowPropertyId
            ?? property?.Metadata.PropertyName
            ?? booking.PropertyId;
        var ownerName = property?.Metadata.OwnerName ?? "Property Management";

        TimeZoneInfo easternZone;
        try { easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
        catch (TimeZoneNotFoundException) { easternZone = TimeZoneInfo.Utc; }

        // Prefer the confirmed cleaning time; fall back to checkout date (the expected cleaning day)
        // when no cleaner had been assigned/confirmed yet.
        var effectiveCleaningTime = booking.ScheduledCleaningTime
            ?? (booking.CheckOutDate != default ? booking.CheckOutDate : (DateTime?)null);

        var effectiveEasternTime = effectiveCleaningTime.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(effectiveCleaningTime.Value, easternZone)
            : (DateTime?)null;
        var effectiveFormattedDate = effectiveEasternTime?.ToString("MMMM dd, yyyy") ?? "Unknown Date";
        var effectiveFormattedTime = booking.ScheduledCleaningTime.HasValue
            ? TimeZoneInfo.ConvertTimeFromUtc(booking.ScheduledCleaningTime.Value, easternZone).ToString("h:mm tt")
            : "10:00 AM"; // checkout time; cleaning starts after checkout
        var effectiveCleaningDateIso = effectiveCleaningTime?.ToString("o") ?? DateTime.UtcNow.ToString("o");

        // Send cancellation email to cleaner (if a cleaner has been assigned)
        if (!string.IsNullOrEmpty(booking.AssignedCleanerEmail) &&
            !string.IsNullOrEmpty(booking.AssignedCleanerName))
        {
            try
            {
                var cleanerHtmlBody = $@"<html><body>
<p>Hello {WebUtility.HtmlEncode(booking.AssignedCleanerName)},</p>
<p>The cleaning scheduled for <strong>{WebUtility.HtmlEncode(propertyName)}</strong> has been <strong style=""color: #dc3545;"">cancelled</strong> because the booking was cancelled by the traveler.</p>
<p><strong>Cancelled Appointment:</strong></p>
<ul>
<li><strong>Property:</strong> {WebUtility.HtmlEncode(propertyName)}</li>
<li><strong>Date:</strong> {WebUtility.HtmlEncode(effectiveFormattedDate)}</li>
<li><strong>Time:</strong> {WebUtility.HtmlEncode(effectiveFormattedTime)}</li>
</ul>
<p>You do not need to attend this cleaning. If you added this to your calendar, the attached cancellation should remove it automatically.</p>
<p>Thank you,<br/>{WebUtility.HtmlEncode(ownerName)}</p>
</body></html>";

                var cleanerRequest = new
                {
                    FromEmail = ownerEmail,
                    ToEmail = booking.AssignedCleanerEmail,
                    Subject = $"Cleaning Cancelled for {propertyName} on {effectiveFormattedDate}",
                    HtmlBody = cleanerHtmlBody,
                    PropertyName = propertyName,
                    CleanerName = booking.AssignedCleanerName,
                    CleanerId = string.Empty,
                    CleanerEmail = booking.AssignedCleanerEmail,
                    OwnerEmail = ownerEmail,
                    CleaningDate = effectiveCleaningDateIso,
                    IsCancellation = true
                };

                var cleanerInvokeResponse = await lambdaClient.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest
                {
                    FunctionName = calendarLambdaName,
                    InvocationType = Amazon.Lambda.InvocationType.RequestResponse,
                    Payload = JsonSerializer.Serialize(cleanerRequest)
                });

                if (!string.IsNullOrEmpty(cleanerInvokeResponse.FunctionError))
                {
                    _logger.LogError("CalendarLambda cleaner cancellation invoke returned FunctionError: {FunctionError}", cleanerInvokeResponse.FunctionError);
                    throw new Exception($"CalendarLambda cleaner cancellation invoke failed with FunctionError: {cleanerInvokeResponse.FunctionError}");
                }
                _logger.LogInformation($"Sent cancellation email to cleaner: {booking.AssignedCleanerEmail}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send cancellation email to cleaner: {ex.Message}");
            }
        }

        // Send cancellation email to owner
        try
        {

            var ownerHtmlBody = $@"<html><body>
<p>Hello {WebUtility.HtmlEncode(ownerName)},</p>
<p>The booking for <strong>{WebUtility.HtmlEncode(propertyName)}</strong> has been <strong style=""color: #dc3545;"">cancelled by the traveler</strong>.</p>
<p>The scheduled cleaning has been automatically cancelled.</p>
<p><strong>Cancelled Appointment:</strong></p>
<ul>
<li><strong>Property:</strong> {WebUtility.HtmlEncode(propertyName)}</li>
<li><strong>Cleaner:</strong> {WebUtility.HtmlEncode(booking.AssignedCleanerName ?? "(not yet assigned)")}</li>
<li><strong>Date:</strong> {WebUtility.HtmlEncode(effectiveFormattedDate)}</li>
<li><strong>Time:</strong> {WebUtility.HtmlEncode(effectiveFormattedTime)}</li>
</ul>
<p>The cleaning appointment has been removed. The attached cancellation should remove it from your calendar automatically.</p>
<p>Thank you,<br/>{WebUtility.HtmlEncode(ownerName)}</p>
</body></html>";

            var ownerRequest = new
            {
                FromEmail = ownerEmail,
                ToEmail = ownerEmail,
                Subject = $"Cleaning Cancelled for {propertyName} on {effectiveFormattedDate}",
                HtmlBody = ownerHtmlBody,
                PropertyName = propertyName,
                CleanerName = booking.AssignedCleanerName ?? "(not yet assigned)",
                CleanerId = string.Empty,
                CleanerEmail = booking.AssignedCleanerEmail,
                OwnerEmail = ownerEmail,
                CleaningDate = effectiveCleaningDateIso,
                IsCancellation = true
            };

            var ownerInvokeResponse = await lambdaClient.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest
            {
                FunctionName = calendarLambdaName,
                InvocationType = Amazon.Lambda.InvocationType.RequestResponse,
                Payload = JsonSerializer.Serialize(ownerRequest)
            });

            if (!string.IsNullOrEmpty(ownerInvokeResponse.FunctionError))
            {
                _logger.LogError("CalendarLambda owner cancellation invoke returned FunctionError: {FunctionError}", ownerInvokeResponse.FunctionError);
                throw new Exception($"CalendarLambda owner cancellation invoke failed with FunctionError: {ownerInvokeResponse.FunctionError}");
            }
            _logger.LogInformation($"Sent cancellation email to owner email address.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send cancellation email to owner: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Generates escalation email HTML with dynamic cleaner list based on actual property configuration
    /// </summary>
    private string GenerateEscalationEmailHtml(
        PropertyConfiguration property,
        Booking booking,
        DateTime cleaningDateTime,
        string ownerToken,
        string callbackApiUrl)
    {
        var cleaningDate = cleaningDateTime.ToString("yyyy-MM-dd").Split('-');
        var formattedDate = $"{cleaningDate[1]}-{cleaningDate[2]}-{cleaningDate[0]}";

        var cleanerButtonsHtml = new System.Text.StringBuilder();
        foreach (var cleaner in property.Cleaners)
        {
            var scheduleUrl = $"{callbackApiUrl}/override?propertyId={System.Net.WebUtility.UrlEncode(property.PropertyId)}&cleanerId={System.Net.WebUtility.UrlEncode(cleaner.CleanerId)}&ownerToken={System.Net.WebUtility.UrlEncode(ownerToken)}&action=schedule&bookingRef={System.Net.WebUtility.UrlEncode(booking.BookingReference)}";

            cleanerButtonsHtml.AppendLine($@"
                <div style=""margin-bottom: 25px; padding: 20px; background-color: #f8f9fa; border-radius: 8px; border-left: 4px solid #007bff;"">
                    <h3 style=""margin-top: 0; color: #343a40;"">{System.Net.WebUtility.HtmlEncode(cleaner.Name)}</h3>
                    <p style=""margin: 10px 0; color: #6c757d;"">
                        <strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(cleaner.Email)}<br>
                        <strong>Phone:</strong> {System.Net.WebUtility.HtmlEncode(cleaner.Phone)}
                    </p>
                    <div style=""margin-top: 15px;"">
                        <a href=""{scheduleUrl}""
                           style=""background-color: #28a745; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px; display: inline-block;"">
                            Schedule This Cleaner
                        </a>
                    </div>
                </div>");
        }

        return $@"<html>
<body>
    <p>Hello {System.Net.WebUtility.HtmlEncode(property.Metadata.OwnerName)},</p>
    <p><strong style=""color: #dc3545;"">URGENT:</strong> All cleaners have been contacted for the following booking, but none have responded or all have declined.</p>
    <p><strong>Property:</strong> {System.Net.WebUtility.HtmlEncode(property.Metadata.PropertyName)}</p>
    <p><strong>Address:</strong> {System.Net.WebUtility.HtmlEncode(property.Address)}</p>
    <p><strong>Cleaning Date:</strong> {System.Net.WebUtility.HtmlEncode(formattedDate)}</p>
    <p><strong>Booking Details:</strong></p>
    <ul>
        <li><strong>Check-in:</strong> {System.Net.WebUtility.HtmlEncode(booking.CheckInDate.ToString("MM-dd-yyyy"))}</li>
        <li><strong>Check-out:</strong> {System.Net.WebUtility.HtmlEncode(booking.CheckOutDate.ToString("MM-dd-yyyy"))}</li>
        <li><strong>Guests:</strong> {booking.NumberOfGuests}</li>
        <li><strong>Nights:</strong> {booking.NumberOfDays}</li>
    </ul>
    <p><strong>Please manually schedule one of the cleaners below:</strong></p>
    {cleanerButtonsHtml}
    <p style=""margin-top: 30px; color: #6c757d; font-size: 12px;"">
        This is an automated escalation email from your Rental Turn Manager system.
    </p>
</body>
</html>";
    }
}

/// <summary>
/// Email secret structure matching Secrets Manager format
/// </summary>
public class EmailSecret
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
    public string? OwnerOverrideToken { get; set; }
}