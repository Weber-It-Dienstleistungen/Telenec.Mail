using System.Windows;

namespace Telenec.Mail.App;

public partial class ContactCategoryWindow :
    Window
{
    public ContactCategoryWindow()
    {
        InitializeComponent();

        Loaded +=
            ContactCategoryWindow_OnLoaded;
    }

    public string CategoryName
    {
        get;
        private set;
    } =
        string.Empty;

    private void ContactCategoryWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        CategoryNameTextBox.Focus();
    }

    private void AddButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var categoryName =
            CategoryNameTextBox.Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                categoryName))
        {
            MessageBox.Show(
                this,
                "Bitte geben Sie einen Namen für die Kategorie ein.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CategoryNameTextBox.Focus();

            return;
        }

        CategoryName =
            categoryName;

        DialogResult =
            true;

        Close();
    }
}