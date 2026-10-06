using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App;

public partial class MailMessageWindow
{
    private Func<
        MailAttachmentData,
        Stream,
        Task<bool>>?
        _attachmentDownloadAsync;

    private bool
        _isSavingAttachment;

    internal void ConfigureAttachmentDownload(
        Func<
            MailAttachmentData,
            Stream,
            Task<bool>>
            attachmentDownloadAsync)
    {
        ArgumentNullException.ThrowIfNull(
            attachmentDownloadAsync);

        _attachmentDownloadAsync =
            attachmentDownloadAsync;

        AttachmentArea.IsEnabled =
            true;
    }

    private async void SaveAttachmentButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_isSavingAttachment ||
            _attachmentDownloadAsync is null ||
            sender is not FrameworkElement element ||
            element.DataContext
                is not MailAttachmentData attachment)
        {
            return;
        }

        var saveDialog =
            new SaveFileDialog
            {
                Title =
                    "Anhang speichern unter",

                FileName =
                    attachment.FileName,

                Filter =
                    "Alle Dateien (*.*)|*.*",

                AddExtension =
                    false,

                OverwritePrompt =
                    true,

                CheckPathExists =
                    true
            };

        var dialogResult =
            saveDialog.ShowDialog(
                this);

        if (dialogResult != true)
        {
            return;
        }

        var targetPath =
            saveDialog.FileName;

        var targetDirectory =
            Path.GetDirectoryName(
                targetPath);

        if (string.IsNullOrWhiteSpace(
                targetDirectory))
        {
            MessageBox.Show(
                this,
                "Der ausgewählte Speicherort ist ungültig.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        var temporaryPath =
            Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}." +
                $"{Guid.NewGuid():N}.telenec-download");

        _isSavingAttachment =
            true;

        AttachmentArea.IsEnabled =
            false;

        Mouse.OverrideCursor =
            Cursors.Wait;

        try
        {
            await using (
                var destination =
                    new FileStream(
                        temporaryPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize:
                            81920,
                        options:
                            FileOptions.Asynchronous |
                            FileOptions.SequentialScan))
            {
                var downloaded =
                    await _attachmentDownloadAsync(
                        attachment,
                        destination);

                if (!downloaded)
                {
                    MessageBox.Show(
                        this,
                        "Der Anhang konnte nicht gespeichert werden.\n\n" +
                        "Die zugehörige Nachricht ist im aktuellen Mailordner " +
                        "nicht mehr eindeutig verfügbar.",
                        "Telenec Mail",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }
            }

            File.Move(
                temporaryPath,
                targetPath,
                overwrite:
                    true);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Der Anhang konnte nicht gespeichert werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und den ausgewählten Speicherort.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TryDeleteTemporaryAttachmentFile(
                temporaryPath);

            Mouse.OverrideCursor =
                null;

            _isSavingAttachment =
                false;

            if (IsVisible)
            {
                AttachmentArea.IsEnabled =
                    _attachmentDownloadAsync
                    is not null;
            }
        }
    }

    private static void
        TryDeleteTemporaryAttachmentFile(
            string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
        }
    }
}