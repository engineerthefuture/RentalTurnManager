/************************
 * Rental Turn Manager
 * BookingParserServiceTests.cs
 * 
 * Unit tests for BookingParserService. Tests parsing of booking information
 * from Airbnb, VRBO, and Booking.com emails including confirmation codes,
 * dates, guest counts, and property IDs.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class BookingParserServiceTests
{
    private readonly Mock<ILogger<BookingParserService>> _mockLogger;
    private readonly BookingParserService _service;

    public BookingParserServiceTests()
    {
        _mockLogger = new Mock<ILogger<BookingParserService>>();
        _service = new BookingParserService(_mockLogger.Object);
    }

    [Fact]
    public void ParseBooking_AirbnbEmail_ReturnsBooking()
    {
        // Arrange
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Reservation Number: HM123456789
                Guest: John Smith
                Listing: 12345678
                Check-in: 01/15/2026
                Check-out: 01/18/2026
                2 guests
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HM123456789");
        result.PropertyId.Should().Be("12345678");
        result.CheckInDate.Should().Be(new DateTime(2026, 1, 15));
        result.CheckOutDate.Should().Be(new DateTime(2026, 1, 18));
        result.NumberOfGuests.Should().Be(2);
    }

    [Fact]
    public void ParseBooking_VrboEmail_ReturnsBooking()
    {
        // Arrange
        var email = new EmailMessage
        {
            From = "noreply@vrbo.com",
            Subject = "Reservation Confirmation",
            Body = @"
                Confirmation Number: 98765432
                Property: 87654321
                Arrival: January 20, 2026
                Departure: January 23, 2026
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("vrbo");
        result.BookingReference.Should().Be("98765432");
        result.PropertyId.Should().Be("87654321");
    }

    [Fact]
    public void ParseBooking_BookingComEmail_ReturnsBooking()
    {
        // Arrange
        var email = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking Confirmation",
            Body = @"
                Booking ID: 7654321098
                Property: 11223344
                Guest Name: Jane Doe
                Check-in: Monday, 25 January 2026
                Check-out: Thursday, 28 January 2026
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("bookingcom");
        result.BookingReference.Should().Be("7654321098");
        result.GuestName.Should().Be("Jane Doe");
    }

    [Fact]
    public void ParseBooking_InvalidEmail_ReturnsNull()
    {
        // Arrange
        var email = new EmailMessage
        {
            From = "unknown@example.com",
            Subject = "Some email",
            Body = "Random content"
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseBooking_NonBookingEmail_ReturnsNull()
    {
        // Arrange
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Your listing performance",
            Body = "This is not a booking email"
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().BeNull();
    }
}
