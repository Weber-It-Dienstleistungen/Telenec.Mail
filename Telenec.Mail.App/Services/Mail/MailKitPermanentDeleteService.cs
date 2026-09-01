using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitPermanentDeleteService :
    IMailPermanentDeleteService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DeleteTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public MailKitPermanentDeleteService(
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

    public async Task DeletePermanentlyAsync(
        string folderId,
        uint expectedUidValidity,
        IReadOnlyList<uint> uniqueIds,
        CancellationToken cancellationToken = default)
    {
        ValidateFolderId(
            folderId);

        if (expectedUidValidity == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedUidValidity),
                "Für das endgültige Löschen ist eine gültige UIDVALIDITY erforderlich.");
        }

        var normalizedUniqueIds =
            NormalizeUniqueIds(
                uniqueIds);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            /*
             * Permanentes Löschen wird ausschließlich dann
             * freigegeben, wenn der Server UIDPLUS unterstützt.
             *
             * Dadurch kann MailKit UID EXPUNGE verwenden und
             * exakt die angegebenen UIDs entfernen.
             *
             * Ohne UIDPLUS führen wir bewusst keinen
             * Permanent-Delete durch.
             */
            if (!client.Capabilities.HasFlag(
                    ImapCapabilities.UidPlus))
            {
                throw new NotSupportedException(
                    "Der IMAP-Server unterstützt kein UIDPLUS. " +
                    "Ein sicheres gezieltes endgültiges Löschen ist deshalb nicht möglich.");
            }

            using var deleteTimeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            deleteTimeoutSource.CancelAfter(
                DeleteTimeout);

            var operationCancellationToken =
                deleteTimeoutSource.Token;

            var requestedFolder =
                await client.GetFolderAsync(
                    folderId,
                    operationCancellationToken);

            if (requestedFolder.Attributes.HasFlag(
                    FolderAttributes.NoSelect))
            {
                throw new InvalidOperationException(
                    "Der angegebene Mailordner kann nicht geöffnet werden.");
            }

            var trashFolder =
                await GetTrashFolderAsync(
                    client,
                    operationCancellationToken);

            /*
             * Eine irreversible Löschung darf ausschließlich
             * aus dem echten serverseitigen Papierkorb erfolgen.
             *
             * Selbst wenn der Aufrufer versehentlich einen
             * normalen Ordner übergibt, wird hier abgebrochen.
             */
            if (!string.Equals(
                    requestedFolder.FullName,
                    trashFolder.FullName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Nachrichten dürfen nur aus dem Papierkorb endgültig gelöscht werden.");
            }

            await requestedFolder.OpenAsync(
                FolderAccess.ReadWrite,
                operationCancellationToken);

            /*
             * UIDs sind ausschließlich innerhalb derselben
             * UIDVALIDITY eindeutig.
             *
             * Hat sich UIDVALIDITY seit dem Laden der UI
             * geändert, könnten dieselben numerischen UIDs
             * inzwischen völlig andere Nachrichten bezeichnen.
             *
             * Deshalb wird in diesem Fall unter keinen
             * Umständen gelöscht.
             */
            if (requestedFolder.UidValidity !=
                expectedUidValidity)
            {
                throw new InvalidOperationException(
                    "Der Papierkorb wurde serverseitig verändert. " +
                    "Das Postfach muss vor dem endgültigen Löschen neu synchronisiert werden.");
            }

            var serverUniqueIds =
                normalizedUniqueIds
                    .Select(
                        uniqueId =>
                            new UniqueId(
                                uniqueId))
                    .ToList();

            /*
             * Vor der ersten verändernden Operation prüfen wir,
             * ob sämtliche angeforderten UIDs noch vorhanden
             * sind.
             *
             * Fehlt auch nur eine UID, wird gar nichts
             * verändert. Der Aufrufer muss zunächst den
             * aktuellen Serverzustand neu laden.
             */
            var summariesBeforeDelete =
                await requestedFolder.FetchAsync(
                    serverUniqueIds,
                    MessageSummaryItems.UniqueId,
                    operationCancellationToken);

            var existingUniqueIds =
                summariesBeforeDelete
                    .Where(
                        summary =>
                            summary.UniqueId.IsValid)
                    .Select(
                        summary =>
                            summary.UniqueId.Id)
                    .ToHashSet();

            var allMessagesStillExist =
                normalizedUniqueIds.All(
                    existingUniqueIds.Contains);

            if (!allMessagesStillExist)
            {
                throw new InvalidOperationException(
                    "Mindestens eine ausgewählte Nachricht ist nicht mehr im Papierkorb vorhanden. " +
                    "Das Postfach muss vor dem endgültigen Löschen neu synchronisiert werden.");
            }

            /*
             * Erst nach sämtlichen Sicherheitsprüfungen werden
             * exakt die ausgewählten UIDs mit \Deleted markiert.
             */
            await requestedFolder.AddFlagsAsync(
                serverUniqueIds,
                MessageFlags.Deleted,
                silent: true,
                operationCancellationToken);

            /*
             * Selektives UID EXPUNGE.
             *
             * Da UIDPLUS zwingend geprüft wurde, darf hier kein
             * allgemeines EXPUNGE verwendet werden.
             *
             * Andere Nachrichten, die möglicherweise durch
             * Roundcube, Smartphone oder einen anderen Client
             * bereits mit \Deleted markiert wurden, werden
             * dadurch nicht mit entfernt.
             */
            await requestedFolder.ExpungeAsync(
                serverUniqueIds,
                operationCancellationToken);

            /*
             * Erfolgsprüfung:
             *
             * Nach erfolgreichem UID EXPUNGE darf keine der
             * angegebenen UIDs mehr vorhanden sein.
             */
            var summariesAfterDelete =
                await requestedFolder.FetchAsync(
                    serverUniqueIds,
                    MessageSummaryItems.UniqueId,
                    operationCancellationToken);

            var remainingUniqueIds =
                summariesAfterDelete
                    .Where(
                        summary =>
                            summary.UniqueId.IsValid)
                    .Select(
                        summary =>
                            summary.UniqueId.Id)
                    .ToList();

            if (remainingUniqueIds.Count > 0)
            {
                /*
                 * Sollte wider Erwarten eine Nachricht noch
                 * existieren, entfernen wir für diese noch
                 * vorhandenen Nachrichten vorsorglich wieder
                 * das \Deleted-Flag.
                 *
                 * Dadurch verhindern wir, dass ein späteres
                 * allgemeines EXPUNGE eines anderen Clients
                 * diese Nachrichten unbeabsichtigt entfernt.
                 */
                var remainingServerUniqueIds =
                    remainingUniqueIds
                        .Select(
                            uniqueId =>
                                new UniqueId(
                                    uniqueId))
                        .ToList();

                try
                {
                    await requestedFolder.RemoveFlagsAsync(
                        remainingServerUniqueIds,
                        MessageFlags.Deleted,
                        silent: true,
                        operationCancellationToken);
                }
                catch
                {
                    /*
                     * Der eigentliche Löschstatus ist in diesem
                     * Ausnahmefall nicht mehr eindeutig.
                     *
                     * Der Aufrufer muss anschließend zwingend
                     * den echten Serverzustand neu laden.
                     */
                }

                throw new InvalidOperationException(
                    "Das endgültige Löschen konnte nicht eindeutig bestätigt werden. " +
                    "Der Papierkorb muss neu synchronisiert werden.");
            }

            /*
             * Auch nach Abschluss darf sich UIDVALIDITY nicht
             * verändert haben.
             */
            if (requestedFolder.UidValidity !=
                expectedUidValidity)
            {
                throw new InvalidOperationException(
                    "Der serverseitige Zustand des Papierkorbs hat sich während des Löschvorgangs geändert. " +
                    "Der Papierkorb muss neu synchronisiert werden.");
            }
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static void ValidateFolderId(
        string folderId)
    {
        if (string.IsNullOrWhiteSpace(
                folderId))
        {
            throw new ArgumentException(
                "Der Mailordner darf nicht leer sein.",
                nameof(folderId));
        }
    }

    private static IReadOnlyList<uint>
        NormalizeUniqueIds(
            IReadOnlyList<uint> uniqueIds)
    {
        ArgumentNullException.ThrowIfNull(
            uniqueIds);

        var normalized =
            uniqueIds
                .Where(
                    uniqueId =>
                        uniqueId > 0)
                .Distinct()
                .ToList();

        if (normalized.Count == 0)
        {
            throw new ArgumentException(
                "Es wurde keine gültige Nachrichten-ID übergeben.",
                nameof(uniqueIds));
        }

        return normalized;
    }

    private static async Task<IMailFolder>
        GetTrashFolderAsync(
            ImapClient client,
            CancellationToken cancellationToken)
    {
        var specialUseTrash =
            client.GetFolder(
                SpecialFolder.Trash);

        if (specialUseTrash is not null &&
            !specialUseTrash.Attributes.HasFlag(
                FolderAttributes.NoSelect))
        {
            return specialUseTrash;
        }

        if (client.PersonalNamespaces.Count > 0)
        {
            var folders =
                await client.GetFoldersAsync(
                    client.PersonalNamespaces[0],
                    StatusItems.None,
                    false,
                    cancellationToken);

            var fallbackTrash =
                folders.FirstOrDefault(
                    folder =>
                        !folder.Attributes.HasFlag(
                            FolderAttributes.NoSelect) &&
                        IsTrashFolderName(
                            folder.Name));

            if (fallbackTrash is not null)
            {
                return fallbackTrash;
            }
        }

        throw new InvalidOperationException(
            "Auf dem Mailserver konnte kein Papierkorb ermittelt werden.");
    }

    private static bool IsTrashFolderName(
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
            "trash" => true,
            "deleted items" => true,
            "deleted messages" => true,
            "papierkorb" => true,
            "gelöschte elemente" => true,
            "geloeschte elemente" => true,
            _ => false
        };
    }

    private async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            CancellationToken cancellationToken)
    {
        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "Es ist kein aktives Mailkonto eingerichtet.");
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
            throw new InvalidOperationException(
                "Für das Mailkonto sind keine Zugangsdaten gespeichert.");
        }

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
                    account.EmailAddress,
                    credential.Password,
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
            /*
             * Ein Fehler beim Disconnect darf das Ergebnis
             * der eigentlichen Operation nicht verändern.
             */
        }
    }
}