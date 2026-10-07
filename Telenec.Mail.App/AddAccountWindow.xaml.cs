using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class AddAccountWindow : Window
{
    private readonly LoginViewModel
        _viewModel;

    public AddAccountWindow(
        LoginViewModel viewModel)
    {
        InitializeComponent();

        _viewModel =
            viewModel;

        DataContext =
            _viewModel;

        _viewModel.PrepareKnownAccount(
            null);
    }

    private void PasswordInput_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.SetPasswordAvailable(
            !string.IsNullOrEmpty(
                PasswordInput.Password));
    }

    private async void AddButton_OnClick(
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

        DialogResult =
            true;
    }

    private void CancelButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;
    }
}