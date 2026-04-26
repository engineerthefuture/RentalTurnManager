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
using Amazon.Lambda;
using Amazon.Lambda.Model;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CallbackLambda;

public class Function
{
    private readonly IAmazonStepFunctions _stepFunctionsClient;
    private readonly IAmazonSecretsManager _secretsManagerClient;
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonLambda _lambdaClient;
    private readonly string _defaultOwnerEmail;

    public Function()
    {
        _stepFunctionsClient = new AmazonStepFunctionsClient();
        _secretsManagerClient = new AmazonSecretsManagerClient();
        _s3Client = new AmazonS3Client();
        _lambdaClient = new AmazonLambdaClient();
        var ownerEnv = System.Environment.GetEnvironmentVariable("OWNER_EMAIL");
        _defaultOwnerEmail = string.IsNullOrEmpty(ownerEnv) ? "support@example.com" : ownerEnv;
    }

    public Function(IAmazonStepFunctions stepFunctionsClient, IAmazonSecretsManager secretsManagerClient, IAmazonS3 s3Client)
    {
        _stepFunctionsClient = stepFunctionsClient;
        _secretsManagerClient = secretsManagerClient;
        _s3Client = s3Client;
        _lambdaClient = new AmazonLambdaClient();
        var ownerEnv = System.Environment.GetEnvironmentVariable("OWNER_EMAIL");
        _defaultOwnerEmail = string.IsNullOrEmpty(ownerEnv) ? "support@example.com" : ownerEnv;
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Received callback request: {JsonSerializer.Serialize(request)}");

        try
        {
            // Check if this is an owner override or cancel request
            if (request.QueryStringParameters.TryGetValue("ownerToken", out var ownerToken))
            {
                context.Logger.LogInformation("Processing owner override or cancel request");
                return await HandleOwnerOverride(request.QueryStringParameters, context);
            }
            
            // Check if this is a cancel request with cancelToken
            if (request.QueryStringParameters.TryGetValue("cancelToken", out var cancelToken))
            {
                context.Logger.LogInformation("Processing cleaning cancellation request");
                return await HandleCleaningCancellation(request.QueryStringParameters, context);
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

            // Get optional time parameter for alternative time slot selection
            request.QueryStringParameters.TryGetValue("time", out var alternativeTime);

            context.Logger.LogInformation($"Processing {response} response for task token. Token length: {taskToken.Length}. Alternative time: {alternativeTime ?? "none"}");

            // Send response to Step Functions
            var taskResponse = new { response = response, alternativeTime = alternativeTime };
            try
            {
                var jsonOptions = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                await _stepFunctionsClient.SendTaskSuccessAsync(new SendTaskSuccessRequest
                {
                    TaskToken = taskToken,
                    Output = JsonSerializer.Serialize(taskResponse, jsonOptions)
                });
                context.Logger.LogInformation($"Successfully sent task success for {response} response");
            }
            catch (Amazon.StepFunctions.Model.InvalidTokenException ex)
            {
                context.Logger.LogError($"Invalid token error: {ex.Message}. This usually means the task has already completed, timed out, or the token is incorrect.");
                
                var ownerEmail = _defaultOwnerEmail;
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
                
                var ownerEmail = _defaultOwnerEmail;
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
            // Values captured from the saved workflow context for display in the override response
            string? propertyDisplayName = null;
            string? assignedCleanerName = null;
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
            var secretName = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
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
                var bucketName = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
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

                    // Check if the content is a JSON string (starts with quote) - States.JsonToString produces this
                    if (workflowContextJson.StartsWith("\""))
                    {
                        // Deserialize the string to get the actual JSON
                        var unescapedJson = JsonSerializer.Deserialize<string>(workflowContextJson);
                        if (unescapedJson != null)
                        {
                            workflowContextJson = unescapedJson;
                            context.Logger.LogInformation($"Unescaped workflow context (length: {workflowContextJson.Length})");
                        }
                    }

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

                    // If the workflowPropertyId wasn't stored in the booking, prefer the property metadata name
                    try
                    {
                        if (string.IsNullOrEmpty(propertyDisplayName) && propertyData.ValueKind == JsonValueKind.Object)
                        {
                            if (propertyData.TryGetProperty("metadata", out var metadataElem) && metadataElem.ValueKind == JsonValueKind.Object &&
                                metadataElem.TryGetProperty("propertyName", out var pnameElem) && pnameElem.ValueKind == JsonValueKind.String)
                            {
                                propertyDisplayName = pnameElem.GetString();
                            }
                        }
                    }
                    catch
                    {
                        // ignore
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

                    // Capture displayable values from the workflow context/property data
                    try
                    {
                        // Assigned cleaner name from the property cleaners array
                        var cleanerElementForName = cleaners[selectedCleanerIndex];
                        if (cleanerElementForName.ValueKind == JsonValueKind.Object && cleanerElementForName.TryGetProperty("name", out var nameElem) && nameElem.ValueKind == JsonValueKind.String)
                        {
                            assignedCleanerName = nameElem.GetString();
                        }
                    }
                    catch
                    {
                        // ignore and leave assignedCleanerName null
                    }

                    try
                    {
                        // Workflow property and assigned cleaner may be stored inside the booking object
                        if (workflowInput != null && workflowInput.TryGetValue("booking", out var bookingElemForId))
                        {
                            var bookingForId = bookingElemForId.ValueKind == JsonValueKind.String
                                ? JsonDocument.Parse(bookingElemForId.GetString()!).RootElement
                                : bookingElemForId;

                            if (bookingForId.ValueKind == JsonValueKind.Object)
                            {
                                // support both snake/camel-case and PascalCase keys depending on serialization
                                if (bookingForId.TryGetProperty("workflowPropertyId", out var wpElem) && wpElem.ValueKind == JsonValueKind.String)
                                {
                                    propertyDisplayName = wpElem.GetString();
                                }
                                else if (bookingForId.TryGetProperty("WorkflowPropertyId", out var wpElem2) && wpElem2.ValueKind == JsonValueKind.String)
                                {
                                    propertyDisplayName = wpElem2.GetString();
                                }

                                if (bookingForId.TryGetProperty("AssignedCleanerName", out var acnElem) && acnElem.ValueKind == JsonValueKind.String)
                                {
                                    assignedCleanerName = acnElem.GetString();
                                }
                                else if (bookingForId.TryGetProperty("assignedCleanerName", out var acnElem2) && acnElem2.ValueKind == JsonValueKind.String)
                                {
                                    assignedCleanerName = acnElem2.GetString();
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignore and leave propertyDisplayName/assignedCleanerName as-is
                    }

                    // Ensure workflowInput is present (defensive check for static analysis)
                    if (workflowInput == null)
                    {
                        context.Logger.LogError("Workflow input unexpectedly null when preparing owner override");
                        throw new Exception("Workflow context missing");
                    }

                    // Update with override values (serialize primitives to JsonElement)
                    workflowInput["currentCleanerIndex"] = JsonDocument.Parse(selectedCleanerIndex.ToString()).RootElement;
                    workflowInput["attemptCount"] = JsonDocument.Parse("0").RootElement;
                    workflowInput["ownerOverride"] = JsonDocument.Parse("true").RootElement;
                    
                    // Serialize the modified input for Step Functions
                    var modifiedInputJson = JsonSerializer.Serialize(workflowInput);
                    
                    // Start a new workflow execution
                    var stateMachineArn = System.Environment.GetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN");
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

            if (string.IsNullOrEmpty(propertyDisplayName)) propertyDisplayName = propertyId;

            var ownerEmail = _defaultOwnerEmail;
            var encodedBookingRef = WebUtility.HtmlEncode(bookingRef);
            var encodedPropertyDisplay = WebUtility.HtmlEncode(propertyDisplayName);
            var encodedCleanerDisplay = WebUtility.HtmlEncode(assignedCleanerName ?? cleanerId);

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
        <div class='detail'>Property: {encodedPropertyDisplay}</div>
        <div class='detail'>Assigned Cleaner: {encodedCleanerDisplay}</div>
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
        var ownerEmail = _defaultOwnerEmail;
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
    
    private async Task<APIGatewayProxyResponse> HandleCleaningCancellation(IDictionary<string, string> queryParams, ILambdaContext context)
    {
        context.Logger.LogInformation("Handling cleaning cancellation");
        
        // Validate required parameters
        if (!queryParams.TryGetValue("cancelToken", out var cancelToken) ||
            !queryParams.TryGetValue("bookingRef", out var bookingRef) ||
            !queryParams.TryGetValue("platform", out var platform) ||
            !queryParams.TryGetValue("propertyId", out var propertyId))
        {
            return new APIGatewayProxyResponse
            {
                StatusCode = 400,
                Body = "Missing required parameters",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
        
        // Verify cancel token matches the stored token in Secrets Manager
        var secretName = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
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
        if (secret?.OwnerOverrideToken == null)
        {
            context.Logger.LogError("OwnerOverrideToken not found in secret");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = "Server configuration error",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
        
        // Verify token
        if (cancelToken != secret.OwnerOverrideToken)
        {
            context.Logger.LogWarning("Invalid cancel token provided");
            return new APIGatewayProxyResponse
            {
                StatusCode = 403,
                Body = "Invalid cancel token",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
        
        // Get cleanerId from query params (optional for backwards compatibility)
        queryParams.TryGetValue("cleanerId", out var cleanerId);
        
        context.Logger.LogInformation($"Token verified successfully for booking {bookingRef}");
        
        // Get booking state from S3 to retrieve cleaner information
        var bucketName = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
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
            // Retrieve booking from S3
            var key = $"bookings/{platform.ToLower()}/{bookingRef}.json";
            var getResponse = await _s3Client.GetObjectAsync(bucketName, key);
            
            string bookingJson;
            using (var reader = new StreamReader(getResponse.ResponseStream))
            {
                bookingJson = await reader.ReadToEndAsync();
            }
            
            var booking = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(bookingJson);
            if (booking == null)
            {
                context.Logger.LogError($"Failed to deserialize booking {bookingRef}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = "Failed to retrieve booking information",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }
            
            // Check if cleaning is already assigned (use PascalCase keys to match S3 JSON)
            string? cleanerName = null;
            string? cleanerEmail = null;
            string? propertyName = null;
            string? ownerName = null;
            DateTime? scheduledTime = null;
            
            if (booking.TryGetValue("AssignedCleanerName", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            {
                cleanerName = nameElement.GetString();
            }
            if (booking.TryGetValue("AssignedCleanerEmail", out var emailElement) && emailElement.ValueKind == JsonValueKind.String)
            {
                cleanerEmail = emailElement.GetString();
            }
            if (booking.TryGetValue("WorkflowPropertyId", out var propElement) && propElement.ValueKind == JsonValueKind.String)
            {
                propertyName = propElement.GetString();
            }
            if (booking.TryGetValue("OwnerName", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.String)
            {
                ownerName = ownerElement.GetString();
            }
            string? bookingTimezone = null;
            if (booking.TryGetValue("Timezone", out var tzElement) && tzElement.ValueKind == JsonValueKind.String)
            {
                bookingTimezone = tzElement.GetString();
            }
            string? bookingCleaningDuration = null;
            if (booking.TryGetValue("CleaningDuration", out var durElement) && durElement.ValueKind == JsonValueKind.String)
            {
                bookingCleaningDuration = durElement.GetString();
            }
            if (booking.TryGetValue("ScheduledCleaningTime", out var timeElement) && timeElement.ValueKind == JsonValueKind.String)
            {
                scheduledTime = DateTime.Parse(timeElement.GetString()!);
            }
            
            context.Logger.LogInformation($"Booking details - CleanerName: {cleanerName ?? "null"}, CleanerEmail: {cleanerEmail ?? "null"}, ScheduledTime: {scheduledTime?.ToString() ?? "null"}, PropertyName: {propertyName ?? "null"}, OwnerName: {ownerName ?? "null"}");
            
            // Update booking status to cancelled (use PascalCase to match BookingState model)
            booking["CleaningStatus"] = JsonDocument.Parse("{\"value\":\"cancelled\"}").RootElement.GetProperty("value");
            booking["CancelledAt"] = JsonDocument.Parse($"{{\"value\":\"{DateTime.UtcNow:O}\"}}").RootElement.GetProperty("value");
            
            // Add cleanerId if provided and not already set (use PascalCase to match BookingState model)
            if (!string.IsNullOrEmpty(cleanerId) && (!booking.ContainsKey("AssignedCleanerId") || booking["AssignedCleanerId"].ValueKind == JsonValueKind.Null))
            {
                booking["AssignedCleanerId"] = JsonDocument.Parse($"{{\"value\":\"{cleanerId}\"}}").RootElement.GetProperty("value");
            }
            
            // Add propertyId if not already set (use PascalCase to match BookingState model)
            if (!booking.ContainsKey("WorkflowPropertyId") || booking["WorkflowPropertyId"].ValueKind == JsonValueKind.Null)
            {
                booking["WorkflowPropertyId"] = JsonDocument.Parse($"{{\"value\":\"{propertyId}\"}}").RootElement.GetProperty("value");
            }
            
            // Save updated booking back to S3
            var updatedJson = JsonSerializer.Serialize(booking, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                ContentBody = updatedJson,
                ContentType = "application/json"
            });
            
            context.Logger.LogInformation($"Marked booking {bookingRef} as cancelled in S3");
            
            // Send cancellation emails with calendar CANCEL via CalendarLambda
            var ownerEmail = _defaultOwnerEmail;
            var calendarLambdaName = System.Environment.GetEnvironmentVariable("CALENDAR_LAMBDA_NAME") ?? "RentalTurnManager-CalendarLambda";
            
            // Get property timezone for time formatting (used in both cleaner and owner emails)
            TimeZoneInfo easternZone;
            try
            {
                easternZone = TimeZoneInfo.FindSystemTimeZoneById(bookingTimezone ?? "America/New_York");
            }
            catch (TimeZoneNotFoundException)
            {
                context.Logger.LogWarning("Property timezone not found, using UTC for time display");
                easternZone = TimeZoneInfo.Utc;
            }
            
            // Send to cleaner if assigned
            if (!string.IsNullOrEmpty(cleanerEmail) && !string.IsNullOrEmpty(cleanerName) && scheduledTime.HasValue)
            {
                try
                {
                    var formattedDate = scheduledTime.Value.ToString("MMMM dd, yyyy");
                    // Convert UTC time to Eastern Time for display
                    var easternTime = TimeZoneInfo.ConvertTimeFromUtc(scheduledTime.Value, easternZone);
                    var formattedTime = easternTime.ToString("h:mm tt");
                    
                    var cleanerHtmlBody = $@"<html><body>
<p>Hello {WebUtility.HtmlEncode(cleanerName)},</p>
<p>The cleaning scheduled for <strong>{WebUtility.HtmlEncode(propertyName ?? propertyId)}</strong> has been <strong style=""color: #dc3545;"">cancelled</strong> by the property owner.</p>
<p><strong>Cancelled Appointment:</strong></p>
<ul>
<li><strong>Property:</strong> {WebUtility.HtmlEncode(propertyName ?? propertyId)}</li>
<li><strong>Date:</strong> {WebUtility.HtmlEncode(formattedDate)}</li>
<li><strong>Time:</strong> {WebUtility.HtmlEncode(formattedTime)}</li>
</ul>
<p>You do not need to attend this cleaning. If you added this to your calendar, the attached cancellation should remove it automatically.</p>
<p>Thank you,<br/>{WebUtility.HtmlEncode(ownerName ?? "Property Management")}</p>
</body></html>";
                    
                    var cleanerRequest = new
                    {
                        FromEmail = ownerEmail,
                        ToEmail = cleanerEmail,
                        Subject = $"Cleaning Cancelled for {propertyName ?? propertyId} on {formattedDate}",
                        HtmlBody = cleanerHtmlBody,
                        PropertyName = propertyName ?? propertyId,
                        CleanerName = cleanerName,
                        CleanerId = cleanerId,
                        CleanerEmail = cleanerEmail,
                        OwnerName = ownerName ?? "Property Management",
                        OwnerEmail = ownerEmail,
                        CleaningDate = scheduledTime.Value.ToString("o"),
                        CleaningDuration = bookingCleaningDuration ?? string.Empty,
                        Timezone = bookingTimezone ?? "America/New_York",
                        IsCancellation = true
                    };
                    
                    var invokeRequest = new InvokeRequest
                    {
                        FunctionName = calendarLambdaName,
                        InvocationType = InvocationType.RequestResponse,
                        Payload = JsonSerializer.Serialize(cleanerRequest)
                    };
                    
                    var cleanerInvokeResponse = await _lambdaClient.InvokeAsync(invokeRequest);
                    if (!string.IsNullOrEmpty(cleanerInvokeResponse.FunctionError))
                    {
                        throw new InvalidOperationException($"CalendarLambda cleaner cancellation invoke failed with FunctionError: {cleanerInvokeResponse.FunctionError}");
                    }
                    context.Logger.LogInformation($"Invoked CalendarLambda to send cancellation email to cleaner: {cleanerEmail}");
                }
                catch (Exception ex)
                {
                    context.Logger.LogError($"Failed to invoke CalendarLambda for cleaner email: {ex.Message}");
                    // Continue anyway - cancellation is recorded
                }
            }
            
            // Send to owner - always send notification even if no scheduled time
            try
            {
                // Use scheduled time if available, otherwise use a reasonable default for the cancellation notice
                var cleaningDate = scheduledTime.HasValue 
                    ? scheduledTime.Value.ToString("o") 
                    : DateTime.UtcNow.ToString("o");
                
                // Convert UTC time to Eastern Time for display
                var formattedDate = scheduledTime.HasValue 
                    ? TimeZoneInfo.ConvertTimeFromUtc(scheduledTime.Value, easternZone).ToString("MMMM dd, yyyy") 
                    : "Unknown Date";
                var formattedTime = scheduledTime.HasValue 
                    ? TimeZoneInfo.ConvertTimeFromUtc(scheduledTime.Value, easternZone).ToString("h:mm tt")
                    : "12:00 PM";
                
                var ownerHtmlBody = $@"<html><body>
<p>Hello {WebUtility.HtmlEncode(ownerName ?? "Property Owner")},</p>
<p>The cleaning for <strong>{WebUtility.HtmlEncode(propertyName ?? propertyId)}</strong> has been <strong style=""color: #dc3545;"">{"cancelled"}</strong>.</p>
<p><strong>Cancelled Appointment:</strong></p>
<ul>
<li><strong>Property:</strong> {WebUtility.HtmlEncode(propertyName ?? propertyId)}</li>
<li><strong>Cleaner:</strong> {WebUtility.HtmlEncode(cleanerName ?? "(not yet assigned)")}</li>
<li><strong>Date:</strong> {WebUtility.HtmlEncode(formattedDate)}</li>
<li><strong>Time:</strong> {WebUtility.HtmlEncode(formattedTime)}</li>
</ul>
<p>The cleaning appointment has been removed. The attached cancellation should remove it from your calendar automatically.</p>
<p>Thank you,<br/>{WebUtility.HtmlEncode(ownerName ?? "Property Management")}</p>
</body></html>";
                    
                var ownerRequest = new
                {
                    FromEmail = ownerEmail,
                    ToEmail = ownerEmail,
                    Subject = $"Cleaning Cancelled for {propertyName ?? propertyId} on {formattedDate}",
                    HtmlBody = ownerHtmlBody,
                    PropertyName = propertyName ?? propertyId,
                    CleanerName = cleanerName ?? "(not yet assigned)",
                    CleanerEmail = cleanerEmail,
                    OwnerName = ownerName ?? "Property Management",
                    OwnerEmail = ownerEmail,
                    CleaningDate = cleaningDate,
                    CleaningDuration = bookingCleaningDuration ?? string.Empty,
                    Timezone = bookingTimezone ?? "America/New_York",
                    IsCancellation = true
                };
                    
                    var invokeRequest = new InvokeRequest
                    {
                        FunctionName = calendarLambdaName,
                        InvocationType = InvocationType.RequestResponse,
                        Payload = JsonSerializer.Serialize(ownerRequest)
                    };
                    
                    var response = await _lambdaClient.InvokeAsync(invokeRequest);
                    if (!string.IsNullOrEmpty(response.FunctionError))
                    {
                        throw new InvalidOperationException($"CalendarLambda owner cancellation invoke failed with FunctionError: {response.FunctionError}");
                    }
                    context.Logger.LogInformation($"Invoked CalendarLambda to send cancellation email to owner: {ownerEmail}, StatusCode: {response.StatusCode}");
                }
                catch (Exception ex)
                {
                    context.Logger.LogError($"Failed to invoke CalendarLambda for owner email: {ex.Message}");
                    // Continue anyway - cancellation is recorded
                }
            
            // Return success page
            var encodedBookingRef = WebUtility.HtmlEncode(bookingRef);
            var encodedPropertyName = WebUtility.HtmlEncode(propertyName ?? propertyId);
            var encodedCleanerName = WebUtility.HtmlEncode(cleanerName ?? "(not yet assigned)");
            
            var successHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cleaning Cancelled</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background-color: #f8f9fa; }}
        .success-container {{ background-color: white; padding: 40px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 500px; margin: 0 auto; }}
        .success-icon {{ color: #28a745; font-size: 48px; margin-bottom: 20px; }}
        .title {{ color: #28a745; font-size: 24px; font-weight: bold; margin-bottom: 15px; }}
        .message {{ font-size: 18px; color: #6c757d; margin-bottom: 10px; }}
        .detail {{ font-size: 14px; color: #495057; margin: 5px 0; }}
    </style>
</head>
<body>
    <div class='success-container' role='alert' aria-live='polite'>
        <div class='success-icon' aria-hidden='true'>✓</div>
        <div class='title'>Cleaning Cancelled Successfully</div>
        <div class='message'>The cleaning has been cancelled.</div>
        <div class='detail'>Booking: {encodedBookingRef}</div>
        <div class='detail'>Property: {encodedPropertyName}</div>
        <div class='detail'>Cleaner: {encodedCleanerName}</div>
        <div class='message' style='margin-top: 20px;'>Cancellation notifications with calendar updates have been sent to the cleaner and owner.</div>
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
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            context.Logger.LogError($"Booking {bookingRef} not found in S3");
            return new APIGatewayProxyResponse
            {
                StatusCode = 404,
                Body = "Booking not found",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
        catch (Exception ex)
        {
            context.Logger.LogError($"Error cancelling cleaning: {ex.Message}");
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = $"Error processing cancellation: {ex.Message}",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
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