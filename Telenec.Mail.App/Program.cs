using Telenec.Mail.App.Services.Updates;
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
            .OnAfterUpdateFastCallback(
                version =>
                    ReleaseNotesUpdateMarker
                        .MarkPending(
                            version.ToString()))
            .Run();

        var application =
            new App();

        application.InitializeComponent();

        application.Run();
    }
}