using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using RentalTurnManager.Models;
using System.Text.Json;

namespace RentalTurnManager.Core.Services;

/// <summary>
/// Implementation of secrets service using AWS Secrets Manager
/// </summary>
public class SecretsService : ISecretsService
{
    private readonly IAmazonSecretsManager _secretsManager;
    private readonly ILogger<SecretsService> _logger;
    private readonly string _emailSecretName;

    public SecretsService(IAmazonSecretsManager secretsManager, ILogger<SecretsService> logger)
    {
        _secretsManager = secretsManager;
        _logger = logger;
        _emailSecretName = Environment.GetEnvironmentVariable("EMAIL_SECRET_NAME") 
            ?? "RentalTurnManager/EmailCredentials";
    }

    public async Task<EmailCredentials> GetEmailCredentialsAsync()
    {
        try
        {
            _logger.LogInformation($"Retrieving email credentials from Secrets Manager: {_emailSecretName}");

            var request = new GetSecretValueRequest
            {
                SecretId = _emailSecretName
            };

            var response = await _secretsManager.GetSecretValueAsync(request);
            var secretJson = response.SecretString;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var credentials = JsonSerializer.Deserialize<EmailCredentials>(secretJson, options);
            
            if (credentials == null)
            {
                throw new InvalidOperationException("Failed to deserialize email credentials");
            }

            _logger.LogInformation($"Successfully retrieved credentials for {credentials.Host}:{credentials.Port}");
            return credentials;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve email credentials from Secrets Manager");
            throw;
        }
    }
}
