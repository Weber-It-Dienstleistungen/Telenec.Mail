using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitFolderManagementService :
    IMailFolderManagementService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private const FolderAttributes
        ProtectedFolderAttributes =
            FolderAttributes.All |
            FolderAttributes.Archive |
            FolderAttributes.Drafts |
            FolderAttributes.Flagged |
            FolderAttributes.Important |
            FolderAttributes.Inbox |
            FolderAttributes.Junk |
            FolderAttributes.Sent |
            FolderAttributes.Trash;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FolderOperationTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public MailKitFolderManagementService(
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

    public async Task<string> CreateFolderAsync(
        string folderName,
        CancellationToken cancellationToken = default)
    {
        var normalizedFolderName =
            NormalizeFolderName(
                folderName);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            if (client.PersonalNamespaces.Count == 0)
            {
                throw new NotSupportedException(
                    "Der Mailserver stellt keinen persönlichen " +
                    "IMAP-Namensraum für neue Ordner bereit.");
            }

            using var operationTimeoutSource =
                CreateOperationTimeoutSource(
                    cancellationToken);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var namespaceRoot =
                client.GetFolder(
                    client.PersonalNamespaces[0]);

            ValidateChildFolderName(
                namespaceRoot,
                normalizedFolderName);

            var createdFolder =
                await namespaceRoot.CreateAsync(
                    normalizedFolderName,
                    isMessageFolder: true,
                    operationCancellationToken);

            await FinalizeCreatedFolderAsync(
                createdFolder,
                operationCancellationToken);

            return createdFolder.FullName;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task<string> CreateSubfolderAsync(
        string parentFolderId,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        var normalizedParentFolderId =
            NormalizeFolderId(
                parentFolderId);

        var normalizedFolderName =
            NormalizeFolderName(
                folderName);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            using var operationTimeoutSource =
                CreateOperationTimeoutSource(
                    cancellationToken);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var parentFolder =
                await client.GetFolderAsync(
                    normalizedParentFolderId,
                    operationCancellationToken);

            if (parentFolder.Attributes.HasFlag(
                    FolderAttributes.NonExistent))
            {
                throw new InvalidOperationException(
                    "Der übergeordnete Ordner existiert nicht mehr.");
            }

            /*
             * \NoInferiors bedeutet ausdrücklich, dass dieser
             * Ordner keine Unterordner besitzen darf.
             */
            if (parentFolder.Attributes.HasFlag(
                    FolderAttributes.NoInferiors))
            {
                throw new InvalidOperationException(
                    $"Unter „{parentFolder.Name}“ können auf diesem " +
                    "Mailserver keine Unterordner angelegt werden.");
            }

            ValidateChildFolderName(
                parentFolder,
                normalizedFolderName);

            /*
             * CREATE relativ zum ausgewählten Elternordner.
             *
             * MailKit berücksichtigt dabei automatisch das
             * vom Server verwendete IMAP-Trennzeichen.
             */
            var createdFolder =
                await parentFolder.CreateAsync(
                    normalizedFolderName,
                    isMessageFolder: true,
                    operationCancellationToken);

            await FinalizeCreatedFolderAsync(
                createdFolder,
                operationCancellationToken);

            return createdFolder.FullName;
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    public async Task DeleteFolderAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        var normalizedFolderId =
            NormalizeFolderId(
                folderId);

        using var client =
            await CreateAuthenticatedClientAsync(
                cancellationToken);

        try
        {
            using var operationTimeoutSource =
                CreateOperationTimeoutSource(
                    cancellationToken);

            var operationCancellationToken =
                operationTimeoutSource.Token;

            var folder =
                await client.GetFolderAsync(
                    normalizedFolderId,
                    operationCancellationToken);

            if (string.Equals(
                    folder.FullName,
                    client.Inbox.FullName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Der Posteingang kann nicht gelöscht werden.");
            }

            if ((folder.Attributes &
                 ProtectedFolderAttributes) != 0)
            {
                throw new InvalidOperationException(
                    "Dieser Systemordner kann nicht gelöscht werden.");
            }

            /*
             * Ein Elternordner mit vorhandenen Unterordnern wird
             * absichtlich nicht rekursiv gelöscht.
             *
             * Der Benutzer muss zuerst die Unterordner entfernen.
             */
            if (folder.Attributes.HasFlag(
                    FolderAttributes.HasChildren))
            {
                throw new InvalidOperationException(
                    "Dieser Ordner enthält Unterordner.\n\n" +
                    "Bitte löschen Sie zuerst die Unterordner.");
            }

            var wasSubscribed =
                folder.IsSubscribed;

            if (wasSubscribed)
            {
                await folder.UnsubscribeAsync(
                    operationCancellationToken);
            }

            try
            {
                await folder.DeleteAsync(
                    operationCancellationToken);
            }
            catch
            {
                if (wasSubscribed)
                {
                    try
                    {
                        await folder.SubscribeAsync(
                            operationCancellationToken);
                    }
                    catch
                    {
                    }
                }

                throw;
            }
        }
        finally
        {
            await DisconnectSafelyAsync(
                client);
        }
    }

    private static async Task FinalizeCreatedFolderAsync(
        IMailFolder createdFolder,
        CancellationToken cancellationToken)
    {
        if (createdFolder is null ||
            string.IsNullOrWhiteSpace(
                createdFolder.FullName))
        {
            throw new InvalidOperationException(
                "Der Mailserver hat den neuen Ordner nicht bestätigt.");
        }

        /*
         * Auch Unterordner werden unmittelbar abonniert.
         *
         * Damit erscheinen sie in Roundcube ohne zusätzliche
         * manuelle Aktivierung.
         */
        if (!createdFolder.IsSubscribed)
        {
            await createdFolder.SubscribeAsync(
                cancellationToken);
        }
    }

    private static void ValidateChildFolderName(
        IMailFolder parentFolder,
        string folderName)
    {
        if (parentFolder.DirectorySeparator != '\0' &&
            folderName.Contains(
                parentFolder.DirectorySeparator))
        {
            throw new ArgumentException(
                "Der Ordnername darf kein IMAP-Trennzeichen enthalten.",
                nameof(folderName));
        }
    }

    private static CancellationTokenSource
        CreateOperationTimeoutSource(
            CancellationToken cancellationToken)
    {
        var timeoutSource =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken);

        timeoutSource.CancelAfter(
            FolderOperationTimeout);

        return timeoutSource;
    }

    private static string NormalizeFolderName(
        string folderName)
    {
        if (folderName is null)
        {
            throw new ArgumentNullException(
                nameof(folderName));
        }

        var normalized =
            folderName.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Bitte geben Sie einen Ordnernamen ein.",
                nameof(folderName));
        }

        if (normalized.Any(
                char.IsControl))
        {
            throw new ArgumentException(
                "Der Ordnername enthält ungültige Steuerzeichen.",
                nameof(folderName));
        }

        return normalized;
    }

    private static string NormalizeFolderId(
        string folderId)
    {
        if (folderId is null)
        {
            throw new ArgumentNullException(
                nameof(folderId));
        }

        var normalized =
            folderId.Trim();

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "Der Mailordner konnte nicht eindeutig bestimmt werden.",
                nameof(folderId));
        }

        return normalized;
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
                "Es ist kein aktives Telenec-Mailkonto vorhanden.");
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
            throw new AuthenticationException(
                "Für das aktive Telenec-Mailkonto sind keine " +
                "gültigen Zugangsdaten gespeichert.");
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
                quit: true);
        }
        catch
        {
        }
    }
}