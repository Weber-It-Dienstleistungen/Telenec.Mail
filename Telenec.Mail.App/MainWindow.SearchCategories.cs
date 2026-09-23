using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private const string SearchCategoryMenuTag =
        "Telenec.Search.CategoryMenu";

    /*
     * MainWindow besitzt bereits einen statischen Konstruktor
     * in einem anderen Partial.
     *
     * Deshalb registrieren wir den ClassHandler bewusst über
     * einen statischen Feldinitialisierer.
     *
     * Der Handler wird nur aktiv, wenn der Border tatsächlich
     * eine SearchResultCardState als Tag trägt.
     */
    private static readonly bool
        _searchCategoryContextMenuHandlerRegistered =
            RegisterSearchCategoryContextMenuHandler();

    private static bool
        RegisterSearchCategoryContextMenuHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(Border),
            ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(
                SearchResultContextMenu_OnOpening),
            true);

        return true;
    }

    private static void SearchResultContextMenu_OnOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (sender is not Border card ||
            card.Tag
                is not SearchResultCardState state ||
            card.ContextMenu is null)
        {
            return;
        }

        if (Window.GetWindow(
                card)
            is not MainWindow window ||
            !window._searchIsActive)
        {
            return;
        }

        window.AddOrRefreshSearchCategoryMenu(
            card.ContextMenu,
            state);
    }

    private void AddOrRefreshSearchCategoryMenu(
        ContextMenu contextMenu,
        SearchResultCardState state)
    {
        var existingCategoryMenu =
            contextMenu
                .Items
                .OfType<MenuItem>()
                .FirstOrDefault(
                    item =>
                        string.Equals(
                            item.Tag?.ToString(),
                            SearchCategoryMenuTag,
                            StringComparison.Ordinal));

        if (existingCategoryMenu is not null)
        {
            contextMenu.Items.Remove(
                existingCategoryMenu);
        }

        var actionSelection =
            GetSelectedSearchResultsForAction(
                state);

        var isSingleSelection =
            actionSelection.Count == 1;

        var categoryMenu =
            new MenuItem
            {
                Header =
                    "Kategorie",

                Tag =
                    SearchCategoryMenuTag,

                IsEnabled =
                    isSingleSelection &&
                    !_searchMutationIsRunning &&
                    !_searchResultIsLoading &&
                    !_searchAttachmentDownloadRunning
            };

        foreach (var category in
                 MailCategoryCatalog.All)
        {
            var categoryItem =
                new MenuItem
                {
                    Header =
                        category.DisplayName,

                    IsCheckable =
                        true,

                    IsChecked =
                        SearchHitHasKeyword(
                            state.Hit,
                            category.Keyword),

                    IsEnabled =
                        categoryMenu.IsEnabled,

                    Tag =
                        category
                };

            categoryItem.Click +=
                async (_, _) =>
                {
                    await SetSearchResultCategoryAsync(
                        state,
                        category);
                };

            categoryMenu.Items.Add(
                categoryItem);
        }

        var moveMenuIndex =
            FindSearchMoveMenuIndex(
                contextMenu);

        if (moveMenuIndex >= 0)
        {
            contextMenu.Items.Insert(
                moveMenuIndex,
                categoryMenu);
        }
        else
        {
            contextMenu.Items.Add(
                categoryMenu);
        }
    }

    private static int FindSearchMoveMenuIndex(
        ContextMenu contextMenu)
    {
        for (var index = 0;
             index < contextMenu.Items.Count;
             index++)
        {
            if (contextMenu.Items[index]
                    is not MenuItem menuItem ||
                menuItem.Header
                    is not string header)
            {
                continue;
            }

            if (header.StartsWith(
                    "Verschieben nach",
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private async Task SetSearchResultCategoryAsync(
        SearchResultCardState state,
        MailCategoryDefinition category)
    {
        if (_searchMutationIsRunning ||
            _searchResultIsLoading ||
            _searchAttachmentDownloadRunning ||
            !_searchIsActive)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(
            category);

        var shouldEnable =
            !SearchHitHasKeyword(
                state.Hit,
                category.Keyword);

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

        try
        {
            var searchService =
                _serviceProvider
                    .GetRequiredService<
                        IMailSearchService>();

            /*
             * Der Search-Service prüft vor UND nach der
             * Änderung:
             *
             * - UIDVALIDITY
             * - UID
             * - Message-ID
             * - tatsächlichen Keyword-Zustand
             *
             * Erst nach erfolgreicher Serverbestätigung
             * aktualisieren wir die lokale Darstellung.
             */
            await searchService
                .SetKeywordAsync(
                    state.Hit,
                    category.Keyword,
                    shouldEnable);

            var updatedKeywords =
                CreateUpdatedSearchKeywords(
                    state.Hit.Keywords,
                    category.Keyword,
                    shouldEnable);

            state.Hit =
                state.Hit with
                {
                    Keywords =
                        updatedKeywords
                };

            /*
             * Falls genau diese Such-Mail rechts geöffnet ist,
             * halten wir auch deren ViewModel synchron.
             */
            if (ReferenceEquals(
                    _selectedSearchResult,
                    state) &&
                _searchPreviewMessage is not null)
            {
                _searchPreviewMessage
                    .SetKeywordState(
                        category.Keyword,
                        shouldEnable);
            }

            /*
             * Falls die normale Nachrichtenliste im
             * Hintergrund denselben Ordner enthält, halten wir
             * auch diesen bereits geladenen Datensatz aktuell.
             *
             * So erscheint beim Beenden der Suche nicht kurz
             * wieder ein veralteter Kategorie-Zustand.
             */
            if (string.Equals(
                    _viewModel.SelectedFolder?.FolderId,
                    state.Hit.FolderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                var normalMessage =
                    _viewModel
                        .Messages
                        .FirstOrDefault(
                            message =>
                                message.UniqueId ==
                                state.Hit.UniqueId);

                normalMessage?
                    .SetKeywordState(
                        category.Keyword,
                        shouldEnable);
            }

            RefreshSearchResultCategoryBadges(
                state);

            state.Card.ContextMenu =
                CreateSearchResultContextMenu(
                    state);
        }
        catch (NotSupportedException)
        {
            MessageBox.Show(
                this,
                "Der Mailserver unterstützt in diesem Ordner keine benutzerdefinierten Kategorien.",
                "Kategorie",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show(
                this,
                "Die Kategorie konnte auf dem Mailserver nicht geändert werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie es erneut.",
                "Kategorie",
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

            UpdateSearchVisualState();
        }
    }

    private static IReadOnlyList<string>
        CreateUpdatedSearchKeywords(
            IReadOnlyList<string>? existingKeywords,
            string keyword,
            bool isEnabled)
    {
        var keywords =
            new HashSet<string>(
                existingKeywords
                ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

        if (isEnabled)
        {
            keywords.Add(
                keyword);
        }
        else
        {
            keywords.Remove(
                keyword);
        }

        return keywords
            .ToArray();
    }

    private static bool SearchHitHasKeyword(
        MailSearchHitData searchHit,
        string keyword)
    {
        return searchHit
            .Keywords?
            .Any(
                currentKeyword =>
                    string.Equals(
                        currentKeyword,
                        keyword,
                        StringComparison.OrdinalIgnoreCase))
            == true;
    }

    private static void RefreshSearchResultCategoryBadges(
        SearchResultCardState state)
    {
        if (state.Card.Child
            is not StackPanel stack)
        {
            return;
        }

        var assignedCategories =
            MailCategoryCatalog
                .All
                .Where(
                    category =>
                        SearchHitHasKeyword(
                            state.Hit,
                            category.Keyword))
                .ToList();

        var categoryPanel =
            stack
                .Children
                .OfType<WrapPanel>()
                .FirstOrDefault();

        if (assignedCategories.Count == 0)
        {
            if (categoryPanel is not null)
            {
                stack.Children.Remove(
                    categoryPanel);
            }

            return;
        }

        if (categoryPanel is null)
        {
            categoryPanel =
                new WrapPanel
                {
                    Margin =
                        new Thickness(
                            0,
                            6,
                            0,
                            0)
                };

            stack.Children.Add(
                categoryPanel);
        }
        else
        {
            categoryPanel
                .Children
                .Clear();
        }

        foreach (var category in
                 assignedCategories)
        {
            var categoryBadge =
                new Border
                {
                    Margin =
                        new Thickness(
                            0,
                            0,
                            6,
                            0),

                    Padding =
                        new Thickness(
                            6,
                            2,
                            6,
                            2),

                    CornerRadius =
                        new CornerRadius(
                            3),

                    Background =
                        category.Background,

                    Child =
                        new TextBlock
                        {
                            Text =
                                category.DisplayName,

                            FontSize =
                                10,

                            FontWeight =
                                FontWeights.SemiBold,

                            Foreground =
                                category.Foreground
                        }
                };

            categoryPanel.Children.Add(
                categoryBadge);
        }
    }
}