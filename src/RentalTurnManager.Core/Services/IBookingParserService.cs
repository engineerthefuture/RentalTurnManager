/************************
 * Rental Turn Manager
 * IBookingParserService.cs
 * 
 * Interface for booking parser service that extracts booking information
 * from platform-specific email messages. Defines contract for parsing
 * confirmation codes, dates, guests, and property details.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for parsing booking information from emails
/// </summary>
public interface IBookingParserService
{
    Booking? ParseBooking(EmailMessage email);
}
