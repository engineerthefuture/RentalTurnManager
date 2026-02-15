using Xunit;
using Moq;
using FluentAssertions;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Amazon.Lambda.TestUtilities;
using RentalTurnManager.CallbackLambda;
using Amazon.Lambda.APIGatewayEvents;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RentalTurnManager.Tests.Services;

public class CallbackFunctionAdditionalTests
{
    [Fact]
    public async Task MissingToken_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>()
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Missing task token");
    }

    [Fact]
    public async Task MissingResponse_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Missing response");
    }

    [Fact]
    public async Task InvalidResponse_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok",
                ["response"] = "maybe"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Invalid response");
    }

    [Fact]
    public async Task SendTaskSuccess_Returns200AndCallsStepFunctions()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ReturnsAsync(new SendTaskSuccessResponse());

        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "valid-token",
                ["response"] = "yes",
                ["time"] = "2026-02-26T10:00:00Z"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Response Recorded");
        stepMock.Verify(x => x.SendTaskSuccessAsync(It.Is<SendTaskSuccessRequest>(r => r.TaskToken == "valid-token"), default), Times.Once);
    }

    [Fact]
    public async Task SendTaskSuccess_InvalidToken_Returns400HtmlContainsOwnerEmail()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new InvalidTokenException("Invalid token"));

        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@test.example");

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok123",
                ["response"] = "no"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Response Already Recorded");
        res.Body.Should().Contain("owner@test.example");
    }

    [Fact]
    public async Task OwnerOverride_MissingParams_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "t"
                // missing other required params
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Missing required parameters");
    }

    [Fact]
    public async Task OwnerOverride_InvalidAction_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "t",
                ["action"] = "invalid",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Invalid action");
    }

    [Fact]
    public async Task OwnerOverride_InvalidToken_Returns401()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "real-token" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "wrong-token",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(401);
        res.Body.Should().Contain("Unauthorized Access");
    }

    [Fact]
    public async Task OwnerOverride_Schedule_SuccessPath_Returns200()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default))
            .ReturnsAsync(new StartExecutionResponse { ExecutionArn = "arn:exec" });

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "owner-token" }) });

        var s3Mock = new Mock<IAmazonS3>();
        // Provide a workflow context JSON with property.cleaners array containing cleanerId
        var workflowContext = JsonSerializer.Serialize(new
        {
            property = new { cleaners = new[] { new { cleanerId = "c1" } } }
        });

        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowContext))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(getObjResp);

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN", "arn:state:machine");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "owner-token",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Owner Override Successful");
        stepMock.Verify(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task CancelCleaning_InvalidToken_Returns403()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "real-token" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "bad",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(403);
        res.Body.Should().Contain("Invalid cancel token");
    }

    [Fact]
    public async Task CancelCleaning_SuccessPath_Returns200()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tokx" }) });

        var s3Mock = new Mock<IAmazonS3>();
        var booking = new Dictionary<string, object>
        {
            ["AssignedCleanerName"] = "Cleaner One",
            ["AssignedCleanerEmail"] = "cleaner@x.com",
            ["WorkflowPropertyId"] = "prop-1",
            ["OwnerName"] = "Owner One",
            ["ScheduledCleaningTime"] = DateTime.UtcNow.ToString("o")
        };

        var bookingJson = JsonSerializer.Serialize(booking);
        var getObjResp = new GetObjectResponse { ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson)) };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getObjResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");
        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@x.com");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tokx",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1",
                ["cleanerId"] = "c1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");
    }
}
