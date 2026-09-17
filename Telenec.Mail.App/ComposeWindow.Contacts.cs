namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    public void PrepareNewMessageTo(
        string emailAddress)
    {
        if (string.IsNullOrWhiteSpace(
                emailAddress))
        {
            return;
        }

        _viewModel.RecipientAddress =
            emailAddress.Trim();
    }
}