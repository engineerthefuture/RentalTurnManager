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
            
            // Get configured subject patterns
            var subjectPatterns = _propertiesConfig.EmailFilters?.SubjectPatterns ?? new List<string> { "Reservation confirmed", "Instant Booking from", "booking confirmation" };
            _logger.LogInformation($"Using subject patterns: {string.Join(", ", subjectPatterns)}");
            
            // Scan emails for new bookings
            _logger.LogInformation($"Scanning emails for new bookings (ForceRescan: {input.ForceRescan})");
            var emails = await emailScanner.ScanForBookingEmailsAsync(emailCredentials, input.ForceRescan, fromAddresses, subjectPatterns);
            _logger.LogInformation($"Found {emails.Count} potential booking emails");

            foreach (var email in emails)
            {
                try
                {
                    // Parse booking information
                    var booking = bookingParser.ParseBooking(email);
                    if (booking == null)
                    {
                        _logger.LogWarning($"Could not parse booking from email: {email.Subject}");
                        continue;
                    }
                    
                    // Validate booking has required fields
                    if (string.IsNullOrEmpty(booking.BookingReference))
                    {
                        _logger.LogWarning($"Booking missing reference ID from email: {email.Subject}");
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
                    
                    if (property == null)
                    {
                        var error = $"No property configuration found for {booking.Platform} property {booking.PropertyId}";
                        _logger.LogError(error);
                        _logger.LogError("Available properties: {PropertyIds}", string.Join(", ", _propertiesConfig.Properties?.Select(p => $"{p.PropertyId}: {string.Join(", ", p.PlatformIds.Select(kv => $"{kv.Key}={kv.Value}"))}") ?? Array.Empty<string>()));
                        response.Errors.Add(error);
                        continue;
                    }

                    // Calculate cleaning time (on checkout date)
                    var cleaningDate = booking.CheckOutDate;
                    var cleaningTime = new TimeSpan(12, 0, 0); // 12 PM default

                    // Get owner email from environment variable
                    var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL");
                    if (string.IsNullOrEmpty(ownerEmail))
                    {
                        _logger.LogWarning("OWNER_EMAIL environment variable not set, using default");
                        ownerEmail = "owner@example.com";
                    }
                    _logger.LogInformation($"Using owner email: {ownerEmail}");

                    // Get callback API URL from environment variable
                    var callbackApiUrl = Environment.GetEnvironmentVariable("CALLBACK_API_URL");
                    if (string.IsNullOrEmpty(callbackApiUrl))
                    {
                        _logger.LogWarning("CALLBACK_API_URL environment variable not set");
                        callbackApiUrl = "";
                    }

                    // Ensure ownerName has a default value if missing
                    if (string.IsNullOrEmpty(property.Metadata.OwnerName))
                    {
                        property.Metadata.OwnerName = "Property Management";
                    }
                    
                    // Get booking state bucket name from environment variable
                    var bookingStateBucket = Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET") ?? 
                                           $"{Environment.GetEnvironmentVariable("NAMESPACE_PREFIX") ?? "bf"}-{Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "dev"}-s3-{(Environment.GetEnvironmentVariable("APP_NAME") ?? "RentalTurnManager").ToLower()}-bookings";

                    // Start Step Functions workflow
                    var workflowInput = new CleanerWorkflowInput
                    {
                        Booking = booking,
                        Property = property,
                        CleaningDateTime = cleaningDate.Add(cleaningTime),
                        CurrentCleanerIndex = 0,
                        AttemptCount = 0,
                        OwnerEmail = ownerEmail,
                        CallbackApiUrl = callbackApiUrl,
                        BookingStateBucket = bookingStateBucket
                    };

                    var executionArn = await stepFunctionService.StartCleanerWorkflowAsync(workflowInput);
                    response.WorkflowsStarted++;
                    _logger.LogInformation($"Started workflow: {executionArn}");

                    // Save booking state
                    await bookingStateService.SaveBookingAsync(booking);
                    _logger.LogInformation($"Saved booking state: {booking.Platform} - {booking.BookingReference}");

                    // Mark email as processed
                    await emailScanner.MarkEmailAsProcessedAsync(emailCredentials, email);
                }
                catch (Exception ex)
                {
                    var error = $"Error processing email '{email.Subject}': {ex.Message}";
                    _logger.LogError(ex, error);
                    response.Errors.Add(error);
                }
            }

            _logger.LogInformation($"Email scan complete. Processed {response.BookingsProcessed} bookings, started {response.WorkflowsStarted} workflows");
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
}
