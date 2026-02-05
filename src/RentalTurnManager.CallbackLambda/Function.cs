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
using System.Net;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CallbackLambda;

public class Function
{
    private readonly IAmazonStepFunctions _stepFunctionsClient;
    private readonly IAmazonSecretsManager _secretsManagerClient;

    public Function()
    {
        _stepFunctionsClient = new AmazonStepFunctionsClient();
        _secretsManagerClient = new AmazonSecretsManagerClient();
    }

    public Function(IAmazonStepFunctions stepFunctionsClient, IAmazonSecretsManager secretsManagerClient)
    {
        _stepFunctionsClient = stepFunctionsClient;
        _secretsManagerClient = secretsManagerClient;
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

            var secret = JsonSerializer.Deserialize<EmailSecret>(secretResponse.SecretString);
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

            // TODO: Implement the actual scheduling/cancellation logic here
            // For now, return success

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