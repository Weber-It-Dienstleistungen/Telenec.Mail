using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

internal static class MailAddressCopyBootstrapper
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(
                OnContextMenuOpening),
            handledEventsToo:
                true);

        EventManager.RegisterClassHandler(
            typeof(TextBox),
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(
                OnContextMenuOpening),
            handledEventsToo:
                true);
    }

    private static void OnContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.DataContext
                is not MailMessageItemViewModel message)
        {
            return;
        }

        if (!IsInsideSupportedMailWindow(
                element))
        {
            return;
        }

        var displayedText =
            GetDisplayedText(
                element);

        if (!TryResolveMailAddress(
                message,
                displayedText,
                out var address))
        {
            return;
        }

        var contextMenu =
            new ContextMenu();

        var copyItem =
            new MenuItem
            {
                Header =
                    "E-Mail-Adresse kopieren",

                Tag =
                    address
            };

        copyItem.Click +=
            CopyMailAddressMenuItem_OnClick;

        contextMenu.Items.Add(
            copyItem);

        element.ContextMenu =
            contextMenu;
    }

    private static void CopyMailAddressMenuItem_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Tag is not string address ||
            string.IsNullOrWhiteSpace(
                address))
        {
            return;
        }

        try
        {
            Clipboard.SetText(
                address.Trim());
        }
        catch
        {
            MessageBox.Show(
                "Die E-Mail-Adresse konnte nicht in die Zwischenablage kopiert werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static bool TryResolveMailAddress(
        MailMessageItemViewModel message,
        string displayedText,
        out string address)
    {
        address =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
                displayedText))
        {
            return false;
        }

        var normalizedText =
            displayedText.Trim();

        if (MatchesAddressText(
                normalizedText,
                message.SenderAddress))
        {
            address =
                message.SenderAddress.Trim();

            return true;
        }

        if (MatchesAddressText(
                normalizedText,
                message.RecipientAddress))
        {
            address =
                message.RecipientAddress.Trim();

            return true;
        }

        return false;
    }

    private static bool MatchesAddressText(
        string displayedText,
        string? address)
    {
        if (string.IsNullOrWhiteSpace(
                address))
        {
            return false;
        }

        var normalizedAddress =
            address.Trim();

        if (string.Equals(
                displayedText,
                normalizedAddress,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        /*
         * Im separaten Lesefenster wird der Empfänger
         * beispielsweise als
         *
         * An: kunde@example.de
         *
         * dargestellt.
         */
        if (string.Equals(
                displayedText,
                $"An: {normalizedAddress}",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        /*
         * Falls die Hauptansicht Absender und Empfänger in
         * einer gemeinsamen Adresszeile darstellt, soll auch
         * diese Darstellung erkannt werden.
         */
        return displayedText.Contains(
            normalizedAddress,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayedText(
        FrameworkElement element)
    {
        return element switch
        {
            TextBlock textBlock =>
                textBlock.Text ?? string.Empty,

            TextBox textBox =>
                textBox.Text ?? string.Empty,

            _ =>
                string.Empty
        };
    }

    private static bool IsInsideSupportedMailWindow(
        DependencyObject element)
    {
        var current =
            element;

        while (current is not null)
        {
            if (current is MainWindow ||
                current is MailMessageWindow)
            {
                return true;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return false;
    }
}