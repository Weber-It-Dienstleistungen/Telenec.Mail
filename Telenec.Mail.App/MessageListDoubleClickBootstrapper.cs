using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

internal static class MessageListDoubleClickBootstrapper
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(ListBoxItem),
            Control.MouseDoubleClickEvent,
            new MouseButtonEventHandler(
                OnListBoxItemMouseDoubleClick));
    }

    private static void OnListBoxItemMouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item ||
            item.DataContext
                is not MailMessageItemViewModel message)
        {
            return;
        }

        var listBox =
            FindAncestor<ListBox>(
                item);

        /*
         * Der Handler gilt ausschließlich für die
         * Nachrichtenliste im Hauptfenster.
         *
         * Andere ListBoxen, beispielsweise Ordner oder
         * Kontakte, bleiben vollständig unberührt.
         */
        if (listBox is null ||
            !string.Equals(
                listBox.Name,
                "MessageListBox",
                StringComparison.Ordinal))
        {
            return;
        }

        var mainWindow =
            FindAncestor<MainWindow>(
                item);

        if (mainWindow is null)
        {
            return;
        }

        e.Handled =
            true;

        /*
         * Das MainWindow kennt den Serverkontext der
         * geöffneten Nachricht.
         *
         * Dadurch kann das Nachrichtenfenster echte Aktionen
         * anbieten, ohne selbst IMAP-/Compose-Logik zu
         * duplizieren.
         */
        mainWindow.OpenMessageWindowFromUi(
            message);
    }

    private static T?
        FindAncestor<T>(
            DependencyObject? element)
        where T : DependencyObject
    {
        var current =
            element;

        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return null;
    }
}