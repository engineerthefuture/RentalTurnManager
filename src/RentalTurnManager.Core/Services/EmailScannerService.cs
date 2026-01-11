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

    public async Task<List<EmailMessage>> ScanForBookingEmailsAsync(EmailCredentials credentials, bool forceRescan = false, List<string>? platformFromAddresses = null)
    {
        var emails = new List<EmailMessage>();
        
        // Use provided addresses or fall back to defaults
        var fromAddresses = platformFromAddresses ?? new List<string> { "airbnb.com", "vrbo.com", "booking.com" };

        try
        {
            using var client = new ImapClient();
            
            _logger.LogInformation($"Connecting to IMAP server {credentials.Host}:{credentials.Port} (SSL: {credentials.UseSsl})");
            await client.ConnectAsync(credentials.Host, credentials.Port, credentials.UseSsl);
            
            _logger.LogInformation($"Authenticating as user: {credentials.Username}");
            try
            {
                await client.AuthenticateAsync(credentials.Username, credentials.Password);
            }
            catch (MailKit.Security.AuthenticationException authEx)
            {
                _logger.LogError(authEx, $"Authentication failed for {credentials.Username} at {credentials.Host}. " +
                    "Common fixes: " +
                    "1) Gmail: Use app-specific password (https://myaccount.google.com/apppasswords), " +
                    "2) iCloud: Generate app-specific password at appleid.apple.com (Account Security > App-Specific Passwords), " +
                    "3) Outlook: Verify IMAP is enabled, " +
                    "4) Check username format (often requires full email address)");
                throw;
            }

            _logger.LogInformation("Successfully connected to IMAP server");

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite);

            _logger.LogInformation($"Inbox has {inbox.Count} messages");

            // Debug: Log all messages to see what we have
            if (inbox.Count > 0)
            {
                _logger.LogInformation("Analyzing all messages in inbox:");
                var allUids = await inbox.SearchAsync(SearchQuery.All);
                foreach (var uid in allUids.Take(5)) // Log first 5 messages
                {
                    try
                    {
                        var msg = await inbox.GetMessageAsync(uid);
                        _logger.LogInformation($"  UID {uid}: From='{msg.From}', Subject='{msg.Subject}'");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"  UID {uid}: Error reading message - {ex.Message}");
                    }
                }
            }

            // Search for emails from booking platforms
            SearchQuery baseQuery;
            
            // Check if wildcard (*) is used to match all emails
            if (fromAddresses.Contains("*"))
            {
                _logger.LogInformation("Using wildcard (*) - scanning all emails");
                baseQuery = SearchQuery.All;
            }
            else if (fromAddresses.Count == 1)
            {
                baseQuery = SearchQuery.FromContains(fromAddresses[0]);
            }
            else
            {
                baseQuery = fromAddresses
                    .Skip(1)
                    .Aggregate<string, SearchQuery>(
                        SearchQuery.FromContains(fromAddresses[0]),
                        (query, address) => query.Or(SearchQuery.FromContains(address))
                    );
            }

            // If not force rescanning, only get unread emails
            var searchQuery = forceRescan ? baseQuery : SearchQuery.NotSeen.And(baseQuery);

            var uids = await inbox.SearchAsync(searchQuery);
            _logger.LogInformation($"Found {uids.Count} {(forceRescan ? "" : "unread ")}booking emails");

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
