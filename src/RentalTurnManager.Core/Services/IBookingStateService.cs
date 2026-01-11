using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for managing booking state in S3
/// </summary>
public interface IBookingStateService
{
    /// <summary>
    /// Retrieve a previously saved booking by its reference
    /// </summary>
    Task<Booking?> GetBookingAsync(string platform, string bookingReference);

    /// <summary>
    /// Save or update a booking record
    /// </summary>
    Task SaveBookingAsync(Booking booking);

    /// <summary>
    /// Check if a booking has changed compared to the saved version
    /// </summary>
    Task<bool> HasBookingChangedAsync(Booking newBooking);

    /// <summary>
    /// Delete a booking record
    /// </summary>
    Task DeleteBookingAsync(string platform, string bookingReference);
}
