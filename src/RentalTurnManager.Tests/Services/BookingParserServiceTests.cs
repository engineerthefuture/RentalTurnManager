using System;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class BookingParserServiceTests
{
    [Fact]
    public void ParseBooking_ReturnsNull_ForUnknownPlatform()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "no-reply@example.org",
            Subject = "Monthly Newsletter",
            Body = "Welcome to our newsletter"
        };

        var result = svc.ParseBooking(email);
        Assert.Null(result);
    }

    [Fact]
    public void ParseAirbnb_WithNumericDatesAndGuests_ParsesCorrectly()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "noreply@airbnb.com",
            Subject = "Reservation confirmed",
            Body = "Confirmation code: HM12345678\nListing #12345\ncheck-in: 01/15/2026\ncheck-out: 01/18/2026\nGuests: 2 adults, 1 child"
        };

        var booking = svc.ParseBooking(email);

        Assert.NotNull(booking);
        Assert.Equal("airbnb", booking!.Platform);
        Assert.Equal("HM12345678", booking.BookingReference);
        Assert.Equal("12345", booking.PropertyId);
        Assert.Equal(new DateTime(2026,1,15), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,1,18), booking.CheckOutDate.Date);
        Assert.Equal(3, booking.NumberOfGuests);
    }

    [Fact]
    public void ParseAirbnb_CalculatesCheckoutFromNights()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "noreply@airbnb.com",
            Subject = "Reservation confirmed",
            Body = "Confirmation code: HMABCDEFG1\nListing #5555\ncheck-in: January 10, 2026\n3 nights"
        };

        var booking = svc.ParseBooking(email);

        Assert.NotNull(booking);
        Assert.Equal("5555", booking!.PropertyId);
        Assert.Equal(new DateTime(2026,1,10), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,1,13), booking.CheckOutDate.Date);
    }

    [Fact]
    public void ParseVrbo_FromSubjectRangeAndProperty_ParsesDatesAndProperty()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "bookings@vrbo.com",
            Subject = "Dec 31, 2025 - Jan 2, 2026 | Vrbo #4906384",
            Body = "Reservation ID: HA-T65Q42\nGuests: 2 adults, 0 children"
        };

        var booking = svc.ParseBooking(email);

        Assert.NotNull(booking);
        Assert.Equal("vrbo", booking!.Platform);
        Assert.Equal("HA-T65Q42", booking.BookingReference);
        Assert.Equal("4906384", booking.PropertyId);
        Assert.Equal(new DateTime(2025,12,31), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,1,2), booking.CheckOutDate.Date);
        Assert.Equal(2, booking.NumberOfGuests);
    }

    [Fact]
    public void ParseBookingCom_BasicParsing_Works()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "confirmation@booking.com",
            Subject = "Your reservation",
            Body = "Booking number: 987654\nCheck-in: January 20, 2026\nCheck-out: January 23, 2026\nProperty: 77777\nGuest name: John Doe"
        };

        var booking = svc.ParseBooking(email);

        Assert.NotNull(booking);
        Assert.Equal("bookingcom", booking!.Platform);
        Assert.Equal("987654", booking.BookingReference);
        Assert.Equal(new DateTime(2026,1,20), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,1,23), booking.CheckOutDate.Date);
        Assert.Equal("77777", booking.PropertyId);
        Assert.Equal("John Doe", booking.GuestName);
    }

    [Fact]
    public void DeterminePlatform_InstantBookingSubject_IsAirbnb()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "noreply@airbnb.com",
            Subject = "Instant Booking from HostName",
            Body = "check-in: March 5, 2026\ncheck-out: March 7, 2026"
        };

        var booking = svc.ParseBooking(email);
        Assert.NotNull(booking);
        Assert.Equal("airbnb", booking!.Platform);
        Assert.Equal(new DateTime(2026,3,5), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,3,7), booking.CheckOutDate.Date);
    }

    [Fact]
    public void ParseAirbnb_UsesHtmlBody_WhenPlainBodyEmpty()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "noreply@airbnb.com",
            Subject = "Reservation confirmed",
            Body = string.Empty,
            HtmlBody = "<p>Confirmation code: HTML123</p><p>check-in: 04/10/2026</p><p>check-out: 04/12/2026</p>"
        };

        var booking = svc.ParseBooking(email);
        Assert.NotNull(booking);
        Assert.Equal("HTML123", booking!.BookingReference);
        Assert.Equal(new DateTime(2026,4,10), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,4,12), booking.CheckOutDate.Date);
    }

    [Fact]
    public void ParseVrbo_WithSubjectDateRange_ParsesDatesAndPropertyId()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "bookings@vrbo.com",
            Subject = "Feb 1, 2026 - Feb 3, 2026 | Vrbo #123456",
            Body = "Reservation ID: VR-ABC-1\nGuests: 1 adult"
        };

        var booking = svc.ParseBooking(email);
        Assert.NotNull(booking);
        Assert.Equal("vrbo", booking!.Platform);
        Assert.Equal(new DateTime(2026,2,1), booking.CheckInDate.Date);
        Assert.Equal(new DateTime(2026,2,3), booking.CheckOutDate.Date);
        Assert.Equal("123456", booking.PropertyId);
    }
}
using System;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class BookingParserServiceTests
{
    [Fact]
    public void ParseBooking_ReturnsNull_ForUnknownPlatform()
    {
        var logger = new Mock<ILogger<BookingParserService>>();
        var svc = new BookingParserService(logger.Object);

        var email = new EmailMessage
        {
            From = "no-reply@example.org",
            Subject = "Monthly Newsletter",
            using System;
            using Xunit;
            using Moq;
            using Microsoft.Extensions.Logging;
            using RentalTurnManager.Core.Services;
            using RentalTurnManager.Models;

            namespace RentalTurnManager.Tests.Services;

            public class BookingParserServiceTests
            {
                [Fact]
                public void ParseBooking_ReturnsNull_ForUnknownPlatform()
                {
                    var logger = new Mock<ILogger<BookingParserService>>();
                    var svc = new BookingParserService(logger.Object);

                    var email = new EmailMessage
                    {
                        From = "no-reply@example.org",
                        Subject = "Monthly Newsletter",
                        Body = "Welcome to our newsletter"
                    };

                    var result = svc.ParseBooking(email);
                    Assert.Null(result);
                }

                [Fact]
                public void ParseAirbnb_WithNumericDatesAndGuests_ParsesCorrectly()
                {
                    var logger = new Mock<ILogger<BookingParserService>>();
                    var svc = new BookingParserService(logger.Object);

                    var email = new EmailMessage
                    {
                        From = "noreply@airbnb.com",
                        Subject = "Reservation confirmed",
                        Body = "Confirmation code: HM12345678\nListing #12345\ncheck-in: 01/15/2026\ncheck-out: 01/18/2026\nGuests: 2 adults, 1 child"
                    };

                    var booking = svc.ParseBooking(email);

                    Assert.NotNull(booking);
                    Assert.Equal("airbnb", booking!.Platform);
                    Assert.Equal("HM12345678", booking.BookingReference);
                    Assert.Equal("12345", booking.PropertyId);
                    Assert.Equal(new DateTime(2026,1,15), booking.CheckInDate.Date);
                    Assert.Equal(new DateTime(2026,1,18), booking.CheckOutDate.Date);
                    Assert.Equal(3, booking.NumberOfGuests);
                }

                [Fact]
                public void ParseAirbnb_CalculatesCheckoutFromNights()
                {
                    var logger = new Mock<ILogger<BookingParserService>>();
                    var svc = new BookingParserService(logger.Object);

                    var email = new EmailMessage
                    {
                        From = "noreply@airbnb.com",
                        Subject = "Reservation confirmed",
                        Body = "Confirmation code: HMABCDEFG1\nListing #5555\ncheck-in: January 10, 2026\n3 nights"
                    };

                    var booking = svc.ParseBooking(email);

                    Assert.NotNull(booking);
                    Assert.Equal("5555", booking!.PropertyId);
                    Assert.Equal(new DateTime(2026,1,10), booking.CheckInDate.Date);
                    Assert.Equal(new DateTime(2026,1,13), booking.CheckOutDate.Date);
                }

                [Fact]
                public void ParseVrbo_FromSubjectRangeAndProperty_ParsesDatesAndProperty()
                {
                    var logger = new Mock<ILogger<BookingParserService>>();
                    var svc = new BookingParserService(logger.Object);

                    var email = new EmailMessage
                    {
                        From = "bookings@vrbo.com",
                        Subject = "Dec 31, 2025 - Jan 2, 2026 | Vrbo #4906384",
                        Body = "Reservation ID: HA-T65Q42\nGuests: 2 adults, 0 children"
                    };

                    var booking = svc.ParseBooking(email);

                    Assert.NotNull(booking);
                    Assert.Equal("vrbo", booking!.Platform);
                    Assert.Equal("HA-T65Q42", booking.BookingReference);
                    Assert.Equal("4906384", booking.PropertyId);
                    Assert.Equal(new DateTime(2025,12,31), booking.CheckInDate.Date);
                    Assert.Equal(new DateTime(2026,1,2), booking.CheckOutDate.Date);
                    Assert.Equal(2, booking.NumberOfGuests);
                }

                [Fact]
                public void ParseBookingCom_BasicParsing_Works()
                {
                    var logger = new Mock<ILogger<BookingParserService>>();
                    var svc = new BookingParserService(logger.Object);

                    var email = new EmailMessage
                    {
                        From = "confirmation@booking.com",
                        Subject = "Your reservation",
                        Body = "Booking number: 987654\nCheck-in: January 20, 2026\nCheck-out: January 23, 2026\nProperty: 77777\nGuest name: John Doe"
                    };

                    var booking = svc.ParseBooking(email);

                    Assert.NotNull(booking);
                    Assert.Equal("bookingcom", booking!.Platform);
                    Assert.Equal("987654", booking.BookingReference);
                    Assert.Equal(new DateTime(2026,1,20), booking.CheckInDate.Date);
                    Assert.Equal(new DateTime(2026,1,23), booking.CheckOutDate.Date);
                    Assert.Equal("77777", booking.PropertyId);
                    Assert.Equal("John Doe", booking.GuestName);
                }
            }
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
    }

    [Fact]
    public void ParseBooking_Airbnb_SubjectArrives_ParsesCheckIn()
    {
        // Arrange - subject contains 'arrives Mar 3' without year
        var email = new EmailMessage
        {
            From = "notify@example.com",
            Subject = "Reservation confirmed - Alice arrives Mar 3",
            Body = @"
                Confirmation code: HMARRIVE123
                Listing: 22233344
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HMARRIVE123");
        // Expect year to be current year (2026)
        result.CheckInDate.Should().Be(new DateTime(DateTime.Now.Year, 3, 3));
    }

    [Fact]
    public void ParseBooking_Airbnb_NumericCheckIn_WithNights_CalculatesCheckOut()
    {
        // Arrange - numeric check-in and nights but no explicit check-out
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Reservation Number: HMNIGHT123
                Listing: 33344455
                Check-in: 03/10/2026
                2 nights
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.CheckInDate.Should().Be(new DateTime(2026, 3, 10));
        result.CheckOutDate.Should().Be(new DateTime(2026, 3, 12));
    }

    [Fact]
    public void ParseBooking_DetectsAirbnb_FromConfirmationCodeInContent()
    {
        // Arrange - From doesn't include airbnb but content has Airbnb-style code
        var email = new EmailMessage
        {
            From = "alerts@something.com",
            Subject = "Reservation confirmed - New confirmation",
            Body = @"
                confirmation code: HMACODE9999
                Listing: 99988877
                Check-in: 05/01/2026
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HMACODE9999");
    }

    [Fact]
    public void ParseBooking_IncompleteAirbnb_ReturnsNull()
    {
        // Arrange - confirmation code present but missing listing and check-in
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Confirmation code: HMNOINFO123
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ParseBooking_Airbnb_CheckIn_WordMonthParsesWithoutYear()
    {
        // Arrange - content contains "Check-in: Wed, Dec 3" style (no year)
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Confirmation code: HMWORDMON123
                Listing: 44455566
                Check-in: Wed, Dec 3
                4 nights
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HMWORDMON123");
        // Check-in year should be current year or next depending on date resolution, assert month/day
        result.CheckInDate.Month.Should().Be(12);
        result.CheckInDate.Day.Should().Be(3);
        result.CheckOutDate.Should().Be(result.CheckInDate.AddDays(4));
    }

    [Fact]
    public void ParseBooking_BookingCom_NamedDateFormats_ParsesDates()
    {
        // Arrange - booking.com style long date strings
        var email = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking Confirmation",
            Body = @"
                Booking ID: 99887766
                Property: 55443322
                Guest Name: Mary Major
                Check-in: Thursday, 3 December 2026
                Check-out: Sunday, 6 December 2026
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("bookingcom");
        result.BookingReference.Should().Be("99887766");
        result.CheckInDate.Should().Be(new DateTime(2026, 12, 3));
        result.CheckOutDate.Should().Be(new DateTime(2026, 12, 6));
    }

    [Fact]
    public void ParseBooking_Airbnb_AdultsAndChildrenSeparated_SumsCorrectly()
    {
        // Arrange - adults and children appear separately (fallback path)
        var email = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Confirmation code: HMSPLIT123
                Listing: 77788899
                Check-in: 07/10/2026
                2 adults
                1 kid
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("airbnb");
        result.BookingReference.Should().Be("HMSPLIT123");
        result.PropertyId.Should().Be("77788899");
        result.NumberOfGuests.Should().Be(3);
    }

    [Fact]
    public void ParseBooking_Vrbo_UnitIdFallback_UsesUnitIdWhenNoProperty()
    {
        // Arrange - no Property but Unit present (fallback branch)
        var email = new EmailMessage
        {
            From = "noreply@vrbo.com",
            Subject = "Reservation Confirmation",
            Body = @"
                Reservation ID: HA-9ABCDEF
                Unit: unit_1234567
                Arrival: March 5, 2026
                Departure: March 8, 2026
            "
        };

        // Act
        var result = _service.ParseBooking(email);

        // Assert
        result.Should().NotBeNull();
        result!.Platform.Should().Be("vrbo");
        result.BookingReference.Should().Be("HA-9ABCDEF");
        result.PropertyId.Should().Be("unit_1234567");
    }

        [Fact]
        public void ParseBooking_Airbnb_SubjectCodeAndListingUrl_ReturnsBooking()
        {
            // Arrange - confirmation code in subject and listing URL in body
            var email = new EmailMessage
            {
                From = "no-reply@notifications.example.com",
                Subject = "Reservation confirmed - HMQABC1234",
                Body = @"
                    Your reservation is confirmed
                    See listing: https://www.airbnb.com/rooms/12345678
                    Check-in: 03/10/2026
                    Check-out: 03/12/2026
                "
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("airbnb");
            result.BookingReference.Should().Be("HMQABC1234");
            result.PropertyId.Should().Be("12345678");
            result.CheckInDate.Should().Be(new DateTime(2026, 3, 10));
        }

        [Fact]
        public void ParseBooking_Airbnb_GuestBreakdown_AdultsAndChildren_SumsCorrectly()
        {
            // Arrange - explicit adults and children breakdown
            var email = new EmailMessage
            {
                From = "automated@airbnb.com",
                Subject = "Reservation confirmed",
                Body = @"
                    Listing: 55566677
                    Check-in: 04/01/2026
                    Check-out: 04/04/2026
                    Guests: 2 adults, 1 children
                "
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.PropertyId.Should().Be("55566677");
            result.NumberOfGuests.Should().Be(3);
        }

        [Fact]
        public void ParseBooking_Vrbo_UnitIdFallback_ExtractsUnitAsPropertyId()
        {
            // Arrange - no Property: line, but Unit is present
            var email = new EmailMessage
            {
                From = "noreply@vrbo.com",
                Subject = "Your booking is confirmed",
                Body = @"
                    Reservation ID: 11223344
                    Unit unit_9999999
                    Arrival: May 5, 2026
                    Departure: May 8, 2026
                "
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("vrbo");
            result.PropertyId.Should().Be("unit_9999999");
        }

        }