using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Telenec.Mail.App.Services.Migration;

public sealed class LegacyMailboxConnectionService
{
    /*
     * Physisch erreichbare Adresse des alten Mailservers.
     */
    public const string LegacyImapAddress =
        "80.79.225.35";

    /*
     * Das vorhandene TLS-Zertifikat gilt weiterhin für
     * necnet.de bzw. *.necnet.de.
     *
     * Deshalb verbinden wir TCP-seitig direkt mit der IP,
     * verwenden für TLS/SNI und Zertifikatsprüfung aber
     * weiterhin necnet.de.
     */
    public const string LegacyTlsHost =
        "necnet.de";

    public const int LegacyImapPort =
        143;

    public async Task<LegacyMailboxConnectionResult>
        TestConnectionAsync(
            string userName,
            string password,
            CancellationToken cancellationToken = default)
    {
        var validationMessage =
            ValidateCredentials(
                userName,
                password);

        if (validationMessage is not null)
        {
            return new LegacyMailboxConnectionResult(
                Success:
                    false,
                InboxMessageCount:
                    0,
                UsesTls:
                    false,
                Message:
                    validationMessage);
        }

        try
        {
            using var client =
                await CreateAuthenticatedClientAsync(
                    userName,
                    password,
                    cancellationToken);

            await client.Inbox.OpenAsync(
                FolderAccess.ReadOnly,
                cancellationToken);

            var inboxMessageCount =
                Math.Max(
                    client.Inbox.Count,
                    0);

            var usesTls =
                client.IsSecure;

            await DisconnectSafelyAsync(
                client);

            return new LegacyMailboxConnectionResult(
                Success:
                    true,
                InboxMessageCount:
                    inboxMessageCount,
                UsesTls:
                    usesTls,
                Message:
                    "Die Verbindung zum alten Postfach wurde erfolgreich hergestellt.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return new LegacyMailboxConnectionResult(
                Success:
                    false,
                InboxMessageCount:
                    0,
                UsesTls:
                    false,
                Message:
                    "Die Anmeldung am alten Postfach wurde abgelehnt. " +
                    "Bitte prüfen Sie E-Mail-Adresse und Passwort.");
        }
        catch (IOException exception)
        {
            return new LegacyMailboxConnectionResult(
                Success:
                    false,
                InboxMessageCount:
                    0,
                UsesTls:
                    false,
                Message:
                    "Der alte Mailserver konnte nicht zuverlässig erreicht werden.\n\n" +
                    exception.Message);
        }
        catch (Exception exception)
        {
            return new LegacyMailboxConnectionResult(
                Success:
                    false,
                InboxMessageCount:
                    0,
                UsesTls:
                    false,
                Message:
                    "Die Verbindung zum alten Postfach ist fehlgeschlagen.\n\n" +
                    exception.Message);
        }
    }

