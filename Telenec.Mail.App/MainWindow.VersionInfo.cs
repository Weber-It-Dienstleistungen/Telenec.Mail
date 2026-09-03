using System.Reflection;
using System.Windows;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    protected override void OnInitialized(
        EventArgs e)
    {
        base.OnInitialized(e);

        Loaded +=
            MainWindowVersionInfo_OnLoaded;
    }

    private void MainWindowVersionInfo_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindowVersionInfo_OnLoaded;

        Title =
            $"Telenec Mail — Version {GetApplicationVersion()}";
    }

    private static string GetApplicationVersion()
    {
        var assembly =
            Assembly.GetEntryAssembly();

        var informationalVersion =
            assembly?
                .GetCustomAttribute<
                    AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(
                informationalVersion))
        {
            var metadataSeparatorIndex =
                informationalVersion.IndexOf(
                    '+');

            if (metadataSeparatorIndex >= 0)
            {
                informationalVersion =
                    informationalVersion[
                        ..metadataSeparatorIndex];
            }

            return informationalVersion;
        }

        return assembly?
                   .GetName()
                   .Version?
                   .ToString()
               ?? "unbekannt";
    }
}