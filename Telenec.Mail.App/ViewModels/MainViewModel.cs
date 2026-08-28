using System.Collections.ObjectModel;

namespace Telenec.Mail.App.ViewModels;

public sealed class MainViewModel : BaseViewModel
{
    private MailFolderItemViewModel? _selectedFolder;

    public MainViewModel()
    {
        MailFolders =
        [
            new MailFolderItemViewModel(
                displayName: "Posteingang",
                unreadCount: 12),

            new MailFolderItemViewModel(
                displayName: "Gesendet"),

            new MailFolderItemViewModel(
                displayName: "Entwürfe",
                hasSeparatorAfter: true),

            new MailFolderItemViewModel(
                displayName: "Archiv"),

            new MailFolderItemViewModel(
                displayName: "Junk"),

            new MailFolderItemViewModel(
                displayName: "Papierkorb")
        ];

        _selectedFolder = MailFolders[0];
    }

    public string ApplicationTitle => "Telenec Mail";

    public ObservableCollection<MailFolderItemViewModel> MailFolders { get; }

    public MailFolderItemViewModel? SelectedFolder
    {
        get => _selectedFolder;

        set
        {
            if (ReferenceEquals(_selectedFolder, value))
            {
                return;
            }

            _selectedFolder = value;
            OnPropertyChanged();
        }
    }
}