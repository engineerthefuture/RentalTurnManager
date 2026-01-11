/************************
 * Rental Turn Manager
 * EmailModels.cs
 * 
 * Data models for email and booking information. Defines structures for
 * parsed bookings, email messages, and email credentials used throughout
 * the application for email scanning and booking processing.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

namespace RentalTurnManager.Models;

/// <summary>
/// Represents a parsed booking from an email
/// </summary>
public class Booking
{
    public string Platform { get; set; } = string.Empty; // airbnb, vrbo, bookingcom
    public string BookingReference { get; set; } = string.Empty;
    public string PropertyId { get; set; } = string.Empty;
    public DateTime CheckInDate { get; set; }
    public DateTime CheckOutDate { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public int NumberOfGuests { get; set; }
    
    /// <summary>
    /// Number of days in the booking (CheckOut - CheckIn)
    /// </summary>
    public int NumberOfDays => (CheckOutDate - CheckInDate).Days;
}

/// <summary>
/// Represents an email message
/// </summary>
public class EmailMessage
{
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Body { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }
}

/// <summary>
/// Email account credentials
/// </summary>
public class EmailCredentials
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; } = true;
}
