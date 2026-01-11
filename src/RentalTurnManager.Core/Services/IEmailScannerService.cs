using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for scanning IMAP emails
/// </summary>
public interface IEmailScannerService
{
    Task<List<EmailMessage>> ScanForBookingEmailsAsync(EmailCredentials credentials, bool forceRescan = false);
    Task MarkEmailAsProcessedAsync(EmailCredentials credentials, EmailMessage email);
}
