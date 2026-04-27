using Xunit;
// Suppress nullable dereference warnings in tests where fields are accessed via reflection
// and APIGatewayProxyRequest fields are created without full nullability annotations.
#pragma warning disable CS8602
#pragma warning disable CS8604
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
using System.Net;

namespace RentalTurnManager.Tests.Services;

/// <summary>
/// Tests targeting specific uncovered branches in CallbackLambda to push coverage above 80%.
/// Gaps identified: HandleCleaningCancellation (65.4%/73.5%), HandleOwnerOverride (64.2%/61.5%),
/// default constructor (0%), EmailSecret.Port/UseSsl getters (0%).
/// </summary>
public class CallbackFunctionCoverageTests
{
    // Helper that sets env vars and restores originals on dispose.
    private static IDisposable TempEnvVars(Dictionary<string, string> vars)
    {
        var originals = new Dictionary<string, string?>();
        foreach (var (k, v) in vars)
        {
            originals[k] = System.Environment.GetEnvironmentVariable(k);
            System.Environment.SetEnvironmentVariable(k, v);
        }
        return new EnvVarRestorer(originals);
    }

    private sealed class EnvVarRestorer(Dictionary<string, string?> originals) : IDisposable
    {
        public void Dispose()
        {
            foreach (var (k, v) in originals)
                System.Environment.SetEnvironmentVariable(k, v);
        }
    }

    // -------------------------------------------------------------------------
    // Default constructor and EmailSecret getters
    // -------------------------------------------------------------------------

    [Fact]
    public void DefaultConstructor_CreatesInstance()
    {
        // The default constructor creates real AWS SDK clients (no network calls made).
        var fn = new RentalTurnManager.CallbackLambda.Function();
        fn.Should().NotBeNull();
    }

