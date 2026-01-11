using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Text.Json;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Implementation of property configuration service
/// </summary>
public class PropertyConfigService : IPropertyConfigService
{
    private readonly ILogger<PropertyConfigService> _logger;
    private readonly PropertiesConfiguration _configuration;
    private readonly List<string> _defaultFromAddresses = new() { "airbnb.com", "vrbo.com", "booking.com" };
    private readonly List<string> _defaultSubjectPatterns = new() { "Reservation confirmed", "Instant Booking from", "booking confirmation" };

    public PropertyConfigService(IConfiguration configuration, ILogger<PropertyConfigService> logger)
    {
        _logger = logger;
        
        // Load properties configuration
        var propertiesSection = configuration.GetSection("properties");
        var emailFiltersSection = configuration.GetSection("emailFilters");
        
        _configuration = new PropertiesConfiguration
        {
            Properties = propertiesSection.Get<List<PropertyConfiguration>>() ?? new List<PropertyConfiguration>(),
            EmailFilters = emailFiltersSection.Get<EmailFilterConfiguration>()
        };

        _logger.LogInformation($"Loaded {_configuration.Properties.Count} property configurations");
    }

    public PropertyConfiguration? FindPropertyByPlatformId(string platform, string platformPropertyId)
    {
        var normalizedPlatform = platform.ToLower() switch
        {
            "airbnb" => "airbnb",
            "vrbo" => "vrbo",
            "bookingcom" or "booking.com" => "bookingcom",
            _ => platform.ToLower()
        };

        var property = _configuration.Properties.FirstOrDefault(p =>
            p.PlatformIds.TryGetValue(normalizedPlatform, out var id) && 
            id.Equals(platformPropertyId, StringComparison.OrdinalIgnoreCase)
        );

        if (property != null)
        {
            _logger.LogInformation($"Found property {property.PropertyId} for {platform} listing {platformPropertyId}");
        }
        else
        {
            _logger.LogWarning($"No property found for {platform} listing {platformPropertyId}");
        }

        return property;
    }

    public List<PropertyConfiguration> GetAllProperties()
    {
        return _configuration.Properties;
    }

    public List<string> GetBookingPlatformFromAddresses()
    {
        if (_configuration?.EmailFilters?.BookingPlatformFromAddresses != null && 
            _configuration.EmailFilters.BookingPlatformFromAddresses.Any())
        {
            return _configuration.EmailFilters.BookingPlatformFromAddresses;
        }
        
        return _defaultFromAddresses;
    }

    public List<string> GetSubjectPatterns()
    {
        if (_configuration?.EmailFilters?.SubjectPatterns != null && 
            _configuration.EmailFilters.SubjectPatterns.Any())
        {
            return _configuration.EmailFilters.SubjectPatterns;
        }
        
        return _defaultSubjectPatterns;
    }
}
