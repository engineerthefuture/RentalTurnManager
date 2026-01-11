/************************
 * Rental Turn Manager
 * IEmailScannerService.cs
 * 
 * Interface for email scanner service that connects to email inbox via IMAP
 * and retrieves booking confirmation emails. Defines contract for filtering
 * and scanning emails from rental platforms.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

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
