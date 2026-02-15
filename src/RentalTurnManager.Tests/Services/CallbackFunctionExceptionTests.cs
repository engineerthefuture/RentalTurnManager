using Xunit;
using Moq;
using FluentAssertions;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.S3;
using Amazon.Lambda.TestUtilities;
using RentalTurnManager.CallbackLambda;
using Amazon.Lambda.APIGatewayEvents;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RentalTurnManager.Tests.Services;

public class CallbackFunctionExceptionTests
{
    [Fact]
    public async Task SendTaskSuccess_InvalidToken_Returns400Html()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new InvalidTokenException("Token has expired"));

        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok1",
                ["response"] = "yes"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Response Already Recorded");
    }

    [Fact]
    public async Task SendTaskSuccess_TaskTimedOut_Returns400Html()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new TaskTimedOutException("Timed out"));

        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok2",
                ["response"] = "no"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Response Already Recorded");
    }

    [Fact]
    public async Task SendTaskSuccess_GenericException_Returns500WithEncodedMessage()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new System.Exception("<script>alert('x')</script>"));

        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["token"] = "tok3",
                ["response"] = "yes"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;");
    }
}
