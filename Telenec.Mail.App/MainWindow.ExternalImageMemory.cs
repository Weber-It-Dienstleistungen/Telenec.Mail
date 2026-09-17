using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Services.Storage;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _externalImageMemoryInitialized;

    private IExternalImagePermissionStore?
        _externalImagePermissionStore;

    /*
     * Bereits in dieser Programmsitzung bekannte Freigaben
     * halten wir zusätzlich im Arbeitsspeicher.
     *
     * Dadurch muss nach dem ersten Datenbankzugriff nicht
     * jedes erneute Öffnen derselben Nachricht wieder auf
     * SQLite zugreifen.
     */
    private readonly HashSet<string>
        _rememberedExternalImageMessageKeys =
            new(
                StringComparer.Ordinal);

    /*
     * Verhindert parallele identische Datenbankabfragen,
     * falls WebView2 während eines schnellen UI-Wechsels
     * mehrere NavigationCompleted-Ereignisse liefert.
     */
    private readonly HashSet<string>
        _externalImagePermissionLookupsInProgress =
            new(
                StringComparer.Ordinal);

    private async void
        MainWindowExternalImageMemory_OnLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (_externalImageMemoryInitialized)
        {
            return;
        }

        _externalImageMemoryInitialized =
            true;

        Loaded -=
            MainWindowExternalImageMemory_OnLoaded;

        /*
         * Wir hängen uns bewusst an den bestehenden
         * ExternalImagesNotice-Container.
         *
         * Dadurch muss der bereits produktive
         * "Trotzdem laden"-Handler nicht verändert werden.
         * Dieser lädt weiterhin die Bilder.
         *
         * Dieser zusätzliche Handler merkt sich lediglich
         * die Entscheidung.
         */
        ExternalImagesNotice.AddHandler(
            Button.ClickEvent,
            new RoutedEventHandler(
                ExternalImagesNotice_OnRememberPermission),
            true);

        try
        {
            /*
             * WebView2 wird über die bereits vorhandene
             * zentrale Initialisierung vorbereitet.
             *
             * Das ist parallel zum normalen Start sicher,
             * weil EnsureWebViewReadyAsync intern dieselbe
             * Initialisierungs-Task wiederverwendet.
             */
            await EnsureWebViewReadyAsync();

            var coreWebView =
                HtmlMailView.CoreWebView2;

            if (coreWebView is null)
            {
                return;
            }

            coreWebView.NavigationCompleted +=
                CoreWebView2_OnNavigationCompletedForExternalImageMemory;
        }
        catch
        {
            /*
             * Das Merken externer Bilder ist eine
             * Komfortfunktion.
             *
             * Ein Problem damit darf niemals verhindern,
             * dass der Benutzer seine E-Mails lesen kann.
             */
        }
    }

    private async void
        ExternalImagesNotice_OnRememberPermission(
            object sender,
            RoutedEventArgs e)
    {
        /*
         * Im ExternalImagesNotice befindet sich derzeit genau
         * der Button "Trotzdem laden".
         *
         * Trotzdem prüfen wir den Auslöser explizit, damit
         * spätere weitere Buttons in diesem Bereich nicht
         * versehentlich dieselbe Bedeutung bekommen.
         */
        if (e.Source is not Button button ||
            !string.Equals(
                button.Content as string,
                "Trotzdem laden",
                StringComparison.Ordinal))
        {
            return;
        }

        var message =
            GetDisplayedMessageForExternalImages();

        if (message is null ||
            !message.HasHtmlBody)
        {
            return;
        }

        var messageKey =
            CreateExternalImageMessageKey(
                message);

        if (string.IsNullOrWhiteSpace(
                messageKey))
        {
            return;
        }

        /*
         * Sofort lokal merken.
         *
         * Selbst wenn SQLite danach kurzfristig nicht
         * erreichbar sein sollte, funktioniert das erneute
         * Öffnen innerhalb dieser Programmsitzung bereits.
         */
        _rememberedExternalImageMessageKeys.Add(
            messageKey);

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                return;
            }

            var permissionStore =
                GetExternalImagePermissionStore();

            await permissionStore
                .AllowAsync(
                    account.AccountId,
                    messageKey);
        }
        catch
        {
            /*
             * Das eigentliche Laden der Bilder wurde bereits
             * durch den bestehenden Button-Handler erlaubt.
             *
             * Ein Fehler beim dauerhaften Merken darf diese
             * Benutzerentscheidung deshalb nicht rückgängig
             * machen und auch keine Fehlermeldung erzeugen.
             */
        }
    }

    private async void
        CoreWebView2_OnNavigationCompletedForExternalImageMemory(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
    {
        /*
         * Bilder sind für die aktuell dargestellte Nachricht
         * bereits erlaubt.
         *
         * Das ist insbesondere beim zweiten NavigateToString
         * nach einer wiederhergestellten Freigabe wichtig,
         * damit keine Schleife entsteht.
         */
        if (_allowExternalImagesForCurrentMessage)
        {
            return;
        }

        var message =
            GetDisplayedMessageForExternalImages();

        if (message is null ||
            !message.HasHtmlBody ||
            !ContainsExternalImages(
                message.HtmlBody!))
        {
            return;
        }

        var messageKey =
            CreateExternalImageMessageKey(
                message);

        if (string.IsNullOrWhiteSpace(
                messageKey))
        {
            return;
        }

        /*
         * Bereits während dieser Sitzung bekannte Freigabe:
         * kein weiterer SQLite-Zugriff nötig.
         */
        if (_rememberedExternalImageMessageKeys.Contains(
                messageKey))
        {
            RestoreExternalImagesForMessage(
                message);

            return;
        }

        if (!_externalImagePermissionLookupsInProgress.Add(
                messageKey))
        {
            return;
        }

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                return;
            }

            var permissionStore =
                GetExternalImagePermissionStore();

            var isAllowed =
                await permissionStore
                    .IsAllowedAsync(
                        account.AccountId,
                        messageKey);

            if (!isAllowed)
            {
                return;
            }

            _rememberedExternalImageMessageKeys.Add(
                messageKey);

            /*
             * Der Benutzer kann während des SQLite-Zugriffs
             * bereits eine andere Nachricht geöffnet haben.
             *
             * In diesem Fall darf die alte Entscheidung
             * keinesfalls auf die neue Nachricht übertragen
             * werden.
             */
            if (!IsDisplayedMessageForExternalImages(
                    message))
            {
                return;
            }

            RestoreExternalImagesForMessage(
                message);
        }
        catch
        {
            /*
             * Bei einem Fehler bleibt das sichere
             * Standardverhalten erhalten:
             *
             * externe Bilder bleiben blockiert und der
             * Benutzer kann sie weiterhin manuell freigeben.
             */
        }
        finally
        {
            _externalImagePermissionLookupsInProgress.Remove(
                messageKey);
        }
    }

    private void RestoreExternalImagesForMessage(
        MailMessageItemViewModel message)
    {
        if (!IsDisplayedMessageForExternalImages(
                message) ||
            !message.HasHtmlBody)
        {
            return;
        }

        _allowExternalImagesForCurrentMessage =
            true;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        var html =
            PrepareHtmlForMailView(
                message.HtmlBody!);

        /*
         * Wir navigieren bewusst direkt erneut.
         *
         * Dadurch funktioniert dieselbe Logik sowohl für die
         * normale Nachrichtenliste als auch für die
         * Suchvorschau.
         *
         * RenderSearchPreviewMessageAsync setzt die
         * Freigabe beim normalen Aufbau absichtlich zurück;
         * deshalb wäre ein erneuter Aufruf dieser Methode an
         * dieser Stelle ungeeignet.
         */
        HtmlMailView
            .CoreWebView2?
            .NavigateToString(
                html);
    }

    private MailMessageItemViewModel?
        GetDisplayedMessageForExternalImages()
    {
        /*
         * Während einer aktiven Suche wird rechts nicht
         * zwingend MainViewModel.SelectedMessage angezeigt,
         * sondern die separat geladene Suchvorschau.
         */
        if (_searchIsActive &&
            _searchPreviewMessage is not null)
        {
            return _searchPreviewMessage;
        }

        return _viewModel.SelectedMessage;
    }

    private bool
        IsDisplayedMessageForExternalImages(
            MailMessageItemViewModel message)
    {
        if (_searchIsActive)
        {
            return ReferenceEquals(
                _searchPreviewMessage,
                message);
        }

        return ReferenceEquals(
            _viewModel.SelectedMessage,
            message);
    }

    private IExternalImagePermissionStore
        GetExternalImagePermissionStore()
    {
        /*
         * Der Store wird bewusst erst bei tatsächlicher
         * Verwendung erstellt.
         *
         * Dadurch müssen wir für diesen kleinen
         * abgeschlossenen Funktionsblock nicht die zentrale
         * DI-Konfiguration verändern.
         */
        _externalImagePermissionStore ??=
            ActivatorUtilities
                .CreateInstance<
                    SqliteExternalImagePermissionStore>(
                    _serviceProvider);

        return _externalImagePermissionStore;
    }

    private static string
        CreateExternalImageMessageKey(
            MailMessageItemViewModel message)
    {
        /*
         * Die RFC Message-ID ist die beste Identität:
         *
         * - unabhängig von IMAP-UID
         * - unabhängig vom Ordner
         * - bleibt auch beim Verschieben der Mail erhalten
         */
        if (!string.IsNullOrWhiteSpace(
                message.MessageId))
        {
            var normalizedMessageId =
                message.MessageId
                    .Trim()
                    .Trim(
                        '<',
                        '>')
                    .Trim();

            if (!string.IsNullOrWhiteSpace(
                    normalizedMessageId))
            {
                return
                    "message-id:" +
                    normalizedMessageId;
            }
        }

        /*
         * Sehr alte oder fehlerhaft erzeugte E-Mails können
         * ohne Message-ID eintreffen.
         *
         * Dafür bilden wir einen stabilen SHA-256-Fingerprint
         * aus unveränderlichen Nachrichtenmerkmalen.
         *
         * Der eigentliche Mailinhalt wird dabei NICHT in der
         * Datenbank gespeichert. Dort landet ausschließlich
         * der Hash.
         */
        var fingerprintSource =
            string.Join(
                "\u001F",
                NormalizeFingerprintPart(
                    message.SenderAddress),
                NormalizeFingerprintPart(
                    message.RecipientAddress),
                NormalizeFingerprintPart(
                    message.Subject),
                NormalizeFingerprintPart(
                    message.DisplayDateTime),
                message.Body ?? string.Empty,
                message.HtmlBody ?? string.Empty);

        var fingerprintBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    fingerprintSource));

        return
            "sha256:" +
            Convert.ToHexString(
                fingerprintBytes);
    }

    private static string NormalizeFingerprintPart(
        string? value)
    {
        return value?
                   .Trim()
               ?? string.Empty;
    }
}