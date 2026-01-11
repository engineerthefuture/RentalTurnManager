using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for managing property configurations
/// </summary>
public interface IPropertyConfigService
{
    PropertyConfiguration? FindPropertyByPlatformId(string platform, string platformPropertyId);
    List<PropertyConfiguration> GetAllProperties();
    List<string> GetBookingPlatformFromAddresses();
    List<string> GetSubjectPatterns();
}
