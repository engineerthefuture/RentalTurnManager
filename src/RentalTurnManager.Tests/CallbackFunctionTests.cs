/************************
 * Rental Turn Manager
 * CallbackFunctionTests.cs
 * 
 * Unit tests for CallbackLambda function covering callback handling,
 * token validation, error responses, and HTML output.
 * 
 * Author: Brent Foster
 * Created: 02-04-2026
 ***********************/

using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.TestUtilities;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using FluentAssertions;
using Moq;
using RentalTurnManager.CallbackLambda;
using Xunit;

namespace RentalTurnManager.Tests;

public class CallbackFunctionTests
{
    private readonly Mock<IAmazonStepFunctions> _mockStepFunctions;
    private readonly TestLambdaContext _context;

    public CallbackFunctionTests()
    {
        _mockStepFunctions = new Mock<IAmazonStepFunctions>();
        _context = new TestLambdaContext
        {
            FunctionName = "CallbackFunction",
            FunctionVersion = "1",
            Logger = new TestLambdaLogger()
        };
    }

    [Fact]
    public async Task FunctionHandler_ValidYesResponse_ReturnsSuccessHtml()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "valid-token-123" },
                { "response", "yes" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ReturnsAsync(new SendTaskSuccessResponse());

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(200);
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
        response.Body.Should().Contain("Response Recorded");
        response.Body.Should().Contain("YES");
        response.Body.Should().Contain("lang=\"en\"");
        response.Body.Should().Contain("role='alert'");
    }

    [Fact]
    public async Task FunctionHandler_ValidNoResponse_ReturnsSuccessHtml()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "valid-token-123" },
                { "response", "no" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ReturnsAsync(new SendTaskSuccessResponse());

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(200);
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
        response.Body.Should().Contain("Response Recorded");
        response.Body.Should().Contain("NO");
    }

    [Fact]
    public async Task FunctionHandler_InvalidTokenException_ReturnsExpiredHtmlWithOwnerEmail()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@test.com");
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "expired-token" },
                { "response", "yes" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new InvalidTokenException("Token has expired"));

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
        response.Body.Should().Contain("This Link Has Expired");
        response.Body.Should().Contain("owner@test.com");
        response.Body.Should().Contain("mailto:owner@test.com");
        response.Body.Should().Contain("lang=\"en\"");
        response.Body.Should().Contain("role='alert'");
        response.Body.Should().Contain("aria-hidden='true'");

        // Cleanup
        Environment.SetEnvironmentVariable("OWNER_EMAIL", null);
    }

    [Fact]
    public async Task FunctionHandler_InvalidTokenException_FallbackToDefaultEmail()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OWNER_EMAIL", null);
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "expired-token" },
                { "response", "yes" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new InvalidTokenException("Token has expired"));

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
        response.Body.Should().Contain("support@example.com");
        response.Body.Should().Contain("mailto:support@example.com");
    }

    [Fact]
    public async Task FunctionHandler_InvalidTokenException_EncodesEmailToPreventXSS()
    {
        // Arrange
        Environment.SetEnvironmentVariable("OWNER_EMAIL", "<script>alert('xss')</script>@test.com");
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "expired-token" },
                { "response", "yes" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new InvalidTokenException("Token has expired"));

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().NotContain("<script>");
        response.Body.Should().Contain("&lt;script&gt;");
        response.Body.Should().Contain("&lt;/script&gt;");

        // Cleanup
        Environment.SetEnvironmentVariable("OWNER_EMAIL", null);
    }

    [Fact]
    public async Task FunctionHandler_MissingToken_ReturnsBadRequest()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "response", "yes" }
            }
        };

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("Missing task token");
    }

    [Fact]
    public async Task FunctionHandler_MissingResponse_ReturnsBadRequest()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "valid-token" }
            }
        };

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("Missing response");
    }

    [Fact]
    public async Task FunctionHandler_InvalidResponse_ReturnsBadRequest()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "valid-token" },
                { "response", "maybe" }
            }
        };

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(400);
        response.Body.Should().Contain("Invalid response");
    }

    [Fact]
    public async Task FunctionHandler_TokenWithSpaces_ReplacesWithPlus()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "token with spaces" },
                { "response", "yes" }
            }
        };

        SendTaskSuccessRequest? capturedRequest = null;
        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .Callback<SendTaskSuccessRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(new SendTaskSuccessResponse());

        // Act
        await function.FunctionHandler(request, _context);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.TaskToken.Should().Be("token+with+spaces");
    }

    [Fact]
    public async Task FunctionHandler_SendTaskSuccessException_EncodesErrorMessage()
    {
        // Arrange
        var function = new Function(_mockStepFunctions.Object);
        var request = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                { "token", "valid-token" },
                { "response", "yes" }
            }
        };

        _mockStepFunctions
            .Setup(x => x.SendTaskSuccessAsync(It.IsAny<SendTaskSuccessRequest>(), default))
            .ThrowsAsync(new Exception("<script>alert('error')</script>"));

        // Act
        var response = await function.FunctionHandler(request, _context);

        // Assert
        response.StatusCode.Should().Be(500);
        response.Headers["Content-Type"].Should().Be("text/html; charset=utf-8");
        response.Body.Should().NotContain("<script>");
        response.Body.Should().Contain("&lt;script&gt;");
    }
}
