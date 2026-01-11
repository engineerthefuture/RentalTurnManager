namespace RentalTurnManager.Models;

/// <summary>
/// Property configuration
/// </summary>
public class PropertyConfiguration
{
    public string PropertyId { get; set; } = string.Empty;
    public Dictionary<string, string> PlatformIds { get; set; } = new();
    public string Address { get; set; } = string.Empty;
    public List<CleanerContact> Cleaners { get; set; } = new();
    public PropertyMetadata Metadata { get; set; } = new();
}

/// <summary>
/// Cleaner contact information
/// </summary>
public class CleanerContact
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int Rank { get; set; }
}

/// <summary>
/// Property metadata
/// </summary>
public class PropertyMetadata
{
    public string PropertyName { get; set; } = string.Empty;
    public int Bedrooms { get; set; }
    public double Bathrooms { get; set; }
    public string CleaningDuration { get; set; } = string.Empty;
    public string AccessInstructions { get; set; } = string.Empty;
    public string SpecialInstructions { get; set; } = string.Empty;
}

/// <summary>
/// Root configuration object
/// </summary>
public class PropertiesConfiguration
{
    public List<PropertyConfiguration> Properties { get; set; } = new();
    public EmailFilterConfiguration? EmailFilters { get; set; }
}

/// <summary>
/// Email filter configuration for booking platforms
/// </summary>
public class EmailFilterConfiguration
{
    public List<string> BookingPlatformFromAddresses { get; set; } = new()
    {
        "airbnb.com",
        "vrbo.com",
        "booking.com"
    };
    
    public List<string> SubjectPatterns { get; set; } = new()
    {
        "Reservation confirmed",
        "Instant Booking from",
        "booking confirmation"
    };
}
