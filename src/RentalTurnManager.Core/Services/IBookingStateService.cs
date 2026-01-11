/************************
 * Rental Turn Manager
 * IBookingStateService.cs
 * 
 * Interface for booking state management service that tracks booking history
 * in Amazon S3. Defines contract for storing, retrieving, and comparing
 * bookings to detect changes and prevent duplicate processing.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

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
