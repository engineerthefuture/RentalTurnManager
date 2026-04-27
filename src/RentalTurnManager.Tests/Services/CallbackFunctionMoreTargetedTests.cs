using Xunit;
// Suppress nullable dereference warnings in tests where APIGatewayProxyRequest fields
// are intentionally created without full nullability annotations.
#pragma warning disable CS8602
using Moq;
using FluentAssertions;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Lambda.TestUtilities;
using RentalTurnManager.CallbackLambda;
using Amazon.Lambda.APIGatewayEvents;
using System.Text.Json;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;

namespace RentalTurnManager.Tests.Services;

public class CallbackFunctionMoreTargetedTests
{
    [Fact]
    public async Task OwnerOverride_Schedule_PropertyAsObject_StartsExecution()
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

        // Workflow context where property is an object (not an escaped string)
        var actualWorkflowObj = JsonSerializer.Serialize(new
        {
            property = new { cleaners = new[] { new { cleanerId = "c1" } } }
        });

        using var getObjResp = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(actualWorkflowObj))
        };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(getObjResp);

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        var prevEmailSecret = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
        var prevBookingBucket = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
        var prevStateMachineArn = System.Environment.GetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN");

        try
        {
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
        finally
        {
            System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", prevEmailSecret);
            System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", prevBookingBucket);
            System.Environment.SetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN", prevStateMachineArn);
        }
    }

    [Fact]
    public async Task CancelCleaning_AssignedCleaner_UpdatesS3_InvokesCalendarLambda()
    {
        var stepMock = new Mock<IAmazonStepFunctions>();
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tokx" }) });

        var s3Mock = new Mock<IAmazonS3>();

        var bookingJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["AssignedCleanerName"] = "Jane Cleaner",
            ["AssignedCleanerEmail"] = "jane@example.com",
            ["WorkflowPropertyId"] = "prop-123",
            ["OwnerName"] = "Owner One"
        });

        using var getResp = new GetObjectResponse { ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson)) };

        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(getResp);

        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var fn = new RentalTurnManager.CallbackLambda.Function(stepMock.Object, secretMock.Object, s3Mock.Object);
        var ctx = new TestLambdaContext();

        // Replace private readonly _lambdaClient with a mock to avoid network calls
        var lambdaMock = new Mock<IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse { StatusCode = 200, Payload = new MemoryStream(Encoding.UTF8.GetBytes("{}")) });

        var fi = typeof(RentalTurnManager.CallbackLambda.Function).GetField("_lambdaClient", BindingFlags.Instance | BindingFlags.NonPublic);
        fi.SetValue(fn, lambdaMock.Object);

        var prevEmailSecret2 = System.Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME");
        var prevBookingBucket2 = System.Environment.GetEnvironmentVariable("BOOKING_STATE_BUCKET");
        var prevCalendarLambda = System.Environment.GetEnvironmentVariable("CALENDAR_LAMBDA_NAME");
        var prevOwnerEmail = System.Environment.GetEnvironmentVariable("OWNER_EMAIL");

        try
        {
            System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", "secret-name");
            System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", "bucket-name");
            System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", "CalendarLambda");
            System.Environment.SetEnvironmentVariable("OWNER_EMAIL", "owner@example.com");

            var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tokx",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1",
                ["cleanerId"] = "cleaner-1"
            }
        };

            var res = await fn.FunctionHandler(req, ctx);

            res.StatusCode.Should().Be(200);
            res.Body.Should().Contain("Cleaning Cancelled Successfully");
            s3Mock.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default), Times.Once);
            lambdaMock.Verify(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default), Times.AtLeastOnce);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("EMAIL_SECRET_NAME", prevEmailSecret2);
            System.Environment.SetEnvironmentVariable("BOOKING_STATE_BUCKET", prevBookingBucket2);
            System.Environment.SetEnvironmentVariable("CALENDAR_LAMBDA_NAME", prevCalendarLambda);
            System.Environment.SetEnvironmentVariable("OWNER_EMAIL", prevOwnerEmail);
        }
    }
}
