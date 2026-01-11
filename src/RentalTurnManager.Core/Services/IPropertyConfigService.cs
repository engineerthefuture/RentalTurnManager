/************************
 * Rental Turn Manager
 * IPropertyConfigService.cs
 * 
 * Interface for property configuration service that resolves platform-specific
 * property IDs to internal property records. Defines contract for property
 * lookup and configuration management.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

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
