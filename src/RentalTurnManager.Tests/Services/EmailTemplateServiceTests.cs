using Xunit;
using FluentAssertions;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;
using System.Collections.Generic;

namespace RentalTurnManager.Tests.Services;

public class EmailTemplateServiceTests
{
    private readonly EmailTemplateService _service;

    public EmailTemplateServiceTests()
    {
        _service = new EmailTemplateService();
    }

    [Fact]
    public void GenerateTimeButtonsHtml_ReturnsEmpty_WhenNoSlots()
    {
        var result = _service.GenerateTimeButtonsHtml(new List<TimeSlot>(), "https://api.test", "token123");

        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateTimeButtonsHtml_EncodesIsoDateTime_AndIncludesCallback()
    {
        var slots = new List<TimeSlot>
        {
            new TimeSlot { Time = "10:00 AM", IsoDateTime = "2026-02-26T10:00:00Z" }
        };

        var html = _service.GenerateTimeButtonsHtml(slots, "https://example.com/callback", "tok-abc");

        html.Should().Contain("https://example.com/callback/respond?token=tok-abc&response=yes&time=");
        html.Should().Contain("10:00 AM");
        html.Should().Contain("%3a"); // encoded ':' from ISO datetime (lowercase)
    }
}
