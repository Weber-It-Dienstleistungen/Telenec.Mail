using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Telenec.Mail.App.Services.Updates;

namespace Telenec.Mail.App;

public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow()
    {
        InitializeComponent();
    }

    public void ShowReleaseNotes(
        ReleaseNotesInfo releaseNotes)
    {
        ArgumentNullException.ThrowIfNull(
            releaseNotes);

        DataContext =
            releaseNotes;

        ReleaseNotesActionText.Visibility =
            !string.IsNullOrWhiteSpace(
                releaseNotes.ActionText) &&
            !string.IsNullOrWhiteSpace(
                releaseNotes.ActionUri)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ReleaseNotesActionLink_OnRequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        e.Uri.AbsoluteUri,

                    UseShellExecute =
                        true
                });

            e.Handled =
                true;
        }
        catch
        {
            MessageBox.Show(
                "Die Windows-Einstellungen konnten nicht geöffnet werden.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}