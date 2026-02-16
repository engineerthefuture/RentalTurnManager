using MailKit;
using MailKit.Search;
using System.Threading.Tasks;

namespace RentalTurnManager.Core.Services;

public interface IImapClient : IDisposable
{
    IMailFolder Inbox { get; }
    Task ConnectAsync(string host, int port, bool useSsl);
    Task AuthenticateAsync(string username, string password);
    Task DisconnectAsync(bool quit);
}
