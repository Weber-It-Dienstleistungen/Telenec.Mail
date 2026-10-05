using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telenec.Mail.App;

internal static class MailWebViewCopyContextMenuBootstrapper
{
    private static readonly ConditionalWeakTable<
        WebView2,
        WebViewState>
        States =
            new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                Window_OnLoaded),
            handledEventsToo:
                true);

        EventManager.RegisterClassHandler(
            typeof(MailMessageWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                Window_OnLoaded),
            handledEventsToo:
                true);
    }

    private static void Window_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        /*
         * Erst nach dem normalen Loaded-Durchlauf suchen wir
         * die HTML-Mail-WebView.
         *
         * Dadurch greifen wir weder in die bestehende
         * Initialisierung noch in die Sicherheitslogik für
         * externe Links/Bilder ein.
         */
        _ =
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    () =>
                    {
                        AttachToMailWebViews(
                            window);
                    }));
    }

    private static void AttachToMailWebViews(
        DependencyObject root)
    {
        foreach (var webView in
                 FindVisualChildren<WebView2>(
                     root))
        {
            /*
             * Ausschließlich die HTML-Mailanzeige.
             *
             * Andere WebViews, beispielsweise Editoren,
             * werden bewusst nicht verändert.
             */
            if (!string.Equals(
                    webView.Name,
                    "HtmlMailView",
                    StringComparison.Ordinal))
            {
                continue;
            }

            Attach(
                webView);
        }
    }

    private static void Attach(
        WebView2 webView)
    {
        var state =
            States.GetOrCreateValue(
                webView);

        if (state.InitializationHookInstalled)
        {
            if (webView.CoreWebView2 is not null)
            {
                ScheduleConfiguration(
                    webView,
                    state);
            }

            return;
        }

        state.InitializationHookInstalled =
            true;

        webView.CoreWebView2InitializationCompleted +=
            (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    return;
                }

                ScheduleConfiguration(
                    webView,
                    state);
            };

        /*
         * Falls die WebView bereits initialisiert wurde,
         * konfigurieren wir sie ebenfalls.
         */
        if (webView.CoreWebView2 is not null)
        {
            ScheduleConfiguration(
                webView,
                state);
        }
    }

    private static void ScheduleConfiguration(
        WebView2 webView,
        WebViewState state)
    {
        /*
         * Die bestehende Mailanzeige setzt während ihrer
         * Initialisierung
         *
         * AreDefaultContextMenusEnabled = false.
         *
         * Deshalb konfigurieren wir unser eingeschränktes
         * Kontextmenü bewusst erst danach.
         */
        _ =
            webView.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(
                    () =>
                    {
                        ConfigureContextMenu(
                            webView,
                            state);
                    }));
    }

    private static void ConfigureContextMenu(
        WebView2 webView,
        WebViewState state)
    {
        var coreWebView =
            webView.CoreWebView2;

        if (coreWebView is null)
        {
            return;
        }

        /*
         * Wir müssen die WebView2-Kontextmenüs grundsätzlich
         * aktivieren, damit ContextMenuRequested überhaupt
         * ausgelöst wird.
         *
         * Der Handler darunter entfernt anschließend ALLES
         * außer dem normalen Copy-Befehl.
         */
        coreWebView
            .Settings
            .AreDefaultContextMenusEnabled =
                true;

        if (ReferenceEquals(
                state.ConfiguredCoreWebView,
                coreWebView))
        {
            return;
        }

        if (state.ConfiguredCoreWebView is not null)
        {
            try
            {
                state.ConfiguredCoreWebView
                    .ContextMenuRequested -=
                        CoreWebView2_OnContextMenuRequested;
            }
            catch
            {
            }
        }

        state.ConfiguredCoreWebView =
            coreWebView;

        coreWebView.ContextMenuRequested +=
            CoreWebView2_OnContextMenuRequested;
    }

    private static void CoreWebView2_OnContextMenuRequested(
        object? sender,
        CoreWebView2ContextMenuRequestedEventArgs e)
    {
        /*
         * Ohne markierten Text soll weiterhin überhaupt kein
         * Browser-Kontextmenü erscheinen.
         */
        if (!e.ContextMenuTarget.HasSelection ||
            string.IsNullOrEmpty(
                e.ContextMenuTarget.SelectionText))
        {
            e.Handled =
                true;

            return;
        }

        /*
         * WebView2 liefert das normale Browser-Kontextmenü.
         *
         * Wir behalten davon ausschließlich den eingebauten
         * Copy-Befehl.
         *
         * Dadurch verwendet WebView2 weiterhin seine native
         * Kopierfunktion, zeigt dem Benutzer aber keinen
         * Browser-/Entwickler-/Speichern-Unter-Ballast.
         */
        var menuItems =
            e.MenuItems;

        for (var index =
                 menuItems.Count - 1;
             index >= 0;
             index--)
        {
            var item =
                menuItems[index];

            if (string.Equals(
                    item.Name,
                    "copy",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            menuItems.RemoveAt(
                index);
        }

        /*
         * Sollte eine zukünftige WebView2-Version in diesem
         * Kontext wider Erwarten keinen Copy-Befehl anbieten,
         * zeigen wir lieber gar kein Menü als ein ungefiltertes
         * Browsermenü.
         */
        if (menuItems.Count == 0)
        {
            e.Handled =
                true;
        }
    }

    private static IEnumerable<T>
        FindVisualChildren<T>(
            DependencyObject root)
        where T : DependencyObject
    {
        var childCount =
            VisualTreeHelper
                .GetChildrenCount(
                    root);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper
                    .GetChild(
                        root,
                        index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in
                     FindVisualChildren<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class WebViewState
    {
        public bool
            InitializationHookInstalled
        { get; set; }

        public CoreWebView2?
            ConfiguredCoreWebView
        { get; set; }
    }
}