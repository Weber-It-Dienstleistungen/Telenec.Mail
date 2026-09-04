using System.IO;

namespace Telenec.Mail.App.Services.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths()
    {
        RootDirectory =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "Telenec",
                "Mail");

        DatabasePath =
            Path.Combine(
                RootDirectory,
                "telenec-mail.db");

        LogDirectory =
            Path.Combine(
                RootDirectory,
                "Logs");

        LogFilePathPattern =
            Path.Combine(
                LogDirectory,
                "telenec-mail-.log");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public string LogDirectory { get; }

    public string LogFilePathPattern { get; }
}