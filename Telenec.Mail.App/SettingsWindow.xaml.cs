using System.Windows;
using System.Windows.Controls;

namespace Telenec.Mail.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void SettingsNavigation_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        /*
         * SelectionChanged kann bereits während
         * InitializeComponent ausgelöst werden.
         *
         * Zu diesem Zeitpunkt sind möglicherweise noch nicht
         * alle benannten Elemente des Fensters erzeugt.
         */
        if (GeneralSettingsPanel is null ||
            ComposeSettingsPanel is null ||
            NotificationSettingsPanel is null)
        {
            return;
        }

        GeneralSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        ComposeSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 1
                ? Visibility.Visible
                : Visibility.Collapsed;

        NotificationSettingsPanel.Visibility =
            SettingsNavigation.SelectedIndex == 2
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}