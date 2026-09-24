using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _mailboxQuotaNavigationInitialized;

    private bool
        _mailboxQuotaRefreshRunning;

    private MailKitQuotaService?
        _mailboxQuotaService;

    private Border?
        _mailboxQuotaCard;

    private TextBlock?
        _mailboxQuotaPercentText;

    private TextBlock?
        _mailboxQuotaDetailText;

    private Border?
        _mailboxQuotaProgressHost;

    private Border?
        _mailboxQuotaProgressFill;

    private double
        _mailboxQuotaPercentage;

    private async void MainWindowQuota_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (!InitializeMailboxQuotaNavigation())
        {
            return;
        }

        Loaded -=
            MainWindowQuota_OnLoaded;

        _mailboxQuotaService =
            new MailKitQuotaService(
                _mailAccountStore,
                _credentialStore);

        RefreshButton.Click +=
            MailboxQuotaRefreshButton_OnClick;

        Closed +=
            MainWindowQuota_OnClosed;

        await RefreshMailboxQuotaAsync();
    }

    private bool InitializeMailboxQuotaNavigation()
    {
        if (_mailboxQuotaNavigationInitialized)
        {
            return true;
        }

        if (FolderListBox.Parent
            is not Grid navigationGrid)
        {
            return false;
        }

        /*
         * Die Kontakte-Navigation wurde unmittelbar vor
         * diesem Loaded-Handler eingefügt.
         *
         * Die letzte Grid-Zeile enthält weiterhin den
         * festen Kontobereich.
         *
         * Unsere neue Auto-Zeile landet damit exakt zwischen
         * "Kontakte" und Kontoanzeige.
         */
        var insertionRow =
            navigationGrid
                .RowDefinitions
                .Count - 1;

        if (insertionRow < 0)
        {
            return false;
        }

        var elementsToMove =
            navigationGrid
                .Children
                .OfType<UIElement>()
                .Where(
                    element =>
                        Grid.GetRow(
                            element) >=
                        insertionRow)
                .ToList();

        foreach (var element in
                 elementsToMove)
        {
            Grid.SetRow(
                element,
                Grid.GetRow(
                    element) + 1);
        }

        navigationGrid
            .RowDefinitions
            .Insert(
                insertionRow,
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

        var quotaCard =
            CreateMailboxQuotaCard();

        Grid.SetRow(
            quotaCard,
            insertionRow);

        navigationGrid
            .Children
            .Add(
                quotaCard);

        _mailboxQuotaNavigationInitialized =
            true;

        return true;
    }

    private Border CreateMailboxQuotaCard()
    {
        /*
         * Bewusst keine vollständig deckende Hintergrundfarbe.
         *
         * Dadurch bleibt der dunkle Navigationsbereich leicht
         * sichtbar und die Karte bekommt eine dezente
         * Glas-/Overlay-Anmutung.
         */
        var glassBackground =
            new SolidColorBrush(
                Color.FromArgb(
                    24,
                    255,
                    255,
                    255));

        var glassBorder =
            new SolidColorBrush(
                Color.FromArgb(
                    42,
                    255,
                    255,
                    255));

        var progressBackground =
            new SolidColorBrush(
                Color.FromArgb(
                    38,
                    255,
                    255,
                    255));

        var card =
            new Border
            {
                Margin =
                    new Thickness(
                        12,
                        0,
                        12,
                        12),

                Padding =
                    new Thickness(
                        12,
                        11,
                        12,
                        11),

                Background =
                    glassBackground,

                BorderBrush =
                    glassBorder,

                BorderThickness =
                    new Thickness(
                        1),

                CornerRadius =
                    new CornerRadius(
                        8),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Postfachspeicher – klicken zum Aktualisieren"
            };

        var content =
            new StackPanel();

        var header =
            new Grid();

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var title =
            new TextBlock
            {
                Text =
                    "Postfachspeicher",

                FontSize =
                    11,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.Text");

        Grid.SetColumn(
            title,
            0);

        header.Children.Add(
            title);

        var percentText =
            new TextBlock
            {
                Text =
                    "…",

                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0),

                FontSize =
                    11,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        percentText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        Grid.SetColumn(
            percentText,
            1);

        header.Children.Add(
            percentText);

        content.Children.Add(
            header);

        var detailText =
            new TextBlock
            {
                Text =
                    "Speicherbelegung wird geladen …",

                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        8),

                FontSize =
                    10,

                TextWrapping =
                    TextWrapping.Wrap
            };

        detailText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        content.Children.Add(
            detailText);

        var progressHost =
            new Border
            {
                Height =
                    7,

                Background =
                    progressBackground,

                CornerRadius =
                    new CornerRadius(
                        3.5),

                ClipToBounds =
                    true
            };

        var progressGrid =
            new Grid();

        var progressFill =
            new Border
            {
                Width =
                    0,

                HorizontalAlignment =
                    HorizontalAlignment.Left,

                CornerRadius =
                    new CornerRadius(
                        3.5)
            };

        progressFill.SetResourceReference(
            Border.BackgroundProperty,
            "Status.Success");

        progressGrid.Children.Add(
            progressFill);

        progressHost.Child =
            progressGrid;

        progressHost.SizeChanged +=
            (_, _) =>
            {
                UpdateMailboxQuotaProgressWidth();
            };

        content.Children.Add(
            progressHost);

        card.Child =
            content;

        card.MouseLeftButtonUp +=
            MailboxQuotaCard_OnMouseLeftButtonUp;

        _mailboxQuotaCard =
            card;

        _mailboxQuotaPercentText =
            percentText;

        _mailboxQuotaDetailText =
            detailText;

        _mailboxQuotaProgressHost =
            progressHost;

        _mailboxQuotaProgressFill =
            progressFill;

        return card;
    }

    private async void MailboxQuotaCard_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        await RefreshMailboxQuotaAsync();
    }

    private async void MailboxQuotaRefreshButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await RefreshMailboxQuotaAsync();
    }

    private async Task RefreshMailboxQuotaAsync()
    {
        if (_mailboxQuotaRefreshRunning ||
            _mailboxQuotaService is null)
        {
            return;
        }

        _mailboxQuotaRefreshRunning =
            true;

        ShowMailboxQuotaLoadingState();

        try
        {
            var quota =
                await _mailboxQuotaService
                    .GetQuotaAsync();

            if (quota is null)
            {
                ShowMailboxQuotaUnavailableState();

                return;
            }

            ShowMailboxQuota(
                quota);
        }
        catch (OperationCanceledException)
        {
            ShowMailboxQuotaUnavailableState();
        }
        catch
        {
            ShowMailboxQuotaUnavailableState();
        }
        finally
        {
            _mailboxQuotaRefreshRunning =
                false;
        }
    }

    private void ShowMailboxQuotaLoadingState()
    {
        if (_mailboxQuotaPercentText is null ||
            _mailboxQuotaDetailText is null)
        {
            return;
        }

        _mailboxQuotaPercentage =
            0;

        _mailboxQuotaPercentText.Text =
            "…";

        _mailboxQuotaPercentText
            .SetResourceReference(
                TextBlock.ForegroundProperty,
                "Navigation.TextSecondary");

        _mailboxQuotaDetailText.Text =
            "Speicherbelegung wird geladen …";

        if (_mailboxQuotaProgressFill is not null)
        {
            _mailboxQuotaProgressFill
                .SetResourceReference(
                    Border.BackgroundProperty,
                    "Navigation.TextSecondary");
        }

        UpdateMailboxQuotaProgressWidth();
    }

    private void ShowMailboxQuotaUnavailableState()
    {
        if (_mailboxQuotaPercentText is null ||
            _mailboxQuotaDetailText is null)
        {
            return;
        }

        _mailboxQuotaPercentage =
            0;

        _mailboxQuotaPercentText.Text =
            "—";

        _mailboxQuotaPercentText
            .SetResourceReference(
                TextBlock.ForegroundProperty,
                "Navigation.TextSecondary");

        _mailboxQuotaDetailText.Text =
            "Speicherbelegung nicht verfügbar";

        if (_mailboxQuotaProgressFill is not null)
        {
            _mailboxQuotaProgressFill
                .SetResourceReference(
                    Border.BackgroundProperty,
                    "Navigation.TextSecondary");
        }

        UpdateMailboxQuotaProgressWidth();
    }

    private void ShowMailboxQuota(
        MailQuotaInfo quota)
    {
        if (quota.IsUnlimited)
        {
            ShowMailboxQuotaUnlimitedState(
                quota);

            return;
        }

        if (!quota.UsedBytes.HasValue ||
            !quota.LimitBytes.HasValue ||
            !quota.UsagePercentage.HasValue)
        {
            ShowMailboxQuotaUnavailableState();

            return;
        }

        var percentage =
            quota.UsagePercentage.Value;

        _mailboxQuotaPercentage =
            percentage;

        if (_mailboxQuotaPercentText is not null)
        {
            _mailboxQuotaPercentText.Text =
                percentage.ToString(
                    "0",
                    CultureInfo.CurrentCulture) +
                " %";
        }

        if (_mailboxQuotaDetailText is not null)
        {
            _mailboxQuotaDetailText.Text =
                $"{FormatMailboxStorageSize(quota.UsedBytes.Value)} von " +
                $"{FormatMailboxStorageSize(quota.LimitBytes.Value)} belegt";
        }

        ApplyMailboxQuotaUsageColor(
            percentage);

        UpdateMailboxQuotaProgressWidth();
    }

    private void ShowMailboxQuotaUnlimitedState(
        MailQuotaInfo quota)
    {
        _mailboxQuotaPercentage =
            0;

        if (_mailboxQuotaPercentText is not null)
        {
            _mailboxQuotaPercentText.Text =
                "Unbegrenzt";

            _mailboxQuotaPercentText
                .SetResourceReference(
                    TextBlock.ForegroundProperty,
                    "Status.Success");
        }

        if (_mailboxQuotaDetailText is not null)
        {
            _mailboxQuotaDetailText.Text =
                quota.UsedBytes.HasValue
                    ? $"{FormatMailboxStorageSize(quota.UsedBytes.Value)} belegt · kein Limit"
                    : "Kein Speicherlimit festgelegt";
        }

        /*
         * Bei einem unbegrenzten Postfach existiert kein
         * sinnvoller Prozentwert.
         *
         * Deshalb bleibt der Fortschrittsbalken leer und
         * dient lediglich als dezentes Designelement.
         */
        if (_mailboxQuotaProgressFill is not null)
        {
            _mailboxQuotaProgressFill
                .SetResourceReference(
                    Border.BackgroundProperty,
                    "Status.Success");
        }

        UpdateMailboxQuotaProgressWidth();
    }

    private void ApplyMailboxQuotaUsageColor(
        double percentage)
    {
        if (_mailboxQuotaProgressFill is null ||
            _mailboxQuotaPercentText is null)
        {
            return;
        }

        string resourceKey;

        if (percentage <= 33.0)
        {
            resourceKey =
                "Status.Success";
        }
        else if (percentage <= 66.0)
        {
            resourceKey =
                "Status.Warning";
        }
        else
        {
            resourceKey =
                "Status.Error";
        }

        _mailboxQuotaProgressFill
            .SetResourceReference(
                Border.BackgroundProperty,
                resourceKey);

        _mailboxQuotaPercentText
            .SetResourceReference(
                TextBlock.ForegroundProperty,
                resourceKey);
    }

    private void UpdateMailboxQuotaProgressWidth()
    {
        if (_mailboxQuotaProgressHost is null ||
            _mailboxQuotaProgressFill is null)
        {
            return;
        }

        var normalizedPercentage =
            Math.Clamp(
                _mailboxQuotaPercentage,
                0.0,
                100.0);

        _mailboxQuotaProgressFill.Width =
            _mailboxQuotaProgressHost
                .ActualWidth *
            normalizedPercentage /
            100.0;
    }

    private static string FormatMailboxStorageSize(
        long bytes)
    {
        const double kibibyte =
            1024.0;

        const double mebibyte =
            kibibyte *
            1024.0;

        const double gibibyte =
            mebibyte *
            1024.0;

        const double tebibyte =
            gibibyte *
            1024.0;

        if (bytes >=
            tebibyte)
        {
            return
                (bytes / tebibyte)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture) +
                " TB";
        }

        if (bytes >=
            gibibyte)
        {
            return
                (bytes / gibibyte)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture) +
                " GB";
        }

        if (bytes >=
            mebibyte)
        {
            return
                (bytes / mebibyte)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture) +
                " MB";
        }

        if (bytes >=
            kibibyte)
        {
            return
                (bytes / kibibyte)
                    .ToString(
                        "0.##",
                        CultureInfo.CurrentCulture) +
                " KB";
        }

        return
            bytes.ToString(
                CultureInfo.CurrentCulture) +
            " B";
    }

    private void MainWindowQuota_OnClosed(
        object? sender,
        EventArgs e)
    {
        RefreshButton.Click -=
            MailboxQuotaRefreshButton_OnClick;

        Closed -=
            MainWindowQuota_OnClosed;

        if (_mailboxQuotaCard is not null)
        {
            _mailboxQuotaCard.MouseLeftButtonUp -=
                MailboxQuotaCard_OnMouseLeftButtonUp;
        }
    }
}