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
        var subject = (email.Subject ?? "").ToLower();
        var content = ((email.HtmlBody ?? "") + " " + (email.Body ?? "")).ToLower();
        
        // First try to determine from From address
        if (from.Contains("airbnb.com"))
            return "airbnb";
        if (from.Contains("vrbo.com"))
            return "vrbo";
        if (from.Contains("booking.com"))
            return "bookingcom";

        // If From address doesn't match, check subject and content for platform indicators
        // Airbnb indicators
        if (subject.Contains("reservation confirmed") || 
            content.Contains("airbnb.com") ||
            Regex.IsMatch(content, @"confirmation\s*code[:\s]+HM[A-Z0-9]+", RegexOptions.IgnoreCase))
        {
            return "airbnb";
        }
        
        // VRBO indicators
        if (subject.Contains("instant booking from") ||
            content.Contains("vrbo.com") ||
            content.Contains("homeaway") ||
            Regex.IsMatch(content, @"confirmation\s*number[:\s]+(?:HA-)?[A-Z0-9]+", RegexOptions.IgnoreCase))
        {
            return "vrbo";
        }
        
        // Booking.com indicators
        if (content.Contains("booking.com"))
        {
            return "bookingcom";
        }

        return string.Empty;
    }

    private Booking? ParseAirbnbBooking(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");
        var subject = email.Subject ?? "";
        
        // Look for confirmation/reservation keywords along with key booking identifiers
        var hasConfirmationKeyword = content.Contains("reservation", StringComparison.OrdinalIgnoreCase) ||
                                     content.Contains("booking", StringComparison.OrdinalIgnoreCase) ||
                                     content.Contains("confirmed", StringComparison.OrdinalIgnoreCase) ||
                                     subject.Contains("confirmed", StringComparison.OrdinalIgnoreCase);
        
        // Check if it has booking-specific content (not just performance/marketing emails)
        var hasBookingContent = Regex.IsMatch(content, @"check[\s-]*in", RegexOptions.IgnoreCase) ||
                               Regex.IsMatch(content, @"(?:confirmation|reservation)\s*(?:code|number)[:\s]+[A-Z0-9]", RegexOptions.IgnoreCase);
        
        if (!hasConfirmationKeyword || !hasBookingContent)
        {
            return null;
        }

        var booking = new Booking
        {
            Platform = "airbnb"
        };

        // Extract booking reference - Airbnb uses codes like HMXX8RX9P5 or HM123456789
        var refMatch = Regex.Match(content, @"(?:confirmation|reservation)\s*(?:code|number)?[:\s>]+([A-Z0-9]{8,12})\b", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value;
        }

        // Extract property name - look for it in various formats in the email
        // Format: Title case or all caps (e.g., "Waterfront Lake Anna - Kayaks, Firepit, Family Fun" or "WATERFRONT LAKE ANNA...")
        // Look for lines that start with capital letter and contain typical property name patterns
        var propertyNameMatch = Regex.Match(content, @"^([A-Z][A-Za-z\s\-,&']+[A-Za-z])\s*$", RegexOptions.Multiline);
        if (propertyNameMatch.Success)
        {
            var potentialName = propertyNameMatch.Groups[1].Value.Trim();
            _logger.LogInformation($"Found potential property name: '{potentialName}' (length: {potentialName.Length})");
            // Should be at least 10 characters and contain meaningful words (not just "CHECK IN" etc)
            if (potentialName.Length >= 10 && 
                !potentialName.Contains("CHECK", StringComparison.OrdinalIgnoreCase) && 
                !potentialName.Contains("RESERVATION", StringComparison.OrdinalIgnoreCase) &&
                !potentialName.Contains("CONFIRMED", StringComparison.OrdinalIgnoreCase) &&
                !potentialName.Contains("INSTANT BOOK", StringComparison.OrdinalIgnoreCase))
            {
                booking.PropertyId = potentialName;
                _logger.LogInformation($"Using property name as PropertyId: '{booking.PropertyId}'");
            }
            else
            {
                _logger.LogInformation($"Rejected property name (too short or contains excluded words)");
            }
        }
        else
        {
            _logger.LogInformation("No property name found in email");
        }

        // If property name not found, try extracting from listing URL or listing number
        if (string.IsNullOrEmpty(booking.PropertyId))
        {
            var listingMatch = Regex.Match(content, @"(?:listing|rooms?)[/:\s#]+(\d+)", RegexOptions.IgnoreCase);
            if (listingMatch.Success)
            {
                booking.PropertyId = listingMatch.Groups[1].Value;
                _logger.LogInformation($"Using numeric listing ID as PropertyId: '{booking.PropertyId}'");
            }
            else
            {
                _logger.LogWarning("Could not extract property identifier from email");
            }
        }

        // Extract dates - Airbnb uses multiple formats:
        // 1. "Wed, Dec 3" (weekday, month abbreviation, day - without year)
        // 2. "December 3, 2025" (full month name with year)
        // 3. "12/3/2025" or "01/15/2026" (numeric format)
        
        // Try numeric format first: "01/15/2026"
        var numericCheckInMatch = Regex.Match(content, @"check[\s-]*in[:\s>]+(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
        var numericCheckOutMatch = Regex.Match(content, @"check[\s-]*out[:\s>]+(\d{1,2}/\d{1,2}/\d{4})", RegexOptions.IgnoreCase);
        
        if (numericCheckInMatch.Success && DateTime.TryParse(numericCheckInMatch.Groups[1].Value, out var numericCheckIn))
        {
            booking.CheckInDate = numericCheckIn;
        }
        
        if (numericCheckOutMatch.Success && DateTime.TryParse(numericCheckOutMatch.Groups[1].Value, out var numericCheckOut))
        {
            booking.CheckOutDate = numericCheckOut;
        }
        
        // If dates not found, try format: "Mon, Dec 3" or "Monday, December 3"
        if (booking.CheckInDate == default)
        {
            var checkInMatch = Regex.Match(content, @"check[\s-]*in[:\s>]+(?:\w+,?\s+)?(\w+\s+\d{1,2}(?:,?\s+\d{4})?)", RegexOptions.IgnoreCase);
            if (checkInMatch.Success)
            {
                var checkInStr = checkInMatch.Groups[1].Value;
                // If year is missing, add current year or next year if date has passed
                if (!checkInStr.Contains("20"))
                {
                    var currentYear = DateTime.Now.Year;
                    checkInStr += $", {currentYear}";
                    
                    // Try parsing with current year
                    if (DateTime.TryParse(checkInStr, out var tempCheckIn))
                    {
                        // If the date is more than 30 days in the past, it's probably next year
                        if (tempCheckIn < DateTime.Now.AddDays(-30))
                        {
                            checkInStr = $"{checkInMatch.Groups[1].Value}, {currentYear + 1}";
                        }
                    }
                }
                
                if (DateTime.TryParse(checkInStr, out var checkIn))
                {
                    booking.CheckInDate = checkIn;
                }
            }
        }
        
        if (booking.CheckOutDate == default)
        {
            var checkOutMatch = Regex.Match(content, @"check[\s-]*out[:\s>]+(?:\w+,?\s+)?(\w+\s+\d{1,2}(?:,?\s+\d{4})?)", RegexOptions.IgnoreCase);
            if (checkOutMatch.Success)
            {
                var checkOutStr = checkOutMatch.Groups[1].Value;
                // If year is missing, infer from check-in date
                if (!checkOutStr.Contains("20"))
                {
                    var year = booking.CheckInDate != default ? booking.CheckInDate.Year : DateTime.Now.Year;
                    checkOutStr += $", {year}";
                    
                    // Try parsing
                    if (DateTime.TryParse(checkOutStr, out var tempCheckOut) && booking.CheckInDate != default)
                    {
                        // If checkout is before checkin, it must be next year
                        if (tempCheckOut < booking.CheckInDate)
                        {
                            checkOutStr = $"{checkOutMatch.Groups[1].Value}, {year + 1}";
                        }
                    }
                }
                
                if (DateTime.TryParse(checkOutStr, out var checkOut))
                {
                    booking.CheckOutDate = checkOut;
                }
            }
        }

        // Extract guest name - check subject first ("Angel Tristan arrives Dec 3")
        var subjectGuestMatch = Regex.Match(subject, @"([A-Z][a-z]+\s+[A-Z][a-z]+)\s+arrives", RegexOptions.IgnoreCase);
        if (subjectGuestMatch.Success)
        {
            booking.GuestName = subjectGuestMatch.Groups[1].Value;
        }
        else
        {
            // Try content
            var guestMatch = Regex.Match(content, @"(?:guest|reserved by|send\s+\w+\s+a\s+message)[:\s>]+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", RegexOptions.IgnoreCase);
            if (guestMatch.Success)
            {
                booking.GuestName = guestMatch.Groups[1].Value;
            }
        }

        // Extract number of guests - look for "6 adults, 2 children" or "3 guests"
        var adultsMatch = Regex.Match(content, @"\b(\d+)\s+adults?\b", RegexOptions.IgnoreCase);
        var childrenMatch = Regex.Match(content, @"\b(\d+)\s+children?\b", RegexOptions.IgnoreCase);
        var guestsMatch = Regex.Match(content, @"\b(\d+)\s+guests?\b", RegexOptions.IgnoreCase);
        
        int totalGuests = 0;
        if (adultsMatch.Success && int.TryParse(adultsMatch.Groups[1].Value, out var adults))
        {
            _logger.LogInformation($"Found adults: {adults}");
            totalGuests += adults;
        }
        if (childrenMatch.Success && int.TryParse(childrenMatch.Groups[1].Value, out var children))
        {
            _logger.LogInformation($"Found children: {children}");
            totalGuests += children;
        }
        // If no adults/children breakdown, use general "guests" count
        if (totalGuests == 0 && guestsMatch.Success && int.TryParse(guestsMatch.Groups[1].Value, out var guests))
        {
            _logger.LogInformation($"Found general guests: {guests}");
            totalGuests = guests;
        }
        
        if (totalGuests > 0)
        {
            booking.NumberOfGuests = totalGuests;
        }
        
        // Log all parsed booking attributes
        _logger.LogInformation($"Parsed Airbnb booking - PropertyId: '{booking.PropertyId}', Reference: '{booking.BookingReference}', CheckIn: {booking.CheckInDate:yyyy-MM-dd}, CheckOut: {booking.CheckOutDate:yyyy-MM-dd}, Guest: '{booking.GuestName}', Guests: {booking.NumberOfGuests} (parsed: adults={adultsMatch.Groups[1].Value}, children={childrenMatch.Groups[1].Value})");

        // Validate we have minimum required data
        if (string.IsNullOrEmpty(booking.PropertyId) || booking.CheckInDate == default)
        {
            _logger.LogWarning($"Incomplete Airbnb booking data - PropertyId: '{booking.PropertyId}', CheckInDate: {booking.CheckInDate}");
            return null;
        }
        return booking;
    }

    private Booking? ParseVrboBooking(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");
        var subject = email.Subject ?? "";
        
        // VRBO emails have distinctive markers
        if (!content.Contains("reservation", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("booking", StringComparison.OrdinalIgnoreCase) &&
            !content.Contains("confirmation", StringComparison.OrdinalIgnoreCase) &&
            !subject.Contains("vrbo", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var booking = new Booking
        {
            Platform = "vrbo"
        };

        // Extract Reservation ID (format: HA-T65Q42 or numeric like 98765432)
        var refMatch = Regex.Match(content, @"(?:Reservation\s+ID|Confirmation\s+Number)[:\s>]+([A-Z]{2}-[A-Z0-9]{6,}|\d{8,})", RegexOptions.IgnoreCase);
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value;
        }

        // Extract Unit/Property ID (format: unit_5480548 or Property: 87654321)
        var unitMatch = Regex.Match(content, @"Unit[:\s>]+(unit_\d+)", RegexOptions.IgnoreCase);
        if (unitMatch.Success)
        {
            booking.PropertyId = unitMatch.Groups[1].Value;
        }
        else
        {
            // Try Property: format from test data
            var propertyMatch = Regex.Match(content, @"Property[:\s>]+(\d+)", RegexOptions.IgnoreCase);
            if (propertyMatch.Success)
            {
                booking.PropertyId = propertyMatch.Groups[1].Value;
            }
            else
            {
                // Try extracting from subject line "Vrbo #4906384"
                var subjectPropertyMatch = Regex.Match(subject, @"Vrbo\s+#(\d+)", RegexOptions.IgnoreCase);
                if (subjectPropertyMatch.Success)
                {
                    booking.PropertyId = subjectPropertyMatch.Groups[1].Value;
                }
            }
        }

        // VRBO uses date range format: "Dec 31, 2025 - Jan 2, 2026"
        // Try extracting from subject first (more reliable)
        var subjectDateMatch = Regex.Match(subject, @"(\w+\s+\d{1,2},\s+\d{4})\s*-\s*(\w+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
        if (subjectDateMatch.Success)
        {
            if (DateTime.TryParse(subjectDateMatch.Groups[1].Value, out var checkIn))
            {
                booking.CheckInDate = checkIn;
            }
            if (DateTime.TryParse(subjectDateMatch.Groups[2].Value, out var checkOut))
            {
                booking.CheckOutDate = checkOut;
            }
        }
        else
        {
            // Try content - look for "Dates" section with format "Dec 31, 2025 - Jan 2, 2026"
            var datesMatch = Regex.Match(content, @"Dates[:\s>]+[^<>]*?(\w+\s+\d{1,2},\s+\d{4})\s*-\s*(\w+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
            if (datesMatch.Success)
            {
                if (DateTime.TryParse(datesMatch.Groups[1].Value, out var checkIn))
                {
                    booking.CheckInDate = checkIn;
                }
                if (DateTime.TryParse(datesMatch.Groups[2].Value, out var checkOut))
                {
                    booking.CheckOutDate = checkOut;
                }
            }
            else
            {
                // Try test format: "Arrival: January 20, 2026" and "Departure: January 23, 2026"
                var arrivalMatch = Regex.Match(content, @"Arrival[:\s]+(\w+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
                var departureMatch = Regex.Match(content, @"Departure[:\s]+(\w+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
                
                if (arrivalMatch.Success && DateTime.TryParse(arrivalMatch.Groups[1].Value, out var checkIn))
                {
                    booking.CheckInDate = checkIn;
                }
                if (departureMatch.Success && DateTime.TryParse(departureMatch.Groups[1].Value, out var checkOut))
                {
                    booking.CheckOutDate = checkOut;
                }
            }
        }

        // Extract guest name - from subject "Instant Booking from Mehrshad Nikfam:"
        var subjectGuestMatch = Regex.Match(subject, @"(?:Instant\s+Booking\s+from|from)\s+([A-Z][a-z]+\s+[A-Z][a-z]+):", RegexOptions.IgnoreCase);
        if (subjectGuestMatch.Success)
        {
            booking.GuestName = subjectGuestMatch.Groups[1].Value;
        }
        else
        {
            // Try content - look for "Traveler Name"
            var guestMatch = Regex.Match(content, @"Traveler\s+Name[:\s>]+([A-Z][a-z]+\s+[A-Z][a-z]+)", RegexOptions.IgnoreCase);
            if (guestMatch.Success)
            {
                booking.GuestName = guestMatch.Groups[1].Value;
            }
        }

        // Extract number of guests - format: "6 adults, 0 children"
        var guestsMatch = Regex.Match(content, @"Guests[:\s>]+(\d+)\s+adults?", RegexOptions.IgnoreCase);
        if (guestsMatch.Success && int.TryParse(guestsMatch.Groups[1].Value, out var adults))
        {
            // Also check for children
            var childrenMatch = Regex.Match(content, @"(\d+)\s+children", RegexOptions.IgnoreCase);
            var children = 0;
            if (childrenMatch.Success && int.TryParse(childrenMatch.Groups[1].Value, out children))
            {
                booking.NumberOfGuests = adults + children;
            }
            else
            {
                booking.NumberOfGuests = adults;
            }
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
            Platform = "bookingcom"
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
