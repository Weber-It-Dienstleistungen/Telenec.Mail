using System.Windows;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}