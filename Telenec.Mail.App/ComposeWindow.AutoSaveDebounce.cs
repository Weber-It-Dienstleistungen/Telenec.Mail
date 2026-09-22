using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    /*
     * Autosave 2.0:
     *
     * Nicht mehr starr alle 30 Sekunden speichern,
     * sondern erst dann, wenn der Benutzer für eine
     * kurze Zeit keine Änderung mehr vorgenommen hat.
     *
     * Dadurch fällt das Speichern nicht mehr mitten
     * in einen laufenden Schreibvorgang.
     */
    private static readonly TimeSpan
        AutoSaveDebounceDelay =
            TimeSpan.FromSeconds(10);

    private DispatcherTimer?
        _autoSaveDebounceTimer;

    private CancellationTokenSource?
        _autoSaveDebounceLifetimeCancellationTokenSource;

    private bool
        _autoSaveDebounceSetupStarted;

    private bool
        _autoSaveDebounceEnabled;

    private bool
        _autoSaveDebounceWindowClosed;

    /*
     * ComposeWindow.xaml.cs registriert seinen normalen
     * Loaded-Handler im Konstruktor.
     *
     * OnInitialized läuft vorher. Wir registrieren hier
     * deshalb nur unseren eigenen Loaded-Handler.
     *
     * Dieser wartet später bewusst, bis der bisherige
     * Compose-Loaded-Workflow vollständig initialisiert
     * wurde.
     */
    protected override void OnInitialized(
        EventArgs e)
    {
        base.OnInitialized(
            e);

        Loaded +=
            ComposeWindowAutoSaveDebounce_OnLoaded;

        Closed +=
            ComposeWindowAutoSaveDebounce_OnClosed;
    }

    private async void ComposeWindowAutoSaveDebounce_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (_autoSaveDebounceSetupStarted)
        {
            return;
        }

        _autoSaveDebounceSetupStarted =
            true;

        /*
         * ComposeWindow_OnLoaded ist async.
         *
         * Deshalb reicht Dispatcher.Yield hier nicht aus:
         * Während InitializeAsync auf das Konto wartet,
         * könnte unser Code sonst zu früh laufen.
         *
         * Wir warten deshalb auf zwei eindeutige Zeichen,
         * dass der bisherige Loaded-Workflow fertig ist:
         *
         * 1. Die Compose-Baseline wurde aufgenommen.
         * 2. Der bisherige 30-Sekunden-Autosave wurde
         *    gestartet.
         */
        while (!_autoSaveDebounceWindowClosed &&
               (!_hasComposeBaseline ||
                _autoSaveCancellationTokenSource
                    is null))
        {
            await Task.Delay(
                50);
        }

        if (_autoSaveDebounceWindowClosed)
        {
            return;
        }

        EnableDebouncedAutoSave();
    }

    private void EnableDebouncedAutoSave()
    {
        if (_autoSaveDebounceEnabled ||
            _autoSaveDebounceWindowClosed)
        {
            return;
        }

        /*
         * Der bisherige starre 30-Sekunden-Loop wird
         * kontrolliert beendet.
         *
         * Der eigentliche Save-Workflow bleibt vollständig
         * erhalten. Wir ersetzen ausschließlich den
         * Zeitpunkt, zu dem er ausgelöst wird.
         */
        StopAutoSaveLoop();

        _autoSaveDebounceLifetimeCancellationTokenSource =
            new CancellationTokenSource();

        var timer =
            new DispatcherTimer(
                DispatcherPriority.Background,
                Dispatcher)
            {
                Interval =
                    AutoSaveDebounceDelay
            };

        timer.Tick +=
            AutoSaveDebounceTimer_OnTick;

        _autoSaveDebounceTimer =
            timer;

        _viewModel.PropertyChanged +=
            ComposeViewModel_OnAutoSavePropertyChanged;

        _viewModel
            .Attachments
            .CollectionChanged +=
                ComposeAttachments_OnAutoSaveCollectionChanged;

        _autoSaveDebounceEnabled =
            true;

        /*
         * Falls zwischen der Baseline-Erstellung und
         * unserer Aktivierung bereits eine Benutzereingabe
         * erfolgt ist, geht auch diese Änderung nicht
         * verloren.
         */
        if (HasUnsavedChanges())
        {
            RestartAutoSaveDebounceTimer();
        }
    }

    private void ComposeViewModel_OnAutoSavePropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        /*
         * Nur echte Inhalte des Entwurfs interessieren
         * den Debounce.
         *
         * Änderungen wie IsBusy, CanSend,
         * IsSavingDraft usw. dürfen den Timer nicht
         * selbst erneut starten.
         */
        switch (e.PropertyName)
        {
            case nameof(
                ComposeMailViewModel
                    .RecipientAddress):

            case nameof(
                ComposeMailViewModel
                    .CcAddress):

            case nameof(
                ComposeMailViewModel
                    .BccAddress):

            case nameof(
                ComposeMailViewModel
                    .Subject):

            case nameof(
                ComposeMailViewModel
                    .Body):

            case nameof(
                ComposeMailViewModel
                    .HtmlBody):

            case nameof(
                ComposeMailViewModel
                    .Importance):

                RestartAutoSaveDebounceTimer();

                break;
        }
    }

    private void ComposeAttachments_OnAutoSaveCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        RestartAutoSaveDebounceTimer();
    }

    private void RestartAutoSaveDebounceTimer()
    {
        if (!_autoSaveDebounceEnabled ||
            _autoSaveDebounceWindowClosed ||
            _autoSaveSuspended ||
            _allowClose ||
            _isProcessingCloseSave)
        {
            return;
        }

        var timer =
            _autoSaveDebounceTimer;

        if (timer is null)
        {
            return;
        }

        timer.Stop();

        if (!HasUnsavedChanges())
        {
            return;
        }

        timer.Start();
    }

    private async void AutoSaveDebounceTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        var timer =
            _autoSaveDebounceTimer;

        if (timer is null)
        {
            return;
        }

        timer.Stop();

        if (!_autoSaveDebounceEnabled ||
            _autoSaveDebounceWindowClosed ||
            _autoSaveSuspended ||
            _allowClose ||
            _isProcessingCloseSave)
        {
            return;
        }

        if (_viewModel.IsBusy)
        {
            if (HasUnsavedChanges())
            {
                timer.Start();
            }

            return;
        }

        if (!HasUnsavedChanges() ||
            !_viewModel.CanSaveDraft)
        {
            return;
        }

        var lifetimeCancellationTokenSource =
            _autoSaveDebounceLifetimeCancellationTokenSource;

        if (lifetimeCancellationTokenSource is null ||
            lifetimeCancellationTokenSource
                .IsCancellationRequested)
        {
            return;
        }

        await TryAutoSaveDraftAsync(
            lifetimeCancellationTokenSource.Token);
    }

    private void ComposeWindowAutoSaveDebounce_OnClosed(
        object? sender,
        EventArgs e)
    {
        _autoSaveDebounceWindowClosed =
            true;

        Loaded -=
            ComposeWindowAutoSaveDebounce_OnLoaded;

        Closed -=
            ComposeWindowAutoSaveDebounce_OnClosed;

        if (_autoSaveDebounceEnabled)
        {
            _viewModel.PropertyChanged -=
                ComposeViewModel_OnAutoSavePropertyChanged;

            _viewModel
                .Attachments
                .CollectionChanged -=
                    ComposeAttachments_OnAutoSaveCollectionChanged;
        }

        _autoSaveDebounceEnabled =
            false;

        var timer =
            _autoSaveDebounceTimer;

        _autoSaveDebounceTimer =
            null;

        if (timer is not null)
        {
            timer.Stop();

            timer.Tick -=
                AutoSaveDebounceTimer_OnTick;
        }

        var lifetimeCancellationTokenSource =
            _autoSaveDebounceLifetimeCancellationTokenSource;

        _autoSaveDebounceLifetimeCancellationTokenSource =
            null;

        if (lifetimeCancellationTokenSource is null)
        {
            return;
        }

        try
        {
            lifetimeCancellationTokenSource
                .Cancel();
        }
        finally
        {
            lifetimeCancellationTokenSource
                .Dispose();
        }
    }
}