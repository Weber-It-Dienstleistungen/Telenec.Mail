using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private ILogger<MainWindow>?
        _webViewDiagnosticsLogger;

    private bool
        _webViewDiagnosticsHookInstalled;

    private int
        _webViewBlockedImageLoggedRenderVersion = -1;

    /*
     * Externe Links werden von Telenec Mail bewusst nicht
     * innerhalb der WebView geöffnet.
     *
     * Der produktive NavigationStarting-Handler bricht diese
     * Navigation ab und übergibt den Link an den
     * Standardbrowser.
     *
     * WebView2 meldet anschließend für genau diese Navigation
     * ein NavigationCompleted mit OperationCanceled.
     *
     * Damit dieser erwartete Sicherheitsmechanismus nicht
     * fälschlich als HTML-Renderingfehler erscheint, merken
     * wir uns die entsprechenden NavigationIds.
     */
    private readonly HashSet<ulong>
        _externalWebNavigationIds =
            new();

    /*
     * Die übrigen MainWindow-Partial-Dateien verwenden bereits
     * verschiedene Window-Lifecycle-Overrides.
     *
     * Für die WebView2-Diagnose verwenden wir deshalb bewusst
     * WPF-Class-Handler.
     *
     * Dadurch muss keine bestehende Datei und insbesondere
     * keine bereits getestete Rendering- oder Security-Logik
     * verändert werden.
     */
    static MainWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnWebViewDiagnosticsLoaded));

        /*
         * Der Click-Handler dient ausschließlich dazu,
         * die bewusste Benutzerfreigabe externer Bilder zu
         * protokollieren.
         *
         * Die eigentliche Button-Logik bleibt vollständig im
         * bestehenden MainWindow.xaml.cs.
         */
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            ButtonBase.ClickEvent,
            new RoutedEventHandler(
                MainWindow_OnWebViewDiagnosticsButtonClick),
            handledEventsToo:
                true);
    }

    private static void
        MainWindow_OnWebViewDiagnosticsLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        window.InstallWebViewDiagnostics();
    }

    private void InstallWebViewDiagnostics()
    {
        if (_webViewDiagnosticsHookInstalled)
        {
            return;
        }

        _webViewDiagnosticsHookInstalled =
            true;

        /*
         * Logging darf die Maildarstellung niemals
         * beeinträchtigen.
         *
         * Sollte der Logger aus irgendeinem Grund nicht
         * auflösbar sein, läuft WebView2 deshalb einfach ohne
         * diese zusätzliche Diagnose weiter.
         */
        try
        {
            _webViewDiagnosticsLogger =
                _serviceProvider
                    .GetRequiredService<
                        ILogger<MainWindow>>();
        }
        catch
        {
            return;
        }

        /*
         * Wir starten WebView2 hier ausdrücklich NICHT selbst.
         *
         * Der bestehende Renderpfad entscheidet weiterhin,
         * wann die Runtime tatsächlich initialisiert wird.
         *
         * Wir beobachten lediglich das bereits vorhandene
         * Initialisierungsereignis.
         */
        HtmlMailView.CoreWebView2InitializationCompleted +=
            HtmlMailView_OnCoreWebView2InitializationCompleted;
    }

    private void
        HtmlMailView_OnCoreWebView2InitializationCompleted(
            object? sender,
            CoreWebView2InitializationCompletedEventArgs e)
    {
        var logger =
            _webViewDiagnosticsLogger;

        if (logger is null)
        {
            return;
        }

        if (!e.IsSuccess)
        {
            var exceptionType =
                e.InitializationException?
                    .GetType()
                    .FullName
                ?? "unknown";

            /*
             * Das Exception-Objekt selbst wird bewusst nicht
             * protokolliert.
             *
             * Dadurch gelangen insbesondere keine lokalen
             * Dateipfade oder sonstige Umgebungsinformationen
             * über einen Stacktrace ins Feldtestlog.
             */
            logger.LogError(
                "WebView2 initialization failed. ExceptionType={ExceptionType}.",
                exceptionType);

            return;
        }

        logger.LogInformation(
            "WebView2 initialization completed successfully.");

        var coreWebView =
            HtmlMailView.CoreWebView2;

        if (coreWebView is null)
        {
            logger.LogError(
                "WebView2 initialization reported success but no CoreWebView2 instance is available.");

            return;
        }

        /*
         * Diese Ereignisse dienen ausschließlich der Diagnose.
         *
         * Die bereits bestehenden produktiven Handler für
         * Navigation, externe Links und Bildblockierung werden
         * weder ersetzt noch verändert.
         */
        coreWebView.NavigationStarting +=
            CoreWebView2_OnDiagnosticsNavigationStarting;

        coreWebView.NavigationCompleted +=
            CoreWebView2_OnDiagnosticsNavigationCompleted;

        coreWebView.ProcessFailed +=
            CoreWebView2_OnDiagnosticsProcessFailed;

        coreWebView.WebResourceRequested +=
            CoreWebView2_OnDiagnosticsWebResourceRequested;
    }

    private void
        CoreWebView2_OnDiagnosticsNavigationStarting(
            object? sender,
            CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsExternalWebUri(
                e.Uri))
        {
            return;
        }

        /*
         * Der produktive Handler wird diese Navigation
         * abbrechen und die URL an den Standardbrowser
         * übergeben.
         *
         * Wir merken uns deshalb die NavigationId, damit das
         * daraus folgende OperationCanceled nicht als
         * Renderingfehler bewertet wird.
         */
        _externalWebNavigationIds.Add(
            e.NavigationId);

        /*
         * Die konkrete Ziel-URL gehört ausdrücklich nicht ins
         * Log.
         *
         * Für die Diagnose genügt die Information, dass aus
         * einer HTML-Mail heraus ein externer Link ausgelöst
         * wurde.
         */
        _webViewDiagnosticsLogger?
            .LogInformation(
                "External web navigation was requested from an HTML mail.");
    }

    private void
        CoreWebView2_OnDiagnosticsNavigationCompleted(
            object? sender,
            CoreWebView2NavigationCompletedEventArgs e)
    {
        /*
         * Eine Navigation zu einem externen Weblink gehört
         * nicht zum HTML-Mail-Rendering.
         *
         * Der produktive Sicherheitsmechanismus bricht sie
         * absichtlich ab und öffnet stattdessen den
         * Standardbrowser.
         *
         * Deshalb entfernen wir die gespeicherte NavigationId
         * und bewerten dieses NavigationCompleted nicht als
         * Render-Ergebnis.
         */
        if (_externalWebNavigationIds.Remove(
                e.NavigationId))
        {
            return;
        }

        /*
         * NavigationCompleted kann theoretisch auch für
         * WebView-interne Vorgänge auftreten.
         *
         * Wir interessieren uns hier ausschließlich für den
         * sichtbaren HTML-Mail-Renderpfad.
         */
        var message =
            _viewModel.SelectedMessage;

        if (message is null ||
            !message.HasHtmlBody)
        {
            return;
        }

        if (e.IsSuccess)
        {
            _webViewDiagnosticsLogger?
                .LogInformation(
                    "HTML mail rendering completed successfully.");

            return;
        }

        _webViewDiagnosticsLogger?
            .LogWarning(
                "HTML mail rendering failed. WebErrorStatus={WebErrorStatus}.",
                e.WebErrorStatus);
    }

    private void
        CoreWebView2_OnDiagnosticsProcessFailed(
            object? sender,
            CoreWebView2ProcessFailedEventArgs e)
    {
        /*
         * ProcessFailedKind ist ein rein technischer
         * WebView2-Zustand und enthält keine Maildaten.
         */
        _webViewDiagnosticsLogger?
            .LogError(
                "WebView2 process failed. ProcessFailedKind={ProcessFailedKind}.",
                e.ProcessFailedKind);
    }

    private void
        CoreWebView2_OnDiagnosticsWebResourceRequested(
            object? sender,
            CoreWebView2WebResourceRequestedEventArgs e)
    {
        /*
         * Sobald der Benutzer externe Bilder ausdrücklich
         * freigegeben hat, handelt es sich nicht mehr um einen
         * blockierten Request.
         */
        if (_allowExternalImagesForCurrentMessage)
        {
            return;
        }

        var uri =
            e.Request.Uri;

        if (string.IsNullOrWhiteSpace(
                uri))
        {
            return;
        }

        var isExternalHttpImage =
            uri.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase);

        if (!isExternalHttpImage)
        {
            return;
        }

        /*
         * Eine HTML-Mail kann sehr viele Tracking- oder
         * Gestaltungsgrafiken enthalten.
         *
         * Deshalb protokollieren wir nicht jeden einzelnen
         * Request, sondern maximal einen Eintrag pro
         * Renderdurchlauf.
         *
         * Die URL selbst wird selbstverständlich nicht
         * protokolliert.
         */
        if (_webViewBlockedImageLoggedRenderVersion ==
            _renderVersion)
        {
            return;
        }

        _webViewBlockedImageLoggedRenderVersion =
            _renderVersion;

        _webViewDiagnosticsLogger?
            .LogInformation(
                "External image request was blocked for the current HTML mail.");
    }

    private static void
        MainWindow_OnWebViewDiagnosticsButtonClick(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        /*
         * Der bestehende XAML-Button besitzt bewusst kein
         * x:Name.
         *
         * Er ist stattdessen über seinen festen sichtbaren
         * Inhalt "Trotzdem laden" und seinen vorhandenen
         * produktiven Click-Handler definiert.
         *
         * Für die reine Diagnose identifizieren wir deshalb
         * hier das tatsächlich geklickte Button-Element über
         * seinen Inhalt.
         */
        if (e.Source is not ButtonBase clickedButton ||
            clickedButton.Content is not string buttonText ||
            !string.Equals(
                buttonText,
                "Trotzdem laden",
                StringComparison.Ordinal))
        {
            return;
        }

        var message =
            window._viewModel.SelectedMessage;

        if (message is null ||
            !message.HasHtmlBody)
        {
            return;
        }

        window._webViewDiagnosticsLogger?
            .LogInformation(
                "User explicitly allowed external images for the current HTML mail.");
    }
}