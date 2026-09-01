using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitDraftCleanupService :
    IMailDraftCleanupService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan CleanupTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IMailAccountStore _mailAccountStore;
    private readonly ICredentialStore _credentialStore;

    public MailKitDraftCleanupService(
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

    public async Task<bool> TryDeleteDraftAsync(
        string folderId,
        uint uniqueId,
        string expectedMessageId,
        CancellationToken cancellationToken = default)
    {
        /*
         * Dieser Dienst wird bewusst nach einer bereits
         * erfolgreich abgeschlossenen Aktion verwendet:
         *
         * - neuer Draft wurde bereits gespeichert
         * - oder E-Mail wurde bereits versendet
         *
         * Ein Fehler beim Cleanup darf deshalb niemals dazu
         * führen, dass wir behaupten, die vorherige Aktion
         * sei nicht erfolgt.
         *
         * Stattdessen liefert der Dienst false zurück.
         */

        if (string.IsNullOrWhiteSpace(
                folderId) ||
            uniqueId == 0 ||
            string.IsNullOrWhiteSpace(
                expectedMessageId))
        {
            return false;
        }

        var normalizedFolderId =
            folderId.Trim();

        var normalizedExpectedMessageId =
            expectedMessageId.Trim();

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync(
                        cancellationToken);

            if (account is null ||
                string.IsNullOrWhiteSpace(
                    account.EmailAddress))
            {
                return false;
            }

            var credential =
                await _credentialStore
                    .ReadAsync(
                        account.AccountId,
                        cancellationToken);

            if (credential is null ||
                string.IsNullOrWhiteSpace(
                    credential.Password))
            {
                return false;
            }

            using var client =
                await CreateAuthenticatedClientAsync(
                    account.EmailAddress,
                    credential.Password,
                    cancellationToken);

            try
            {
                using var cleanupTimeoutSource =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                cleanupTimeoutSource.CancelAfter(
                    CleanupTimeout);

                var operationCancellationToken =
                    cleanupTimeoutSource.Token;

                var folder =
                    await client.GetFolderAsync(
                        normalizedFolderId,
                        operationCancellationToken);

                if (folder.Attributes.HasFlag(
                        FolderAttributes.NoSelect))
                {
                    return false;
                }

                /*
                 * Wir löschen ausschließlich aus einem
                 * eindeutig erkannten Entwürfe-Ordner.
                 *
                 * Ein Fehler in der Aufruferlogik darf also
                 * niemals dazu führen, dass eine normale Mail
                 * permanent entfernt wird.
                 */
                if (!IsDraftFolder(
                        client,
                        folder))
                {
                    return false;
                }

                await folder.OpenAsync(
                    FolderAccess.ReadWrite,
                    operationCancellationToken);

                var sourceUniqueId =
                    new UniqueId(
                        uniqueId);

                var summaries =
                    await folder.FetchAsync(
                        new[]
                        {
                            sourceUniqueId
                        },
                        MessageSummaryItems.UniqueId |
                        MessageSummaryItems.Envelope,
                        operationCancellationToken);

                var summary =
                    summaries.FirstOrDefault();

                /*
                 * Wenn die alte UID gar nicht mehr vorhanden
                 * ist, ist unser gewünschter Endzustand
                 * bereits erreicht:
                 *
                 * Der alte Draft existiert nicht mehr.
                 */
                if (summary is null ||
                    !summary.UniqueId.IsValid)
                {
                    return true;
                }

                var currentMessageId =
                    summary.Envelope?
                        .MessageId?
                        .Trim();

                /*
                 * Niemals allein aufgrund einer UID löschen.
                 *
                 * Die Nachricht unter dieser UID muss noch
                 * exakt dieselbe Message-ID besitzen, die beim
                 * Öffnen des Drafts verifiziert wurde.
                 */
                if (string.IsNullOrWhiteSpace(
                        currentMessageId) ||
                    !string.Equals(
                        currentMessageId,
                        normalizedExpectedMessageId,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                /*
                 * Erst jetzt darf die Nachricht als gelöscht
                 * markiert werden.
                 */
                await folder.AddFlagsAsync(
                    sourceUniqueId,
                    MessageFlags.Deleted,
                    silent: true,
                    operationCancellationToken);

                /*
                 * Selektives Expunge ausschließlich für die
                 * zuvor verifizierte UID.
                 *
                 * Kein allgemeines Expunge(), damit andere
                 * eventuell bereits als \\Deleted markierte
                 * Nachrichten nicht unbeabsichtigt betroffen
                 * sind.
                 */
                await folder.ExpungeAsync(
                    new[]
                    {
                        sourceUniqueId
                    },
                    operationCancellationToken);

                return true;
            }
            finally
            {
                await DisconnectSafelyAsync(
                    client);
            }
        }
        catch
        {
            /*
             * Bewusst kein Throw:
             *
             * Der Cleanup erfolgt erst NACH einer sicheren
             * Speicherung bzw. einem erfolgreichen Versand.
             *
             * Ein Fehler bedeutet daher:
             *
             * "Neue Version ist sicher, alte Version konnte
             *  nicht entfernt werden."
             *
             * Dieser Zustand wird später in der UI als
             * Warnung behandelt.
             */
            return false;
        }
    }

    private static bool IsDraftFolder(
        ImapClient client,
        IMailFolder folder)
    {
        var specialUseDrafts =
            client.GetFolder(
                SpecialFolder.Drafts);

        if (specialUseDrafts is not null &&
            string.Equals(
                specialUseDrafts.FullName,
                folder.FullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsDraftFolderName(
            folder.Name);
    }

    private static bool IsDraftFolderName(
        string folderName)
    {
        if (string.IsNullOrWhiteSpace(
                folderName))
        {
            return false;
        }

        return folderName
            .Trim()
            .ToLowerInvariant() switch
        {
            "drafts" => true,
            "draft" => true,
            "draft messages" => true,
            "entwürfe" => true,
            "entwurf" => true,
            _ => false
        };
    }

    private static async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            string userName,
            string password,
            CancellationToken cancellationToken)
    {
        var client =
            new ImapClient();

        try
        {
            using (var connectionTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                connectionTimeoutSource.CancelAfter(
                    ConnectionTimeout);

                await client.ConnectAsync(
                    ImapHost,
                    ImapPort,
                    SecureSocketOptions.SslOnConnect,
                    connectionTimeoutSource.Token);
            }

            using (var authenticationTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                authenticationTimeoutSource.CancelAfter(
                    AuthenticationTimeout);

                await client.AuthenticateAsync(
                    userName,
                    password,
                    authenticationTimeoutSource.Token);
            }

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    private static async Task DisconnectSafelyAsync(
        ImapClient client)
    {
        if (!client.IsConnected)
        {
            return;
        }

        try
        {
            await client.DisconnectAsync(
                true,
                CancellationToken.None);
        }
        catch
        {
        }
    }
}