using RentalTurnManager.Models;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Service for retrieving secrets from AWS Secrets Manager
/// </summary>
public interface ISecretsService
{
    Task<EmailCredentials> GetEmailCredentialsAsync();
}
