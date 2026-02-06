/************************
 * Rental Turn Manager
 * Function.cs (Callback Lambda)
 * 
 * AWS Lambda function that handles HTTP callbacks from cleaners via
 * API Gateway. Processes cleaner responses (confirm/deny) and sends
 * task success/failure signals back to Step Functions workflows and  
 * provides a message to the cleaner.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.S3;
using Amazon.S3.Model;
using System.Net;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CallbackLambda;

public class Function
{
    private readonly IAmazonStepFunctions _stepFunctionsClient;
    private readonly IAmazonSecretsManager _secretsManagerClient;
    private readonly IAmazonS3 _s3Client;

    public Function()
    {
        _stepFunctionsClient = new AmazonStepFunctionsClient();
        _secretsManagerClient = new AmazonSecretsManagerClient();
        _s3Client = new AmazonS3Client();
    }

    public Function(IAmazonStepFunctions stepFunctionsClient, IAmazonSecretsManager secretsManagerClient, IAmazonS3 s3Client)
    {
        _stepFunctionsClient = stepFunctionsClient;
        _secretsManagerClient = secretsManagerClient;
        _s3Client = s3Client;
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Received callback request: {JsonSerializer.Serialize(request)}");

        try
        {
            // Check if this is an owner override request
            if (request.QueryStringParameters.TryGetValue("ownerToken", out var ownerToken))
            {
                context.Logger.LogInformation("Processing owner override request");
                return await HandleOwnerOverride(request.QueryStringParameters, context);
            }

            // Extract query parameters
            if (!request.QueryStringParameters.TryGetValue("token", out var taskToken))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Missing task token",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            context.Logger.LogInformation($"Received token (length: {taskToken.Length}): {taskToken.Substring(0, Math.Min(50, taskToken.Length))}....");

            // URL decoding converts + to space, so we need to convert spaces back to +
            taskToken = taskToken.Replace(" ", "+");

            if (!request.QueryStringParameters.TryGetValue("response", out var response))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Missing response",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // Validate response
            if (response != "yes" && response != "no")
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Invalid response. Must be 'yes' or 'no'",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            context.Logger.LogInformation($"Processing {response} response for task token. Token length: {taskToken.Length}");

            // Send response to Step Functions
            var taskResponse = new { response = response };
            try
            {
                await _stepFunctionsClient.SendTaskSuccessAsync(new SendTaskSuccessRequest
                {
                    TaskToken = taskToken,
                    Output = JsonSerializer.Serialize(taskResponse)
                });
                context.Logger.LogInformation($"Successfully sent task success for {response} response");
            }
            catch (Amazon.StepFunctions.Model.InvalidTokenException ex)
            {
                context.Logger.LogError($"Invalid token error: {ex.Message}. This usually means the task has already completed, timed out, or the token is incorrect.");
                
                var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL") ?? "support@example.com";
                var encodedEmail = WebUtility.HtmlEncode(ownerEmail);
                var encodedResponse = WebUtility.HtmlEncode(response.ToUpper());
                
                var errorHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Response Already Recorded</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background-color: #f8f9fa; }}
        .error-container {{ background-color: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        .error-icon {{ color: #ffc107; font-size: 48px; margin-bottom: 20px; }}
        .error-title {{ color: #856404; font-size: 24px; font-weight: bold; margin-bottom: 15px; }}
        .message {{ font-size: 18px; color: #6c757d; margin-bottom: 10px; line-height: 1.5; }}
        .highlight {{ font-weight: bold; color: #495057; }}
        .contact-link {{ color: #007bff; text-decoration: none; }}
        .contact-link:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='error-container' role='alert' aria-live='polite'>
        <div class='error-icon' aria-hidden='true'>ℹ️</div>
        <div class='error-title'>Response Already Recorded</div>
        <div class='message'>A response has already been received for this request, or the link has expired.</div>
        <div class='message'>You attempted to respond: <span class='highlight'>{encodedResponse}</span></div>
        <div class='message'>If you need to change your response, please contact <a href='mailto:{encodedEmail}' class='contact-link'>{encodedEmail}</a> for assistance.</div>
    </div>
</body>
</html>";
                
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = errorHtml,
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
                };
            }
            catch (TaskTimedOutException ex)
            {
                context.Logger.LogError($"Task timeout error: {ex.Message}");
                
                var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL") ?? "support@example.com";
                var encodedEmail = WebUtility.HtmlEncode(ownerEmail);
                var encodedResponse = WebUtility.HtmlEncode(response.ToUpper());
                
                var errorHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Response Already Recorded</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background-color: #f8f9fa; }}
        .error-container {{ background-color: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        .error-icon {{ color: #ffc107; font-size: 48px; margin-bottom: 20px; }}
        .error-title {{ color: #856404; font-size: 24px; font-weight: bold; margin-bottom: 15px; }}
        .message {{ font-size: 18px; color: #6c757d; margin-bottom: 10px; line-height: 1.5; }}
        .highlight {{ font-weight: bold; color: #495057; }}
        .contact-link {{ color: #007bff; text-decoration: none; }}
        .contact-link:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='error-container' role='alert' aria-live='polite'>
        <div class='error-icon' aria-hidden='true'>ℹ️</div>
        <div class='error-title'>Response Already Recorded</div>
        <div class='message'>A response has already been received for this request, or the link has expired.</div>
        <div class='message'>You attempted to respond: <span class='highlight'>{encodedResponse}</span></div>
        <div class='message'>If you need to change your response, please contact <a href='mailto:{encodedEmail}' class='contact-link'>{encodedEmail}</a> for assistance.</div>
    </div>
</body>
</html>";
                
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = errorHtml,
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Error sending task success: {ex.GetType().Name} - {ex.Message}");
                throw;
            }

            // Return HTML response
            var isYes = response.Equals("yes", StringComparison.OrdinalIgnoreCase);
            var color = isYes ? "#28a745" : "#dc3545";
            var icon = isYes ? "✓" : "✗";
            
            var htmlResponse = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Response Recorded</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; }}
        .response {{ color: {color}; font-size: 24px; }}
        .message {{ margin-top: 20px; font-size: 18px; }}
    </style>
</head>
<body>
    <div role='alert' aria-live='polite'>
        <div class='response' aria-hidden='true'>{icon}</div>
        <div class='response'>Response Recorded</div>
        <div class='message'>Thank you! Your response ({response.ToUpper()}) has been recorded.</div>
        <div class='message'>You can close this window.</div>
    </div>
</body>
</html>";

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = htmlResponse,
                Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing callback: {ex.Message}");
            var encodedMessage = WebUtility.HtmlEncode(ex.Message);
            var errorResponse = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Error</title>
</head>
<body>
    <h1>Error</h1>
    <p>{encodedMessage}</p>
</body>
</html>";
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = errorResponse,
                Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
            };
        }
    }

    private async Task<APIGatewayProxyResponse> HandleOwnerOverride(IDictionary<string, string> queryParams, ILambdaContext context)
    {
        try
        {
            // Validate required parameters
            if (!queryParams.TryGetValue("ownerToken", out var providedToken) ||
                !queryParams.TryGetValue("action", out var action) ||
                !queryParams.TryGetValue("cleanerId", out var cleanerId) ||
                !queryParams.TryGetValue("propertyId", out var propertyId) ||
                !queryParams.TryGetValue("bookingRef", out var bookingRef))
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Missing required parameters",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // Validate action
            if (action != "schedule" && action != "cancel")
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = "Invalid action. Must be 'schedule' or 'cancel'",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // Get owner token from Secrets Manager
            var secretName = Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
            if (string.IsNullOrEmpty(secretName))
            {
                context.Logger.LogError("EMAIL_SECRET_NAME environment variable not set");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = "Server configuration error",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            var secretResponse = await _secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
            {
                SecretId = secretName
            });

            var secret = JsonSerializer.Deserialize<EmailSecret>(secretResponse.SecretString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (secret == null || string.IsNullOrEmpty(secret.OwnerOverrideToken))
            {
                context.Logger.LogError("Owner override token not found in secret");
                return CreateUnauthorizedResponse();
            }

            // Validate token
            if (providedToken != secret.OwnerOverrideToken)
            {
                context.Logger.LogWarning($"Invalid owner token provided for booking {bookingRef}");
                return CreateUnauthorizedResponse();
            }

            context.Logger.LogInformation($"Valid owner override: {action} cleaner {cleanerId} for booking {bookingRef}");

            if (action == "schedule")
            {
                // Retrieve workflow context from S3
                var bucketName = Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
                if (string.IsNullOrEmpty(bucketName))
                {
                    context.Logger.LogError("BOOKING_STATE_BUCKET environment variable not set");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 500,
                        Body = "Server configuration error",
                        Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                    };
                }

                try
                {
                    // First, try to get the platform by listing objects with the bookingRef prefix
                    // The booking files are stored as: bookings/{platform}/{bookingRef}.json
                    // We need to find which platform folder contains this booking
                    
                    context.Logger.LogInformation($"Searching for workflow context for booking {bookingRef}");
                    
                    string? workflowContextJson = null;
                    string? foundKey = null;
                    
                    // Try common platforms
                    var platforms = new[] { "airbnb", "vrbo", "bookingcom" };
                    foreach (var platform in platforms)
                    {
                        var s3Key = $"bookings/{platform}/{bookingRef}_workflow-context.json";
                        try
                        {
                            context.Logger.LogInformation($"Trying s3://{bucketName}/{s3Key}");
                            var getObjectResponse = await _s3Client.GetObjectAsync(new GetObjectRequest
                            {
                                BucketName = bucketName,
                                Key = s3Key
                            });

                            using (var reader = new StreamReader(getObjectResponse.ResponseStream))
                            {
                                workflowContextJson = await reader.ReadToEndAsync();
                            }
                            
                            foundKey = s3Key;
                            context.Logger.LogInformation($"Found workflow context at {s3Key}");
                            break;
                        }
                        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            // Try next platform
                            continue;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(workflowContextJson))
                    {
                        context.Logger.LogError($"Workflow context not found for booking {bookingRef} in any platform folder");
                        return new APIGatewayProxyResponse
                        {
                            StatusCode = 404,
                            Body = "Workflow context not found. Please ensure the escalation email was sent.",
                            Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                        };
                    }

                    context.Logger.LogInformation($"Retrieved workflow context (length: {workflowContextJson.Length})");

                    // Log first 500 chars for debugging
                    context.Logger.LogInformation($"Workflow context preview: {workflowContextJson.Substring(0, Math.Min(500, workflowContextJson.Length))}");

                    // Build a new workflow input by deserializing to Dictionary<string, JsonElement>
                    var workflowInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        workflowContextJson, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (workflowInput == null)
                    {
                        context.Logger.LogError("Failed to deserialize workflow context");
                        throw new Exception("Invalid workflow context");
                    }

                    // Get the property data to find the cleaner index
                    if (!workflowInput.TryGetValue("property", out var propertyElement))
                    {
                        context.Logger.LogError("Property data not found in workflow context");
                        throw new Exception("Invalid workflow context: missing property data");
                    }

                    // Property might be a string (escaped JSON) or an object
                    JsonElement propertyData;
                    if (propertyElement.ValueKind == JsonValueKind.String)
                    {
                        // Parse the escaped JSON string
                        propertyData = JsonDocument.Parse(propertyElement.GetString()!).RootElement;
                    }
                    else
                    {
                        propertyData = propertyElement;
                    }

                    if (!propertyData.TryGetProperty("cleaners", out var cleanersArray))
                    {
                        context.Logger.LogError("Cleaners array not found in property data");
                        throw new Exception("Invalid property data: missing cleaners");
                    }

                    var cleaners = cleanersArray.EnumerateArray().ToList();
                    var selectedCleanerIndex = cleaners.FindIndex(c => 
                        c.TryGetProperty("cleanerId", out var id) && id.GetString() == cleanerId);
                    
                    if (selectedCleanerIndex == -1)
                    {
                        context.Logger.LogError($"Cleaner {cleanerId} not found in property configuration");
                        throw new Exception("Cleaner not found");
                    }

                    context.Logger.LogInformation($"Found cleaner at index {selectedCleanerIndex}");

                    // Update with override values (serialize primitives to JsonElement)
                    workflowInput["currentCleanerIndex"] = JsonDocument.Parse(selectedCleanerIndex.ToString()).RootElement;
                    workflowInput["attemptCount"] = JsonDocument.Parse("0").RootElement;
                    workflowInput["ownerOverride"] = JsonDocument.Parse("true").RootElement;
                    
                    // Serialize the modified input for Step Functions
                    var modifiedInputJson = JsonSerializer.Serialize(workflowInput);
                    
                    // Start a new workflow execution
                    var stateMachineArn = Environment.GetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN");
                    if (string.IsNullOrEmpty(stateMachineArn))
                    {
                        context.Logger.LogError("CLEANER_WORKFLOW_STATE_MACHINE_ARN environment variable not set");
                        throw new Exception("State machine ARN not configured");
                    }

                    var executionName = $"owner-override-{bookingRef}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                    var startExecutionRequest = new StartExecutionRequest
                    {
                        StateMachineArn = stateMachineArn,
                        Name = executionName,
                        Input = modifiedInputJson
                    };

                    var executionResponse = await _stepFunctionsClient.StartExecutionAsync(startExecutionRequest);
                    context.Logger.LogInformation($"Started workflow execution: {executionResponse.ExecutionArn}");
                }
                catch (Exception ex)
                {
                    context.Logger.LogError($"Error restarting workflow: {ex.Message}");
                    throw;
                }
            }
            else if (action == "cancel")
            {
                context.Logger.LogInformation($"Owner cancelled cleaning for booking {bookingRef}");
            }

            var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL") ?? "owner@example.com";
            var encodedCleanerId = WebUtility.HtmlEncode(cleanerId);
            var encodedBookingRef = WebUtility.HtmlEncode(bookingRef);
            var encodedPropertyId = WebUtility.HtmlEncode(propertyId);

            var successHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Override Successful</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background-color: #f8f9fa; }}
        .success-container {{ background-color: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        .success-icon {{ color: #28a745; font-size: 48px; margin-bottom: 20px; }}
        .title {{ color: #28a745; font-size: 24px; font-weight: bold; margin-bottom: 15px; }}
        .message {{ font-size: 18px; color: #495057; margin-bottom: 10px; line-height: 1.5; }}
        .detail {{ font-size: 16px; color: #6c757d; margin-top: 10px; }}
    </style>
</head>
<body>
    <div class='success-container' role='alert' aria-live='polite'>
        <div class='success-icon' aria-hidden='true'>✓</div>
        <div class='title'>Owner Override Successful</div>
        <div class='message'>You have {(action == "schedule" ? "scheduled" : "cancelled")} the cleaner.</div>
        <div class='detail'>Booking: {encodedBookingRef}</div>
        <div class='detail'>Property: {encodedPropertyId}</div>
        <div class='detail'>Cleaner: {encodedCleanerId}</div>
        {(action == "schedule" ? "<div class='message' style='margin-top: 20px;'>The cleaner and owner will receive confirmation emails shortly.</div>" : "")}
        <div class='message' style='margin-top: 20px;'>You can close this window.</div>
    </div>
</body>
</html>";

            return new APIGatewayProxyResponse
            {
                StatusCode = 200,
                Body = successHtml,
                Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error processing owner override: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    private APIGatewayProxyResponse CreateUnauthorizedResponse()
    {
        var ownerEmail = Environment.GetEnvironmentVariable("OWNER_EMAIL") ?? "support@example.com";
        var encodedEmail = WebUtility.HtmlEncode(ownerEmail);

        var errorHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Unauthorized</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background-color: #f8f9fa; }}
        .error-container {{ background-color: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        .error-icon {{ color: #dc3545; font-size: 48px; margin-bottom: 20px; }}
        .error-title {{ color: #dc3545; font-size: 24px; font-weight: bold; margin-bottom: 15px; }}
        .message {{ font-size: 18px; color: #6c757d; margin-bottom: 10px; line-height: 1.5; }}
        .contact-link {{ color: #007bff; text-decoration: none; }}
        .contact-link:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='error-container' role='alert' aria-live='polite'>
        <div class='error-icon' aria-hidden='true'>⚠️</div>
        <div class='error-title'>Unauthorized Access</div>
        <div class='message'>The override token is invalid or missing.</div>
        <div class='message'>If you believe this is an error, please contact <a href='mailto:{encodedEmail}' class='contact-link'>{encodedEmail}</a>.</div>
    </div>
</body>
</html>";

        return new APIGatewayProxyResponse
        {
            StatusCode = 401,
            Body = errorHtml,
            Headers = new Dictionary<string, string> { { "Content-Type", "text/html; charset=utf-8" } }
        };
    }
}

public class EmailSecret
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
    public string? OwnerOverrideToken { get; set; }}