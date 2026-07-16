using Xunit;
using Moq;
using FluentAssertions;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;

namespace RentalTurnManager.Tests.Services;

public class BookingStateServiceTests
{
    private readonly Mock<IAmazonS3> _mockS3;
    private readonly Mock<ILogger<BookingStateService>> _mockLogger;
    private readonly BookingStateService _service;

    public BookingStateServiceTests()
    {
        _mockS3 = new Mock<IAmazonS3>();
        _mockLogger = new Mock<ILogger<BookingStateService>>();
        _service = new BookingStateService(_mockS3.Object, _mockLogger.Object, "test-bucket", "bookings/");
    }

    [Fact]
    public async Task GetBookingAsync_ReturnsBooking_WhenObjectExists()
    {
        var booking = new Booking
        {
            BookingReference = "REF123",
            Platform = "airbnb",
            PropertyId = "prop-1"
        };

        var json = JsonSerializer.Serialize(booking);
        using var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(response);

        var result = await _service.GetBookingAsync("airbnb", "REF123");

        result.Should().NotBeNull();
        result!.BookingReference.Should().Be("REF123");
    }

    [Fact]
    public async Task GetBookingAsync_ReturnsNull_WhenNotFound()
    {
        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("Not found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        var result = await _service.GetBookingAsync("airbnb", "MISSING");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveBookingAsync_CallsPutObject()
    {
        var booking = new Booking
        {
            BookingReference = "REF456",
            Platform = "vrbo",
            PropertyId = "prop-2"
        };

        _mockS3
            .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse());

        await _service.SaveBookingAsync(booking);

        _mockS3.Verify(x => x.PutObjectAsync(It.Is<PutObjectRequest>(r => r.BucketName == "test-bucket" && r.Key.Contains("vrbo/REF456")), default), Times.Once);
    }

    [Fact]
    public async Task HasBookingChangedAsync_ReturnsTrue_WhenNew()
    {
        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ThrowsAsync(new AmazonS3Exception("Not found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        var newBooking = new Booking { BookingReference = "NEW", Platform = "airbnb" };

        var result = await _service.HasBookingChangedAsync(newBooking);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasBookingChangedAsync_ReturnsFalse_WhenUnchanged()
    {
        var existing = new Booking
        {
            BookingReference = "SAME",
            Platform = "airbnb",
            PropertyId = "p1",
            CheckInDate = new System.DateTime(2026,1,1),
            CheckOutDate = new System.DateTime(2026,1,3),
            NumberOfGuests = 2,
            GuestName = "Alice"
        };

        var json = JsonSerializer.Serialize(existing);
        using var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(response);

        var result = await _service.HasBookingChangedAsync(existing);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBookingAsync_CallsDeleteObject()
    {
        _mockS3
            .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), default))
            .ReturnsAsync(new DeleteObjectResponse());

        await _service.DeleteBookingAsync("airbnb", "TODELETE");

        _mockS3.Verify(x => x.DeleteObjectAsync(It.Is<DeleteObjectRequest>(r => r.BucketName == "test-bucket" && r.Key.Contains("airbnb/TODELETE")), default), Times.Once);
    }

    [Fact]
    public async Task HasBookingChangedAsync_ReturnsFalse_WhenConfirmedBookingDatesShiftExactlyOneYear()
    {
        // Regression test: a booking that has already been confirmed (CleanerConfirmedAt set)
        // should NOT be considered "changed" when the only difference is dates moving forward
        // by exactly 1 year. This is the signature of a year-inference re-parse artifact.
        var checkIn = new System.DateTime(2026, 6, 15);
        var checkOut = new System.DateTime(2026, 6, 17);

        var existing = new Booking
        {
            BookingReference = "HMCAWCRT3K",
            Platform = "airbnb",
            PropertyId = "1477018601970190586",
            CheckInDate = checkIn,
            CheckOutDate = checkOut,
            NumberOfGuests = 4,
            GuestName = "Jennifer Mang",
            CleanerConfirmedAt = new System.DateTime(2026, 6, 15, 3, 38, 2, System.DateTimeKind.Utc)
        };

        var reparsed = new Booking
        {
            BookingReference = "HMCAWCRT3K",
            Platform = "airbnb",
            PropertyId = "1477018601970190586",
            CheckInDate = checkIn.AddYears(1),   // 2027-06-15 — the artifact
            CheckOutDate = checkOut.AddYears(1),  // 2027-06-17
            NumberOfGuests = 4,
            GuestName = "Jennifer Mang"
        };

        var json = JsonSerializer.Serialize(existing);
        using var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(response);

        var result = await _service.HasBookingChangedAsync(reparsed);

        result.Should().BeFalse("a confirmed booking's dates must not be overwritten by a 1-year shift artifact");
    }

    [Fact]
    public async Task HasBookingChangedAsync_ReturnsTrue_WhenConfirmedBookingDatesChangeByMoreThanOneYear()
    {
        // A legitimate date change (not exactly 1 year) on a confirmed booking should still
        // be detected so genuine modifications are not suppressed.
        var existing = new Booking
        {
            BookingReference = "HMLEGIT999",
            Platform = "airbnb",
            PropertyId = "prop-1",
            CheckInDate = new System.DateTime(2026, 6, 15),
            CheckOutDate = new System.DateTime(2026, 6, 17),
            NumberOfGuests = 2,
            GuestName = "Test Guest",
            CleanerConfirmedAt = System.DateTime.UtcNow
        };

        var updated = new Booking
        {
            BookingReference = "HMLEGIT999",
            Platform = "airbnb",
            PropertyId = "prop-1",
            CheckInDate = new System.DateTime(2026, 7, 10), // different month/day, not +1 year
            CheckOutDate = new System.DateTime(2026, 7, 12),
            NumberOfGuests = 2,
            GuestName = "Test Guest"
        };

        var json = JsonSerializer.Serialize(existing);
        using var response = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(json))
        };

        _mockS3
            .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), default))
            .ReturnsAsync(response);

        var result = await _service.HasBookingChangedAsync(updated);

        result.Should().BeTrue("a genuine date change must still be detected even for confirmed bookings");
    }
}
