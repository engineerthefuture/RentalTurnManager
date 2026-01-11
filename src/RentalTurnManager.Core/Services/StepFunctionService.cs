/************************
 * Rental Turn Manager
 * StepFunctionService.cs
 * 
 * Service that starts AWS Step Functions workflow executions for cleaner
 * coordination. Passes booking details and property configuration to the
 * workflow state machine for automated cleaner notification and tracking.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Text.Json;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Implementation of Step Functions service
/// </summary>
public class StepFunctionService : IStepFunctionService
{
    private readonly IAmazonStepFunctions _stepFunctions;
    private readonly ILogger<StepFunctionService> _logger;
    private readonly string _stateMachineArn;

    public StepFunctionService(IAmazonStepFunctions stepFunctions, ILogger<StepFunctionService> logger)
    {
        _stepFunctions = stepFunctions;
        _logger = logger;
        _stateMachineArn = Environment.GetEnvironmentVariable("CLEANER_WORKFLOW_STATE_MACHINE_ARN")
            ?? throw new InvalidOperationException("CLEANER_WORKFLOW_STATE_MACHINE_ARN environment variable is not set");
    }

    public async Task<string> StartCleanerWorkflowAsync(CleanerWorkflowInput input)
    {
        try
        {
            var inputJson = JsonSerializer.Serialize(input, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var executionName = $"booking-{input.Booking.BookingReference}-{DateTime.UtcNow:yyyyMMddHHmmss}";

            _logger.LogInformation($"Starting Step Functions execution: {executionName}");

            var request = new StartExecutionRequest
            {
                StateMachineArn = _stateMachineArn,
                Name = executionName,
                Input = inputJson
            };

            var response = await _stepFunctions.StartExecutionAsync(request);

            _logger.LogInformation($"Started execution: {response.ExecutionArn}");

            return response.ExecutionArn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Step Functions workflow");
            throw;
        }
    }
}
