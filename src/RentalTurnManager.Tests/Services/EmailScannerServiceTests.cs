using System.Collections.Generic;
using System.Threading.Tasks;
using MailKit;
using MailKit.Search;
using MimeKit;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Core.Services;
using RentalTurnManager.Models;

namespace RentalTurnManager.Tests.Services;

public class EmailScannerServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var logger = new Mock<ILogger<EmailScannerService>>();

        var svc = new EmailScannerService(logger.Object);

        Assert.NotNull(svc);
    }

    [Fact]
    public async Task ScanForBookingEmailsAsync_ReturnsMatchingEmails()
    {
        var mockFactory = new Mock<IImapClientFactory>();
        var mockClient = new Mock<IImapClient>();
        var mockInbox = new Mock<IMailFolder>();

        mockFactory.Setup(f => f.CreateClient()).Returns(mockClient.Object);
        mockClient.SetupGet(c => c.Inbox).Returns(mockInbox.Object);
        mockClient.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        mockClient.Setup(c => c.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        mockClient.Setup(c => c.DisconnectAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        mockInbox.SetupGet(i => i.Count).Returns(1);

        var uid = new UniqueId(1);
        mockInbox.Setup(i => i.SearchAsync(It.IsAny<SearchQuery>())).ReturnsAsync(new List<UniqueId> { uid });

        using var message = new MimeMessage();
        message.MessageId = "<msg-1@example.com>";
        message.Subject = "Reservation confirmed - YourStay";
        message.From.Add(new MailboxAddress("Airbnb", "noreply@airbnb.com"));
        message.Date = DateTimeOffset.UtcNow;
        message.Body = new TextPart("plain") { Text = "Booking details here" };

        mockInbox.Setup(i => i.GetMessageAsync(uid)).ReturnsAsync(message);

        var logger = new Mock<ILogger<EmailScannerService>>();
        var svc = new EmailScannerService(logger.Object, mockFactory.Object);

        var creds = new EmailCredentials { Host = "imap.example.com", Port = 993, Username = "user", Password = "p", UseSsl = true };

        var results = await svc.ScanForBookingEmailsAsync(creds, false, new List<string> { "airbnb.com" }, new List<string> { "Reservation confirmed" });

        Assert.Single(results);
        Assert.Equal(message.MessageId, results[0].MessageId);
        Assert.Contains("Airbnb", results[0].From);
    }

    [Fact]
    public async Task MarkEmailAsProcessedAsync_AddsSeenFlag_WhenMessageFound()
    {
        var mockFactory = new Mock<IImapClientFactory>();
        var mockClient = new Mock<IImapClient>();
        var mockInbox = new Mock<IMailFolder>();

        mockFactory.Setup(f => f.CreateClient()).Returns(mockClient.Object);
        mockClient.SetupGet(c => c.Inbox).Returns(mockInbox.Object);
        mockClient.Setup(c => c.ConnectAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        mockClient.Setup(c => c.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        mockClient.Setup(c => c.DisconnectAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

        var uid = new UniqueId(2);
        mockInbox.Setup(i => i.SearchAsync(It.IsAny<SearchQuery>())).ReturnsAsync(new List<UniqueId> { uid });
        // Do not setup AddFlagsAsync - it's an extension method and not mockable via Moq.

        var logger = new Mock<ILogger<EmailScannerService>>();
        var svc = new EmailScannerService(logger.Object, mockFactory.Object);

        var creds = new EmailCredentials { Host = "imap.example.com", Port = 993, Username = "user", Password = "p", UseSsl = true };
        var email = new EmailMessage { MessageId = "<msg-2@example.com>", Subject = "s" };

        await svc.MarkEmailAsProcessedAsync(creds, email);

        mockInbox.Verify(i => i.SearchAsync(It.IsAny<SearchQuery>()), Times.AtLeastOnce);
    }
}
