using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for interacting with AWS Step Functions
/// </summary>
public interface IStepFunctionService
{
    Task<string> StartCleanerWorkflowAsync(CleanerWorkflowInput input);
}
