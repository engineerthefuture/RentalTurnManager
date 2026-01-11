/************************
 * Rental Turn Manager
 * PropertyConfigServiceTests.cs
 * 
 * Unit tests for PropertyConfigService. Tests property ID resolution,
 * platform mapping lookups (Airbnb/VRBO/Booking.com), and configuration
 * management for multiple properties.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class PropertyConfigServiceTests
{
    private readonly Mock<ILogger<PropertyConfigService>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly PropertyConfigService _service;

    public PropertyConfigServiceTests()
    {
        _mockLogger = new Mock<ILogger<PropertyConfigService>>();
        
        // Create test configuration
        var properties = new Dictionary<string, string>
        {
            ["properties:0:propertyId"] = "property-001",
            ["properties:0:platformIds:airbnb"] = "AIRBNB_001",
            ["properties:0:platformIds:vrbo"] = "VRBO_001",
            ["properties:0:platformIds:bookingcom"] = "BOOKING_001",
            ["properties:0:address"] = "123 Test St",
            ["properties:0:metadata:propertyName"] = "Test Property",
            ["properties:1:propertyId"] = "property-002",
            ["properties:1:platformIds:airbnb"] = "AIRBNB_002",
            ["properties:1:address"] = "456 Test Ave"
        };

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(properties!)
            .Build();

        _service = new PropertyConfigService(_configuration, _mockLogger.Object);
    }

    [Theory]
    [InlineData("airbnb", "AIRBNB_001", "property-001")]
    [InlineData("vrbo", "VRBO_001", "property-001")]
    [InlineData("bookingcom", "BOOKING_001", "property-001")]
    [InlineData("airbnb", "AIRBNB_002", "property-002")]
    public void FindPropertyByPlatformId_ValidMapping_ReturnsProperty(string platform, string platformId, string expectedPropertyId)
    {
        // Act
        var result = _service.FindPropertyByPlatformId(platform, platformId);

        // Assert
        result.Should().NotBeNull();
        result!.PropertyId.Should().Be(expectedPropertyId);
    }

    [Fact]
    public void FindPropertyByPlatformId_InvalidPlatformId_ReturnsNull()
    {
        // Act
        var result = _service.FindPropertyByPlatformId("airbnb", "INVALID_ID");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void FindPropertyByPlatformId_InvalidPlatform_ReturnsNull()
    {
        // Act
        var result = _service.FindPropertyByPlatformId("invalid", "AIRBNB_001");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GetAllProperties_ReturnsAllProperties()
    {
        // Act
        var result = _service.GetAllProperties();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.PropertyId == "property-001");
        result.Should().Contain(p => p.PropertyId == "property-002");
    }
}
