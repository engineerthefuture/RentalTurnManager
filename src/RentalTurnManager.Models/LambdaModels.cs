namespace RentalTurnManager.Models;

/// <summary>
/// Lambda function request input
/// </summary>
public class LambdaRequest
{
    /// <summary>
    /// Optional: Specify a date range to scan
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Optional: Specify end date for scanning
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// Optional: Force rescan of already processed emails
    /// </summary>
    public bool ForceRescan { get; set; }
}

/// <summary>
/// Lambda function response output
/// </summary>
public class LambdaResponse
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public int BookingsProcessed { get; set; }
    public int WorkflowsStarted { get; set; }
    public List<string> Errors { get; set; } = new();
}