    public async Task<LegacyMailboxInventoryResult>
        GetInventoryAsync(
            string userName,
            string password,
            CancellationToken cancellationToken = default)
    {
        var validationMessage =
            ValidateCredentials(
                userName,
                password);

        if (validationMessage is not null)
        {
            return new LegacyMailboxInventoryResult(
                Success:
                    false,
                Folders:
                    Array.Empty<
                        LegacyMailboxFolderInventory>(),
                TotalMessageCount:
                    0,
                Message:
                    validationMessage);
        }

        try
        {
            using var client =
                await CreateAuthenticatedClientAsync(
                    userName,
                    password,
                    cancellationToken);

            var folders =
                new List<IMailFolder>();

            /*
             * Zuerst versuchen wir, die Ordner zusammen mit
             * ihren STATUS-Werten abzurufen.
             *
             * MailKit 4.17 liefert hier IList<IMailFolder>.
             *
             * Sollte der alte Server diese Variante nicht
             * vollständig unterstützen, holen wir nur die
             * Ordner und fragen die Zähler anschließend
             * einzeln ab.
             */
            if (client.PersonalNamespaces.Count >
                0)
            {
                IList<IMailFolder>
                    serverFolders;

                try
                {
                    serverFolders =
                        await client.GetFoldersAsync(
                            client.PersonalNamespaces[0],
                            StatusItems.Count |
                            StatusItems.Unread,
                            false,
                            cancellationToken);
                }
                catch (NotSupportedException)
                {
                    serverFolders =
                        await client.GetFoldersAsync(
                            client.PersonalNamespaces[0],
                            StatusItems.None,
                            false,
                            cancellationToken);
                }

                folders.AddRange(
                    serverFolders);
            }

            /*
             * Einige Server liefern INBOX bei LIST nicht in
             * derselben Form zurück wie die übrigen Ordner.
             *
             * Deshalb stellen wir explizit sicher, dass der
             * Posteingang Bestandteil der Inventur ist.
             */
            var inboxAlreadyIncluded =
                folders.Any(
                    folder =>
                        string.Equals(
                            folder.FullName,
                            client.Inbox.FullName,
                            StringComparison.OrdinalIgnoreCase));

            if (!inboxAlreadyIncluded)
            {
                folders.Insert(
                    0,
                    client.Inbox);
            }

            var uniqueFolders =
                folders
                    .GroupBy(
                        folder =>
                            folder.FullName,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(
                        group =>
                            group.First())
                    .OrderBy(
                        folder =>
                            string.Equals(
                                folder.FullName,
                                client.Inbox.FullName,
                                StringComparison.OrdinalIgnoreCase)
                                ? 0
                                : 1)
                    .ThenBy(
                        folder =>
                            folder.FullName,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            var inventoryFolders =
                new List<
                    LegacyMailboxFolderInventory>();

            long totalMessageCount =
                0;

            foreach (var folder in
                     uniqueFolders)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                var isSelectable =
                    !folder.Attributes.HasFlag(
                        FolderAttributes.NoSelect);

                var messageCount =
                    0;

                var unreadCount =
                    0;

                if (isSelectable)
                {
                    await UpdateFolderStatusAsync(
                        folder,
                        cancellationToken);

                    messageCount =
                        Math.Max(
                            folder.Count,
                            0);

                    unreadCount =
                        Math.Max(
                            folder.Unread,
                            0);

                    /*
                     * Falls STATUS auf dem Legacy-Server keine
                     * Nachrichtenanzahl geliefert hat, öffnen
                     * wir den Ordner READ ONLY.
                     *
                     * Auch dieser Fallback verändert keinerlei
                     * Nachricht oder Flag.
                     */
                    if (folder.Count <
                        0)
                    {
                        messageCount =
                            await GetFolderCountReadOnlyAsync(
                                folder,
                                cancellationToken);
                    }

                    totalMessageCount +=
                        messageCount;
                }

                inventoryFolders.Add(
                    new LegacyMailboxFolderInventory(
                        FullName:
                            folder.FullName,

                        DisplayName:
                            GetFolderDisplayName(
                                folder,
                                client.Inbox.FullName),

                        MessageCount:
                            messageCount,

                        UnreadCount:
                            unreadCount,

                        IsSelectable:
                            isSelectable,

                        DirectorySeparator:
                            folder.DirectorySeparator));
            }

            await DisconnectSafelyAsync(
                client);

            return new LegacyMailboxInventoryResult(
                Success:
                    true,
                Folders:
                    inventoryFolders,
                TotalMessageCount:
                    totalMessageCount,
                Message:
                    "Der Bestand des alten Postfachs wurde vollständig eingelesen.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return new LegacyMailboxInventoryResult(
                Success:
                    false,
                Folders:
                    Array.Empty<
                        LegacyMailboxFolderInventory>(),
                TotalMessageCount:
                    0,
                Message:
                    "Die Anmeldung am alten Postfach wurde abgelehnt. " +
                    "Bitte prüfen Sie E-Mail-Adresse und Passwort.");
        }
        catch (Exception exception)
        {
            return new LegacyMailboxInventoryResult(
                Success:
                    false,
                Folders:
                    Array.Empty<
                        LegacyMailboxFolderInventory>(),
                TotalMessageCount:
                    0,
                Message:
                    "Die Bestandsaufnahme des alten Postfachs ist fehlgeschlagen.\n\n" +
                    exception.Message);
        }
    }

    private static async Task<ImapClient>
        CreateAuthenticatedClientAsync(
            string userName,
            string password,
            CancellationToken cancellationToken)
    {
        var client =
            new ImapClient
            {
                Timeout =
                    15000
            };

        Socket? socket =
            null;

        try
        {
            socket =
                new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);

            await socket.ConnectAsync(
                new IPEndPoint(
                    IPAddress.Parse(
                        LegacyImapAddress),
                    LegacyImapPort),
                cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            /*
             * Der Socket geht an die alte IP-Adresse.
             *
             * Für STARTTLS wird necnet.de verwendet, damit
             * die normale Zertifikatsprüfung weiterhin
             * vollständig funktioniert.
             */
            await client.ConnectAsync(
                socket,
                LegacyTlsHost,
                LegacyImapPort,
                SecureSocketOptions.StartTls,
                cancellationToken);

            /*
             * Nach erfolgreicher Übergabe besitzt MailKit
             * den Socket.
             */
            socket =
                null;

            cancellationToken
                .ThrowIfCancellationRequested();

            if (!client.IsSecure)
            {
                throw new IOException(
                    "Der alte Mailserver konnte keine verschlüsselte STARTTLS-Verbindung herstellen.");
            }

            await client.AuthenticateAsync(
                userName.Trim(),
                password,
                cancellationToken);

            return client;
        }
        catch
        {
            try
            {
                socket?.Dispose();
            }
            catch
            {
            }

            client.Dispose();

            throw;
        }
    }

    private static async Task
        UpdateFolderStatusAsync(
            IMailFolder folder,
            CancellationToken cancellationToken)
    {
        try
        {
            await folder.StatusAsync(
                StatusItems.Count |
                StatusItems.Unread,
                cancellationToken);
        }
        catch (NotSupportedException)
        {
            /*
             * Einige ältere IMAP-Server unterstützen STATUS
             * nur eingeschränkt.
             *
             * Die Nachrichtenanzahl wird dann im nächsten
             * Schritt über ein READ-ONLY-Öffnen ermittelt.
             */
        }
    }

    private static async Task<int>
        GetFolderCountReadOnlyAsync(
            IMailFolder folder,
            CancellationToken cancellationToken)
    {
        var openedHere =
            false;

        try
        {
            if (!folder.IsOpen)
            {
                await folder.OpenAsync(
                    FolderAccess.ReadOnly,
                    cancellationToken);

                openedHere =
                    true;
            }

            return Math.Max(
                folder.Count,
                0);
        }
        finally
        {
            if (openedHere &&
                folder.IsOpen)
            {
                try
                {
                    await folder.CloseAsync(
                        false,
                        cancellationToken);
                }
                catch
                {
                }
            }
        }
    }

    private static string
        GetFolderDisplayName(
            IMailFolder folder,
            string inboxFullName)
    {
        if (string.Equals(
                folder.FullName,
                inboxFullName,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Posteingang";
        }

        if (!string.IsNullOrWhiteSpace(
                folder.FullName))
        {
            return folder.FullName;
        }

        return folder.Name;
    }

    private static string?
        ValidateCredentials(
            string userName,
            string password)
    {
        if (string.IsNullOrWhiteSpace(
                userName))
        {
            return
                "Bitte geben Sie die E-Mail-Adresse des alten Postfachs ein.";
        }

        if (string.IsNullOrEmpty(
                password))
        {
            return
                "Bitte geben Sie das Passwort des alten Postfachs ein.";
        }

        return null;
    }

    private static async Task
        DisconnectSafelyAsync(
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

public sealed record LegacyMailboxConnectionResult(
    bool Success,
    int InboxMessageCount,
    bool UsesTls,
    string Message);

public sealed record LegacyMailboxFolderInventory(
    string FullName,
    string DisplayName,
    int MessageCount,
    int UnreadCount,
    bool IsSelectable,
    char DirectorySeparator);

public sealed record LegacyMailboxInventoryResult(
    bool Success,
    IReadOnlyList<LegacyMailboxFolderInventory> Folders,
    long TotalMessageCount,
    string Message)
{
    public int FolderCount =>
        Folders.Count;
}