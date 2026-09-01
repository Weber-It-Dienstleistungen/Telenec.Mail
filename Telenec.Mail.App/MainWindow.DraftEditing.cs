using MailKit.Security;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool _draftUiHookInstalled;

    private Button? _primaryMessageActionButton;
    private Button? _replyAllMessageActionButton;
    private Button? _forwardMessageActionButton;

    /*
     * Die Draft-spezifischen UI-Erweiterungen werden erst
     * nach dem vollständigen Rendern installiert.
     *
     * Dadurch existieren sowohl die Nachrichtenliste als
     * auch die sichtbaren Aktionsbuttons bereits sicher im
     * Visual Tree.
     */
    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        if (_draftUiHookInstalled)
        {
            return;
        }

        _draftUiHookInstalled =
            true;

        MessageListBox.PreviewMouseRightButtonDown +=
            MessageListBox_OnDraftPreviewMouseRightButtonDown;

        _viewModel.PropertyChanged +=
            DraftViewModel_OnPropertyChanged;

        Closed +=
            MainWindow_DraftEditing_OnClosed;

        FindVisibleMessageActionButtons();

        ConfigureVisibleMessageActions();
    }

    private void MainWindow_DraftEditing_OnClosed(
        object? sender,
        EventArgs e)
    {
        MessageListBox.PreviewMouseRightButtonDown -=
            MessageListBox_OnDraftPreviewMouseRightButtonDown;

        _viewModel.PropertyChanged -=
            DraftViewModel_OnPropertyChanged;

        Closed -=
            MainWindow_DraftEditing_OnClosed;
    }

    private void DraftViewModel_OnPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName !=
            nameof(MainViewModel.SelectedFolder))
        {
            return;
        }

        ConfigureVisibleMessageActions();
    }

    /*
     * Wir verwenden bewusst die bereits vorhandenen Buttons
     * aus MainWindow.xaml.
     *
     * Dadurch muss die große und stabile XAML-Datei für
     * diesen kleinen UX-Schritt nicht verändert werden.
     */
    private void FindVisibleMessageActionButtons()
    {
        foreach (var button in
                 FindVisualChildren<Button>(
                     this))
        {
            var buttonText =
                GetButtonText(
                    button);

            switch (buttonText)
            {
                case "Antworten":
                    _primaryMessageActionButton ??=
                        button;
                    break;

                case "Allen antworten":
                    _replyAllMessageActionButton ??=
                        button;
                    break;

                case "Weiterleiten":
                    _forwardMessageActionButton ??=
                        button;
                    break;
            }
        }
    }

    private void ConfigureVisibleMessageActions()
    {
        if (_primaryMessageActionButton is null ||
            _replyAllMessageActionButton is null ||
            _forwardMessageActionButton is null)
        {
            FindVisibleMessageActionButtons();
        }

        var primaryButton =
            _primaryMessageActionButton;

        var replyAllButton =
            _replyAllMessageActionButton;

        var forwardButton =
            _forwardMessageActionButton;

        if (primaryButton is null ||
            replyAllButton is null ||
            forwardButton is null)
        {
            return;
        }

        /*
         * Eventhandler zunächst immer auf einen definierten
         * Zustand zurücksetzen.
         *
         * Dadurch kann beliebig oft zwischen Ordnern
         * gewechselt werden, ohne dass Handler mehrfach
         * registriert werden.
         */
        primaryButton.Click -=
            ReplySelectedMessageButton_OnClick;

        primaryButton.Click -=
            EditDraftSelectedMessageButton_OnClick;

        if (IsDraftFolderSelectedForEditing())
        {
            /*
             * Ein Entwurf ist eine noch nicht abgeschlossene
             * eigene Nachricht.
             *
             * Antworten / Allen antworten / Weiterleiten sind
             * hier keine sinnvollen Primäraktionen.
             */
            SetButtonText(
                primaryButton,
                "Entwurf bearbeiten");

            primaryButton.ToolTip =
                "Diesen Entwurf bearbeiten";

            primaryButton.Click +=
                EditDraftSelectedMessageButton_OnClick;

            replyAllButton.Visibility =
                Visibility.Collapsed;

            forwardButton.Visibility =
                Visibility.Collapsed;

            return;
        }

        /*
         * Außerhalb des Entwürfe-Ordners exakt den bisherigen
         * Zustand wiederherstellen.
         */
        SetButtonText(
            primaryButton,
            "Antworten");

        primaryButton.ToolTip =
            "Auf diese Nachricht antworten (Strg+R)";

        primaryButton.Click +=
            ReplySelectedMessageButton_OnClick;

        replyAllButton.Visibility =
            Visibility.Visible;

        forwardButton.Visibility =
            Visibility.Visible;
    }

    private async void EditDraftSelectedMessageButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var message =
            _viewModel.SelectedMessage;

        if (_viewModel.IsLoading ||
            message is null)
        {
            return;
        }

        await EditDraftFromUiAsync(
            message);
    }

    private void MessageListBox_OnDraftPreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var menuOwner =
            FindContextMenuOwner(
                e.OriginalSource as DependencyObject);

        if (menuOwner?.ContextMenu is not ContextMenu contextMenu)
        {
            return;
        }

        ConfigureDraftAwareContextMenu(
            contextMenu);
    }

    private void ConfigureDraftAwareContextMenu(
        ContextMenu contextMenu)
    {
        var menuItems =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .ToArray();

        /*
         * Erwartete Reihenfolge der bestehenden
         * Message-ContextMenu-Einträge:
         *
         * 0 Antworten
         * 1 Allen antworten
         * 2 Weiterleiten
         * 3 Als ungelesen markieren
         * 4 Löschen/Wiederherstellen
         */
        if (menuItems.Length < 3)
        {
            return;
        }

        var primaryMenuItem =
            menuItems[0];

        primaryMenuItem.Click -=
            ReplyMenuItem_OnClick;

        primaryMenuItem.Click -=
            EditDraftMenuItem_OnClick;

        if (IsDraftFolderSelectedForEditing())
        {
            primaryMenuItem.Header =
                "Entwurf bearbeiten";

            primaryMenuItem.Click +=
                EditDraftMenuItem_OnClick;

            menuItems[1].Visibility =
                Visibility.Collapsed;

            menuItems[2].Visibility =
                Visibility.Collapsed;

            return;
        }

        primaryMenuItem.Header =
            "Antworten";

        primaryMenuItem.Click +=
            ReplyMenuItem_OnClick;

        menuItems[1].Visibility =
            Visibility.Visible;

        menuItems[2].Visibility =
            Visibility.Visible;
    }

    private static FrameworkElement?
        FindContextMenuOwner(
            DependencyObject? source)
    {
        var current =
            source;

        while (current is not null)
        {
            if (current is FrameworkElement frameworkElement &&
                frameworkElement.ContextMenu is not null)
            {
                return frameworkElement;
            }

            /*
             * Sobald das ListBoxItem erreicht wurde, liegt das
             * gesuchte Element aus dem DataTemplate nicht
             * weiter oben.
             */
            if (current is ListBoxItem)
            {
                break;
            }

            current =
                GetVisualOrLogicalParent(
                    current);
        }

        return null;
    }

    private static DependencyObject?
        GetVisualOrLogicalParent(
            DependencyObject element)
    {
        if (element is Visual ||
            element is Visual3D)
        {
            var visualParent =
                VisualTreeHelper.GetParent(
                    element);

            if (visualParent is not null)
            {
                return visualParent;
            }
        }

        return element switch
        {
            FrameworkElement frameworkElement =>
                frameworkElement.Parent,

            FrameworkContentElement contentElement =>
                contentElement.Parent,

            _ =>
                null
        };
    }

    private async void EditDraftMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_viewModel.IsLoading ||
            sender is not FrameworkElement menuItem ||
            menuItem.DataContext is not MailMessageItemViewModel message)
        {
            return;
        }

        await EditDraftFromUiAsync(
            message);
    }

    /*
     * Beide Oberflächenwege verwenden absichtlich exakt
     * dieselbe Bearbeitungslogik:
     *
     * - Rechtsklick -> Entwurf bearbeiten
     * - sichtbarer Button -> Entwurf bearbeiten
     *
     * Es existiert damit nur ein sicherheitskritischer
     * Draft-Workflow.
     */
    private async Task EditDraftFromUiAsync(
        MailMessageItemViewModel message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        if (_viewModel.IsLoading ||
            !IsDraftFolderSelectedForEditing() ||
            !_viewModel.Messages.Contains(
                message))
        {
            return;
        }

        var sourceFolder =
            _viewModel.SelectedFolder;

        if (sourceFolder is null ||
            string.IsNullOrWhiteSpace(
                sourceFolder.FolderId))
        {
            MessageBox.Show(
                "Der Entwürfe-Ordner konnte nicht eindeutig bestimmt werden.\n\n" +
                "Der Entwurf wird deshalb nicht zur Bearbeitung geöffnet.",
                "Entwurf kann nicht bearbeitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (message.UniqueId == 0)
        {
            MessageBox.Show(
                "Der Entwurf besitzt keine gültige Server-ID.\n\n" +
                "Er wird deshalb nicht zur Bearbeitung geöffnet.",
                "Entwurf kann nicht bearbeitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (string.IsNullOrWhiteSpace(
                message.MessageId))
        {
            MessageBox.Show(
                "Der Entwurf besitzt keine eindeutige Message-ID.\n\n" +
                "Telenec Mail bearbeitet ihn deshalb aus Sicherheitsgründen nicht automatisch.",
                "Entwurf kann nicht sicher bearbeitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<ComposeWindow>();

        composeWindow.Owner =
            this;

        try
        {
            /*
             * Vor dem Bearbeiten wird der Entwurf frisch vom
             * Server geladen und erneut verifiziert.
             *
             * Die sichtbare Nachrichtenliste allein reicht
             * ausdrücklich nicht als Identitätsnachweis.
             */
            await composeWindow
                .PrepareDraftEditAsync(
                    sourceFolder.FolderId,
                    message.UniqueId,
                    message.MessageId);
        }
        catch (MailDraftEditException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Entwurf kann nicht bearbeitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }
        catch (MailKit.Security.AuthenticationException)
        {
            MessageBox.Show(
                "Der Mailserver hat die gespeicherten Zugangsdaten nicht akzeptiert.\n\n" +
                "Der Entwurf wurde nicht geöffnet.",
                "Anmeldung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }
        catch (SslHandshakeException)
        {
            MessageBox.Show(
                "Die sichere Verbindung zum Mailserver konnte nicht hergestellt werden.\n\n" +
                "Der Entwurf wurde nicht geöffnet.",
                "Sicherheitsfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                "Das Laden des Entwurfs hat zu lange gedauert und wurde abgebrochen.",
                "Zeitüberschreitung",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }
        catch
        {
            MessageBox.Show(
                "Der Entwurf konnte nicht sicher vom Mailserver geladen werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Entwurf kann nicht bearbeitet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        await ShowComposeWindowAsync(
            composeWindow);

        /*
         * Wenn Speichern oder Versand den serverseitigen
         * Draft-Bestand verändert hat, aktualisieren wir den
         * Entwürfe-Ordner sofort.
         */
        if (composeWindow.DraftMailboxChanged &&
            IsDraftFolderSelectedForEditing())
        {
            await _viewModel
                .ReloadAsync();
        }
    }

    private bool IsDraftFolderSelectedForEditing()
    {
        return string.Equals(
            _viewModel.SelectedFolder?.DisplayName,
            "Entwürfe",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string?
        GetButtonText(
            Button button)
    {
        return button.Content switch
        {
            TextBlock textBlock =>
                textBlock.Text,

            string text =>
                text,

            _ =>
                null
        };
    }

    private static void SetButtonText(
        Button button,
        string text)
    {
        if (button.Content is TextBlock textBlock)
        {
            textBlock.Text =
                text;

            return;
        }

        button.Content =
            text;
    }

    private static IEnumerable<T>
        FindVisualChildren<T>(
            DependencyObject parent)
        where T : DependencyObject
    {
        var childCount =
            VisualTreeHelper.GetChildrenCount(
                parent);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper.GetChild(
                    parent,
                    index);

            if (child is T matchingChild)
            {
                yield return matchingChild;
            }

            foreach (var descendant in
                     FindVisualChildren<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }
}