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
    }

    [Fact]
    public void ParseBooking_Airbnb_SubjectArrives_ParsesCheckIn()
    {
        // Arrange - subject contains 'arrives Dec 21' without year
        var email = new EmailMessage
        {
            From = "notify@example.com",
            Subject = "Reservation confirmed - Alice arrives Dec 21",
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
        result.CheckInDate.Should().Be(new DateTime(DateTime.Now.Year, 12, 21));
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

        [Fact]
        public void ParseCancellation_VrboEmail_WithPropertyIdAndReservation_ReturnsBooking()
        {
            // Arrange - VRBO cancellation email matching the example in the issue
            var email = new EmailMessage
            {
                From = "noreply@vrbo.com",
                Subject = "Booking canceled by traveler: Aug 8, 2026 - Aug 15, 2026 (Property ID 4706321) Reservation HA-W2T49E",
                Body = "Your booking has been cancelled by the traveler."
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("vrbo");
            result.BookingReference.Should().Be("HA-W2T49E");
            result.PropertyId.Should().Be("4706321");
            result.CheckInDate.Should().Be(new DateTime(2026, 8, 8));
            result.CheckOutDate.Should().Be(new DateTime(2026, 8, 15));
        }

        [Fact]
        public void ParseCancellation_VrboEmail_SameYearDateRange_ReturnsBooking()
        {
            // Arrange - VRBO cancellation with "Month Day - Month Day, Year" format
            var email = new EmailMessage
            {
                From = "sender@messages.homeaway.com",
                Subject = "Booking canceled by traveler: Apr 3 - Apr 6, 2026 (Property ID 4906384) Reservation HA-25496K",
                Body = string.Empty
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("vrbo");
            result.BookingReference.Should().Be("HA-25496K");
            result.PropertyId.Should().Be("4906384");
            result.CheckInDate.Should().Be(new DateTime(2026, 4, 3));
            result.CheckOutDate.Should().Be(new DateTime(2026, 4, 6));
        }

        [Fact]
        public void ParseCancellation_AirbnbEmail_WithConfirmationCode_ReturnsBooking()
        {
            // Arrange
            var email = new EmailMessage
            {
                From = "automated@airbnb.com",
                Subject = "Reservation canceled",
                Body = @"
                    Your reservation has been canceled.
                    Confirmation code: HMQDDDMPRY
                    Listing: 12345678
                "
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("airbnb");
            result.BookingReference.Should().Be("HMQDDDMPRY");
            result.PropertyId.Should().Be("12345678");
        }

        [Fact]
        public void ParseCancellation_BookingComEmail_WithBookingId_ReturnsBooking()
        {
            // Arrange
            var email = new EmailMessage
            {
                From = "noreply@booking.com",
                Subject = "Booking cancelled",
                Body = @"
                    Booking ID: 9988776655
                    Property: 11223344
                    The booking has been cancelled.
                "
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("bookingcom");
            result.BookingReference.Should().Be("9988776655");
            result.PropertyId.Should().Be("11223344");
        }

        [Fact]
        public void ParseCancellation_BookingCom_CanceledBookingSubject_ExtractsReference()
        {
            // Arrange - real Booking.com cancellation email format
            var email = new EmailMessage
            {
                From = "noreply@booking.com",
                Subject = "Canceled booking! (5474030366, Monday, December 21, 2026)",
                Body = @"
Cancellation — 5474030366

Dear Brent Foster,

Unfortunately, Brent Foster canceled their booking.
Following the cancellation of reservation 5474030366, we can confirm
that the guest's cancellation fees are now $0.

https://admin.booking.com/hotel/hoteladmin/extranet_ng/manage/booking.html?res_id=5474030366&hotel_id=99887766&lang=en-us
"
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("bookingcom");
            result.BookingReference.Should().Be("5474030366");
            result.PropertyId.Should().Be("99887766");
        }

        [Fact]
        public void ParseCancellation_BookingCom_CanceledBookingSubject_NoHotelIdInBody_StillExtractsReference()
        {
            // Arrange - subject has reference, body uses em-dash format but no URL
            var email = new EmailMessage
            {
                From = "noreply@booking.com",
                Subject = "Canceled booking! (9900112233, Friday, January 9, 2026)",
                Body = "Cancellation — 9900112233\nThe booking has been cancelled."
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("bookingcom");
            result.BookingReference.Should().Be("9900112233");
        }

        [Fact]
        public void ParseCancellation_BookingCom_BodyReservationPattern_ExtractsReference()
        {
            // Arrange - no "Canceled booking!" subject but body has "reservation XXXXXX"
            var email = new EmailMessage
            {
                From = "noreply@booking.com",
                Subject = "Booking cancellation notice",
                Body = "Following the cancellation of reservation 7712345678, no fees apply."
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("bookingcom");
            result.BookingReference.Should().Be("7712345678");
        }

        [Fact]
        public void ParseCancellation_UnknownPlatform_ReturnsNull()
        {
            // Arrange
            var email = new EmailMessage
            {
                From = "unknown@example.com",
                Subject = "Booking canceled",
                Body = "No platform-specific content"
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ParseCancellation_VrboEmail_NoBookingReference_ReturnsNull()
        {
            // Arrange - VRBO cancellation with no recognizable reservation ID
            var email = new EmailMessage
            {
                From = "noreply@vrbo.com",
                Subject = "Booking canceled by traveler",
                Body = "No reservation ID in this email."
            };

            // Act
            var result = _service.ParseCancellation(email);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public void ParseBooking_Airbnb_TwoColumnPlainTextLayout_ParsesCheckOutDate()
        {
            // Arrange - simulates the Airbnb host confirmation email plain-text body format:
            //   "Check-in     Checkout"
            //   "Mon, Dec 21   Thu, Dec 24"
            // The checkout date has no year and must be inferred from the check-in year.
            var email = new EmailMessage
            {
                From = "automated@airbnb.com",
                Subject = "Reservation confirmed - Ty Cheeseburger arrives Dec 21",
                Body = @"
NEW BOOKING CONFIRMED! TY ARRIVES DEC 21.

https://www.airbnb.com/rooms/1477018601970190586

Check-in     Checkout
             
Mon, Dec 21   Thu, Dec 24
             
3:00 PM      11:00 AM

GUESTS

4 adults

CONFIRMATION CODE
HMTYTZ48P9
"
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("airbnb");
            result.BookingReference.Should().Be("HMTYTZ48P9");
            result.PropertyId.Should().Be("1477018601970190586");
            result.GuestName.Should().Be("Ty Cheeseburger");
            result.CheckInDate.Should().Be(new DateTime(DateTime.Now.Year, 12, 21));
            result.CheckOutDate.Should().Be(new DateTime(DateTime.Now.Year, 12, 24));
            result.NumberOfGuests.Should().Be(4);
        }

        [Fact]
        public void ParseBooking_Airbnb_InlineCheckoutWithYear_ParsesCheckOutDate()
        {
            // Arrange - checkout is only present in inline text with explicit year
            var email = new EmailMessage
            {
                From = "automated@airbnb.com",
                Subject = "Reservation confirmed - Ty Cheeseburger arrives Dec 21",
                Body = @"
                    Confirmation code: HMINLINE123
                    Listing: 1477018601970190586
                    Check-in: December 21, 2026
                    checkout: December 24, 2026
                    2 guests
                "
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("airbnb");
            result.BookingReference.Should().Be("HMINLINE123");
            result.CheckInDate.Should().Be(new DateTime(2026, 12, 21));
            result.CheckOutDate.Should().Be(new DateTime(2026, 12, 24));
        }

        [Fact]
        public void ParseBooking_Airbnb_MultilineCheckoutWithYear_ParsesCheckOutDate()
        {
            // Arrange - checkout appears with day of week on a separate line
            var email = new EmailMessage
            {
                From = "automated@airbnb.com",
                Subject = "Reservation confirmed - Ty Cheeseburger arrives Dec 21",
                Body = @"
                    Confirmation code: HMMULTI123
                    Listing: 1477018601970190586
                    Check-in: December 21, 2026

                    Checkout
                    Thursday
                    December 24, 2026

                    2 guests
                "
            };

            // Act
            var result = _service.ParseBooking(email);

            // Assert
            result.Should().NotBeNull();
            result!.Platform.Should().Be("airbnb");
            result.BookingReference.Should().Be("HMMULTI123");
            result.CheckInDate.Should().Be(new DateTime(2026, 12, 21));
            result.CheckOutDate.Should().Be(new DateTime(2026, 12, 24));
        }

    // -----------------------------------------------------------------------
    // Booking.com two-email pairing tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ParseBookings_BookingCom_ConfirmationAndRequest_ReturnsMergedBooking()
    {
        // Arrange
        var confirmEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking.com - New booking! (5474030366, Monday, December 21, 2026)",
            Body = string.Empty
        };
        var requestEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "New booking request – accept or decline by 10:45 AM on Apr 13, 2026",
            Body = @"
   Request details

   2 nights
   2 adults

   Check-in

   December 21, 2026

   Check-out

   December 23, 2026
"
        };

        // Act
        var results = _service.ParseBookings(new[] { confirmEmail, requestEmail });

        // Assert
        results.Should().HaveCount(1);
        var (booking, sourceEmails) = results[0];
        booking.Platform.Should().Be("bookingcom");
        booking.BookingReference.Should().Be("5474030366");
        booking.CheckInDate.Should().Be(new DateTime(2026, 12, 21));
        booking.CheckOutDate.Should().Be(new DateTime(2026, 12, 23));
        booking.NumberOfGuests.Should().Be(2);
        sourceEmails.Should().Contain(confirmEmail);
        sourceEmails.Should().Contain(requestEmail);
    }

    [Fact]
    public void ParseBookings_BookingCom_ConfirmationOnly_ReturnsEmpty()
    {
        // Arrange – no matching request email means we cannot build a complete booking
        var confirmEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking.com - New booking! (9900112233, Friday, January 9, 2026)",
            Body = string.Empty
        };

        // Act
        var results = _service.ParseBookings(new[] { confirmEmail });

        // Assert – incomplete booking should not be returned
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseBookings_BookingCom_RequestOnly_ReturnsEmpty()
    {
        // Arrange – no confirmation means no booking reference so nothing to process
        var requestEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "New booking request – accept or decline by 10:45 AM on Apr 13, 2026",
            Body = @"
   Check-in
   December 21, 2026
   Check-out
   December 23, 2026
   2 adults
"
        };

        // Act
        var results = _service.ParseBookings(new[] { requestEmail });

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseBookings_BookingCom_MismatchedCheckInDates_ReturnsEmpty()
    {
        // Arrange – confirmation check-in differs from request check-in: no pairing
        var confirmEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking.com - New booking! (1122334455, Wednesday, March 4, 2026)",
            Body = string.Empty
        };
        var requestEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "New booking request – accept or decline by 10:45 AM on Mar 1, 2026",
            Body = @"
   Check-in
   March 11, 2026
   Check-out
   March 14, 2026
   2 adults
"
        };

        // Act
        var results = _service.ParseBookings(new[] { confirmEmail, requestEmail });

        // Assert – dates don't match, so no complete booking
        results.Should().BeEmpty();
    }

    [Fact]
    public void ParseBookings_MixedPlatforms_ReturnsAllParsedBookings()
    {
        // Arrange – one Airbnb email and one Booking.com pair
        var airbnbEmail = new EmailMessage
        {
            From = "automated@airbnb.com",
            Subject = "Reservation confirmed",
            Body = @"
                Confirmation code: HMMIXED001
                Listing: 11112222
                Check-in: 06/01/2026
                Check-out: 06/04/2026
                2 guests
            "
        };
        var bcConfirmEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "Booking.com - New booking! (7788990011, Monday, June 1, 2026)",
            Body = string.Empty
        };
        var bcRequestEmail = new EmailMessage
        {
            From = "noreply@booking.com",
            Subject = "New booking request – accept or decline by 10:45 AM on May 28, 2026",
            Body = @"
   Check-in
   June 1, 2026
   Check-out
   June 4, 2026
   3 adults
"
        };

        // Act
        var results = _service.ParseBookings(new[] { airbnbEmail, bcConfirmEmail, bcRequestEmail });

        // Assert
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Booking.Platform == "airbnb" && r.Booking.BookingReference == "HMMIXED001");
        results.Should().Contain(r => r.Booking.Platform == "bookingcom" && r.Booking.BookingReference == "7788990011");
        var bcBooking = results.First(r => r.Booking.Platform == "bookingcom").Booking;
        bcBooking.NumberOfGuests.Should().Be(3);
        bcBooking.CheckInDate.Should().Be(new DateTime(2026, 6, 1));
        bcBooking.CheckOutDate.Should().Be(new DateTime(2026, 6, 4));
    }
    }