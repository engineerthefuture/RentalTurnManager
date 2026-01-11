/************************
 * Rental Turn Manager
 * ISecretsService.cs
 * 
 * Interface for secrets management service that retrieves credentials from
 * AWS Secrets Manager. Defines contract for accessing email credentials and
 * other sensitive configuration values.
 * 
 * Author: Brent Foster
 * Created: 01-11-2026
 ***********************/

using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for retrieving secrets from AWS Secrets Manager
/// </summary>
public interface ISecretsService
{
    Task<EmailCredentials> GetEmailCredentialsAsync();
}
