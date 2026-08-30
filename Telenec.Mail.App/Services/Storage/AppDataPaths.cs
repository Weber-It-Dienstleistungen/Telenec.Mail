using System.IO;

namespace Telenec.Mail.App.Services.Storage;

public sealed class AppDataPaths
{
    public AppDataPaths()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Telenec",
            "Mail");

        DatabasePath = Path.Combine(
            RootDirectory,
            "telenec-mail.db");
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }
}