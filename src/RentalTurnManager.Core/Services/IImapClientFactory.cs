namespace RentalTurnManager.Core.Services;

public interface IImapClientFactory
{
    IImapClient CreateClient();
}
