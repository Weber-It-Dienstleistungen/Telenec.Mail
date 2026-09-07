using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const int SearchResultLimit =
        200;

    private const string SearchMailDragDataFormat =
        "Telenec.Mail.SearchMessageSelection";

    private static int
        _searchClassHandlersRegistered;

    private bool
        _searchUiInitialized;

    private bool
        _searchIsRunning;

    private bool
        _searchIsActive;

    private bool
        _searchWholeMailbox;

    private bool
        _searchResultIsLoading;

    private bool
        _searchAttachmentDownloadRunning;

    private bool
        _searchMutationIsRunning;

    private bool
        _searchDragInProgress;

    private bool
        _searchPreserveSelectionUntilMouseUp;

    private Border?
        _searchHostBorder;

    private TextBox?
        _searchTextBox;

    private TextBlock?
        _searchHintText;

    private Button?
        _searchScopeButton;

    private TextBlock?
        _searchScopeText;

    private Button?
        _searchButton;

    private Button?
        _clearSearchButton;

    private Grid?
        _searchResultsHost;

    private TextBlock?
        _searchResultsStatusText;

    private StackPanel?
        _searchResultsStack;

    /*
     * _selectedSearchResult ist weiterhin die Nachricht,
     * deren Inhalt rechts angezeigt wird.
     *
     * Davon getrennt hält _selectedSearchResults die echte
     * Mehrfachauswahl.
     */
    private SearchResultCardState?
        _selectedSearchResult;

    private readonly List<SearchResultCardState>
        _selectedSearchResults =
            new();

    private SearchResultCardState?
        _searchSelectionAnchor;

    private SearchResultCardState?
        _searchDragCandidate;

    private IReadOnlyList<SearchResultCardState>
        _searchDragSelectionSnapshot =
            Array.Empty<SearchResultCardState>();

    private MailMessageItemViewModel?
        _searchPreviewMessage;

    private SearchReadingPaneContext?
        _searchReadingPaneContext;

    private Point
        _searchDragStartPoint;

    private void MainWindowSearch_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        InitializeSearchUi();

        if (_searchUiInitialized)
        {
            Loaded -=
                MainWindowSearch_OnLoaded;
        }
    }

    private void InitializeSearchUi()
    {
        if (_searchUiInitialized)
        {
            return;
        }

        if (MessageListBox.Parent
            is not Grid messageColumnGrid)
        {
            return;
        }

        var searchHost =
            messageColumnGrid
                .Children
                .OfType<Border>()
                .FirstOrDefault(
                    child =>
                        Grid.GetRow(
                            child) == 1);

        if (searchHost is null)
        {
            return;
        }

        EnsureSearchClassHandlersRegistered();

        _searchHostBorder =
            searchHost;

        _searchUiInitialized =
            true;

        searchHost.Height =
            42;

        searchHost.Padding =
            new Thickness(0);

        searchHost.Child =
            CreateSearchBar();

        CreateSearchResultsHost(
            messageColumnGrid);

        InitializeSearchReadingPane();

        FolderListBox.PreviewDragOver +=
            SearchFolderListBox_OnPreviewDragOver;

        FolderListBox.PreviewDrop +=
            SearchFolderListBox_OnPreviewDrop;

        UpdateCurrentFolderSearchScopeText();
        UpdateSearchVisualState();

        _viewModel.PropertyChanged +=
            MainViewModel_OnSearchPropertyChanged;

        Closed +=
            MainWindowSearch_OnClosed;
    }

    private static void EnsureSearchClassHandlersRegistered()
    {
        if (Interlocked.Exchange(
                ref _searchClassHandlersRegistered,
                1) != 0)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(
                SearchButton_OnClassClick));

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(
                SearchWindow_OnClassPreviewKeyDown));
    }

    private static void SearchButton_OnClassClick(
        object sender,
        RoutedEventArgs e)
    {
        if (e.Handled ||
            sender is not Button button)
        {
            return;
        }

        if (Window.GetWindow(
                button)
            is not MainWindow window)
        {
            return;
        }

        if (button.DataContext
                is MailAttachmentData attachment &&
            window.TryGetSearchAttachmentContext(
                attachment,
                out _))
        {
            e.Handled =
                true;

            _ =
                window.SaveSearchAttachmentFromUiAsync(
                    attachment);

            return;
        }

        if (window.TryHandleSearchReadingPaneButton(
                button))
        {
            e.Handled =
                true;
        }
    }

    private static void
        SearchWindow_OnClassPreviewKeyDown(
            object sender,
            KeyEventArgs e)
    {
        if (sender is not MainWindow window ||
            !window._searchIsActive)
        {
            return;
        }

        if (Keyboard.FocusedElement
            is TextBoxBase)
        {
            return;
        }

        var modifiers =
            Keyboard.Modifiers;

        if (e.Key == Key.R &&
            modifiers ==
            (ModifierKeys.Control |
             ModifierKeys.Shift))
        {
            e.Handled =
                true;

            if (window._selectedSearchResult
                    is not null)
            {
                _ =
                    window.ReplyAllToSearchResultFromUiAsync(
                        window._selectedSearchResult);
            }

            return;
        }

        if (e.Key == Key.R &&
            modifiers ==
            ModifierKeys.Control)
        {
            e.Handled =
                true;

            if (window._selectedSearchResult
                    is not null)
            {
                _ =
                    window.ReplyToSearchResultFromUiAsync(
                        window._selectedSearchResult);
            }

            return;
        }

        if (e.Key == Key.F &&
            modifiers ==
            ModifierKeys.Control)
        {
            e.Handled =
                true;

            if (window._selectedSearchResult
                    is not null)
            {
                _ =
                    window.ForwardSearchResultFromUiAsync(
                        window._selectedSearchResult);
            }

            return;
        }

        if (e.Key == Key.Delete &&
            modifiers ==
            ModifierKeys.None)
        {
            e.Handled =
                true;

            var selectedMessages =
                window.GetSelectedSearchResults();

            if (selectedMessages.Count > 0)
            {
                _ =
                    window.DeleteSearchResultsFromUiAsync(
                        selectedMessages);
            }

            return;
        }

        /*
         * Search-Moves besitzen noch keinen eigenen
         * Undo-Snapshot.
         *
         * Deshalb verhindern wir während einer Suche, dass
         * Ctrl+Z versehentlich eine ältere normale
         * Mailoperation rückgängig macht.
         */
        if (e.Key == Key.Z &&
            modifiers.HasFlag(
                ModifierKeys.Control))
        {
            e.Handled =
                true;
        }
    }

    private void InitializeSearchReadingPane()
    {
        if (Content is not Grid rootGrid)
        {
            return;
        }

        var readingPane =
            rootGrid
                .Children
                .OfType<Border>()
                .FirstOrDefault(
                    border =>
                        Grid.GetColumn(
                            border) == 2);

        if (readingPane is null)
        {
            return;
        }

        _searchReadingPaneContext =
            new SearchReadingPaneContext(
                _viewModel);

        readingPane.DataContext =
            _searchReadingPaneContext;
    }

    private Grid CreateSearchBar()
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        11,
                        0,
                        6,
                        0)
            };

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var searchGlyph =
            new TextBlock
            {
                Text =
                    "\uE721",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    15,

                Margin =
                    new Thickness(
                        0,
                        0,
                        9,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        searchGlyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetColumn(
            searchGlyph,
            0);

        grid.Children.Add(
            searchGlyph);

        var inputHost =
            new Grid();

        var searchTextBox =
            new TextBox
            {
                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(0),

                Background =
                    Brushes.Transparent,

                FontSize =
                    13,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                ToolTip =
                    "Suchbegriff eingeben und Enter drücken"
            };

        searchTextBox.SetResourceReference(
            TextBox.ForegroundProperty,
            "Text.Primary");

        searchTextBox.KeyDown +=
            SearchTextBox_OnKeyDown;

        searchTextBox.TextChanged +=
            SearchTextBox_OnTextChanged;

        _searchTextBox =
            searchTextBox;

        inputHost.Children.Add(
            searchTextBox);

        var hintText =
            new TextBlock
            {
                Text =
                    "E-Mails durchsuchen",

                FontSize =
                    13,

                VerticalAlignment =
                    VerticalAlignment.Center,

                IsHitTestVisible =
                    false
            };

        hintText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Muted");

        _searchHintText =
            hintText;

        inputHost.Children.Add(
            hintText);

        Grid.SetColumn(
            inputHost,
            1);

        grid.Children.Add(
            inputHost);

        var separator =
            new Border
            {
                Width =
                    1,

                Height =
                    22,

                Margin =
                    new Thickness(
                        8,
                        0,
                        7,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        separator.SetResourceReference(
            Border.BackgroundProperty,
            "Border.Default");

        Grid.SetColumn(
            separator,
            2);

        grid.Children.Add(
            separator);

        var scopeButton =
            CreateSearchScopeButton();

        _searchScopeButton =
            scopeButton;

        Grid.SetColumn(
            scopeButton,
            3);

        grid.Children.Add(
            scopeButton);

        var searchButton =
            new Button
            {
                Width =
                    32,

                Height =
                    30,

                MinHeight =
                    0,

                Margin =
                    new Thickness(
                        6,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(0),

                VerticalAlignment =
                    VerticalAlignment.Center,

                ToolTip =
                    "Suche starten"
            };

        searchButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        var searchButtonGlyph =
            new TextBlock
            {
                Text =
                    "\uE721",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    14,

                Foreground =
                    Brushes.White,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        searchButton.Content =
            searchButtonGlyph;

        searchButton.Click +=
            SearchButton_OnClick;

        _searchButton =
            searchButton;

        Grid.SetColumn(
            searchButton,
            4);

        grid.Children.Add(
            searchButton);

        var clearButton =
            new Button
            {
                Width =
                    28,

                Height =
                    30,

                Margin =
                    new Thickness(
                        3,
                        0,
                        0,
                        0),

                Padding =
                    new Thickness(0),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Suche beenden",

                Visibility =
                    Visibility.Collapsed
            };

        var clearButtonGlyph =
            new TextBlock
            {
                Text =
                    "\uE711",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    13,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        clearButtonGlyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        clearButton.Content =
            clearButtonGlyph;

        clearButton.Click +=
            ClearSearchButton_OnClick;

        _clearSearchButton =
            clearButton;

        Grid.SetColumn(
            clearButton,
            5);

        grid.Children.Add(
            clearButton);

        return grid;
    }

    private Button CreateSearchScopeButton()
    {
        var button =
            new Button
            {
                Height =
                    30,

                Padding =
                    new Thickness(
                        5,
                        0,
                        5,
                        0),

                VerticalAlignment =
                    VerticalAlignment.Center,

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Suchbereich auswählen"
            };

        var contentPanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        var scopeText =
            new TextBlock
            {
                MaxWidth =
                    98,

                FontSize =
                    12,

                TextTrimming =
                    TextTrimming.CharacterEllipsis,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        scopeText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        _searchScopeText =
            scopeText;

        var chevron =
            new TextBlock
            {
                Text =
                    "\uE70D",

                Margin =
                    new Thickness(
                        5,
                        0,
                        0,
                        0),

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    9,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        chevron.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Muted");

        contentPanel.Children.Add(
            scopeText);

        contentPanel.Children.Add(
            chevron);

        button.Content =
            contentPanel;

        button.Click +=
            SearchScopeButton_OnClick;

        return button;
    }

    private void SearchScopeButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_searchScopeButton is null)
        {
            return;
        }

        var menu =
            new ContextMenu
            {
                PlacementTarget =
                    _searchScopeButton,

                Placement =
                    PlacementMode.Bottom
            };

        var folderName =
            _viewModel
                .SelectedFolder?
                .DisplayName;

        var currentFolderItem =
            new MenuItem
            {
                Header =
                    !string.IsNullOrWhiteSpace(
                        folderName)
                        ? folderName
                        : "Aktueller Ordner",

                IsCheckable =
                    true,

                IsChecked =
                    !_searchWholeMailbox
            };

        currentFolderItem.Click +=
            (_, _) =>
            {
                SetSearchScope(
                    wholeMailbox: false);
            };

        var wholeMailboxItem =
            new MenuItem
            {
                Header =
                    "Ganzes Postfach",

                IsCheckable =
                    true,

                IsChecked =
                    _searchWholeMailbox
            };

        wholeMailboxItem.Click +=
            (_, _) =>
            {
                SetSearchScope(
                    wholeMailbox: true);
            };

        menu.Items.Add(
            currentFolderItem);

        menu.Items.Add(
            wholeMailboxItem);

        _searchScopeButton.ContextMenu =
            menu;

        menu.IsOpen =
            true;
    }

    private void SetSearchScope(
        bool wholeMailbox)
    {
        if (_searchWholeMailbox ==
            wholeMailbox)
        {
            return;
        }

        _searchWholeMailbox =
            wholeMailbox;

        if (_searchIsActive)
        {
            ClearSearchView(
                clearText: false,
                resetScope: false);
        }

        UpdateCurrentFolderSearchScopeText();
    }

    private void CreateSearchResultsHost(
        Grid messageColumnGrid)
    {
        var resultsHost =
            new Grid
            {
                Visibility =
                    Visibility.Collapsed,

                Background =
                    Brushes.Transparent
            };

        resultsHost.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    GridLength.Auto
            });

        resultsHost.RowDefinitions.Add(
            new RowDefinition
            {
                Height =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var statusText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        22,
                        2,
                        22,
                        12),

                FontSize =
                    12,

                FontWeight =
                    FontWeights.SemiBold
            };

        statusText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        _searchResultsStatusText =
            statusText;

        Grid.SetRow(
            statusText,
            0);

        resultsHost.Children.Add(
            statusText);

        var resultStack =
            new StackPanel();

        _searchResultsStack =
            resultStack;

        var scrollViewer =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,

                Content =
                    resultStack
            };

        Grid.SetRow(
            scrollViewer,
            1);

        resultsHost.Children.Add(
            scrollViewer);

        Grid.SetRow(
            resultsHost,
            2);

        Panel.SetZIndex(
            resultsHost,
            10);

        messageColumnGrid.Children.Add(
            resultsHost);

        _searchResultsHost =
            resultsHost;
    }

    private async void SearchButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        await ExecuteSearchAsync();
    }

    private async void SearchTextBox_OnKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key !=
            Key.Enter)
        {
            return;
        }

        e.Handled =
            true;

        await ExecuteSearchAsync();
    }

    private void SearchTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateSearchVisualState();
    }

    private void ClearSearchButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        ClearSearchView(
            clearText: true,
            resetScope: true);

        _searchTextBox?.Focus();
    }

    private async Task ExecuteSearchAsync()
    {
        if (_searchIsRunning ||
            _searchResultIsLoading ||
            _searchAttachmentDownloadRunning ||
            _searchMutationIsRunning ||
            _searchTextBox is null)
        {
            return;
        }

        var searchText =
            _searchTextBox
                .Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                searchText))
        {
            ClearSearchView(
                clearText: false,
                resetScope: false);

            return;
        }

        var selectedFolder =
            _viewModel.SelectedFolder;

        if (!_searchWholeMailbox &&
            selectedFolder is null)
        {
            MessageBox.Show(
                this,
                "Bitte wählen Sie zunächst einen Mailordner aus.",
                "E-Mails durchsuchen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        ClearSearchPreview();

        _searchIsRunning =
            true;

        SetSearchControlsEnabled(
            false);

        FolderListBox.IsHitTestVisible =
            false;

        try
        {
            var searchService =
                _serviceProvider
                    .GetRequiredService<
                        IMailSearchService>();

            IReadOnlyList<MailSearchHitData>
                results;

            if (_searchWholeMailbox)
            {
                results =
                    await searchService
                        .SearchMailboxAsync(
                            searchText,
                            SearchResultLimit);
            }
            else
            {
                results =
                    await searchService
                        .SearchFolderAsync(
                            selectedFolder!.FolderId,
                            searchText,
                            SearchResultLimit);
            }

            ShowSearchResults(
                results,
                _searchWholeMailbox,
                selectedFolder?.DisplayName);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ClearSearchView(
                clearText: false,
                resetScope: false);

            MessageBox.Show(
                this,
                "Die Suche konnte nicht abgeschlossen werden.\n\n" +
                "Bitte prüfen Sie die Verbindung zum Mailserver und " +
                "versuchen Sie es anschließend erneut.",
                "E-Mails durchsuchen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            FolderListBox.IsHitTestVisible =
                true;

            _searchIsRunning =
                false;

            SetSearchControlsEnabled(
                true);

            UpdateSearchVisualState();
        }
    }

    private void ShowSearchResults(
        IReadOnlyList<MailSearchHitData> results,
        bool searchWholeMailbox,
        string? selectedFolderDisplayName)
    {
        if (_searchResultsHost is null ||
            _searchResultsStatusText is null ||
            _searchResultsStack is null)
        {
            return;
        }

        _searchIsActive =
            true;

        _selectedSearchResult =
            null;

        _selectedSearchResults.Clear();

        _searchSelectionAnchor =
            null;

        _searchDragCandidate =
            null;

        _searchDragSelectionSnapshot =
            Array.Empty<SearchResultCardState>();

        _searchReadingPaneContext?
            .SetSearchMode(
                true);

        _searchResultsStack
            .Children
            .Clear();

        var scopeText =
            searchWholeMailbox
                ? "Ganzes Postfach"
                : !string.IsNullOrWhiteSpace(
                    selectedFolderDisplayName)
                    ? selectedFolderDisplayName
                    : "Aktueller Ordner";

        _searchResultsStatusText.Text =
            results.Count switch
            {
                0 =>
                    $"Keine Treffer · {scopeText}",

                1 =>
                    $"1 Treffer · {scopeText}",

                _ =>
                    $"{results.Count} Treffer · {scopeText}"
            };

        if (results.Count == 0)
        {
            AddEmptySearchResultText();
        }
        else
        {
            foreach (var result in results)
            {
                _searchResultsStack
                    .Children
                    .Add(
                        CreateSearchResultCard(
                            result,
                            searchWholeMailbox));
            }
        }

        MessageListBox.Visibility =
            Visibility.Collapsed;

        _searchResultsHost.Visibility =
            Visibility.Visible;

        SetMessagePagingSuppressedForSearch(
            true);

        UpdateSearchVisualState();
    }

    private void AddEmptySearchResultText()
    {
        if (_searchResultsStack is null)
        {
            return;
        }

        var emptyText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        22,
                        30,
                        22,
                        0),

                Text =
                    "Für diesen Suchbegriff wurden keine E-Mails gefunden.",

                FontSize =
                    13,

                TextWrapping =
                    TextWrapping.Wrap
            };

        emptyText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Muted");

        _searchResultsStack
            .Children
            .Add(
                emptyText);
    }

    private Border CreateSearchResultCard(
        MailSearchHitData result,
        bool showFolder)
    {
        var container =
            new Border
            {
                Padding =
                    new Thickness(
                        22,
                        14,
                        22,
                        14),

                BorderThickness =
                    new Thickness(
                        0,
                        0,
                        0,
                        1),

                Background =
                    Brushes.Transparent,

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "E-Mail öffnen"
            };

        container.SetResourceReference(
            Border.BorderBrushProperty,
            "Border.Default");

        var stack =
            new StackPanel();

        var headerGrid =
            new Grid();

        headerGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        headerGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var senderText =
            new TextBlock
            {
                Text =
                    result.Sender,

                FontSize =
                    13,

                FontWeight =
                    result.IsUnread
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        senderText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        Grid.SetColumn(
            senderText,
            0);

        headerGrid.Children.Add(
            senderText);

        var dateText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        8,
                        0,
                        0,
                        0),

                Text =
                    FormatSearchResultDate(
                        result.Date),

                FontSize =
                    11
            };

        dateText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetColumn(
            dateText,
            1);

        headerGrid.Children.Add(
            dateText);

        stack.Children.Add(
            headerGrid);

        var subjectText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                Text =
                    result.Subject,

                FontSize =
                    14,

                FontWeight =
                    result.IsUnread
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,

                TextWrapping =
                    TextWrapping.Wrap
            };

        subjectText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        stack.Children.Add(
            subjectText);

        var metaText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),

                Text =
                    CreateSearchResultMetaText(
                        result,
                        showFolder),

                FontSize =
                    11,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        metaText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        stack.Children.Add(
            metaText);

        container.Child =
            stack;

        var state =
            new SearchResultCardState(
                result,
                container,
                senderText,
                subjectText);

        container.Tag =
            state;

        container.ContextMenu =
            CreateSearchResultContextMenu(
                state);

        container.MouseLeftButtonUp +=
            SearchResultCard_OnMouseLeftButtonUp;

        container.MouseEnter +=
            SearchResultCard_OnMouseEnter;

        container.MouseLeave +=
            SearchResultCard_OnMouseLeave;

        container.PreviewMouseLeftButtonDown +=
            SearchResultCard_OnPreviewMouseLeftButtonDown;

        container.PreviewMouseMove +=
            SearchResultCard_OnPreviewMouseMove;

        container.PreviewMouseRightButtonDown +=
            SearchResultCard_OnPreviewMouseRightButtonDown;

        return container;
    }

    private ContextMenu CreateSearchResultContextMenu(
        SearchResultCardState state)
    {
        var actionSelection =
            GetSelectedSearchResultsForAction(
                state);

        var isSingleSelection =
            actionSelection.Count == 1;

        var menu =
            new ContextMenu();

        var replyItem =
            new MenuItem
            {
                Header =
                    "Antworten",

                IsEnabled =
                    isSingleSelection
            };

        replyItem.Click +=
            async (_, _) =>
            {
                await ReplyToSearchResultFromUiAsync(
                    state);
            };

        menu.Items.Add(
            replyItem);

        var replyAllItem =
            new MenuItem
            {
                Header =
                    "Allen antworten",

                IsEnabled =
                    isSingleSelection
            };

        replyAllItem.Click +=
            async (_, _) =>
            {
                await ReplyAllToSearchResultFromUiAsync(
                    state);
            };

        menu.Items.Add(
            replyAllItem);

        var forwardItem =
            new MenuItem
            {
                Header =
                    "Weiterleiten",

                IsEnabled =
                    isSingleSelection
            };

        forwardItem.Click +=
            async (_, _) =>
            {
                await ForwardSearchResultFromUiAsync(
                    state);
            };

        menu.Items.Add(
            forwardItem);

        menu.Items.Add(
            new Separator());

        var readStateItem =
            new MenuItem
            {
                Header =
                    state.Hit.IsUnread
                        ? "Als gelesen markieren"
                        : "Als ungelesen markieren",

                /*
                 * Read/Unread bleibt vorerst bewusst eine
                 * Einzelaktion.
                 *
                 * Massenverschieben/-löschen ist davon
                 * unabhängig.
                 */
                IsEnabled =
                    isSingleSelection
            };

        readStateItem.Click +=
            async (_, _) =>
            {
                await SetSearchResultUnreadStateAsync(
                    state,
                    desiredUnread:
                        !state.Hit.IsUnread,
                    showError:
                        true);
            };

        menu.Items.Add(
            readStateItem);

        var moveMenu =
            new MenuItem
            {
                Header =
                    actionSelection.Count > 1
                        ? $"Verschieben nach ({actionSelection.Count})"
                        : "Verschieben nach"
            };

        foreach (var folder in
                 _viewModel.MailFolders)
        {
            var targetFolder =
                folder;

            /*
             * Bei Mehrfachauswahl aus unterschiedlichen
             * Ursprungsordnern darf ein Ziel trotzdem
             * angeboten werden.
             *
             * Nachrichten, die bereits in diesem Ziel liegen,
             * werden beim Ausführen einfach übersprungen.
             */
            var anyMessageCanMove =
                actionSelection.Any(
                    selected =>
                        !string.Equals(
                            selected.Hit.FolderId,
                            targetFolder.FolderId,
                            StringComparison.OrdinalIgnoreCase));

            if (!anyMessageCanMove)
            {
                continue;
            }

            var targetItem =
                new MenuItem
                {
                    Header =
                        targetFolder.DisplayName
                };

            targetItem.Click +=
                async (_, _) =>
                {
                    await MoveSearchResultsFromUiAsync(
                        GetSelectedSearchResultsForAction(
                            state),
                        targetFolder);
                };

            moveMenu.Items.Add(
                targetItem);
        }

        moveMenu.IsEnabled =
            moveMenu.Items.Count > 0;

        menu.Items.Add(
            moveMenu);

        var selectionContainsTrash =
            actionSelection.Any(
                selected =>
                    IsSearchHitInTrash(
                        selected.Hit));

        menu.Items.Add(
            new Separator());

        var deleteItem =
            new MenuItem
            {
                Header =
                    actionSelection.Count > 1
                        ? $"Löschen ({actionSelection.Count})"
                        : "Löschen",

                /*
                 * Eine gemischte Auswahl mit bereits im
                 * Papierkorb befindlichen Nachrichten wird
                 * bewusst nicht teilweise gelöscht.
                 */
                IsEnabled =
                    !selectionContainsTrash
            };

        deleteItem.Click +=
            async (_, _) =>
            {
                await DeleteSearchResultsFromUiAsync(
                    GetSelectedSearchResultsForAction(
                        state));
            };

        menu.Items.Add(
            deleteItem);

        return menu;
    }

    private void
        SearchResultCard_OnPreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        if (sender is not Border card ||
            card.Tag
                is not SearchResultCardState state)
        {
            return;
        }

        /*
         * Rechtsklick auf eine bereits ausgewählte Mail
         * erhält die komplette Mehrfachauswahl.
         *
         * Rechtsklick auf eine nicht ausgewählte Mail macht
         * dagegen genau diese Mail zur Auswahl.
         */
        if (!IsSearchResultSelected(
                state))
        {
            SelectOnlySearchResult(
                state);

            _ =
                OpenSearchResultAsync(
                    state);
        }

        card.ContextMenu =
            CreateSearchResultContextMenu(
                state);
    }

    private void SearchResultCard_OnMouseEnter(
        object sender,
        MouseEventArgs e)
    {
        if (sender is not Border card ||
            card.Tag
                is not SearchResultCardState state ||
            IsSearchResultSelected(
                state))
        {
            return;
        }

        card.SetResourceReference(
            Border.BackgroundProperty,
            "Surface.Hover");
    }

    private void SearchResultCard_OnMouseLeave(
        object sender,
        MouseEventArgs e)
    {
        if (sender is not Border card ||
            card.Tag
                is not SearchResultCardState state ||
            IsSearchResultSelected(
                state))
        {
            return;
        }

        card.Background =
            Brushes.Transparent;
    }

    private void
        SearchResultCard_OnPreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        if (sender is not Border card ||
            card.Tag
                is not SearchResultCardState state)
        {
            _searchDragCandidate =
                null;

            return;
        }

        _searchDragCandidate =
            state;

        _searchDragStartPoint =
            e.GetPosition(
                this);

        _searchDragSelectionSnapshot =
            Array.Empty<SearchResultCardState>();

        _searchPreserveSelectionUntilMouseUp =
            false;

        var modifiers =
            Keyboard.Modifiers &
            (ModifierKeys.Control |
             ModifierKeys.Shift);

        /*
         * Entspricht dem Verhalten unserer normalen
         * Extended-Selection:
         *
         * Beginnt ein Drag ohne Modifier auf einer bereits
         * mehrfach ausgewählten Mail, darf der MouseDown die
         * Auswahl noch nicht auf genau dieses Element
         * reduzieren.
         *
         * Wir sichern deshalb den kompletten Snapshot.
         */
        if (modifiers ==
                ModifierKeys.None &&
            IsSearchResultSelected(
                state) &&
            _selectedSearchResults.Count > 1)
        {
            _searchPreserveSelectionUntilMouseUp =
                true;

            _searchDragSelectionSnapshot =
                GetSelectedSearchResults();

            return;
        }

        ApplySearchSelection(
            state,
            modifiers);
    }

    private void SearchResultCard_OnPreviewMouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (_searchDragInProgress ||
            _searchMutationIsRunning ||
            _searchResultIsLoading ||
            e.LeftButton !=
                MouseButtonState.Pressed ||
            _searchDragCandidate is null ||
            sender is not Border card ||
            !ReferenceEquals(
                card,
                _searchDragCandidate.Card))
        {
            return;
        }

        var currentPoint =
            e.GetPosition(
                this);

        var horizontalDistance =
            Math.Abs(
                currentPoint.X -
                _searchDragStartPoint.X);

        var verticalDistance =
            Math.Abs(
                currentPoint.Y -
                _searchDragStartPoint.Y);

        if (horizontalDistance <
                SystemParameters
                    .MinimumHorizontalDragDistance &&
            verticalDistance <
                SystemParameters
                    .MinimumVerticalDragDistance)
        {
            return;
        }

        IReadOnlyList<SearchResultCardState>
            messagesToDrag;

        if (_searchDragSelectionSnapshot.Count > 0 &&
            _searchDragSelectionSnapshot.Contains(
                _searchDragCandidate))
        {
            messagesToDrag =
                _searchDragSelectionSnapshot;
        }
        else if (IsSearchResultSelected(
                     _searchDragCandidate))
        {
            messagesToDrag =
                GetSelectedSearchResults();
        }
        else
        {
            messagesToDrag =
                new[]
                {
                    _searchDragCandidate
                };
        }

        if (messagesToDrag.Count == 0)
        {
            return;
        }

        var dataObject =
            new DataObject();

        dataObject.SetData(
            SearchMailDragDataFormat,
            messagesToDrag.ToList());

        _searchDragInProgress =
            true;

        try
        {
            DragDrop.DoDragDrop(
                card,
                dataObject,
                DragDropEffects.Move);
        }
        finally
        {
            _searchDragInProgress =
                false;

            _searchDragCandidate =
                null;

            _searchDragSelectionSnapshot =
                Array.Empty<SearchResultCardState>();

            _searchPreserveSelectionUntilMouseUp =
                false;
        }
    }

    private async void SearchResultCard_OnMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_searchDragInProgress ||
            sender is not Border card ||
            card.Tag
                is not SearchResultCardState state)
        {
            return;
        }

        e.Handled =
            true;

        /*
         * Klick auf eine bereits mehrfach markierte Mail ohne
         * tatsächlichen Drag:
         *
         * Dann verhält es sich wie eine normale ListBox und
         * reduziert die Auswahl beim Loslassen auf diese Mail.
         */
        if (_searchPreserveSelectionUntilMouseUp)
        {
            SelectOnlySearchResult(
                state);
        }

        _searchPreserveSelectionUntilMouseUp =
            false;

        _searchDragCandidate =
            null;

        _searchDragSelectionSnapshot =
            Array.Empty<SearchResultCardState>();

        if (IsSearchResultSelected(
                state))
        {
            await OpenSearchResultAsync(
                state);

            return;
        }

        /*
         * Ctrl-Klick kann das aktive Element aus der Auswahl
         * entfernen.
         */
        if (ReferenceEquals(
                _selectedSearchResult,
                state))
        {
            var replacement =
                _selectedSearchResults
                    .LastOrDefault();

            if (replacement is not null)
            {
                await OpenSearchResultAsync(
                    replacement);
            }
            else
            {
                ClearSearchPreviewMessageOnly();
            }
        }
    }

    private void ApplySearchSelection(
        SearchResultCardState state,
        ModifierKeys modifiers)
    {
        var controlPressed =
            modifiers.HasFlag(
                ModifierKeys.Control);

        var shiftPressed =
            modifiers.HasFlag(
                ModifierKeys.Shift);

        if (shiftPressed)
        {
            SelectSearchResultRange(
                state,
                additive:
                    controlPressed);

            return;
        }

        if (controlPressed)
        {
            ToggleSearchResultSelection(
                state);

            _searchSelectionAnchor =
                state;

            return;
        }

        SelectOnlySearchResult(
            state);
    }

    private void SelectOnlySearchResult(
        SearchResultCardState state)
    {
        _selectedSearchResults.Clear();

        _selectedSearchResults.Add(
            state);

        _searchSelectionAnchor =
            state;

        RefreshSearchSelectionAppearance();
    }

    private void ToggleSearchResultSelection(
        SearchResultCardState state)
    {
        if (IsSearchResultSelected(
                state))
        {
            _selectedSearchResults.Remove(
                state);
        }
        else
        {
            _selectedSearchResults.Add(
                state);
        }

        RefreshSearchSelectionAppearance();
    }

    private void SelectSearchResultRange(
        SearchResultCardState state,
        bool additive)
    {
        var orderedStates =
            GetSearchResultStatesInDisplayOrder();

        if (orderedStates.Count == 0)
        {
            return;
        }

        var anchor =
            _searchSelectionAnchor
            ?? state;

        var anchorIndex =
            orderedStates.IndexOf(
                anchor);

        var targetIndex =
            orderedStates.IndexOf(
                state);

        if (anchorIndex < 0 ||
            targetIndex < 0)
        {
            SelectOnlySearchResult(
                state);

            return;
        }

        if (!additive)
        {
            _selectedSearchResults.Clear();
        }

        var first =
            Math.Min(
                anchorIndex,
                targetIndex);

        var last =
            Math.Max(
                anchorIndex,
                targetIndex);

        for (var index = first;
             index <= last;
             index++)
        {
            var candidate =
                orderedStates[index];

            if (!_selectedSearchResults.Contains(
                    candidate))
            {
                _selectedSearchResults.Add(
                    candidate);
            }
        }

        if (_searchSelectionAnchor is null)
        {
            _searchSelectionAnchor =
                anchor;
        }

        RefreshSearchSelectionAppearance();
    }

    private bool IsSearchResultSelected(
        SearchResultCardState state)
    {
        return _selectedSearchResults
            .Contains(
                state);
    }

    private IReadOnlyList<SearchResultCardState>
        GetSelectedSearchResults()
    {
        return _selectedSearchResults
            .ToList();
    }

    private IReadOnlyList<SearchResultCardState>
        GetSelectedSearchResultsForAction(
            SearchResultCardState clickedState)
    {
        if (IsSearchResultSelected(
                clickedState))
        {
            return GetSelectedSearchResults();
        }

        return new[]
        {
            clickedState
        };
    }

    private List<SearchResultCardState>
        GetSearchResultStatesInDisplayOrder()
    {
        if (_searchResultsStack is null)
        {
            return new List<
                SearchResultCardState>();
        }

        return _searchResultsStack
            .Children
            .OfType<Border>()
            .Select(
                border =>
                    border.Tag)
            .OfType<
                SearchResultCardState>()
            .ToList();
    }

    private void RefreshSearchSelectionAppearance()
    {
        foreach (var state in
                 GetSearchResultStatesInDisplayOrder())
        {
            if (IsSearchResultSelected(
                    state))
            {
                state.Card.SetResourceReference(
                    Border.BackgroundProperty,
                    "Surface.Selected");
            }
            else
            {
                state.Card.Background =
                    Brushes.Transparent;
            }
        }
    }

    private async Task<bool> OpenSearchResultAsync(
        SearchResultCardState state)
    {
        if (_searchResultIsLoading ||
            _searchMutationIsRunning ||
            _searchAttachmentDownloadRunning ||
            !_searchIsActive)
        {
            return false;
        }

        if (!IsSearchResultSelected(
                state))
        {
            SelectOnlySearchResult(
                state);
        }

        if (ReferenceEquals(
                _selectedSearchResult,
                state) &&
            _searchPreviewMessage is not null)
        {
            return true;
        }

        _searchResultIsLoading =
            true;

        state.Card.Cursor =
            Cursors.Wait;

        SetSearchControlsEnabled(
            false);

        try
        {
            var searchService =
                _serviceProvider
                    .GetRequiredService<
                        IMailSearchService>();

            var messageData =
                await searchService
                    .LoadMessageAsync(
                        state.Hit);

            if (messageData is null)
            {
                MessageBox.Show(
                    this,
                    "Die gefundene E-Mail ist in diesem Serverzustand " +
                    "nicht mehr eindeutig verfügbar.\n\n" +
                    "Möglicherweise wurde sie zwischenzeitlich verschoben, " +
                    "gelöscht oder der Ordner wurde serverseitig verändert.\n\n" +
                    "Bitte führen Sie die Suche erneut aus.",
                    "E-Mail öffnen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            var previewMessage =
                CreateSearchPreviewMessage(
                    messageData);

            _selectedSearchResult =
                state;

            _searchPreviewMessage =
                previewMessage;

            RefreshSearchSelectionAppearance();

            _searchReadingPaneContext?
                .SetSearchPreviewMessage(
                    previewMessage,
                    IsSearchHitInTrash(
                        state.Hit));

            await RenderSearchPreviewMessageAsync(
                previewMessage);

            if (state.Hit.IsUnread)
            {
                await SetSearchResultUnreadStateAsync(
                    state,
                    desiredUnread:
                        false,
                    showError:
                        false);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            MessageBox.Show(
                this,
                "Die E-Mail konnte nicht geladen werden.\n\n" +
                "Bitte prüfen Sie die Verbindung zum Mailserver und " +
                "versuchen Sie es anschließend erneut.",
                "E-Mail öffnen",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return false;
        }
        finally
        {
            state.Card.Cursor =
                Cursors.Hand;

            _searchResultIsLoading =
                false;

            SetSearchControlsEnabled(
                true);

            UpdateSearchVisualState();
        }
    }

    private async Task
        SetSearchResultUnreadStateAsync(
            SearchResultCardState state,
            bool desiredUnread,
            bool showError)
    {
        if (_searchMutationIsRunning ||
            state.Hit.IsUnread ==
                desiredUnread)
        {
            return;
        }

        try
        {
            var searchService =
                _serviceProvider
                    .GetRequiredService<
                        IMailSearchService>();

            if (desiredUnread)
            {
                await searchService
                    .MarkAsUnreadAsync(
                        state.Hit);
            }
            else
            {
                await searchService
                    .MarkAsReadAsync(
                        state.Hit);
            }

            var wasUnread =
                state.Hit.IsUnread;

            state.Hit =
                state.Hit with
                {
                    IsUnread =
                        desiredUnread
                };

            if (wasUnread !=
                desiredUnread)
            {
                var folder =
                    FindSearchFolder(
                        state.Hit.FolderId);

                if (folder is not null)
                {
                    if (desiredUnread)
                    {
                        folder.IncrementUnreadCount();
                    }
                    else
                    {
                        folder.DecrementUnreadCount();
                    }
                }
            }

            UpdateSearchResultReadAppearance(
                state);

            if (ReferenceEquals(
                    _selectedSearchResult,
                    state) &&
                _searchPreviewMessage is not null)
            {
                if (desiredUnread)
                {
                    _searchPreviewMessage
                        .MarkAsUnread();
                }
                else
                {
                    _searchPreviewMessage
                        .MarkAsRead();
                }
            }
        }
        catch
        {
            if (!showError)
            {
                return;
            }

            MessageBox.Show(
                this,
                desiredUnread
                    ? "Die Nachricht konnte nicht als ungelesen markiert werden.\n\n" +
                      "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut."
                    : "Die Nachricht konnte nicht als gelesen markiert werden.\n\n" +
                      "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateSearchResultReadAppearance(
        SearchResultCardState state)
    {
        var fontWeight =
            state.Hit.IsUnread
                ? FontWeights.SemiBold
                : FontWeights.Normal;

        state.SenderText.FontWeight =
            fontWeight;

        state.SubjectText.FontWeight =
            fontWeight;

        state.Card.ContextMenu =
            CreateSearchResultContextMenu(
                state);
    }

    private bool TryHandleSearchReadingPaneButton(
        Button button)
    {
        if (!_searchIsActive)
        {
            return false;
        }

        if (ReferenceEquals(
                button,
                DeleteSelectedMessageButton))
        {
            var selectedMessages =
                GetSelectedSearchResults();

            if (selectedMessages.Count > 0)
            {
                _ =
                    DeleteSearchResultsFromUiAsync(
                        selectedMessages);
            }

            return true;
        }

        if (button.Content
            is not TextBlock textBlock)
        {
            return false;
        }

        switch (textBlock.Text)
        {
            case "Antworten":

                if (_selectedSearchResult
                    is not null)
                {
                    _ =
                        ReplyToSearchResultFromUiAsync(
                            _selectedSearchResult);
                }

                return true;

            case "Allen antworten":

                if (_selectedSearchResult
                    is not null)
                {
                    _ =
                        ReplyAllToSearchResultFromUiAsync(
                            _selectedSearchResult);
                }

                return true;

            case "Weiterleiten":

                if (_selectedSearchResult
                    is not null)
                {
                    _ =
                        ForwardSearchResultFromUiAsync(
                            _selectedSearchResult);
                }

                return true;

            default:
                return false;
        }
    }

    private async Task<bool>
        EnsureSearchResultLoadedForActionAsync(
            SearchResultCardState state)
    {
        if (ReferenceEquals(
                _selectedSearchResult,
                state) &&
            _searchPreviewMessage is not null)
        {
            return true;
        }

        return await OpenSearchResultAsync(
            state);
    }

    private async Task
        ReplyToSearchResultFromUiAsync(
            SearchResultCardState state)
    {
        if (!await EnsureSearchResultLoadedForActionAsync(
                state) ||
            _searchPreviewMessage is null)
        {
            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<
                    ComposeWindow>();

        composeWindow.PrepareReply(
            _searchPreviewMessage);

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private async Task
        ReplyAllToSearchResultFromUiAsync(
            SearchResultCardState state)
    {
        if (!await EnsureSearchResultLoadedForActionAsync(
                state) ||
            _searchPreviewMessage is null)
        {
            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<
                    ComposeWindow>();

        composeWindow.PrepareReplyAll(
            _searchPreviewMessage);

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private async Task
        ForwardSearchResultFromUiAsync(
            SearchResultCardState state)
    {
        if (!await EnsureSearchResultLoadedForActionAsync(
                state) ||
            _searchPreviewMessage is null)
        {
            return;
        }

        var composeWindow =
            _serviceProvider
                .GetRequiredService<
                    ComposeWindow>();

        composeWindow.PrepareForward(
            _searchPreviewMessage);

        await ShowComposeWindowAsync(
            composeWindow);
    }

    private Task DeleteSearchResultFromUiAsync(
        SearchResultCardState state)
    {
        return DeleteSearchResultsFromUiAsync(
            new[]
            {
                state
            });
    }

    private async Task DeleteSearchResultsFromUiAsync(
        IReadOnlyList<SearchResultCardState> states)
    {
        var normalizedStates =
            NormalizeSearchResultSelection(
                states);

        if (normalizedStates.Count == 0)
        {
            return;
        }

        if (normalizedStates.Any(
                state =>
                    IsSearchHitInTrash(
                        state.Hit)))
        {
            MessageBox.Show(
                this,
                normalizedStates.Count == 1
                    ? "Die Nachricht befindet sich bereits im Papierkorb.\n\n" +
                      "Endgültiges Löschen wird aus der Suchansicht bewusst nicht angeboten."
                    : "Die Auswahl enthält mindestens eine Nachricht aus dem Papierkorb.\n\n" +
                      "Aus Sicherheitsgründen wird die Auswahl nicht teilweise gelöscht.\n\n" +
                      "Entfernen Sie die Papierkorb-Nachrichten aus der Auswahl und versuchen Sie es erneut.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        await ExecuteSearchMoveMutationAsync(
            normalizedStates,
            targetFolder:
                null,
            moveToTrash:
                true);
    }

    private Task MoveSearchResultFromUiAsync(
        SearchResultCardState state,
        MailFolderItemViewModel targetFolder)
    {
        return MoveSearchResultsFromUiAsync(
            new[]
            {
                state
            },
            targetFolder);
    }

    private async Task MoveSearchResultsFromUiAsync(
        IReadOnlyList<SearchResultCardState> states,
        MailFolderItemViewModel targetFolder)
    {
        ArgumentNullException.ThrowIfNull(
            targetFolder);

        var normalizedStates =
            NormalizeSearchResultSelection(
                states)
                .Where(
                    state =>
                        !string.Equals(
                            state.Hit.FolderId,
                            targetFolder.FolderId,
                            StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (normalizedStates.Count == 0)
        {
            return;
        }

        await ExecuteSearchMoveMutationAsync(
            normalizedStates,
            targetFolder,
            moveToTrash:
                false);
    }

    private static IReadOnlyList<SearchResultCardState>
        NormalizeSearchResultSelection(
            IReadOnlyList<SearchResultCardState> states)
    {
        ArgumentNullException.ThrowIfNull(
            states);

        return states
            .Where(
                state =>
                    state is not null)
            .GroupBy(
                state =>
                    $"{state.Hit.FolderId}\0" +
                    $"{state.Hit.UidValidity}\0" +
                    $"{state.Hit.UniqueId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(
                group =>
                    group.First())
            .ToList();
    }

    private async Task ExecuteSearchMoveMutationAsync(
        IReadOnlyList<SearchResultCardState> states,
        MailFolderItemViewModel? targetFolder,
        bool moveToTrash)
    {
        var normalizedStates =
            NormalizeSearchResultSelection(
                states);

        if (normalizedStates.Count == 0 ||
            _searchMutationIsRunning ||
            _searchResultIsLoading ||
            _searchAttachmentDownloadRunning)
        {
            return;
        }

        _searchMutationIsRunning =
            true;

        SetSearchControlsEnabled(
            false);

        if (_searchResultsHost is not null)
        {
            _searchResultsHost.IsHitTestVisible =
                false;
        }

        FolderListBox.IsHitTestVisible =
            false;

        var restartAutomaticSynchronization =
            false;

        var refreshSearch =
            false;

        try
        {
            restartAutomaticSynchronization =
                await PauseAutomaticSynchronizationAsync();

            var searchService =
                _serviceProvider
                    .GetRequiredService<
                        IMailSearchService>();

            /*
             * Die aktuelle Search-Service-Schnittstelle
             * validiert jeden Treffer einzeln über
             * FolderId + UIDVALIDITY + UID + Message-ID.
             *
             * Deshalb führen wir die ausgewählten Treffer
             * bewusst nacheinander aus.
             *
             * Der Auto-Sync bleibt während der gesamten
             * Massenaktion pausiert.
             */
            foreach (var state in
                     normalizedStates)
            {
                if (moveToTrash)
                {
                    await searchService
                        .MoveToTrashAsync(
                            state.Hit);
                }
                else
                {
                    if (targetFolder is null)
                    {
                        return;
                    }

                    if (string.Equals(
                            state.Hit.FolderId,
                            targetFolder.FolderId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await searchService
                        .MoveAsync(
                            state.Hit,
                            targetFolder.FolderId);
                }
            }

            await TrySynchronizeAfterSearchMutationAsync();

            refreshSearch =
                true;
        }
        catch
        {
            /*
             * Bei mehreren Nachrichten kann zu diesem
             * Zeitpunkt theoretisch bereits ein Teil der
             * Auswahl serverseitig verschoben worden sein.
             *
             * Deshalb:
             *
             * - keine automatische Wiederholung
             * - Serverzustand neu synchronisieren
             * - Suche neu ausführen
             * - keine Behauptung "alles fehlgeschlagen"
             */
            await TrySynchronizeAfterSearchMutationAsync();

            refreshSearch =
                true;

            MessageBox.Show(
                this,
                normalizedStates.Count == 1
                    ? moveToTrash
                        ? "Das Verschieben in den Papierkorb konnte nicht eindeutig bestätigt werden.\n\n" +
                          "Das Postfach wurde soweit möglich neu synchronisiert.\n\n" +
                          "Bitte prüfen Sie die aktuelle Suchansicht, bevor Sie die Aktion erneut ausführen."
                        : "Das Verschieben der Nachricht konnte nicht eindeutig bestätigt werden.\n\n" +
                          "Das Postfach wurde soweit möglich neu synchronisiert.\n\n" +
                          "Bitte prüfen Sie die aktuelle Suchansicht, bevor Sie die Aktion erneut ausführen."
                    : moveToTrash
                        ? "Das Verschieben der ausgewählten Nachrichten in den Papierkorb konnte nicht vollständig eindeutig bestätigt werden.\n\n" +
                          "Ein Teil der Nachrichten kann bereits verschoben worden sein.\n\n" +
                          "Das Postfach wurde soweit möglich neu synchronisiert. Bitte prüfen Sie die aktuelle Suchansicht, bevor Sie die Aktion erneut ausführen."
                        : "Das Verschieben der ausgewählten Nachrichten konnte nicht vollständig eindeutig bestätigt werden.\n\n" +
                          "Ein Teil der Nachrichten kann bereits verschoben worden sein.\n\n" +
                          "Das Postfach wurde soweit möglich neu synchronisiert. Bitte prüfen Sie die aktuelle Suchansicht, bevor Sie die Aktion erneut ausführen.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _searchMutationIsRunning =
                false;

            FolderListBox.IsHitTestVisible =
                true;

            if (_searchResultsHost is not null)
            {
                _searchResultsHost.IsHitTestVisible =
                    true;
            }

            SetSearchControlsEnabled(
                true);

            if (restartAutomaticSynchronization &&
                IsVisible)
            {
                StartAutomaticSynchronization();
            }
        }

        if (refreshSearch &&
            _searchIsActive)
        {
            await ExecuteSearchAsync();
        }
    }

    private async Task
        TrySynchronizeAfterSearchMutationAsync()
    {
        try
        {
            await _viewModel
                .SynchronizeAsync();
        }
        catch
        {
        }
    }

    private void
        SearchFolderListBox_OnPreviewDragOver(
            object sender,
            DragEventArgs e)
    {
        if (!TryGetDraggedSearchResults(
                e.Data,
                out var states))
        {
            return;
        }

        e.Handled =
            true;

        if (_searchMutationIsRunning ||
            _searchResultIsLoading ||
            states.Count == 0)
        {
            e.Effects =
                DragDropEffects.None;

            return;
        }

        var targetFolder =
            GetFolderFromElement(
                e.OriginalSource
                    as DependencyObject);

        if (targetFolder is null)
        {
            e.Effects =
                DragDropEffects.None;

            return;
        }

        var anyMessageCanMove =
            states.Any(
                state =>
                    !string.Equals(
                        targetFolder.FolderId,
                        state.Hit.FolderId,
                        StringComparison.OrdinalIgnoreCase));

        e.Effects =
            anyMessageCanMove
                ? DragDropEffects.Move
                : DragDropEffects.None;
    }

    private async void
        SearchFolderListBox_OnPreviewDrop(
            object sender,
            DragEventArgs e)
    {
        if (!TryGetDraggedSearchResults(
                e.Data,
                out var states))
        {
            return;
        }

        e.Handled =
            true;

        var targetFolder =
            GetFolderFromElement(
                e.OriginalSource
                    as DependencyObject);

        if (targetFolder is null)
        {
            e.Effects =
                DragDropEffects.None;

            return;
        }

        var messagesToMove =
            states
                .Where(
                    state =>
                        !string.Equals(
                            targetFolder.FolderId,
                            state.Hit.FolderId,
                            StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (messagesToMove.Count == 0)
        {
            e.Effects =
                DragDropEffects.None;

            return;
        }

        e.Effects =
            DragDropEffects.Move;

        await MoveSearchResultsFromUiAsync(
            messagesToMove,
            targetFolder);
    }

    private static bool TryGetDraggedSearchResults(
        IDataObject data,
        out IReadOnlyList<SearchResultCardState> states)
    {
        states =
            Array.Empty<SearchResultCardState>();

        if (!data.GetDataPresent(
                SearchMailDragDataFormat))
        {
            return false;
        }

        if (data.GetData(
                SearchMailDragDataFormat)
            is not IEnumerable<
                SearchResultCardState>
                draggedStates)
        {
            return false;
        }

        var snapshot =
            draggedStates
                .Where(
                    state =>
                        state is not null)
                .ToList();

        if (snapshot.Count == 0)
        {
            return false;
        }

        states =
            snapshot;

        return true;
    }

    private static MailMessageItemViewModel
        CreateSearchPreviewMessage(
            MailMessageData message)
    {
        return new MailMessageItemViewModel(
            sender:
                message.Sender,

            senderAddress:
                message.SenderAddress,

            recipientAddress:
                message.RecipientAddress,

            subject:
                message.Subject,

            preview:
                message.Preview,

            displayTime:
                message.DisplayTime,

            displayDateTime:
                message.DisplayDateTime,

            senderInitial:
                message.SenderInitial,

            greeting:
                message.Greeting,

            body:
                message.Body,

            closing:
                message.Closing,

            signature:
                message.Signature,

            isUnread:
                message.IsUnread,

            emphasizeSender:
                message.EmphasizeSender,

            highlightTitle:
                message.HighlightTitle,

            highlightText:
                message.HighlightText,

            htmlBody:
                message.HtmlBody,

            uniqueId:
                message.UniqueId,

            attachments:
                message.Attachments,

            hasSmimeSignature:
                message.HasSmimeSignature,

            messageId:
                message.MessageId,

            references:
                message.References,

            toAddresses:
                message.ToAddresses,

            ccAddresses:
                message.CcAddresses,

            replyToAddresses:
                message.ReplyToAddresses);
    }

    private bool TryGetSearchAttachmentContext(
        MailAttachmentData attachment,
        out MailSearchHitData? searchHit)
    {
        searchHit =
            null;

        if (!_searchIsActive ||
            _selectedSearchResult is null ||
            _searchPreviewMessage is null)
        {
            return false;
        }

        var belongsToPreview =
            _searchPreviewMessage
                .Attachments
                .Any(
                    current =>
                        ReferenceEquals(
                            current,
                            attachment));

        if (!belongsToPreview)
        {
            return false;
        }

        searchHit =
            _selectedSearchResult.Hit;

        return true;
    }

    private async Task
        SaveSearchAttachmentFromUiAsync(
            MailAttachmentData attachment)
    {
        if (_searchAttachmentDownloadRunning ||
            !TryGetSearchAttachmentContext(
                attachment,
                out var searchHit) ||
            searchHit is null)
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

        if (dialogResult !=
            true)
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

        _searchAttachmentDownloadRunning =
            true;

        SetSearchControlsEnabled(
            false);

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
                var searchService =
                    _serviceProvider
                        .GetRequiredService<
                            IMailSearchService>();

                await searchService
                    .DownloadAttachmentAsync(
                        searchHit,
                        attachment.PartSpecifier,
                        destination);
            }

            File.Move(
                temporaryPath,
                targetPath,
                overwrite:
                    true);
        }
        catch
        {
            MessageBox.Show(
                this,
                "Der Anhang konnte nicht gespeichert werden.\n\n" +
                "Die E-Mail wurde möglicherweise seit der Suche " +
                "verschoben oder verändert, oder der Mailserver ist " +
                "momentan nicht erreichbar.\n\n" +
                "Bitte führen Sie die Suche gegebenenfalls erneut aus.",
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            try
            {
                if (File.Exists(
                        temporaryPath))
                {
                    File.Delete(
                        temporaryPath);
                }
            }
            catch
            {
            }

            _searchAttachmentDownloadRunning =
                false;

            SetSearchControlsEnabled(
                true);

            UpdateSearchVisualState();
        }
    }

    private async Task RenderSearchPreviewMessageAsync(
        MailMessageItemViewModel message)
    {
        var renderVersion =
            ++_renderVersion;

        _allowExternalImagesForCurrentMessage =
            false;

        ExternalImagesNotice.Visibility =
            Visibility.Collapsed;

        if (!message.HasHtmlBody)
        {
            ShowPlainTextView();
            return;
        }

        PlainTextMailView.Visibility =
            Visibility.Collapsed;

        HtmlMailView.Visibility =
            Visibility.Visible;

        try
        {
            await EnsureWebViewReadyAsync();

            if (renderVersion !=
                    _renderVersion ||
                !ReferenceEquals(
                    _searchPreviewMessage,
                    message))
            {
                return;
            }

            var html =
                PrepareHtmlForMailView(
                    message.HtmlBody!);

            HtmlMailView
                .CoreWebView2
                .NavigateToString(
                    html);
        }
        catch
        {
            ShowPlainTextView();
        }
    }

    private string CreateSearchResultMetaText(
        MailSearchHitData result,
        bool showFolder)
    {
        var parts =
            new List<string>();

        if (showFolder)
        {
            parts.Add(
                $"Ordner: {GetSearchFolderDisplayName(result.FolderId)}");
        }

        if (!string.IsNullOrWhiteSpace(
                result.SenderAddress))
        {
            parts.Add(
                result.SenderAddress);
        }

        if (!string.IsNullOrWhiteSpace(
                result.RecipientAddress))
        {
            parts.Add(
                $"→ {result.RecipientAddress}");
        }

        return string.Join(
            "   ",
            parts);
    }

    private string GetSearchFolderDisplayName(
        string folderId)
    {
        return FindSearchFolder(
                   folderId)?
                   .DisplayName
               ?? folderId;
    }

    private MailFolderItemViewModel?
        FindSearchFolder(
            string folderId)
    {
        return _viewModel
            .MailFolders
            .FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.FolderId,
                        folderId,
                        StringComparison.OrdinalIgnoreCase));
    }

    private bool IsSearchHitInTrash(
        MailSearchHitData searchHit)
    {
        var folder =
            FindSearchFolder(
                searchHit.FolderId);

        if (folder is not null &&
            string.Equals(
                folder.DisplayName,
                "Papierkorb",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalized =
            searchHit
                .FolderId
                .Trim()
                .ToLowerInvariant();

        return normalized switch
        {
            "trash" => true,
            "papierkorb" => true,
            "deleted items" => true,
            "deleted messages" => true,
            "gelöschte elemente" => true,
            "geloeschte elemente" => true,
            _ => false
        };
    }

    private static string FormatSearchResultDate(
        DateTimeOffset date)
    {
        if (date ==
            DateTimeOffset.MinValue)
        {
            return string.Empty;
        }

        var localDate =
            date.LocalDateTime;

        if (localDate.Date ==
            DateTime.Today)
        {
            return localDate
                .ToString(
                    "HH:mm");
        }

        return localDate
            .ToString(
                "dd.MM.yyyy HH:mm");
    }

    private void ClearSearchView(
        bool clearText,
        bool resetScope)
    {
        _searchIsActive =
            false;

        _searchReadingPaneContext?
            .SetSearchMode(
                false);

        ClearSearchPreview();

        if (_searchResultsHost is not null)
        {
            _searchResultsHost.Visibility =
                Visibility.Collapsed;
        }

        _searchResultsStack?
            .Children
            .Clear();

        if (_searchResultsStatusText is not null)
        {
            _searchResultsStatusText.Text =
                string.Empty;
        }

        MessageListBox.Visibility =
            Visibility.Visible;

        SetMessagePagingSuppressedForSearch(
            false);

        if (clearText &&
            _searchTextBox is not null)
        {
            _searchTextBox.Clear();
        }

        if (resetScope)
        {
            _searchWholeMailbox =
                false;

            UpdateCurrentFolderSearchScopeText();
        }

        UpdateSearchVisualState();
    }

    private void ClearSearchPreview()
    {
        _selectedSearchResult =
            null;

        _searchPreviewMessage =
            null;

        _selectedSearchResults.Clear();

        _searchSelectionAnchor =
            null;

        _searchDragCandidate =
            null;

        _searchDragSelectionSnapshot =
            Array.Empty<SearchResultCardState>();

        RefreshSearchSelectionAppearance();

        _searchReadingPaneContext?
            .SetSearchPreviewMessage(
                null,
                false);

        _allowExternalImagesForCurrentMessage =
            false;

        if (_searchIsActive)
        {
            ShowPlainTextView();
        }
        else
        {
            _ =
                RenderSelectedMessageAsync();
        }
    }

    private void ClearSearchPreviewMessageOnly()
    {
        _selectedSearchResult =
            null;

        _searchPreviewMessage =
            null;

        _searchReadingPaneContext?
            .SetSearchPreviewMessage(
                null,
                false);

        _allowExternalImagesForCurrentMessage =
            false;

        ShowPlainTextView();
    }

    private void SetSearchControlsEnabled(
        bool enabled)
    {
        if (_searchTextBox is not null)
        {
            _searchTextBox.IsEnabled =
                enabled;
        }

        if (_searchScopeButton is not null)
        {
            _searchScopeButton.IsEnabled =
                enabled;
        }

        if (_searchButton is not null)
        {
            _searchButton.IsEnabled =
                enabled;
        }

        if (_clearSearchButton is not null)
        {
            _clearSearchButton.IsEnabled =
                enabled;
        }
    }

    private void UpdateSearchVisualState()
    {
        var hasText =
            !string.IsNullOrWhiteSpace(
                _searchTextBox?.Text);

        if (_searchHintText is not null)
        {
            _searchHintText.Visibility =
                hasText
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        if (_clearSearchButton is not null)
        {
            _clearSearchButton.Visibility =
                hasText ||
                _searchIsActive
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void SetMessagePagingSuppressedForSearch(
        bool suppressed)
    {
        if (MessageListBox.Parent
            is not Grid messageColumnGrid)
        {
            return;
        }

        foreach (var child in
                 messageColumnGrid
                     .Children
                     .OfType<FrameworkElement>()
                     .Where(
                         element =>
                             Grid.GetRow(
                                 element) == 3))
        {
            child.IsHitTestVisible =
                !suppressed;

            child.Opacity =
                suppressed
                    ? 0
                    : 1;

            child.MaxHeight =
                suppressed
                    ? 0
                    : double.PositiveInfinity;
        }
    }

    private void UpdateCurrentFolderSearchScopeText()
    {
        if (_searchScopeText is null)
        {
            return;
        }

        if (_searchWholeMailbox)
        {
            _searchScopeText.Text =
                "Ganzes Postfach";

            return;
        }

        var folderName =
            _viewModel
                .SelectedFolder?
                .DisplayName;

        _searchScopeText.Text =
            !string.IsNullOrWhiteSpace(
                folderName)
                ? folderName
                : "Aktueller Ordner";
    }

    private void MainViewModel_OnSearchPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (string.Equals(
                e.PropertyName,
                nameof(
                    MainViewModel.SelectedMessage),
                StringComparison.Ordinal) &&
            _searchPreviewMessage is not null)
        {
            _ =
                RenderSearchPreviewMessageAsync(
                    _searchPreviewMessage);

            return;
        }

        if (!string.Equals(
                e.PropertyName,
                nameof(
                    MainViewModel.SelectedFolder),
                StringComparison.Ordinal))
        {
            return;
        }

        UpdateCurrentFolderSearchScopeText();

        if (_searchIsActive &&
            !_searchWholeMailbox)
        {
            ClearSearchView(
                clearText:
                    false,
                resetScope:
                    false);
        }
    }

    private void MainWindowSearch_OnClosed(
        object? sender,
        EventArgs e)
    {
        _viewModel.PropertyChanged -=
            MainViewModel_OnSearchPropertyChanged;

        FolderListBox.PreviewDragOver -=
            SearchFolderListBox_OnPreviewDragOver;

        FolderListBox.PreviewDrop -=
            SearchFolderListBox_OnPreviewDrop;

        _searchReadingPaneContext?
            .Dispose();

        _searchReadingPaneContext =
            null;

        Closed -=
            MainWindowSearch_OnClosed;
    }

    private sealed class SearchResultCardState
    {
        public SearchResultCardState(
            MailSearchHitData hit,
            Border card,
            TextBlock senderText,
            TextBlock subjectText)
        {
            Hit =
                hit;

            Card =
                card;

            SenderText =
                senderText;

            SubjectText =
                subjectText;
        }

        public MailSearchHitData Hit
        { get; set; }

        public Border Card
        { get; }

        public TextBlock SenderText
        { get; }

        public TextBlock SubjectText
        { get; }
    }

    private sealed class SearchReadingPaneContext :
        INotifyPropertyChanged,
        IDisposable
    {
        private readonly MainViewModel
            _viewModel;

        private MailMessageItemViewModel?
            _searchPreviewMessage;

        private bool
            _searchModeActive;

        private bool
            _searchPreviewIsTrash;

        public SearchReadingPaneContext(
            MainViewModel viewModel)
        {
            ArgumentNullException.ThrowIfNull(
                viewModel);

            _viewModel =
                viewModel;

            _viewModel.PropertyChanged +=
                MainViewModel_OnPropertyChanged;
        }

        public event PropertyChangedEventHandler?
            PropertyChanged;

        public MailMessageItemViewModel?
            SelectedMessage =>
                _searchModeActive
                    ? _searchPreviewMessage
                    : _viewModel.SelectedMessage;

        public bool IsLoading =>
            _viewModel.IsLoading ||
            (_searchModeActive &&
             _searchPreviewMessage is null);

        public bool IsTrashFolderSelected =>
            !_searchModeActive &&
            _viewModel.IsTrashFolderSelected;

        public string MessageActionToolTip =>
            !_searchModeActive
                ? _viewModel.MessageActionToolTip
                : _searchPreviewIsTrash
                    ? "Nachricht befindet sich bereits im Papierkorb"
                    : "Nachricht löschen";

        public string MessageActionGlyph =>
            !_searchModeActive
                ? _viewModel.MessageActionGlyph
                : "\uE74D";

        public void SetSearchMode(
            bool active)
        {
            if (_searchModeActive ==
                active)
            {
                return;
            }

            _searchModeActive =
                active;

            if (!active)
            {
                _searchPreviewMessage =
                    null;

                _searchPreviewIsTrash =
                    false;
            }

            NotifySearchContextChanged();
        }

        public void SetSearchPreviewMessage(
            MailMessageItemViewModel? message,
            bool isTrash)
        {
            if (ReferenceEquals(
                    _searchPreviewMessage,
                    message) &&
                _searchPreviewIsTrash ==
                    isTrash)
            {
                return;
            }

            _searchPreviewMessage =
                message;

            _searchPreviewIsTrash =
                message is not null &&
                isTrash;

            NotifySearchContextChanged();
        }

        private void NotifySearchContextChanged()
        {
            OnPropertyChanged(
                nameof(SelectedMessage));

            OnPropertyChanged(
                nameof(IsLoading));

            OnPropertyChanged(
                nameof(IsTrashFolderSelected));

            OnPropertyChanged(
                nameof(MessageActionToolTip));

            OnPropertyChanged(
                nameof(MessageActionGlyph));
        }

        private void MainViewModel_OnPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(
                    MainViewModel.SelectedMessage):

                    if (!_searchModeActive)
                    {
                        OnPropertyChanged(
                            nameof(SelectedMessage));
                    }

                    break;

                case nameof(
                    MainViewModel.IsLoading):

                    OnPropertyChanged(
                        nameof(IsLoading));

                    break;

                case nameof(
                    MainViewModel.IsTrashFolderSelected):

                    if (!_searchModeActive)
                    {
                        OnPropertyChanged(
                            nameof(IsTrashFolderSelected));
                    }

                    break;

                case nameof(
                    MainViewModel.MessageActionToolTip):

                    if (!_searchModeActive)
                    {
                        OnPropertyChanged(
                            nameof(MessageActionToolTip));
                    }

                    break;

                case nameof(
                    MainViewModel.MessageActionGlyph):

                    if (!_searchModeActive)
                    {
                        OnPropertyChanged(
                            nameof(MessageActionGlyph));
                    }

                    break;
            }
        }

        private void OnPropertyChanged(
            string propertyName)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(
                    propertyName));
        }

        public void Dispose()
        {
            _viewModel.PropertyChanged -=
                MainViewModel_OnPropertyChanged;
        }
    }
}