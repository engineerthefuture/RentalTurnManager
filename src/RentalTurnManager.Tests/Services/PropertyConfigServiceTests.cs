using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RentalTurnManager.Core.Services;
using System.Collections.Generic;

namespace RentalTurnManager.Tests.Services;

public class PropertyConfigServiceTests
{
    [Fact]
    public void GetBookingPlatformFromAddresses_ReturnsDefaults_WhenNoConfig()
    {
        var config = new ConfigurationBuilder().Build();
        var logger = new Mock<ILogger<PropertyConfigService>>().Object;

        var svc = new PropertyConfigService(config, logger);

        var addresses = svc.GetBookingPlatformFromAddresses();

        addresses.Should().Contain(new[] { "airbnb.com", "vrbo.com", "booking.com" });
    }

    [Fact]
    public void GetBookingPlatformFromAddresses_ReturnsConfigured_WhenPresent()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["emailFilters:BookingPlatformFromAddresses:0"] = "custom1.com",
            ["emailFilters:BookingPlatformFromAddresses:1"] = "custom2.com",
            ["emailFilters:SubjectPatterns:0"] = "My Subject"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var logger = new Mock<ILogger<PropertyConfigService>>().Object;

        var svc = new PropertyConfigService(config, logger);

        var addresses = svc.GetBookingPlatformFromAddresses();

        addresses.Should().Contain(new[] { "custom1.com", "custom2.com" });
    }

    [Fact]
    public void GetSubjectPatterns_ReturnsDefaults_WhenNoConfig()
    {
        var config = new ConfigurationBuilder().Build();
        var logger = new Mock<ILogger<PropertyConfigService>>().Object;

        var svc = new PropertyConfigService(config, logger);

        var patterns = svc.GetSubjectPatterns();

        patterns.Should().Contain(new[] { "Reservation confirmed", "Instant Booking from", "booking confirmation" });
    }

    [Fact]
    public void GetSubjectPatterns_ReturnsConfigured_WhenPresent()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["emailFilters:SubjectPatterns:0"] = "Pattern One",
            ["emailFilters:SubjectPatterns:1"] = "Pattern Two"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var logger = new Mock<ILogger<PropertyConfigService>>().Object;

        var svc = new PropertyConfigService(config, logger);

        var patterns = svc.GetSubjectPatterns();

        patterns.Should().Contain(new[] { "Pattern One", "Pattern Two" });
    }
}
