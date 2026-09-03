using System.Windows;
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
    }

    private void CloseButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}