using Velopack;

namespace Telenec.Mail.App;

internal static class Program
{
    [STAThread]
    private static void Main(
        string[] args)
    {
        VelopackApp
            .Build()
            .Run();

        var application =
            new App();

        application.InitializeComponent();

        application.Run();
    }
}