/************************
 * Rental Turn Manager
 * IStepFunctionService.cs
 * 
 * Interface for Step Functions service that starts workflow executions for
 * cleaner coordination. Defines contract for triggering state machine
 * workflows with booking and property details.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for interacting with AWS Step Functions
/// </summary>
public interface IStepFunctionService
{
    Task<string> StartCleanerWorkflowAsync(CleanerWorkflowInput input);
}