    [Fact]
    public void EmailSecret_PortAndUseSsl_Getters_ReturnStoredValues()
    {
        var secret = new EmailSecret { Port = 587, UseSsl = true };
        secret.Port.Should().Be(587);
        secret.UseSsl.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // HandleCleaningCancellation - uncovered branches
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelCleaning_MissingRequiredParams_Returns400()
    {
        // Missing platform and propertyId
        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            new Mock<IAmazonSecretsManager>().Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1"
                // missing platform and propertyId
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(400);
        res.Body.Should().Contain("Missing required parameters");
    }

    [Fact]
    public async Task CancelCleaning_MissingEmailSecretName_Returns500()
    {
        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            new Mock<IAmazonSecretsManager>().Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        // Ensure EMAIL_SECRET_NAME is absent for this test
        using var _ = TempEnvVars(new Dictionary<string, string> { ["EMAIL_SECRET_NAME"] = "" });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Server configuration error");
    }

    [Fact]
    public async Task CancelCleaning_SecretHasNullOwnerToken_Returns500()
    {
        // Secret JSON has no OwnerOverrideToken field → secret?.OwnerOverrideToken == null
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = "{}" });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string> { ["EMAIL_SECRET_NAME"] = "secret-name" });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Server configuration error");
    }

    [Fact]
    public async Task CancelCleaning_MissingBookingBucket_Returns500()
    {
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "valid-tok" })
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = ""
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "valid-tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Server configuration error");
    }

    [Fact]
    public async Task CancelCleaning_BookingDeserializesToNull_Returns500()
    {
        // S3 returns the JSON literal "null" → JsonSerializer returns null Dictionary
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("null"))
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Failed to retrieve booking information");
    }

    [Fact]
    public async Task CancelCleaning_InvalidTimezone_FallsBackToUtcAndSucceeds()
    {
        // Booking contains an unknown timezone → TimeZoneNotFoundException → UTC fallback → 200
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var bookingJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Timezone"] = "Invalid/NonExistent/Timezone",
            ["CleaningDuration"] = "PT3H",
            ["ScheduledCleaningTime"] = DateTime.UtcNow.AddDays(1).ToString("o")
        });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
            });
        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);

        var lambdaMock = new Mock<IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse
            {
                StatusCode = 200,
                Payload = new MemoryStream(Encoding.UTF8.GetBytes("{}"))
            });
        typeof(RentalTurnManager.CallbackLambda.Function)
            .GetField("_lambdaClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fn, lambdaMock.Object);

        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket",
            ["CALENDAR_LAMBDA_NAME"] = "CalendarLambda",
            ["OWNER_EMAIL"] = "owner@example.com"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");
    }

    [Fact]
    public async Task CancelCleaning_CalendarLambdaFunctionError_ContinuesAndReturns200()
    {
        // CalendarLambda returns FunctionError → caught by inner catch → cancellation still succeeds
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var bookingJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["AssignedCleanerName"] = "Jane",
            ["AssignedCleanerEmail"] = "jane@test.com",
            ["WorkflowPropertyId"] = "Beachhouse",
            ["OwnerName"] = "Bob",
            ["Timezone"] = "America/New_York",
            ["CleaningDuration"] = "PT3H",
            ["ScheduledCleaningTime"] = DateTime.UtcNow.AddDays(2).ToString("o")
        });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(bookingJson))
            });
        s3Mock
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);

        var lambdaMock = new Mock<IAmazonLambda>();
        lambdaMock
            .Setup(x => x.InvokeAsync(It.IsAny<InvokeRequest>(), default))
            .ReturnsAsync(new InvokeResponse
            {
                StatusCode = 200,
                FunctionError = "Unhandled",
                Payload = new MemoryStream(Encoding.UTF8.GetBytes("{}"))
            });
        typeof(RentalTurnManager.CallbackLambda.Function)
            .GetField("_lambdaClient", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fn, lambdaMock.Object);

        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket",
            ["CALENDAR_LAMBDA_NAME"] = "CalendarLambda",
            ["OWNER_EMAIL"] = "owner@example.com"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Cleaning Cancelled Successfully");
    }

    [Fact]
    public async Task CancelCleaning_S3GenericException_Returns500()
    {
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("S3 connection refused"));

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["cancelToken"] = "tok",
                ["bookingRef"] = "b1",
                ["platform"] = "airbnb",
                ["propertyId"] = "p1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Error processing cancellation");
    }

    // -------------------------------------------------------------------------
    // HandleOwnerOverride - uncovered branches
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OwnerOverride_MissingEmailSecretName_Returns500()
    {
        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            new Mock<IAmazonSecretsManager>().Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string> { ["EMAIL_SECRET_NAME"] = "" });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Server configuration error");
    }

    [Fact]
    public async Task OwnerOverride_SecretNullOwnerToken_Returns401()
    {
        // Secret JSON has no OwnerOverrideToken → secret == null || IsNullOrEmpty(token) → CreateUnauthorizedResponse()
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse { SecretString = "{}" });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string> { ["EMAIL_SECRET_NAME"] = "secret-name" });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task OwnerOverride_Schedule_MissingBookingBucket_Returns500()
    {
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            new Mock<IAmazonS3>().Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = ""
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(500);
        res.Body.Should().Contain("Server configuration error");
    }

    [Fact]
    public async Task OwnerOverride_Schedule_WorkflowNotFoundInAnyPlatform_Returns404()
    {
        // All three platform S3 lookups throw NotFound → workflowContextJson is null → 404
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var notFoundEx = new AmazonS3Exception("Not found") { StatusCode = HttpStatusCode.NotFound };
        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ThrowsAsync(notFoundEx);

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(404);
        res.Body.Should().Contain("Workflow context not found");
    }

    [Fact]
    public async Task OwnerOverride_Schedule_CleanerIdNotFound_Returns500()
    {
        // Cleaner id in request doesn't match any cleaner in the workflow context
        // → FindIndex returns -1 → throws → outer FunctionHandler catch returns 500
        // Covers the "id.GetString() != cleanerId" (right-side false) branch of the FindIndex lambda.
        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var workflowJson = JsonSerializer.Serialize(new
        {
            property = new
            {
                cleaners = new[]
                {
                    new { cleanerId = "cleaner-A", name = "Alice" },
                    new { cleanerId = "cleaner-B", name = "Bob" }
                }
            }
        });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowJson))
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            new Mock<IAmazonStepFunctions>().Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket",
            ["CLEANER_WORKFLOW_STATE_MACHINE_ARN"] = "arn:aws:states:us-east-1:123:stateMachine:test"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "cleaner-MISSING",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        // Exception is rethrown through HandleOwnerOverride and caught in FunctionHandler → 500
        res.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task OwnerOverride_Schedule_CleanerWithoutCleanerIdProp_ThenMatchingCleaner_Succeeds()
    {
        // First cleaner element has no "cleanerId" property → TryGetProperty returns false (short-circuit branch).
        // Second element matches → index found → workflow execution proceeds.
        // This covers the left-side false branch of the FindIndex lambda closure.
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default))
            .ReturnsAsync(new StartExecutionResponse { ExecutionArn = "arn:exec" });

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        // First cleaner has no cleanerId (covers TryGetProperty false → short-circuit)
        // Second cleaner has the matching cleanerId
        var workflowJson = JsonSerializer.Serialize(new
        {
            property = new
            {
                metadata = new { propertyName = "Beach House" },
                cleaners = new object[]
                {
                    new { name = "No-Id Cleaner" },            // no cleanerId property
                    new { cleanerId = "c-target", name = "Target Cleaner" }
                }
            }
        });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowJson))
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            stepMock.Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket",
            ["CLEANER_WORKFLOW_STATE_MACHINE_ARN"] = "arn:aws:states:us-east-1:123:stateMachine:test"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c-target",
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
    public async Task OwnerOverride_Schedule_WorkflowContextWithBookingMetadata_ExtractsDisplayNames()
    {
        // Workflow context includes a booking element with workflowPropertyId and AssignedCleanerName
        // (camelCase variant) — covers those optional extraction branches.
        var stepMock = new Mock<IAmazonStepFunctions>();
        stepMock
            .Setup(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default))
            .ReturnsAsync(new StartExecutionResponse { ExecutionArn = "arn:exec2" });

        var secretMock = new Mock<IAmazonSecretsManager>();
        secretMock
            .Setup(x => x.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), default))
            .ReturnsAsync(new GetSecretValueResponse
            {
                SecretString = JsonSerializer.Serialize(new { OwnerOverrideToken = "tok" })
            });

        var workflowJson = JsonSerializer.Serialize(new
        {
            property = new
            {
                cleaners = new[] { new { cleanerId = "c1", name = "Alice" } }
            },
            booking = new
            {
                workflowPropertyId = "Mountain Cabin",
                assignedCleanerName = "Alice"
            }
        });

        var s3Mock = new Mock<IAmazonS3>();
        s3Mock
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(new GetObjectResponse
            {
                ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(workflowJson))
            });

        var fn = new RentalTurnManager.CallbackLambda.Function(
            stepMock.Object,
            secretMock.Object,
            s3Mock.Object);
        var ctx = new TestLambdaContext();

        using var _ = TempEnvVars(new Dictionary<string, string>
        {
            ["EMAIL_SECRET_NAME"] = "secret-name",
            ["BOOKING_STATE_BUCKET"] = "bucket",
            ["CLEANER_WORKFLOW_STATE_MACHINE_ARN"] = "arn:aws:states:us-east-1:123:stateMachine:test"
        });

        var req = new APIGatewayProxyRequest
        {
            QueryStringParameters = new Dictionary<string, string>
            {
                ["ownerToken"] = "tok",
                ["action"] = "schedule",
                ["cleanerId"] = "c1",
                ["propertyId"] = "p1",
                ["bookingRef"] = "b1"
            }
        };

        var res = await fn.FunctionHandler(req, ctx);

        res.StatusCode.Should().Be(200);
        res.Body.Should().Contain("Mountain Cabin");
        res.Body.Should().Contain("Alice");
    }
}
