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
        var response = new GetObjectResponse
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
        var response = new GetObjectResponse
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
}
