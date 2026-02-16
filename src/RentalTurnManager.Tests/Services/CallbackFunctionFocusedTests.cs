using Xunit;
// Suppress nullable dereference warnings in tests where APIGatewayProxyRequest fields
// are intentionally created without full nullability annotations.
#pragma warning disable CS8602
using Moq;
using FluentAssertions;
using Amazon.StepFunctions;
using Amazon.SecretsManager;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Lambda.TestUtilities;
using RentalTurnManager.CallbackLambda;
using Amazon.Lambda.APIGatewayEvents;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RentalTurnManager.Tests.Services;

public class CallbackFunctionFocusedTests
{
    [Fact]
    public async Task OwnerOverride_MissingParameters_Returns400()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.StartExecutionAsync(It.IsAny<Amazon.StepFunctions.Model.StartExecutionRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.StepFunctions.Model.StartExecutionResponse { ExecutionArn = "arn:exec" });
        var secretMock = new Mock<IAmazonSecretsManager>();
        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                // intentionally omit required parameters
                ["ownerToken"] = "tok",
                ["action"] = "schedule"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Missing required parameters");
    }

    [Fact]
    public async Task OwnerOverride_InvalidToken_Returns401()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "correct-token" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");

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

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(401);
        res.Body.Should().Contain("Unauthorized Access");
    }

    [Fact]
    public async Task CancelCleaning_MinimalBooking_Returns200_And_PutObjectCalled()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "ctok" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var bookingJson = "{}";
        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
        };

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
        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@example.com");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "ctok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");

        s3Mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task CancelCleaning_ScheduledBooking_InvokesCalendarForCleanerAndOwner()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "ctok2" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var bookingObj = new Dictionary<string, object>
        {
            ["AssignedCleanerName"] = "Cleaner One",
            ["AssignedCleanerEmail"] = "cleaner@example.com",
            ["WorkflowPropertyId"] = "prop-1",
            ["OwnerName"] = "Owner One",
            ["ScheduledCleaningTime"] = "2026-02-26T10:00:00+00:00"
        };

        var bookingJson = JsonSerializer.Serialize(bookingObj);
        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getObjResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var lambdaMock = new Mock<Amazon.Lambda.IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<Amazon.Lambda.Model.InvokeRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.Lambda.Model.InvokeResponse { StatusCode = 200 });

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        // Inject mocked lambda client via reflection
        var lambdaField = typeof(RentalTurnManager.CallbackLambda.Function).GetField("_lambdaClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lambdaField.SetValue(fn, lambdaMock.Object);

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");
        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@example.com");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "ctok2",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");

        s3Mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task OwnerOverride_Cancel_ReturnsCancelledHtml()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "otok" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "otok",
                ["action"] = "cancel",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Owner Override Successful");
        res.Body.Should().Contain("cancelled");
    }

    [Fact]
    public async Task CancelCleaning_NoCleaner_SendsOwnerOnly()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "ctok3" }) });

        var s3Mock = new Mock<IAmazonS3>();

        // Booking without cleaner and without scheduled time
        var bookingObj = new Dictionary<string, object>
        {
            ["WorkflowPropertyId"] = "prop-2",
            ["OwnerName"] = "Owner Two"
        };

        var bookingJson = JsonSerializer.Serialize(bookingObj);
        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getObjResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var lambdaMock = new Mock<Amazon.Lambda.IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<Amazon.Lambda.Model.InvokeRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.Lambda.Model.InvokeResponse { StatusCode = 200 });

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        // Inject mocked lambda client via reflection
        var lambdaField = typeof(RentalTurnManager.CallbackLambda.Function).GetField("_lambdaClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lambdaField.SetValue(fn, lambdaMock.Object);

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner2@example.com");
        System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "ctok3",
                ["bookingRef"] = "b2",
                ["platform"] = "airbnb",
                ["propertyId"] = "p2"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");

        s3Mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
    }

    [Fact]
    public async Task OwnerOverride_PropertyAsEscapedString_ParsesCleanerAndSchedules()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.StartExecutionAsync(It.IsAny<Amazon.StepFunctions.Model.StartExecutionRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.StepFunctions.Model.StartExecutionResponse { ExecutionArn = "arn:exec" });

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "otok2" }) });


        var s3Mock = new Mock<IAmazonS3>();

        // workflow context where 'property' is an escaped JSON string containing cleaners
        var workflow = new Dictionary<string, object>
        {
            ["property"] = System.Text.Json.JsonSerializer.Serialize(new { cleaners = new[] { new { cleanerId = "c123" } } })
        };

        var workflowJson = JsonSerializer.Serialize(workflow);
        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowJson))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(getObjResp);
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getObjResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var lambdaMock = new Mock<Amazon.Lambda.IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<Amazon.Lambda.Model.InvokeRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.Lambda.Model.InvokeResponse { StatusCode = 200 });

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var lambdaField = typeof(RentalTurnManager.CallbackLambda.Function).GetField("_lambdaClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lambdaField.SetValue(fn, lambdaMock.Object);

        var prevEmailSecret = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
        var prevBookingBucket = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
        var prevCalendarLambda = System.Environment.GetEnvironmentVariable("CALENDAR_LAMBDA_NAME");
        var prevOwnerEmail = System.Environment.GetEnvironmentVariable("OWNER_EMAIL");
        var prevStateMachine = System.Environment.GetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN");

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");
        System.Environment.SetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN", "arn:exec");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "otok2",
                ["action"] = "schedule",
                ["cleanerId"] = "c123",
                ["propertyId"] = "p1",
                ["bookingRef"] = "bx"
            }
        };
        APIGatewayProxyResponse res;
        try
        {
            res = await fn.FunctionHandler(req, ctx);

            res.StatusCode.Should().Be(200);
            stepMock.Verify(x => x.StartExecutionAsync(It.IsAny<Amazon.StepFunctions.Model.StartExecutionRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", prevEmailSecret);
            System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", prevBookingBucket);
            System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", prevCalendarLambda);
            System.Environment.SetEnvironmentVariable("OWNER_EMAIL", prevOwnerEmail);
            System.Environment.SetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN", prevStateMachine);
        }
    }

    [Fact]
    public async Task CancelCleaning_Scheduled_WithCleaner_InvokesCleanerAndOwnerCalendars()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<Amazon.SecretsManager.Model.GetSecretValueRequest>(), default))
            .ReturnsAsync(new Amazon.SecretsManager.Model.GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "ctok4" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var bookingObj = new Dictionary<string, object>
        {
            ["WorkflowPropertyId"] = "prop-3",
            ["CleanerName"] = "Cleaner Three",
            ["CleanerEmail"] = "cleaner3@example.com",
            ["OwnerName"] = "Owner Three",
            ["ScheduledTime"] = "2026-02-26T10:00:00Z",
            ["PropertyName"] = "prop-3"
        };

        var bookingJson = JsonSerializer.Serialize(bookingObj);
        var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getObjResp);
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(getObjResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var lambdaMock = new Mock<Amazon.Lambda.IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<Amazon.Lambda.Model.InvokeRequest>(), It.IsAny<System.Threading.CancellationToken>()))
            .ReturnsAsync(new Amazon.Lambda.Model.InvokeResponse { StatusCode = 200 });

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var lambdaField = typeof(RentalTurnManager.CallbackLambda.Function).GetField("_lambdaClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lambdaField.SetValue(fn, lambdaMock.Object);

        var prevEmailSecret2 = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
        var prevBookingBucket2 = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
        var prevCalendarLambda2 = System.Environment.GetEnvironmentVariable("CALENDAR_LAMBDA_NAME");
        var prevOwnerEmail2 = System.Environment.GetEnvironmentVariable("OWNER_EMAIL");

        System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
        System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
        System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");
        System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner3@example.com");

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "ctok4",
                ["bookingRef"] = "b3",
                ["platform"] = "airbnb",
                ["propertyId"] = "p3"
            }
        };
        APIGatewayProxyResponse res2;
        try
        {
            res2 = await fn.FunctionHandler(req, ctx);

            res2.StatusCode.Should().Be(200);
            s3Mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.AtLeastOnce);
            lambdaMock.Verify(x => x.InvokeAsync(It.IsAny<Amazon.Lambda.Model.InvokeRequest>(), It.IsAny<System.Threading.CancellationToken>()), Times.AtLeastOnce);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", prevEmailSecret2);
            System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", prevBookingBucket2);
            System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", prevCalendarLambda2);
            System.Environment.SetEnvironmentVariable("OWNER_EMAIL", prevOwnerEmail2);
        }
    }
}
