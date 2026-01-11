/************************
 * Rental Turn Manager
 * BookingStateService.cs
 * 
 * Service that manages booking state persistence in Amazon S3. Tracks
 * booking history to detect changes and prevent duplicate workflow triggers.
 * Stores bookings as JSON files organized by platform and confirmation code.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Text;
using System.Text.Json;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for managing booking state in S3
/// </summary>
public class BookingStateService : IBookingStateService
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<BookingStateService> _logger;
    private readonly string _bucketName;
    private readonly string _keyPrefix;

    public BookingStateService(
        IAmazonS3 s3Client,
        ILogger<BookingStateService> logger,
        string bucketName,
        string keyPrefix = "bookings/")
    {
        _s3Client = s3Client;
        _logger = logger;
        _bucketName = bucketName;
        _keyPrefix = keyPrefix;
    }

    public async Task<Booking?> GetBookingAsync(string platform, string bookingReference)
    {
        try
        {
            var key = GetS3Key(platform, bookingReference);
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(request);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            
            return JsonSerializer.Deserialize<Booking>(json);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation($"Booking not found in S3: {platform}/{bookingReference}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error retrieving booking from S3: {platform}/{bookingReference}");
            throw;
        }
    }

    public async Task SaveBookingAsync(Booking booking)
    {
        try
        {
            var key = GetS3Key(booking.Platform, booking.BookingReference);
            var json = JsonSerializer.Serialize(booking, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                ContentBody = json,
                ContentType = "application/json"
            };

            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation($"Saved booking to S3: {key}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error saving booking to S3: {booking.Platform}/{booking.BookingReference}");
            throw;
        }
    }

    public async Task<bool> HasBookingChangedAsync(Booking newBooking)
    {
        var existingBooking = await GetBookingAsync(newBooking.Platform, newBooking.BookingReference);
        
        if (existingBooking == null)
        {
            _logger.LogInformation($"Booking is new: {newBooking.Platform}/{newBooking.BookingReference}");
            return true; // New booking
        }

        // Compare relevant fields to determine if booking has changed
        var hasChanged = 
            existingBooking.PropertyId != newBooking.PropertyId ||
            existingBooking.CheckInDate != newBooking.CheckInDate ||
            existingBooking.CheckOutDate != newBooking.CheckOutDate ||
            existingBooking.NumberOfGuests != newBooking.NumberOfGuests ||
            existingBooking.GuestName != newBooking.GuestName;

        if (hasChanged)
        {
            _logger.LogInformation($"Booking has changed: {newBooking.Platform}/{newBooking.BookingReference}");
            _logger.LogInformation($"  PropertyId: {existingBooking.PropertyId} -> {newBooking.PropertyId}");
            _logger.LogInformation($"  CheckIn: {existingBooking.CheckInDate:yyyy-MM-dd} -> {newBooking.CheckInDate:yyyy-MM-dd}");
            _logger.LogInformation($"  CheckOut: {existingBooking.CheckOutDate:yyyy-MM-dd} -> {newBooking.CheckOutDate:yyyy-MM-dd}");
            _logger.LogInformation($"  Guests: {existingBooking.NumberOfGuests} -> {newBooking.NumberOfGuests}");
            _logger.LogInformation($"  GuestName: {existingBooking.GuestName} -> {newBooking.GuestName}");
        }
        else
        {
            _logger.LogInformation($"Booking unchanged: {newBooking.Platform}/{newBooking.BookingReference}");
        }

        return hasChanged;
    }

    public async Task DeleteBookingAsync(string platform, string bookingReference)
    {
        try
        {
            var key = GetS3Key(platform, bookingReference);
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(request);
            _logger.LogInformation($"Deleted booking from S3: {key}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error deleting booking from S3: {platform}/{bookingReference}");
            throw;
        }
    }

    private string GetS3Key(string platform, string bookingReference)
    {
        // Sanitize booking reference for use in S3 key
        var sanitizedRef = bookingReference.Replace("/", "_").Replace("\\", "_");
        return $"{_keyPrefix}{platform}/{sanitizedRef}.json";
    }
}
