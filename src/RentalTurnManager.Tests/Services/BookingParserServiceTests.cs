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

    [Fact]
    public void ParseBooking_AirbnbEmailWithSubjectDateAndGuestMessage_ExtractsCorrectly()
    {
        // Arrange - Tests that guest messages starting with "Hello" aren't extracted as property names
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed - John Dickman arrives Feb 26",
            Body = @"
                Reservation confirmed
                
                Cozy Lake House Waterfront Paradise
                
                Hello there
                
                I'd love to book your cozy home on the lake for a stay with my family
                
                Confirmation code: HMQDDDMPRY
                
                Listing: 12345678
                Check-in: 02/26/2026
                Check-out: 02/28/2026
                
                4 adults
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HMQDDDMPRY");
        result.GuestName.Should().Be("John Dickman");
        result.CheckInDate.Should().Be(new DateTime(2026, 2, 26));
        result.CheckOutDate.Should().Be(new DateTime(2026, 2, 28));
        result.NumberOfGuests.Should().Be(4);
        // Property name should be the actual property title or listing ID, not the guest message
        result.PropertyId.Should().NotContain("Hello");
        result.PropertyId.Should().NotContain("I'd love");
    }

    [Fact]
    public void ParseBooking_VrboEmailWithSubjectDateRange_ExtractsCorrectly()
    {
        // Arrange - Tests VRBO date format "Apr 3 - Apr 6, 2026" (no comma after first date)
        var email = new EmailMessage
        {
            From = "sender@messages.homeaway.com",
            Subject = "Instant Booking from Sara Moriarty: Apr 3 - Apr 6, 2026 - Vrbo #4906384",
            Body = @"
                Your booking is confirmed
                
                Property #4906384
                Unit unit_5480548
                Reservation ID HA-25496K
                Dates Apr 3 - Apr 6, 2026, 3 nights
                Guests 2 adults, 1 child
                Traveler Name Sara Moriarty
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("vrbo");
        result.BookingReference.Should().Be("HA-25496K");
        result.PropertyId.Should().Be("4906384");
        result.GuestName.Should().Be("Sara Moriarty");
        result.CheckInDate.Should().Be(new DateTime(2026, 4, 3));
        result.CheckOutDate.Should().Be(new DateTime(2026, 4, 6));
        result.NumberOfGuests.Should().Be(3);
    }}