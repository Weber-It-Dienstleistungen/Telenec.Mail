using MailKit.Net.Imap;
using MailKit.Security;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitQuotaService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan
        ConnectionTimeout =
            TimeSpan.FromSeconds(15);

    private static readonly TimeSpan
        AuthenticationTimeout =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        QuotaTimeout =
            TimeSpan.FromSeconds(15);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public MailKitQuotaService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
    }

    public async Task<MailQuotaInfo?>
        GetQuotaAsync(
            CancellationToken cancellationToken = default)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            return null;
        }

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrWhiteSpace(
                credential.UserName) ||
            string.IsNullOrEmpty(
                credential.Password))
        {
            return null;
        }

        using var client =
            new ImapClient();

        try
        {
            using (
                var connectionTimeoutSource =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                connectionTimeoutSource
                    .CancelAfter(
                        ConnectionTimeout);

                await client
                    .ConnectAsync(
                        ImapHost,
                        ImapPort,
                        SecureSocketOptions.SslOnConnect,
                        connectionTimeoutSource.Token);
            }

            using (
                var authenticationTimeoutSource =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                authenticationTimeoutSource
                    .CancelAfter(
                        AuthenticationTimeout);

                await client
                    .AuthenticateAsync(
                        credential.UserName,
                        credential.Password,
                        authenticationTimeoutSource.Token);
            }

            /*
             * Unterstützt der IMAP-Server QUOTA überhaupt
             * nicht, können wir weder ein Limit noch einen
             * expliziten Unlimited-Zustand zuverlässig
             * feststellen.
             */
            if (!client.SupportsQuotas)
            {
                return null;
            }

            MailKit.FolderQuota quota;

            using (
                var quotaTimeoutSource =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken))
            {
                quotaTimeoutSource
                    .CancelAfter(
                        QuotaTimeout);

                quota =
                    await client
                        .Inbox
                        .GetQuotaAsync(
                            quotaTimeoutSource.Token);
            }

            /*
             * IMAP QUOTA STORAGE wird von MailKit in
             * Kilobyte bereitgestellt.
             *
             * Wichtig:
             *
             * Ein fehlendes StorageLimit ist KEIN Fehler.
             * Es kann schlicht bedeuten, dass für dieses
             * Postfach kein Speicherlimit gesetzt wurde.
             */
            long? usedBytes =
                quota.CurrentStorageSize.HasValue
                    ? (long)quota
                          .CurrentStorageSize
                          .Value *
                      1024L
                    : null;

            long? limitBytes =
                quota.StorageLimit.HasValue &&
                quota.StorageLimit.Value > 0
                    ? (long)quota
                          .StorageLimit
                          .Value *
                      1024L
                    : null;

            return new MailQuotaInfo(
                UsedBytes:
                    usedBytes,

                LimitBytes:
                    limitBytes);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            /*
             * Die Quota-Anzeige ist eine Komfortfunktion.
             *
             * Netzwerk-, Server- oder QUOTA-Fehler dürfen
             * niemals den normalen Mailbetrieb beeinträchtigen.
             */
            return null;
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client
                        .DisconnectAsync(
                            true,
                            CancellationToken.None);
                }
                catch
                {
                    /*
                     * Fehler beim Disconnect verändern das
                     * Quota-Ergebnis nicht.
                     */
                }
            }
        }
    }
}