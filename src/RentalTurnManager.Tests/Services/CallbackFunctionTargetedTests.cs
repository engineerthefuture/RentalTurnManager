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

public class CallbackFunctionTargetedTests
{
    [Fact]
    public async Task OwnerOverride_Schedule_EscapedWorkflowContext_StartsExecution()
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

        // Build a workflow context then double-serialize it so the S3 body starts with a quote
        var actualWorkflow = JsonSerializer.Serialize(new
        {
            property = new { cleaners = new[] { new { cleanerId = "c1" } } }
        });
        var escaped = JsonSerializer.Serialize(actualWorkflow); // produces a quoted JSON string

        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(escaped))
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
    public async Task CancelCleaning_S3NotFound_Returns404()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tokx" }) });

        var s3Mock = new Mock<IAmazonS3>();
        var notFoundEx = new AmazonS3Exception("Not found") { StatusCode = System.Net.HttpStatusCode.NotFound };
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(notFoundEx);

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tokx",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(404);
        res.Body.Should().Contain("Booking not found");
    }
}
