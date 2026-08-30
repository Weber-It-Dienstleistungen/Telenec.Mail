using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;
    private readonly MainWindow _mainWindow;

    public LoginWindow(
        LoginViewModel viewModel,
        MainWindow mainWindow)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _mainWindow = mainWindow;

        DataContext = _viewModel;
    }

    public void PrepareKnownAccount(
        string? emailAddress)
    {
        _viewModel.PrepareKnownAccount(
            emailAddress);
    }

    private void PasswordInput_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.SetPasswordAvailable(
            !string.IsNullOrEmpty(
                PasswordInput.Password));
    }

    private async void LoginButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var success =
            await _viewModel.LoginAsync(
                PasswordInput.Password);

        if (!success)
        {
            return;
        }

        PasswordInput.Clear();

        Application.Current.MainWindow =
            _mainWindow;

        _mainWindow.Show();

        Close();
    }
}