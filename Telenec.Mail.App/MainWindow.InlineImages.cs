using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    /*
     * Erkennt Bild-Tags, deren cid:-Referenz im HTML nach der
     * normalen MIME-Aufbereitung noch vorhanden ist.
     *
     * Kleine CID-Bilder werden bereits in
     * ImapMailDataSource erfolgreich als data:-URI
     * eingebettet.
     *
     * Bleibt hier noch ein cid:-Bild übrig, konnte es dort
     * nicht eingebettet werden - typischerweise weil die
     * resultierende HTML-Größe die WebView2-Grenze
     * überschreiten würde.
     */
    private static readonly Regex
        UnresolvedCidImageRegex =
            new(
                @"<img\b(?=[^>]*\bsrc\s*=\s*(?:""cid:[^""]*""|'cid:[^']*'|cid:[^\s>]+))[^>]*>",
                RegexOptions.IgnoreCase |
                RegexOptions.Singleline |
                RegexOptions.CultureInvariant);

    /*
     * Native WPF-Vorschau für Bild-MIME-Parts.
     *
     * Für Nachrichten mit großen ungelösten CID-Bildern
     * verwenden wir bewusst den vorhandenen Plaintext-Body
     * und hängen die Bilder darunter.
     *
     * Dadurch umgehen wir die Größenbegrenzung von
     * WebView2.NavigateToString vollständig.
     */
    private static readonly bool
        InlineImageSupportClassHandlersRegistered =
            RegisterInlineImageSupportClassHandlers();

    private bool
        _inlineImageSupportInstalled;

    private int
        _inlineImagePreviewVersion;

    private readonly List<FrameworkElement>
        _inlineImagePreviewElements =
            new();

    private static bool
        RegisterInlineImageSupportClassHandlers()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnInlineImageSupportLoaded));

        return true;
    }

    private static void
        MainWindow_OnInlineImageSupportLoaded(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        window.InstallInlineImageSupport();
    }

    private void InstallInlineImageSupport()
    {
        if (_inlineImageSupportInstalled)
        {
            return;
        }

        _inlineImageSupportInstalled =
            true;

        _viewModel.PropertyChanged +=
            MainViewModel_OnPropertyChangedForInlineImages;

        Closed +=
            MainWindowInlineImages_OnClosed;

        QueueInlineImagePreviewRender();
    }

    private void
        MainViewModel_OnPropertyChangedForInlineImages(
            object? sender,
            PropertyChangedEventArgs e)
    {
        if (!string.Equals(
                e.PropertyName,
                nameof(
                    MainViewModel.SelectedMessage),
                StringComparison.Ordinal))
        {
            return;
        }

        _inlineImagePreviewVersion++;

        ClearInlineImagePreview();

        QueueInlineImagePreviewRender();
    }

    private void QueueInlineImagePreviewRender()
    {
        if (!_inlineImageSupportInstalled)
        {
            return;
        }

        var previewVersion =
            ++_inlineImagePreviewVersion;

        /*
         * Der normale Nachrichtenrenderer soll zuerst seine
         * Entscheidung treffen.
         *
         * Erst danach prüfen wir, ob für diese konkrete Mail
         * der große-CID-Fallback erforderlich ist.
         */
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(
                () =>
                {
                    _ =
                        RenderInlineImagePreviewAsync(
                            previewVersion);
                }));
    }

    private async Task RenderInlineImagePreviewAsync(
        int previewVersion)
    {
        try
        {
            if (_searchIsActive)
            {
                return;
            }

            var message =
                _viewModel.SelectedMessage;

            if (message is null ||
                message.UniqueId == 0)
            {
                return;
            }

            var imageAttachments =
                message
                    .Attachments
                    .Where(
                        IsInlinePreviewImage)
                    .ToArray();

            if (imageAttachments.Length == 0)
            {
                return;
            }

            var requiresNativeFallback =
                RequiresNativeInlineImageFallback(
                    message);

            if (!requiresNativeFallback)
            {
                return;
            }

            if (!IsCurrentInlineImagePreview(
                    message,
                    previewVersion))
            {
                return;
            }

            /*
             * Entscheidend:
             *
             * Bei einem großen ungelösten CID-Bild wechseln
             * wir bewusst vom HTML-Renderer auf die bereits
             * vorhandene Plaintext-Darstellung.
             *
             * MailMessageItemViewModel.Body enthält den
             * geladenen Textteil der Nachricht.
             *
             * Dadurch verlieren wir den Text nicht mehr, nur
             * weil das zugehörige Bild nicht in NavigateToString
             * passt.
             */
            ShowPlainTextView();

            var contentPanel =
                GetPlainTextContentPanel();

            if (contentPanel is null)
            {
                return;
            }

            ClearInlineImagePreview();

            foreach (var attachment in
                     imageAttachments)
            {
                if (!IsCurrentInlineImagePreview(
                        message,
                        previewVersion))
                {
                    return;
                }

                var temporaryPath =
                    CreateTemporaryInlineImagePath(
                        attachment);

                try
                {
                    /*
                     * Exakt derselbe IMAP-/MailKit-
                     * Downloadpfad, den wir mit "Speichern
                     * unter" bereits erfolgreich verifiziert
                     * haben.
                     */
                    await using (
                        var destination =
                            new FileStream(
                                temporaryPath,
                                FileMode.Create,
                                FileAccess.Write,
                                FileShare.None,
                                bufferSize:
                                    81920,
                                useAsync:
                                    true))
                    {
                        var downloaded =
                            await _viewModel
                                .DownloadAttachmentAsync(
                                    message,
                                    attachment,
                                    destination);

                        if (!downloaded)
                        {
                            continue;
                        }
                    }

                    if (!IsCurrentInlineImagePreview(
                            message,
                            previewVersion))
                    {
                        return;
                    }

                    if (!File.Exists(
                            temporaryPath))
                    {
                        continue;
                    }

                    var fileInfo =
                        new FileInfo(
                            temporaryPath);

                    if (fileInfo.Length <= 0)
                    {
                        continue;
                    }

                    var imageSource =
                        LoadInlinePreviewBitmapFromFile(
                            temporaryPath);

                    if (imageSource is null)
                    {
                        continue;
                    }

                    if (!IsCurrentInlineImagePreview(
                            message,
                            previewVersion))
                    {
                        return;
                    }

                    var previewElement =
                        CreateInlinePreviewImageControl(
                            imageSource,
                            attachment);

                    contentPanel.Children.Add(
                        previewElement);

                    _inlineImagePreviewElements.Add(
                        previewElement);
                }
                catch
                {
                    /*
                     * Ein einzelnes Bild darf den eigentlichen
                     * Mailreader niemals beeinträchtigen.
                     *
                     * Der Anhang bleibt weiterhin oben
                     * verfügbar.
                     */
                }
                finally
                {
                    TryDeleteTemporaryInlineImage(
                        temporaryPath);
                }
            }
        }
        catch
        {
            /*
             * Die Inline-Vorschau ist ein zusätzlicher
             * Darstellungsweg.
             *
             * Der Mailinhalt und der Attachment-Fallback
             * bleiben bei jedem Fehler verfügbar.
             */
        }
    }

    private static bool
        RequiresNativeInlineImageFallback(
            MailMessageItemViewModel message)
    {
        /*
         * Plaintext-Mail mit image/*-Part:
         *
         * Bild wird nativ unter dem Text dargestellt.
         */
        if (!message.HasHtmlBody)
        {
            return true;
        }

        /*
         * HTML-Mail:
         *
         * Nur eingreifen, wenn nach der normalen
         * CID-Verarbeitung tatsächlich noch ein
         * <img src="cid:..."> vorhanden ist.
         *
         * Normale HTML-Mails und bereits erfolgreich
         * eingebettete CID-Bilder bleiben vollständig beim
         * bisherigen WebView2-Renderer.
         */
        return
            !string.IsNullOrWhiteSpace(
                message.HtmlBody) &&
            UnresolvedCidImageRegex.IsMatch(
                message.HtmlBody);
    }

    private Panel?
        GetPlainTextContentPanel()
    {
        return PlainTextMailView.Content
            as Panel;
    }

    private static string
        CreateTemporaryInlineImagePath(
            MailAttachmentData attachment)
    {
        var extension =
            GetSafeInlineImageExtension(
                attachment);

        return Path.Combine(
            Path.GetTempPath(),
            "TelenecMail_" +
            Guid.NewGuid()
                .ToString("N") +
            extension);
    }

    private static string
        GetSafeInlineImageExtension(
            MailAttachmentData attachment)
    {
        try
        {
            var extension =
                Path.GetExtension(
                    attachment.FileName);

            if (!string.IsNullOrWhiteSpace(
                    extension) &&
                extension.Length <= 10 &&
                extension.All(
                    character =>
                        char.IsLetterOrDigit(
                            character) ||
                        character == '.'))
            {
                return extension;
            }
        }
        catch
        {
        }

        return attachment
            .ContentType
            .Trim()
            .ToLowerInvariant() switch
        {
            "image/jpeg" =>
                ".jpg",

            "image/png" =>
                ".png",

            "image/gif" =>
                ".gif",

            "image/bmp" =>
                ".bmp",

            "image/tiff" =>
                ".tif",

            "image/webp" =>
                ".webp",

            _ =>
                ".img"
        };
    }

    private static BitmapSource?
        LoadInlinePreviewBitmapFromFile(
            string filePath)
    {
        try
        {
            using var stream =
                new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

            var decoder =
                BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

            var frame =
                decoder.Frames.FirstOrDefault();

            if (frame is null ||
                frame.PixelWidth <= 0 ||
                frame.PixelHeight <= 0)
            {
                return null;
            }

            BitmapSource result =
                frame;

            /*
             * Für die Vorschau reicht eine maximale Breite
             * von 1600 Pixeln.
             *
             * Das reduziert den Arbeitsspeicher bei großen
             * Smartphone-Fotos erheblich.
             */
            const int maximumPreviewWidth =
                1600;

            if (frame.PixelWidth >
                maximumPreviewWidth)
            {
                var scale =
                    maximumPreviewWidth /
                    (double)frame.PixelWidth;

                var transformed =
                    new TransformedBitmap(
                        frame,
                        new ScaleTransform(
                            scale,
                            scale));

                transformed.Freeze();

                result =
                    transformed;
            }
            else if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static FrameworkElement
        CreateInlinePreviewImageControl(
            ImageSource imageSource,
            MailAttachmentData attachment)
    {
        var image =
            new Image
            {
                Source =
                    imageSource,

                MaxWidth =
                    760,

                Stretch =
                    Stretch.Uniform,

                HorizontalAlignment =
                    HorizontalAlignment.Left,

                SnapsToDevicePixels =
                    true,

                ToolTip =
                    attachment.FileName
            };

        RenderOptions.SetBitmapScalingMode(
            image,
            BitmapScalingMode.HighQuality);

        var border =
            new Border
            {
                Margin =
                    new Thickness(
                        0,
                        24,
                        0,
                        0),

                MaxWidth =
                    760,

                HorizontalAlignment =
                    HorizontalAlignment.Left,

                CornerRadius =
                    new CornerRadius(8),

                ClipToBounds =
                    true,

                Background =
                    Brushes.Transparent,

                Child =
                    image
            };

        return border;
    }

    private static bool IsInlinePreviewImage(
        MailAttachmentData attachment)
    {
        if (string.IsNullOrWhiteSpace(
                attachment.PartSpecifier) ||
            string.IsNullOrWhiteSpace(
                attachment.ContentType))
        {
            return false;
        }

        return attachment
            .ContentType
            .StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCurrentInlineImagePreview(
        MailMessageItemViewModel message,
        int previewVersion)
    {
        return
            !_searchIsActive &&
            previewVersion ==
                _inlineImagePreviewVersion &&
            ReferenceEquals(
                _viewModel.SelectedMessage,
                message);
    }

    private void ClearInlineImagePreview()
    {
        if (_inlineImagePreviewElements.Count ==
            0)
        {
            return;
        }

        var contentPanel =
            GetPlainTextContentPanel();

        if (contentPanel is not null)
        {
            foreach (var element in
                     _inlineImagePreviewElements)
            {
                contentPanel.Children.Remove(
                    element);
            }
        }

        _inlineImagePreviewElements.Clear();
    }

    private static void
        TryDeleteTemporaryInlineImage(
            string filePath)
    {
        try
        {
            if (File.Exists(
                    filePath))
            {
                File.Delete(
                    filePath);
            }
        }
        catch
        {
        }
    }

    private void MainWindowInlineImages_OnClosed(
        object? sender,
        EventArgs e)
    {
        _inlineImagePreviewVersion++;

        _viewModel.PropertyChanged -=
            MainViewModel_OnPropertyChangedForInlineImages;

        ClearInlineImagePreview();

        Closed -=
            MainWindowInlineImages_OnClosed;
    }
}