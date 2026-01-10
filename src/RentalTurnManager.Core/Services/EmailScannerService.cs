using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using MimeKit;
using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Implementation of email scanner service using MailKit/IMAP
/// </summary>
public class EmailScannerService : IEmailScannerService
{
    private readonly ILogger<EmailScannerService> _logger;
    private const string ProcessedLabel = "RentalTurnManager/Processed";

    public EmailScannerService(ILogger<EmailScannerService> logger)
    {
        _logger = logger;
    }

    public async Task<List<EmailMessage>> ScanForBookingEmailsAsync(EmailCredentials credentials)
    {
        var emails = new List<EmailMessage>();

        try
        {
            using var client = new ImapClient();
            
            _logger.LogInformation($"Connecting to IMAP server {credentials.Host}:{credentials.Port}");
            await client.ConnectAsync(credentials.Host, credentials.Port, credentials.UseSsl);
            await client.AuthenticateAsync(credentials.Username, credentials.Password);

            _logger.LogInformation("Successfully connected to IMAP server");

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite);

            _logger.LogInformation($"Inbox has {inbox.Count} messages");

            // Search for unread emails from booking platforms
            var searchQuery = SearchQuery.NotSeen.And(
                SearchQuery.Or(
                    SearchQuery.Or(
                        SearchQuery.FromContains("airbnb.com"),
                        SearchQuery.FromContains("vrbo.com")
                    ),
                    SearchQuery.FromContains("booking.com")
                )
            );

            var uids = await inbox.SearchAsync(searchQuery);
            _logger.LogInformation($"Found {uids.Count} unread booking emails");

            foreach (var uid in uids)
            {
                try
                {
                    var message = await inbox.GetMessageAsync(uid);
                    
                    var emailMessage = new EmailMessage
                    {
                        MessageId = message.MessageId,
                        Subject = message.Subject ?? string.Empty,
                        From = message.From.ToString(),
                        Date = message.Date.UtcDateTime,
                        Body = message.TextBody ?? string.Empty,
                        HtmlBody = message.HtmlBody ?? string.Empty,
                        IsProcessed = false
                    };

                    emails.Add(emailMessage);
                    _logger.LogInformation($"Retrieved email: {emailMessage.Subject}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error retrieving email with UID {uid}");
                }
            }

            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning emails");
            throw;
        }

        return emails;
    }

    public async Task MarkEmailAsProcessedAsync(EmailCredentials credentials, EmailMessage email)
    {
        try
        {
            using var client = new ImapClient();
            
            await client.ConnectAsync(credentials.Host, credentials.Port, credentials.UseSsl);
            await client.AuthenticateAsync(credentials.Username, credentials.Password);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite);

            // Search for the email by Message-ID
            var query = SearchQuery.HeaderContains("Message-ID", email.MessageId);
            var uids = await inbox.SearchAsync(query);

            if (uids.Count > 0)
            {
                // Mark as seen (read)
                await inbox.AddFlagsAsync(uids, MessageFlags.Seen, true);
                _logger.LogInformation($"Marked email as processed: {email.Subject}");
            }

            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error marking email as processed: {email.Subject}");
            // Don't throw - this is not critical
        }
    }
}
