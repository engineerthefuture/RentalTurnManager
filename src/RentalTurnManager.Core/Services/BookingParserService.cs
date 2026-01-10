using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Text.RegularExpressions;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Implementation of booking parser service
/// </summary>
public class BookingParserService : IBookingParserService
{
    private readonly ILogger<BookingParserService> _logger;

    public BookingParserService(ILogger<BookingParserService> logger)
    {
        _logger = logger;
    }

    public Booking? ParseBooking(EmailMessage email)
    {
        try
        {
            var platform = DeterminePlatform(email);
            if (string.IsNullOrEmpty(platform))
            {
                _logger.LogWarning($"Could not determine platform for email: {email.Subject}");
                return null;
            }

            return platform.ToLower() switch
            {
                "airbnb" => ParseAirbnbBooking(email),
                "vrbo" => ParseVrboBooking(email),
                "bookingcom" => ParseBookingComBooking(email),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing booking from email: {email.Subject}");
            return null;
        }
    }

    private string DeterminePlatform(EmailMessage email)
    {
        var from = email.From.ToLower();
        
        if (from.Contains("airbnb.com"))
            return "airbnb";
        if (from.Contains("vrbo.com"))
            return "vrbo";
        if (from.Contains("booking.com"))
            return "bookingcom";

        return string.Empty;
    }

    private Booking? ParseAirbnbBooking(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");
        
        // Look for confirmation/reservation keywords along with key booking identifiers
        var hasConfirmationKeyword = content.Contains("reservation", StringComparison.OrdinalIgnoreCase) ||
                                     content.Contains("booking", StringComparison.OrdinalIgnoreCase) ||
                                     content.Contains("confirmed", StringComparison.OrdinalIgnoreCase);
        
        // Check if it has booking-specific content (not just performance/marketing emails)
        var hasBookingContent = Regex.IsMatch(content, @"check[\s-]*in", RegexOptions.IgnoreCase) ||
                               Regex.IsMatch(content, @"(?:confirmation|reservation)\s*(?:code|number)[:\s]+[A-Z0-9]", RegexOptions.IgnoreCase);
        
        if (!hasConfirmationKeyword || !hasBookingContent)
        {
            return null;
        }

        var booking = new Booking
        {
            Platform = "airbnb",
            RawEmailContent = content
        };

        // Extract booking reference (e.g., HM123456789)
        var refMatch = Regex.Match(content, @"(?:reservation|confirmation)\s*(?:code|number)?[:\s]+([A-Z]{2}\d+|[A-Z0-9]{10,})", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value;
        }

        // Extract property ID from listing number (should come before guests to avoid conflict)
        var listingMatch = Regex.Match(content, @"listing[:\s#]+(\d+)", RegexOptions.IgnoreCase);
        if (listingMatch.Success)
        {
            booking.PropertyId = listingMatch.Groups[1].Value;
        }

        // Extract dates - looking for check-in and check-out
        var checkInMatch = Regex.Match(content, @"check[\s-]*in[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\w+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase);
        var checkOutMatch = Regex.Match(content, @"check[\s-]*out[:\s]+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\w+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase);

        if (checkInMatch.Success && DateTime.TryParse(checkInMatch.Groups[1].Value, out var checkIn))
        {
            booking.CheckInDate = checkIn;
        }

        if (checkOutMatch.Success && DateTime.TryParse(checkOutMatch.Groups[1].Value, out var checkOut))
        {
            booking.CheckOutDate = checkOut;
        }

        // Extract guest name
        var guestMatch = Regex.Match(content, @"(?:guest|reserved by)[:\s]+([A-Z][a-z]+\s+[A-Z][a-z]+)", RegexOptions.IgnoreCase);
        if (guestMatch.Success)
        {
            booking.GuestName = guestMatch.Groups[1].Value;
        }

        // Extract number of guests - use word boundary to avoid matching listing numbers
        var guestsMatch = Regex.Match(content, @"\b(\d+)\s+guests?\b", RegexOptions.IgnoreCase);
        if (guestsMatch.Success && int.TryParse(guestsMatch.Groups[1].Value, out var guests))
        {
            booking.NumberOfGuests = guests;
        }

        return booking;
    }

    private Booking? ParseVrboBooking(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");
        
        if (!content.Contains("reservation", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("confirmation", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var booking = new Booking
        {
            Platform = "vrbo",
            RawEmailContent = content
        };

        // VRBO uses different patterns - adjust as needed
        var refMatch = Regex.Match(content, @"(?:confirmation|reservation)\s*(?:number)?[:\s#]+([0-9]{8,})", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value;
        }

        var checkInMatch = Regex.Match(content, @"arrival[:\s]+(\w+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase);
        var checkOutMatch = Regex.Match(content, @"departure[:\s]+(\w+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase);

        if (checkInMatch.Success && DateTime.TryParse(checkInMatch.Groups[1].Value, out var checkIn))
        {
            booking.CheckInDate = checkIn;
        }

        if (checkOutMatch.Success && DateTime.TryParse(checkOutMatch.Groups[1].Value, out var checkOut))
        {
            booking.CheckOutDate = checkOut;
        }

        var propertyMatch = Regex.Match(content, @"property[:\s#]+([0-9]+)", RegexOptions.IgnoreCase);
        if (propertyMatch.Success)
        {
            booking.PropertyId = propertyMatch.Groups[1].Value;
        }

        return booking;
    }

    private Booking? ParseBookingComBooking(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");
        
        if (!content.Contains("confirmation", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("booking", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var booking = new Booking
        {
            Platform = "bookingcom",
            RawEmailContent = content
        };

        var refMatch = Regex.Match(content, @"(?:booking|reservation)\s*(?:number|ID)[:\s#]+([0-9]+)", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value;
        }

        // Booking.com typically uses "Check-in" and "Check-out"
        var checkInMatch = Regex.Match(content, @"check-in[:\s]+(\w+,?\s+\d{1,2}\s+\w+\s+\d{4})", RegexOptions.IgnoreCase);
        var checkOutMatch = Regex.Match(content, @"check-out[:\s]+(\w+,?\s+\d{1,2}\s+\w+\s+\d{4})", RegexOptions.IgnoreCase);

        if (checkInMatch.Success && DateTime.TryParse(checkInMatch.Groups[1].Value, out var checkIn))
        {
            booking.CheckInDate = checkIn;
        }

        if (checkOutMatch.Success && DateTime.TryParse(checkOutMatch.Groups[1].Value, out var checkOut))
        {
            booking.CheckOutDate = checkOut;
        }

        var propertyMatch = Regex.Match(content, @"property[:\s]+([0-9]+)", RegexOptions.IgnoreCase);
        if (propertyMatch.Success)
        {
            booking.PropertyId = propertyMatch.Groups[1].Value;
        }

        var guestMatch = Regex.Match(content, @"guest\s+name[:\s]+([A-Z][a-z]+\s+[A-Z][a-z]+)\b", RegexOptions.IgnoreCase);
        if (guestMatch.Success)
        {
            booking.GuestName = guestMatch.Groups[1].Value.Trim();
        }

        return booking;
    }
}
