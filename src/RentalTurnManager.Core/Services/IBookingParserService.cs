using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for parsing booking information from emails
/// </summary>
public interface IBookingParserService
{
    Booking? ParseBooking(EmailMessage email);
}
