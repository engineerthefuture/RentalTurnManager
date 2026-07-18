/************************
 * Rental Turn Manager
 * BookingParserService.cs
 * 
 * Service that parses booking information from platform-specific emails
 * (Airbnb, VRBO, Booking.com). Extracts confirmation codes, dates, guest
 * counts, property IDs, and other booking details using regex patterns.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Linq;
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
        if (from.Contains("vrbo.com") || from.Contains("homeaway"))
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

    public Booking? ParseCancellation(EmailMessage email)
    {
        try
        {
            var platform = DeterminePlatform(email);
            if (string.IsNullOrEmpty(platform))
            {
                _logger.LogWarning($"Could not determine platform for cancellation email: {email.Subject}");
                return null;
            }

            return platform.ToLower() switch
            {
                "airbnb" => ParseAirbnbCancellation(email),
                "vrbo" => ParseVrboCancellation(email),
                "bookingcom" => ParseBookingComCancellation(email),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error parsing cancellation from email: {email.Subject}");
            return null;
        }
    }

    private Booking? ParseVrboCancellation(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");

        var booking = new Booking { Platform = "vrbo" };

        // Extract Property ID from subject: "(Property ID 4706321)" or "Property ID 4706321"
        var propertyMatch = Regex.Match(subject, @"\(Property\s+ID\s+(\d+)\)", RegexOptions.IgnoreCase);
        if (!propertyMatch.Success)
            propertyMatch = Regex.Match(subject, @"Property\s+ID\s+(\d+)", RegexOptions.IgnoreCase);
        if (!propertyMatch.Success)
            propertyMatch = Regex.Match(content, @"Property[:\s#>]+(\d+)", RegexOptions.IgnoreCase);
        if (propertyMatch.Success)
            booking.PropertyId = propertyMatch.Groups[1].Value;

        // Extract Reservation ID from subject or content: "Reservation HA-W2T49E"
        var refMatch = Regex.Match(subject, @"Reservation\s+([A-Z]{2}-[A-Z0-9]{4,})", RegexOptions.IgnoreCase);
        if (!refMatch.Success)
            refMatch = Regex.Match(content, @"(?:Reservation\s+ID|Confirmation\s+Number)[:\s>]+([A-Z]{2}-[A-Z0-9]{6,}|\d{8,})", RegexOptions.IgnoreCase);
        if (refMatch.Success)
            booking.BookingReference = refMatch.Groups[1].Value.ToUpper();

        // Extract date range from subject: "Aug 8, 2026 - Aug 15, 2026"
        var subjectDateMatch = Regex.Match(subject, @"(\w+\s+\d{1,2}),?\s+(\d{4})\s*-\s*(\w+\s+\d{1,2}),?\s+(\d{4})", RegexOptions.IgnoreCase);
        if (subjectDateMatch.Success)
        {
            if (DateTime.TryParse($"{subjectDateMatch.Groups[1].Value}, {subjectDateMatch.Groups[2].Value}", out var checkIn))
                booking.CheckInDate = checkIn;
            if (DateTime.TryParse($"{subjectDateMatch.Groups[3].Value}, {subjectDateMatch.Groups[4].Value}", out var checkOut))
                booking.CheckOutDate = checkOut;
        }
        else
        {
            // Try "Aug 8 - Aug 15, 2026" (same year)
            var singleYearMatch = Regex.Match(subject, @"(\w+\s+\d{1,2})\s*-\s*(\w+\s+\d{1,2}),\s+(\d{4})", RegexOptions.IgnoreCase);
            if (singleYearMatch.Success)
            {
                var year = singleYearMatch.Groups[3].Value;
                if (DateTime.TryParse($"{singleYearMatch.Groups[1].Value}, {year}", out var checkIn))
                    booking.CheckInDate = checkIn;
                if (DateTime.TryParse($"{singleYearMatch.Groups[2].Value}, {year}", out var checkOut))
                    booking.CheckOutDate = checkOut;
            }
        }

        if (string.IsNullOrEmpty(booking.BookingReference))
        {
            _logger.LogWarning($"Could not extract booking reference from VRBO cancellation email: {subject}");
            return null;
        }

        _logger.LogInformation($"Parsed VRBO cancellation - PropertyId: '{booking.PropertyId}', Reference: '{booking.BookingReference}'");
        return booking;
    }

    private Booking? ParseAirbnbCancellation(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");

        var booking = new Booking { Platform = "airbnb" };

        // Extract confirmation code (HM-style)
        var refMatch = Regex.Match(content, @"(?:confirmation|reservation)\s*code[:\s>]+([A-Z0-9]{8,12})\b", RegexOptions.IgnoreCase);
        if (!refMatch.Success)
            refMatch = Regex.Match(content, @"\b(HM[A-Z0-9]{8,10})\b", RegexOptions.IgnoreCase);
        if (!refMatch.Success)
            refMatch = Regex.Match(subject, @"\b(HM[A-Z0-9]{8,10})\b", RegexOptions.IgnoreCase);
        if (refMatch.Success)
            booking.BookingReference = refMatch.Groups[1].Value.ToUpper();

        // Extract listing/property ID
        var listingMatch = Regex.Match(content, @"(?:listing|rooms?)[/:\s#]+(\d+)", RegexOptions.IgnoreCase);
        if (listingMatch.Success)
            booking.PropertyId = listingMatch.Groups[1].Value;

        if (string.IsNullOrEmpty(booking.BookingReference))
        {
            _logger.LogWarning($"Could not extract booking reference from Airbnb cancellation email: {subject}");
            return null;
        }

        _logger.LogInformation($"Parsed Airbnb cancellation - PropertyId: '{booking.PropertyId}', Reference: '{booking.BookingReference}'");
        return booking;
    }

    private Booking? ParseBookingComCancellation(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");

        var booking = new Booking { Platform = "bookingcom" };

        // Subject: "Canceled booking! (5474030366, Monday, December 21, 2026)"
        var subjectMatch = Regex.Match(subject,
            @"Canceled booking!\s*\((\d+),",
            RegexOptions.IgnoreCase);
        if (subjectMatch.Success)
            booking.BookingReference = subjectMatch.Groups[1].Value;

        // Fallback: body contains "Cancellation — 5474030366" or "reservation 5474030366"
        if (string.IsNullOrEmpty(booking.BookingReference))
        {
            var refMatch = Regex.Match(content,
                @"Cancellation\s*[\—\-–]\s*(\d{6,})",
                RegexOptions.IgnoreCase);
            if (!refMatch.Success)
                refMatch = Regex.Match(content,
                    @"(?:cancellation of )?reservation\s+(\d{6,})",
                    RegexOptions.IgnoreCase);
            if (!refMatch.Success)
                refMatch = Regex.Match(content,
                    @"(?:booking|reservation)\s*(?:number|ID)[:\s#]+(\d+)",
                    RegexOptions.IgnoreCase);
            if (refMatch.Success)
                booking.BookingReference = refMatch.Groups[1].Value;
        }

        // Property ID from URL parameter hotel_id=XXXXXXXX or plain-text property: XXXXXXXX
        var propMatch = Regex.Match(content, @"hotel[_]?id[=:](\d+)", RegexOptions.IgnoreCase);
        if (!propMatch.Success)
            propMatch = Regex.Match(content, @"/hotels?/(\d{6,})", RegexOptions.IgnoreCase);
        if (!propMatch.Success)
            propMatch = Regex.Match(content, @"property[:\s]+(\d{6,})", RegexOptions.IgnoreCase);
        if (propMatch.Success)
            booking.PropertyId = propMatch.Groups[1].Value;

        if (string.IsNullOrEmpty(booking.BookingReference))
        {
            _logger.LogWarning($"Could not extract booking reference from Booking.com cancellation email: {subject}");
            return null;
        }

        _logger.LogInformation($"Parsed Booking.com cancellation - PropertyId: '{booking.PropertyId}', Reference: '{booking.BookingReference}'");
        return booking;
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

        // Extract booking reference - Airbnb uses codes like HMFMAQS9MB, HMXX8RX9P5 or HM123456789
        // Try multiple patterns to increase reliability
        var refMatch = Regex.Match(content, @"(?:confirmation|reservation)\s*(?:code|number)[:\s>]+([A-Z0-9]{8,12})\b", RegexOptions.IgnoreCase);
        if (!refMatch.Success)
        {
            // Try alternative pattern without the word "code" or "number" but with colon
            refMatch = Regex.Match(content, @"(?:confirmation|reservation)\s*code[:\s>]+([A-Z0-9]{8,12})\b", RegexOptions.IgnoreCase);
        }
        if (!refMatch.Success)
        {
            // Try looking for just the Airbnb-style confirmation code pattern (HM followed by 8-10 alphanumeric)
            refMatch = Regex.Match(content, @"\b(HM[A-Z0-9]{8,10})\b", RegexOptions.IgnoreCase);
        }
        if (!refMatch.Success)
        {
            // Try subject line pattern - but look for code after dashes/spaces, not the word "confirmed" itself
            refMatch = Regex.Match(subject, @"(?:confirmed|confirmation)\s*[-–—]\s*([A-Z0-9]{8,12})\b", RegexOptions.IgnoreCase);
        }
        if (refMatch.Success)
        {
            booking.BookingReference = refMatch.Groups[1].Value.ToUpper();
            _logger.LogInformation($"Extracted booking reference: {booking.BookingReference}");
        }
        else
        {
            _logger.LogWarning("Could not extract booking reference from email");
        }

        // Extract property ID from listing URL or listing number
        var listingMatch = Regex.Match(content, @"(?:listing|rooms?)[/:\s#]+(\d+)", RegexOptions.IgnoreCase);
        if (listingMatch.Success)
        {
            booking.PropertyId = listingMatch.Groups[1].Value;
            _logger.LogInformation($"Using listing ID as PropertyId: '{booking.PropertyId}'");
        }
        else
        {
            _logger.LogWarning("Could not extract property identifier from email");
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
        
        // Try extracting check-in date from subject line format "arrives [Month] [Day]" before trying text-based content extraction
        if (booking.CheckInDate == default)
        {
            var subjectDateMatch = Regex.Match(subject, @"arrives\s+(\w+\s+\d{1,2})", RegexOptions.IgnoreCase);
            if (subjectDateMatch.Success)
            {
                var dateStr = subjectDateMatch.Groups[1].Value;
                // Anchor year inference to the email's own send date, not the current date.
                // This prevents re-scans of old emails from bumping dates to the following year.
                // Fall back to UtcNow only if the email has no date (e.g. unit tests without Date set).
                var emailSendDate = email.Date != default ? email.Date : DateTime.UtcNow;
                var emailYear = emailSendDate.Year;
                var dateWithYear = $"{dateStr}, {emailYear}";
                
                // Try parsing with the email's year
                if (DateTime.TryParse(dateWithYear, out var tempCheckIn))
                {
                    // Only advance to next year if the parsed date is more than 30 days before
                    // the email was sent (i.e. clearly wrong year, e.g. a Dec email with a Jan date).
                    if (tempCheckIn.Date < emailSendDate.Date.AddDays(-30))
                    {
                        dateWithYear = $"{dateStr}, {emailYear + 1}";
                        DateTime.TryParse(dateWithYear, out tempCheckIn);
                    }
                    booking.CheckInDate = tempCheckIn;
                    _logger.LogInformation($"Extracted check-in date from subject: {booking.CheckInDate:yyyy-MM-dd}");
                }
            }
        }
        
        // If dates not found, try format: "Mon, Dec 3" or "Monday, December 3"
        if (booking.CheckInDate == default)
        {
            var checkInMatch = Regex.Match(content, @"check[\s-]*in[:\s>]+(?:\w+,?\s+)?(\w+\s+\d{1,2}(?:,?\s+\d{4})?)", RegexOptions.IgnoreCase);
            if (checkInMatch.Success)
            {
                var checkInStr = checkInMatch.Groups[1].Value;
                // If year is missing, infer from the email's send date (not the current date).
                // This prevents re-scans of old emails from bumping dates to the following year.
                // Fall back to UtcNow only if the email has no date (e.g. unit tests without Date set).
                if (!checkInStr.Contains("20"))
                {
                    var emailSendDate = email.Date != default ? email.Date : DateTime.UtcNow;
                    var emailYear = emailSendDate.Year;
                    checkInStr += $", {emailYear}";
                    
                    // Only advance to next year if the parsed date is more than 30 days before
                    // the email was sent (i.e. clearly wrong year, e.g. a Dec email with a Jan date).
                    if (DateTime.TryParse(checkInStr, out var tempCheckIn) &&
                        tempCheckIn.Date < emailSendDate.Date.AddDays(-30))
                    {
                        checkInStr = $"{checkInMatch.Groups[1].Value}, {emailYear + 1}";
                    }
                }
                
                if (DateTime.TryParse(checkInStr, out var checkIn))
                {
                    booking.CheckInDate = checkIn;
                }
            }
        }
        
        // Calculate check-out date from number of nights if check-in date is available
        if (booking.CheckOutDate == default && booking.CheckInDate != default)
        {
            var nightsMatch = Regex.Match(content, @"(\d+)\s+nights?", RegexOptions.IgnoreCase);
            if (nightsMatch.Success && int.TryParse(nightsMatch.Groups[1].Value, out var nights))
            {
                booking.CheckOutDate = booking.CheckInDate.AddDays(nights);
                _logger.LogInformation($"Calculated check-out date from {nights} nights: {booking.CheckOutDate:yyyy-MM-dd}");
            }
        }

        // Try to parse checkout date directly if not found yet
        // Handle format: "Checkout\n[Day of week]\nMonth Day, Year" or "checkout: Month Day, Year"
        if (booking.CheckOutDate == default)
        {
            // First try the inline format with colon
            var checkOutMatch = Regex.Match(content, @"check[\s-]*out[:\s>]+(?:\w+,?\s+)?(\w+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase);
            if (checkOutMatch.Success)
            {
                if (DateTime.TryParse(checkOutMatch.Groups[1].Value, out var checkOut))
                {
                    booking.CheckOutDate = checkOut;
                    _logger.LogInformation($"Extracted checkout date (inline format): {booking.CheckOutDate:yyyy-MM-dd}");
                }
            }
            else
            {
                // Try format with day of week on separate line: "Checkout\nMonday\nMarch 9, 2026"
                // This pattern allows for whitespace/newlines between checkout and the date
                var checkOutMultilineMatch = Regex.Match(content, @"check[\s-]*out[\s\r\n<>]+(?:\w+[\s\r\n<>]+)?(\w+\s+\d{1,2},\s+\d{4})", RegexOptions.IgnoreCase);
                if (checkOutMultilineMatch.Success &&
                    DateTime.TryParse(checkOutMultilineMatch.Groups[1].Value, out var checkOut))
                {
                    booking.CheckOutDate = checkOut;
                    _logger.LogInformation($"Extracted checkout date (multiline format): {booking.CheckOutDate:yyyy-MM-dd}");
                }
            }
        }

        // Try Airbnb two-column plain text layout (no year on dates):
        // "Check-in     Checkout"
        // "Fri, Mar 6   Mon, Mar 9"
        if (booking.CheckInDate == default || booking.CheckOutDate == default)
        {
            var twoColMatch = Regex.Match(content,
                @"check[\s-]*in\s+check[\s-]*out[\s\S]{0,50}?((?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s+\w+\s+\d{1,2})\s+((?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s+\w+\s+\d{1,2})",
                RegexOptions.IgnoreCase);
            if (twoColMatch.Success)
            {
                // Anchor year inference to the email's own send date, not the current date.
                // Fall back to UtcNow only if the email has no date (e.g. unit tests without Date set).
                var emailSendDate = email.Date != default ? email.Date : DateTime.UtcNow;
                var emailYear = emailSendDate.Year;
                var checkInAbbrev = twoColMatch.Groups[1].Value;
                var checkOutAbbrev = twoColMatch.Groups[2].Value;

                if (booking.CheckInDate == default)
                {
                    var cleanCheckIn = Regex.Replace(checkInAbbrev, @"^(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s*", "", RegexOptions.IgnoreCase);
                    if (DateTime.TryParse($"{cleanCheckIn}, {emailYear}", out var ci))
                    {
                        // Only advance to next year if the date is clearly before the email was sent.
                        if (ci.Date < emailSendDate.Date.AddDays(-30)) ci = ci.AddYears(1);
                        booking.CheckInDate = ci;
                        _logger.LogInformation($"Extracted check-in from two-column format: {booking.CheckInDate:yyyy-MM-dd}");
                    }
                }

                if (booking.CheckOutDate == default && booking.CheckInDate != default)
                {
                    var cleanCheckOut = Regex.Replace(checkOutAbbrev, @"^(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun),\s*", "", RegexOptions.IgnoreCase);
                    var checkOutYear = booking.CheckInDate.Year;
                    if (DateTime.TryParse($"{cleanCheckOut}, {checkOutYear}", out var co))
                    {
                        // If checkout is before check-in it must be the following year (e.g. Dec 30 → Jan 3)
                        if (co < booking.CheckInDate) co = co.AddYears(1);
                        booking.CheckOutDate = co;
                        _logger.LogInformation($"Extracted checkout from two-column format: {booking.CheckOutDate:yyyy-MM-dd}");
                    }
                }
            }
        }

        // Extract guest name - check subject first ("Angel Guester arrives Dec 3")
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

        // Extract number of guests - look for "Guests\n6 adults, 2 children" or fallback to individual matches
        // Use more flexible pattern to handle HTML tags, newlines, and other whitespace
        var guestBreakdownMatch = Regex.Match(content, @"Guests[:\s\r\n<>]*(\d+)\s+adults?[,\s]*(\d+)\s+(?:children?|kids?)", RegexOptions.IgnoreCase);
        
        int totalGuests = 0;
        if (guestBreakdownMatch.Success)
        {
            // Found the specific "Guests X adults, Y children" pattern
            if (int.TryParse(guestBreakdownMatch.Groups[1].Value, out var adults) &&
                int.TryParse(guestBreakdownMatch.Groups[2].Value, out var children))
            {
                _logger.LogInformation($"Found guest breakdown: {adults} adults, {children} children");
                totalGuests = adults + children;
            }
        }
        else
        {
            // Fallback to individual patterns - check for adults and children separately
            var adultsMatch = Regex.Match(content, @"\b(\d+)\s+adults?\b", RegexOptions.IgnoreCase);
            var childrenMatch = Regex.Match(content, @"\b(\d+)\s+(?:children?|kids?)\b", RegexOptions.IgnoreCase);
            var guestsMatch = Regex.Match(content, @"\b(\d+)\s+guests?\b", RegexOptions.IgnoreCase);
            
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
        }
        
        if (totalGuests > 0)
        {
            booking.NumberOfGuests = totalGuests;
        }
        
        // Log all parsed booking attributes
        _logger.LogInformation($"Parsed Airbnb booking - PropertyId: '{booking.PropertyId}', Reference: '{booking.BookingReference}', CheckIn: {booking.CheckInDate:yyyy-MM-dd}, CheckOut: {booking.CheckOutDate:yyyy-MM-dd}, Guest: '{booking.GuestName}', Guests: {booking.NumberOfGuests}");

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

        // Extract Property ID (prioritize Property ID over Unit ID)
        // Try Property: format first (format: Property #4906384 or Property: 4906384)
        var propertyMatch = Regex.Match(content, @"Property[:\s#>]+(\d+)", RegexOptions.IgnoreCase);
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
            else
            {
                // Fallback: Try Unit ID only if Property ID not found (format: unit_5480548)
                var unitMatch = Regex.Match(content, @"Unit[:\s>]+(unit_\d+)", RegexOptions.IgnoreCase);
                if (unitMatch.Success)
                {
                    booking.PropertyId = unitMatch.Groups[1].Value;
                }
            }
        }

        // VRBO uses date range format: "Dec 31, 2025 - Jan 2, 2026" or "Apr 3 - Apr 6, 2026"
        // Try extracting from subject first (more reliable)
        // Pattern handles both "Month Day, Year - Month Day, Year" and "Month Day - Month Day, Year"
        var subjectDateMatch = Regex.Match(subject, @"(\w+\s+\d{1,2})(?:,\s+\d{4})?\s*-\s*(\w+\s+\d{1,2}),\s+(\d{4})", RegexOptions.IgnoreCase);
        if (subjectDateMatch.Success)
        {
            var year = subjectDateMatch.Groups[3].Value;
            var checkInStr = $"{subjectDateMatch.Groups[1].Value}, {year}";
            var checkOutStr = $"{subjectDateMatch.Groups[2].Value}, {year}";
            
            if (DateTime.TryParse(checkInStr, out var checkIn))
            {
                booking.CheckInDate = checkIn;
            }
            if (DateTime.TryParse(checkOutStr, out var checkOut))
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

        // Extract number of guests - format: "6 adults, 0 children" or "2 adults, 1 child"
        var guestsMatch = Regex.Match(content, @"Guests[:\s>]+(\d+)\s+adults?", RegexOptions.IgnoreCase);
        if (guestsMatch.Success && int.TryParse(guestsMatch.Groups[1].Value, out var adults))
        {
            // Also check for children (both "child" and "children")
            var childrenMatch = Regex.Match(content, @"(\d+)\s+child(?:ren)?", RegexOptions.IgnoreCase);
            booking.NumberOfGuests = childrenMatch.Success && int.TryParse(childrenMatch.Groups[1].Value, out var children)
                ? adults + children
                : adults;
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

    // -----------------------------------------------------------------------
    // ParseBookings – multi-email batch processing
    // -----------------------------------------------------------------------

    public List<(Booking Booking, List<EmailMessage> SourceEmails)> ParseBookings(IEnumerable<EmailMessage> emails)
    {
        var result = new List<(Booking, List<EmailMessage>)>();

        var bookingComEmails = new List<EmailMessage>();
        var otherEmails = new List<EmailMessage>();

        foreach (var email in emails)
        {
            var platform = DeterminePlatform(email);
            if (platform == "bookingcom")
                bookingComEmails.Add(email);
            else
                otherEmails.Add(email);
        }

        // Airbnb / VRBO: one email → one booking (existing logic)
        foreach (var email in otherEmails)
        {
            try
            {
                var booking = ParseBooking(email);
                if (booking != null)
                    result.Add((booking, new List<EmailMessage> { email }));
                else
                    _logger.LogWarning($"Could not parse booking from email: {email.Subject}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing booking from email: {email.Subject}");
            }
        }

        // Booking.com: pair request + confirmation emails
        result.AddRange(ParseBookingComEmailPairs(bookingComEmails));

        return result;
    }

    // -----------------------------------------------------------------------
    // Booking.com two-email pairing
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns true when the email is a Booking.com new-booking confirmation.
    /// Subject format: "Booking.com - New booking! (5474030366, Monday, December 21, 2026)"
    /// </summary>
    private static bool IsBookingComConfirmationEmail(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        return Regex.IsMatch(subject, @"Booking\.com\s*-\s*New booking!", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns true when the email is a Booking.com booking request (pre-confirmation).
    /// Subject format: "New booking request – accept or decline by 10:45 AM on Apr 13, 2026"
    /// </summary>
    private static bool IsBookingComRequestEmail(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        return Regex.IsMatch(subject, @"New booking request", RegexOptions.IgnoreCase) &&
               Regex.IsMatch(subject, @"accept or decline", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Parses a Booking.com confirmation email.
    /// Returns a partial <see cref="Booking"/> with BookingReference and CheckInDate populated.
    /// PropertyId is extracted from the content when available.
    /// </summary>
    private Booking? ParseBookingComConfirmationEmail(EmailMessage email)
    {
        var subject = email.Subject ?? "";
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");

        // Subject: "Booking.com - New booking! (5474030366, Monday, December 21, 2026)"
        var subjectMatch = Regex.Match(subject,
            @"New booking!\s*\((\d+),\s*\w+,\s*([A-Za-z]+ \d{1,2},\s*\d{4})\)",
            RegexOptions.IgnoreCase);

        if (!subjectMatch.Success)
        {
            _logger.LogWarning($"Could not parse Booking.com confirmation subject: {subject}");
            return null;
        }

        var booking = new Booking { Platform = "bookingcom" };
        booking.BookingReference = subjectMatch.Groups[1].Value;

        if (DateTime.TryParse(subjectMatch.Groups[2].Value, out var checkIn))
            booking.CheckInDate = checkIn;

        // Property ID from content. Booking.com extranet links include the hotel/property ID
        // in several common forms:
        //   hotel_id=XXXXXXXX
        //   hotelid=XXXXXXXX
        //   /hotels/XXXXXXXX
        //   property: XXXXXXXX  (plain-text fallback)
        var propMatch = Regex.Match(content, @"hotel[_]?id[=:]\s*(\d+)", RegexOptions.IgnoreCase);
        if (!propMatch.Success)
            propMatch = Regex.Match(content, @"/hotels?/(\d{6,})", RegexOptions.IgnoreCase);
        if (!propMatch.Success)
            propMatch = Regex.Match(content, @"property[:\s/]+(\d{6,})", RegexOptions.IgnoreCase);
        if (propMatch.Success)
            booking.PropertyId = propMatch.Groups[1].Value;

        // Guest name from content
        var guestMatch = Regex.Match(content, @"guest\s+name[:\s]+([A-Z][a-z]+\s+[A-Z][a-z]+)\b", RegexOptions.IgnoreCase);
        if (guestMatch.Success)
            booking.GuestName = guestMatch.Groups[1].Value.Trim();

        _logger.LogInformation($"Parsed Booking.com confirmation – Reference: '{booking.BookingReference}', CheckIn: {booking.CheckInDate:yyyy-MM-dd}, PropertyId: '{booking.PropertyId}'");
        return booking;
    }

    /// <summary>
    /// Parses a Booking.com booking-request email (the pre-acceptance email).
    /// Returns a partial <see cref="Booking"/> with CheckInDate, CheckOutDate, and NumberOfGuests.
    /// </summary>
    private Booking? ParseBookingComRequestEmail(EmailMessage email)
    {
        var content = (email.HtmlBody ?? "") + " " + (email.Body ?? "");

        var booking = new Booking { Platform = "bookingcom" };

        // Check-in / Check-out appear as plain text labels followed by "Month Day, Year"
        var checkInMatch = Regex.Match(content,
            @"Check-in\s+([A-Za-z]+ \d{1,2},\s*\d{4})",
            RegexOptions.IgnoreCase);
        var checkOutMatch = Regex.Match(content,
            @"Check-out\s+([A-Za-z]+ \d{1,2},\s*\d{4})",
            RegexOptions.IgnoreCase);

        if (checkInMatch.Success && DateTime.TryParse(checkInMatch.Groups[1].Value, out var checkIn))
            booking.CheckInDate = checkIn;

        if (checkOutMatch.Success && DateTime.TryParse(checkOutMatch.Groups[1].Value, out var checkOut))
            booking.CheckOutDate = checkOut;

        if (booking.CheckInDate == default || booking.CheckOutDate == default)
        {
            _logger.LogWarning($"Could not extract dates from Booking.com request email: {email.Subject}");
            return null;
        }

        // Number of guests (adults + children)
        var adultsMatch = Regex.Match(content, @"(\d+)\s+adults?", RegexOptions.IgnoreCase);
        var childrenMatch = Regex.Match(content, @"(\d+)\s+child(?:ren)?", RegexOptions.IgnoreCase);
        int guests = 0;
        if (adultsMatch.Success && int.TryParse(adultsMatch.Groups[1].Value, out var adults))
            guests += adults;
        if (childrenMatch.Success && int.TryParse(childrenMatch.Groups[1].Value, out var children))
            guests += children;
        if (guests > 0)
            booking.NumberOfGuests = guests;

        _logger.LogInformation($"Parsed Booking.com request – CheckIn: {booking.CheckInDate:yyyy-MM-dd}, CheckOut: {booking.CheckOutDate:yyyy-MM-dd}, Guests: {booking.NumberOfGuests}");
        return booking;
    }

    private List<(Booking Booking, List<EmailMessage> SourceEmails)> ParseBookingComEmailPairs(
        IEnumerable<EmailMessage> emails)
    {
        var confirmations = new List<EmailMessage>();
        var requests = new List<EmailMessage>();

        foreach (var email in emails)
        {
            if (IsBookingComConfirmationEmail(email))
                confirmations.Add(email);
            else if (IsBookingComRequestEmail(email))
                requests.Add(email);
            else
                _logger.LogWarning($"Unrecognised Booking.com email type, skipping: {email.Subject}");
        }

        var result = new List<(Booking, List<EmailMessage>)>();

        foreach (var confirmEmail in confirmations)
        {
            try
            {
                var confirmBooking = ParseBookingComConfirmationEmail(confirmEmail);
                if (confirmBooking == null) continue;

                // Find a matching request email by check-in date
                EmailMessage? matchedRequest = null;
                Booking? requestBooking = null;

                foreach (var reqEmail in requests)
                {
                    var rb = ParseBookingComRequestEmail(reqEmail);
                    if (rb != null && rb.CheckInDate.Date == confirmBooking.CheckInDate.Date)
                    {
                        matchedRequest = reqEmail;
                        requestBooking = rb;
                        break;
                    }
                }

                if (matchedRequest == null || requestBooking == null)
                {
                    _logger.LogWarning(
                        $"No matching Booking.com request email found for confirmation '{confirmEmail.Subject}' " +
                        $"(check-in {confirmBooking.CheckInDate:yyyy-MM-dd}). Cannot create complete booking.");
                    continue;
                }

                // Merge: confirmation supplies reference; request supplies dates + guests
                confirmBooking.CheckOutDate = requestBooking.CheckOutDate;
                if (confirmBooking.NumberOfGuests == 0)
                    confirmBooking.NumberOfGuests = requestBooking.NumberOfGuests;

                _logger.LogInformation(
                    $"Merged Booking.com booking – Reference: '{confirmBooking.BookingReference}', " +
                    $"CheckIn: {confirmBooking.CheckInDate:yyyy-MM-dd}, CheckOut: {confirmBooking.CheckOutDate:yyyy-MM-dd}");

                result.Add((confirmBooking, new List<EmailMessage> { confirmEmail, matchedRequest }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error pairing Booking.com emails for: {confirmEmail.Subject}");
            }
        }

        return result;
    }
}
