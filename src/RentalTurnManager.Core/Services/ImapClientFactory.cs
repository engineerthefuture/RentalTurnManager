namespace RentalTurnManager.Core.Services;

public class ImapClientFactory : IImapClientFactory
{
    public IImapClient CreateClient() => new ImapClientAdapter();
}
