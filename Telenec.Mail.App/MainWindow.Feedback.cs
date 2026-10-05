using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string FeedbackMenuItemTag =
        "Telenec.Mail.Feedback";

    private const string FeedbackMenuSeparatorTag =
        "Telenec.Mail.Feedback.Separator";

    private const string FeedbackRecipient =
        "fabian.weber@telenec.de";

    private static readonly bool
        FeedbackMenuClassHandlerRegistered =
            RegisterFeedbackMenuClassHandler();

    private bool
        _feedbackMenuInstalled;

    private static bool
        RegisterFeedbackMenuClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnFeedbackMenuLoaded));

        return true;
    }

    private static void MainWindow_OnFeedbackMenuLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        if (!ReferenceEquals(
                e.OriginalSource,
                window))
        {
            return;
        }

        /*
         * Die bestehenden dynamischen Menüpunkte werden
         * zuerst installiert.
         *
         * Dadurch können wir Feedback anschließend gezielt
         * direkt vor "Konto abmelden" einfügen.
         */
        window.InstallMigrationMenu();
        window.InstallSettingsMenu();
        window.InstallFeedbackMenu();
    }

    private void InstallFeedbackMenu()
    {
        if (_feedbackMenuInstalled)
        {
            return;
        }

        var contextMenu =
            AccountMenuButton.ContextMenu;

        if (contextMenu is null)
        {
            return;
        }

        var existingFeedbackItem =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag as string,
                            FeedbackMenuItemTag,
                            StringComparison.Ordinal));

        if (existingFeedbackItem is not null)
        {
            _feedbackMenuInstalled =
                true;

            return;
        }

        var feedbackMenuItem =
            new MenuItem
            {
                Header =
                    "Feedback melden …",

                Tag =
                    FeedbackMenuItemTag
            };

        feedbackMenuItem.Items.Add(
            CreateFeedbackTypeMenuItem(
                "Fehler"));

        feedbackMenuItem.Items.Add(
            CreateFeedbackTypeMenuItem(
                "Fehlende Funktion"));

        feedbackMenuItem.Items.Add(
            CreateFeedbackTypeMenuItem(
                "Lob"));

        var logoutIndex =
            FindLogoutMenuItemIndex(
                contextMenu);

        if (logoutIndex < 0)
        {
            contextMenu.Items.Add(
                feedbackMenuItem);

            contextMenu.Items.Add(
                new Separator
                {
                    Tag =
                        FeedbackMenuSeparatorTag
                });
        }
        else
        {
            contextMenu.Items.Insert(
                logoutIndex,
                feedbackMenuItem);

            contextMenu.Items.Insert(
                logoutIndex + 1,
                new Separator
                {
                    Tag =
                        FeedbackMenuSeparatorTag
                });
        }

        _feedbackMenuInstalled =
            true;
    }

    private MenuItem CreateFeedbackTypeMenuItem(
        string feedbackType)
    {
        var item =
            new MenuItem
            {
                Header =
                    feedbackType,

                Tag =
                    feedbackType
            };

        item.Click +=
            FeedbackTypeMenuItem_OnClick;

        return item;
    }

    private static int FindLogoutMenuItemIndex(
        ContextMenu contextMenu)
    {
        for (var index = 0;
             index < contextMenu.Items.Count;
             index++)
        {
            if (contextMenu.Items[index]
                    is not MenuItem menuItem)
            {
                continue;
            }

            if (string.Equals(
                    menuItem.Header
                        as string,
                    "Konto abmelden",
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private void FeedbackTypeMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Tag
                is not string feedbackType ||
            string.IsNullOrWhiteSpace(
                feedbackType))
        {
            return;
        }

        OpenFeedbackComposer(
            feedbackType);
    }

    private void OpenFeedbackComposer(
        string feedbackType)
    {
        try
        {
            var composeWindow =
                _serviceProvider
                    .GetRequiredService<
                        ComposeWindow>();

            composeWindow.Owner =
                this;

            composeWindow
                .PrepareFeedback(
                    recipientAddress:
                        FeedbackRecipient,

                    feedbackType:
                        feedbackType,

                    applicationVersion:
                        GetApplicationVersion());

            composeWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Das Feedback-Fenster konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Feedback melden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}