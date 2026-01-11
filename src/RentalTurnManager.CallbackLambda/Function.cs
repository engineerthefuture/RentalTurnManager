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

            context.Logger.LogInformation($"Processing {response} response for task token");

            // Decode base64 token
            string decodedToken;
            try
            {
                var tokenBytes = Convert.FromBase64String(taskToken);
                decodedToken = System.Text.Encoding.UTF8.GetString(tokenBytes);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"Failed to decode base64 token: {ex.Message}");
                return new APIGatewayProxyResponse
                {
                    StatusCode = 400,
                    Body = $"Invalid token format: {ex.Message}",
                    Headers = new Dictionary<string, string> { { "Content-Type", "text/plain" } }
                };
            }

            // Send response to Step Functions
            var taskResponse = new { response = response };
            await _stepFunctionsClient.SendTaskSuccessAsync(new SendTaskSuccessRequest
            {
                TaskToken = decodedToken,
                Output = JsonSerializer.Serialize(taskResponse)
            });

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
                Headers = new Dictionary<string, string> { { "Content-Type", "text/html" } }
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
