/************************
 * Rental Turn Manager
 * Function.cs (Callback Lambda)
 * 
 * AWS Lambda function that handles HTTP callbacks from cleaners via
 * API Gateway. Processes cleaner responses (confirm/deny) and sends
 * task success/failure signals back to Step Functions workflows.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace RentalTurnManager.CallbackLambda;

public class Function
{
    private readonly IAmazonStepFunctions _stepFunctionsClient;

    public Function()
    {
        _stepFunctionsClient = new AmazonStepFunctionsClient();
    }

    public Function(IAmazonStepFunctions stepFunctionsClient)
    {
        _stepFunctionsClient = stepFunctionsClient;
    }

    public async Task<APIGatewayProxyResponse> FunctionHandler(APIGatewayProxyRequest request, ILambdaContext context)
    {
        context.Logger.LogInformation($"Received callback request: {JsonSerializer.Serialize(request)}");

        try
        {
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
                
                var errorHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Link Expired</title>
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
    <div class='error-container'>
        <div class='error-icon'>⚠️</div>
        <div class='error-title'>This Link Has Expired</div>
        <div class='message'>This response link has already been used or the request has timed out.</div>
        <div class='message'>If you need to update your response, please contact <a href='mailto:{ownerEmail}' class='contact-link'>{ownerEmail}</a> for assistance.</div>
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
            var htmlResponse = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Response Recorded</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; }}
        .success {{ color: #28a745; font-size: 24px; }}
        .message {{ margin-top: 20px; font-size: 18px; }}
    </style>
</head>
<body>
    <div class='success'>✓ Response Recorded</div>
    <div class='message'>Thank you! Your response ({response.ToUpper()}) has been recorded.</div>
    <div class='message'>You can close this window.</div>
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
            return new APIGatewayProxyResponse
            {
                StatusCode = 500,
                Body = $"Error: {ex.Message}",
                Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
            };
        }
    }
}
