using MailKit;
using MailKit.Net.Imap;
using System.Threading.Tasks;

namespace RentalTurnManager.Core.Services;

public class ImapClientAdapter : IImapClient
{
    private readonly ImapClient _client = new();

    public IMailFolder Inbox => _client.Inbox;

    public Task ConnectAsync(string host, int port, bool useSsl)
        => _client.ConnectAsync(host, port, useSsl);

    public Task AuthenticateAsync(string username, string password)
        => _client.AuthenticateAsync(username, password);

    public Task DisconnectAsync(bool quit)
        => _client.DisconnectAsync(quit);

    public void Dispose()
    {
        _client?.Dispose();
    }
}
