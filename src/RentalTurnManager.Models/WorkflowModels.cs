namespace RentalTurnManager.Models;

/// <summary>
/// Input for Step Functions cleaner workflow
/// </summary>
public class CleanerWorkflowInput
{
    public Booking Booking { get; set; } = new();
    public PropertyConfiguration Property { get; set; } = new();
    public DateTime CleaningDateTime { get; set; }
    public int CurrentCleanerIndex { get; set; }
    public int AttemptCount { get; set; }
    public string? LastResponse { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
}

/// <summary>
/// State for cleaner workflow execution
/// </summary>
public class CleanerWorkflowState
{
    public CleanerWorkflowInput Input { get; set; } = new();
    public CleanerContact? CurrentCleaner { get; set; }
    public bool CleanerConfirmed { get; set; }
    public bool AllCleanersExhausted { get; set; }
    public string? EmailSent { get; set; }
    public string? CalendarInviteSent { get; set; }
    public List<string> ContactedCleaners { get; set; } = new();
}

/// <summary>
/// Message templates configuration
/// </summary>
public class MessageTemplates
{
    public Dictionary<string, MessageTemplate> Templates { get; set; } = new();
}

/// <summary>
/// Individual message template
/// </summary>
public class MessageTemplate
{
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Location { get; set; }
}
