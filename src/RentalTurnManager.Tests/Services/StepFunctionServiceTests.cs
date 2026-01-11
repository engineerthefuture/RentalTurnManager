/************************
 * Rental Turn Manager
 * StepFunctionServiceTests.cs
 * 
 * Unit tests for StepFunctionService. Tests workflow execution startup,
 * parameter passing to state machines, and error handling for Step
 * Functions operations.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Xunit;
using Moq;
using FluentAssertions;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class StepFunctionServiceTests
{
    private readonly Mock<IAmazonStepFunctions> _mockStepFunctions;
    private readonly Mock<ILogger<StepFunctionService>> _mockLogger;
    private readonly StepFunctionService _service;

    public StepFunctionServiceTests()
    {
        _mockStepFunctions = new Mock<IAmazonStepFunctions>();
        _mockLogger = new Mock<ILogger<StepFunctionService>>();
        
        // Set environment variable for state machine ARN
        Environment.SetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN", "arn:aws:states:us-east-1:123456789012:stateMachine:test");
        
        _service = new StepFunctionService(_mockStepFunctions.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task StartCleanerWorkflowAsync_ValidInput_ReturnsExecutionArn()
    {
        // Arrange
        var input = new CleanerWorkflowInput
        {
            Booking = new Booking
            {
                BookingReference = "TEST123",
                Platform = "airbnb"
            },
            Property = new PropertyConfiguration
            {
                PropertyId = "property-001"
            },
            CleaningDateTime = DateTime.UtcNow.AddDays(1),
            CurrentCleanerIndex = 0,
            AttemptCount = 0
        };

        var expectedArn = "arn:aws:states:us-east-1:123456789012:execution:test:execution-id";
        var response = new StartExecutionResponse
        {
            ExecutionArn = expectedArn
        };

        _mockStepFunctions
            .Setup(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default))
            .ReturnsAsync(response);

        // Act
        var result = await _service.StartCleanerWorkflowAsync(input);

        // Assert
        result.Should().Be(expectedArn);
        _mockStepFunctions.Verify(x => x.StartExecutionAsync(
            It.Is<StartExecutionRequest>(r => 
                r.StateMachineArn == "arn:aws:states:us-east-1:123456789012:stateMachine:test" &&
                r.Name.Contains("booking-TEST123")),
            default), Times.Once);
    }

    [Fact]
    public async Task StartCleanerWorkflowAsync_StepFunctionsError_ThrowsException()
    {
        // Arrange
        var input = new CleanerWorkflowInput();

        _mockStepFunctions
            .Setup(x => x.StartExecutionAsync(It.IsAny<StartExecutionRequest>(), default))
            .ThrowsAsync(new InvalidExecutionInputException("Invalid input"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidExecutionInputException>(() => 
            _service.StartCleanerWorkflowAsync(input));
    }
}
