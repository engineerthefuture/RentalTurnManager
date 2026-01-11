using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for scanning IMAP emails
/// </summary>
public interface IEmailScannerService
{
    Task<List<EmailMessage>> ScanForBookingEmailsAsync(EmailCredentials credentials, bool forceRescan = false, List<string>? platformFromAddresses = null, List<string>? subjectPatterns = null);
    Task MarkEmailAsProcessedAsync(EmailCredentials credentials, EmailMessage email);
}
