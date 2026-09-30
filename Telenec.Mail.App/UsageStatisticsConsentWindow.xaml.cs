using System.Windows;

namespace Telenec.Mail.App;

public partial class UsageStatisticsConsentWindow : Window
{
    public UsageStatisticsConsentWindow()
    {
        InitializeComponent();
    }

    public bool ConsentGranted
    {
        get;
        private set;
    }

    private void GrantButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ConsentGranted =
            true;

        DialogResult =
            true;
    }

    private void DeclineButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ConsentGranted =
            false;

        DialogResult =
            false;
    }
}