using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    private bool
        _folderManagementUiInitialized;

    private bool
        _folderManagementOperationRunning;

    private bool
        _folderHierarchyUpdateRunning;

    private bool
        _folderHierarchyUpdateScheduled;

    private readonly HashSet<string>
        _collapsedFolderIds =
            new(
                StringComparer.OrdinalIgnoreCase);

    private Button?
        _createFolderButton;

    /*
     * SelectedFolder ist für diesen Zweck nicht ausreichend,
     * weil nach dem Programmstart praktisch immer bereits ein
     * Ordner ausgewählt ist.
     *
     * Hier speichern wir ausschließlich einen Ordner, den der
     * Benutzer bewusst in der Navigation angeklickt hat.
     */
    private MailFolderItemViewModel?
        _folderCreationParentCandidate;

    private char?
        _folderHierarchySeparator;

    private void MainWindowFolderManagement_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        InitializeFolderManagementUi();

        if (_folderManagementUiInitialized)
        {
            Loaded -=
                MainWindowFolderManagement_OnLoaded;
        }
    }

    private void InitializeFolderManagementUi()
    {
        if (_folderManagementUiInitialized)
        {
            return;
        }

        if (FolderListBox.Parent
            is not Grid navigationGrid)
        {
            return;
        }

        if (navigationGrid.RowDefinitions.Count < 5)
        {
            return;
        }

        foreach (UIElement child in
                 navigationGrid.Children)
        {
            var row =
                Grid.GetRow(
                    child);

            if (row >= 3)
            {
                Grid.SetRow(
                    child,
                    row + 1);
            }
        }

        navigationGrid
            .RowDefinitions
            .Insert(
                3,
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });

        var folderHeader =
            CreateFolderManagementHeader();

        Grid.SetRow(
            folderHeader,
            3);

        navigationGrid.Children.Add(
            folderHeader);

        FolderListBox.PreviewMouseRightButtonDown +=
            FolderListBox_OnPreviewMouseRightButtonDownForManagement;

        FolderListBox.PreviewMouseLeftButtonDown +=
            FolderListBox_OnPreviewMouseLeftButtonDownForCreationTarget;

        PreviewMouseLeftButtonDown +=
            MainWindow_OnPreviewMouseLeftButtonDownForFolderCreationTarget;

        _viewModel
            .MailFolders
            .CollectionChanged +=
                MailFolders_OnCollectionChangedForHierarchy;

        FolderListBox
            .ItemContainerGenerator
            .StatusChanged +=
                FolderListBoxItemContainerGenerator_OnStatusChanged;

        Closed +=
            MainWindowFolderManagement_OnClosed;

        UpdateFolderHierarchy();

        _folderManagementUiInitialized =
            true;
    }

    private Grid CreateFolderManagementHeader()
    {
        var grid =
            new Grid
            {
                Margin =
                    new Thickness(
                        20,
                        0,
                        12,
                        6)
            };

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

        var title =
            new TextBlock
            {
                Text =
                    "Ordner",

                FontSize =
                    11,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        Grid.SetColumn(
            title,
            0);

        grid.Children.Add(
            title);

        var addButton =
            new Button
            {
                Width =
                    30,

                Height =
                    30,

                Padding =
                    new Thickness(0),

                Background =
                    Brushes.Transparent,

                BorderThickness =
                    new Thickness(0),

                Cursor =
                    Cursors.Hand,

                ToolTip =
                    "Neuen Ordner anlegen"
            };

        var glyph =
            new TextBlock
            {
                Text =
                    "\uE710",

                FontFamily =
                    new FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    15,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        glyph.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Navigation.TextSecondary");

        addButton.Content =
            glyph;

        addButton.Click +=
            CreateFolderButton_OnClick;

        _createFolderButton =
            addButton;

        Grid.SetColumn(
            addButton,
            1);

        grid.Children.Add(
            addButton);

        return grid;
    }

    /*
     * =========================================================
     * Kontextsensitives +
     * =========================================================
     */

    private void
        FolderListBox_OnPreviewMouseLeftButtonDownForCreationTarget(
            object sender,
            MouseButtonEventArgs e)
    {
        var source =
            e.OriginalSource
            as DependencyObject;

        var item =
            FindVisualParent<ListBoxItem>(
                source);

        if (item?.DataContext
            is not MailFolderItemViewModel folder)
        {
            return;
        }

        if (TryHandleFolderExpandCollapseClick(
                e,
                item,
                folder))
        {
            return;
        }

        SetFolderCreationParentCandidate(
            folder);
    }

    private bool TryHandleFolderExpandCollapseClick(
        MouseButtonEventArgs e,
        ListBoxItem item,
        MailFolderItemViewModel folder)
    {
        if (!folder.HasChildFolders)
        {
            return false;
        }

        var folderNameText =
            FindVisualDescendantByName<TextBlock>(
                item,
                "FolderNameText");

        if (folderNameText is null ||
            folderNameText.ActualWidth <= 0 ||
            folderNameText.ActualHeight <= 0)
        {
            return false;
        }

        var mousePosition =
            e.GetPosition(
                folderNameText);

        if (mousePosition.X < 0 ||
            mousePosition.Y < 0 ||
            mousePosition.X >
                folderNameText.ActualWidth ||
            mousePosition.Y >
                folderNameText.ActualHeight)
        {
            return false;
        }

        /*
         * Der Pfeil ist Bestandteil des NavigationDisplayName.
         *
         * Pro Hierarchiestufe verwenden wir dort genau ein
         * EM-Space. Bei 14-Punkt-Navigation entspricht das
         * ungefähr 14 Pixeln.
         *
         * Nur der Bereich bis kurz hinter dem Pfeil löst das
         * Ein-/Ausklappen aus. Ein normaler Klick auf den
         * Ordnernamen öffnet weiterhin wie gewohnt den Ordner.
         */
        var toggleHitWidth =
            (folder.HierarchyDepth * 14.0) +
            20.0;

        if (mousePosition.X >
            toggleHitWidth)
        {
            return false;
        }

        ToggleFolderExpansion(
            folder);

        e.Handled =
            true;

        return true;
    }

    private void ToggleFolderExpansion(
        MailFolderItemViewModel folder)
    {
        if (!folder.HasChildFolders)
        {
            return;
        }

        var separator =
            _folderHierarchySeparator;

        if (!separator.HasValue)
        {
            separator =
                DetectFolderHierarchySeparator(
                    _viewModel
                        .MailFolders
                        .ToList());
        }

        if (!separator.HasValue)
        {
            return;
        }

        var isCurrentlyCollapsed =
            _collapsedFolderIds.Contains(
                folder.FolderId);

        if (isCurrentlyCollapsed)
        {
            _collapsedFolderIds.Remove(
                folder.FolderId);
        }
        else
        {
            _collapsedFolderIds.Add(
                folder.FolderId);

            var selectedFolder =
                _viewModel.SelectedFolder;

            if (selectedFolder is not null &&
                IsDescendantFolder(
                    selectedFolder.FolderId,
                    folder.FolderId,
                    separator.Value))
            {
                _viewModel.SelectedFolder =
                    folder;
            }
        }

        UpdateFolderHierarchy();
    }

    private static bool IsDescendantFolder(
        string candidateFolderId,
        string parentFolderId,
        char separator)
    {
        if (string.Equals(
                candidateFolderId,
                parentFolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var childPrefix =
            parentFolderId +
            separator;

        return candidateFolderId
            .StartsWith(
                childPrefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private void
        MainWindow_OnPreviewMouseLeftButtonDownForFolderCreationTarget(
            object sender,
            MouseButtonEventArgs e)
    {
        var source =
            e.OriginalSource
            as DependencyObject;

        if (source is null)
        {
            return;
        }

        if (IsVisualDescendantOf(
                source,
                FolderListBox))
        {
            return;
        }

        if (_createFolderButton is not null &&
            IsVisualDescendantOf(
                source,
                _createFolderButton))
        {
            return;
        }

        SetFolderCreationParentCandidate(
            null);
    }

    private void SetFolderCreationParentCandidate(
        MailFolderItemViewModel? folder)
    {
        _folderCreationParentCandidate =
            folder;

        UpdateCreateFolderButtonToolTip();
    }

    private MailFolderItemViewModel?
        GetCurrentFolderCreationParentCandidate()
    {
        var candidate =
            _folderCreationParentCandidate;

        if (candidate is null)
        {
            return null;
        }

        var currentFolder =
            _viewModel
                .MailFolders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            candidate.FolderId,
                            StringComparison.OrdinalIgnoreCase));

        if (currentFolder is null)
        {
            SetFolderCreationParentCandidate(
                null);

            return null;
        }

        _folderCreationParentCandidate =
            currentFolder;

        return currentFolder;
    }

    private void UpdateCreateFolderButtonToolTip()
    {
        if (_createFolderButton is null)
        {
            return;
        }

        var parent =
            _folderCreationParentCandidate;

        _createFolderButton.ToolTip =
            parent is null
                ? "Neuen Ordner anlegen"
                : $"Unterordner in „{parent.DisplayName}“ anlegen";
    }

    private static bool IsVisualDescendantOf(
        DependencyObject source,
        DependencyObject ancestor)
    {
        var current =
            source;

        while (current is not null)
        {
            if (ReferenceEquals(
                    current,
                    ancestor))
            {
                return true;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return false;
    }

    /*
     * =========================================================
     * Hierarchische Ordnerdarstellung
     * =========================================================
     */

    private void MailFolders_OnCollectionChangedForHierarchy(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (_folderHierarchyUpdateRunning ||
            _folderHierarchyUpdateScheduled)
        {
            return;
        }

        /*
         * ObservableCollection darf nicht innerhalb ihres
         * eigenen CollectionChanged-Ereignisses erneut mit
         * Move(...) verändert werden.
         *
         * Deshalb wird die Hierarchiesortierung bewusst auf den
         * nächsten Dispatcher-Zyklus verschoben.
         */
        _folderHierarchyUpdateScheduled =
            true;

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    _folderHierarchyUpdateScheduled =
                        false;

                    UpdateFolderHierarchy();
                }));
    }

    private void
        FolderListBoxItemContainerGenerator_OnStatusChanged(
            object? sender,
            EventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(
                ApplyFolderNavigationBindings));
    }

    private void UpdateFolderHierarchy()
    {
        if (_folderHierarchyUpdateRunning)
        {
            return;
        }

        var folders =
            _viewModel
                .MailFolders
                .ToList();

        if (folders.Count == 0)
        {
            RefreshFolderNavigationView();

            return;
        }

        var currentFolderIds =
            folders
                .Select(
                    folder =>
                        folder.FolderId)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        _collapsedFolderIds.RemoveWhere(
            folderId =>
                !currentFolderIds.Contains(
                    folderId));

        _folderHierarchySeparator ??=
            DetectFolderHierarchySeparator(
                folders);

        var separator =
            _folderHierarchySeparator;

        _folderHierarchyUpdateRunning =
            true;

        try
        {
            if (!separator.HasValue)
            {
                foreach (var folder in
                         folders)
                {
                    folder.UpdateHierarchyState(
                        hierarchyDepth:
                            0,

                        hasChildFolders:
                            false,

                        isExpanded:
                            true,

                        isVisibleInNavigation:
                            true);
                }

                RefreshFolderNavigationView();
            }
            else
            {
                foreach (var folder in
                         folders)
                {
                    var depth =
                        CalculateFolderHierarchyDepth(
                            folder.FolderId,
                            separator.Value);

                    folder.UpdateHierarchyDepth(
                        depth);
                }

                var orderedFolders =
                    OrderFoldersByHierarchy(
                        folders,
                        separator.Value);

                for (var targetIndex = 0;
                     targetIndex < orderedFolders.Count;
                     targetIndex++)
                {
                    var folder =
                        orderedFolders[targetIndex];

                    var currentIndex =
                        _viewModel
                            .MailFolders
                            .IndexOf(
                                folder);

                    if (currentIndex < 0 ||
                        currentIndex == targetIndex)
                    {
                        continue;
                    }

                    _viewModel
                        .MailFolders
                        .Move(
                            currentIndex,
                            targetIndex);
                }

                var foldersById =
                    folders
                        .ToDictionary(
                            folder =>
                                folder.FolderId,
                            StringComparer.OrdinalIgnoreCase);

                foreach (var folder in
                         folders)
                {
                    var depth =
                        CalculateFolderHierarchyDepth(
                            folder.FolderId,
                            separator.Value);

                    var hasChildFolders =
                        HasDirectChildFolders(
                            folder,
                            folders,
                            separator.Value);

                    var isExpanded =
                        !_collapsedFolderIds.Contains(
                            folder.FolderId);

                    var isVisibleInNavigation =
                        IsFolderVisibleInNavigation(
                            folder,
                            foldersById,
                            separator.Value);

                    folder.UpdateHierarchyState(
                        hierarchyDepth:
                            depth,

                        hasChildFolders:
                            hasChildFolders,

                        isExpanded:
                            isExpanded,

                        isVisibleInNavigation:
                            isVisibleInNavigation);
                }

                RefreshFolderNavigationView();
            }
        }
        finally
        {
            _folderHierarchyUpdateRunning =
                false;
        }

        Dispatcher.BeginInvoke(
            new Action(
                ApplyFolderNavigationBindings));
    }

    private static bool HasDirectChildFolders(
        MailFolderItemViewModel parentFolder,
        IReadOnlyList<MailFolderItemViewModel> folders,
        char separator)
    {
        return folders.Any(
            candidate =>
            {
                if (ReferenceEquals(
                        candidate,
                        parentFolder))
                {
                    return false;
                }

                var parentFolderId =
                    GetVisibleParentFolderId(
                        candidate.FolderId,
                        separator);

                return string.Equals(
                    parentFolderId,
                    parentFolder.FolderId,
                    StringComparison.OrdinalIgnoreCase);
            });
    }

    private bool IsFolderVisibleInNavigation(
        MailFolderItemViewModel folder,
        IReadOnlyDictionary<string, MailFolderItemViewModel> foldersById,
        char separator)
    {
        var parentFolderId =
            GetVisibleParentFolderId(
                folder.FolderId,
                separator);

        var visitedParentIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        while (!string.IsNullOrWhiteSpace(
                   parentFolderId))
        {
            if (!visitedParentIds.Add(
                    parentFolderId))
            {
                return true;
            }

            if (_collapsedFolderIds.Contains(
                    parentFolderId))
            {
                return false;
            }

            if (!foldersById.ContainsKey(
                    parentFolderId))
            {
                return true;
            }

            parentFolderId =
                GetVisibleParentFolderId(
                    parentFolderId,
                    separator);
        }

        return true;
    }

    private void RefreshFolderNavigationView()
    {
        var view =
            CollectionViewSource
                .GetDefaultView(
                    _viewModel.MailFolders);

        view.Filter =
            item =>
                item is not
                    MailFolderItemViewModel folder ||
                folder.IsVisibleInNavigation;

        view.Refresh();
    }

    private static IReadOnlyList<MailFolderItemViewModel>
        OrderFoldersByHierarchy(
            IReadOnlyList<MailFolderItemViewModel> folders,
            char separator)
    {
        var foldersById =
            folders
                .ToDictionary(
                    folder =>
                        folder.FolderId,
                    StringComparer.OrdinalIgnoreCase);

        var rootFolders =
            new List<MailFolderItemViewModel>();

        var childrenByParentId =
            new Dictionary<
                string,
                List<MailFolderItemViewModel>>(
                    StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            var parentFolderId =
                GetVisibleParentFolderId(
                    folder.FolderId,
                    separator);

            if (string.IsNullOrWhiteSpace(
                    parentFolderId) ||
                !foldersById.ContainsKey(
                    parentFolderId))
            {
                rootFolders.Add(
                    folder);

                continue;
            }

            if (!childrenByParentId.TryGetValue(
                    parentFolderId,
                    out var children))
            {
                children =
                    new List<MailFolderItemViewModel>();

                childrenByParentId.Add(
                    parentFolderId,
                    children);
            }

            children.Add(
                folder);
        }

        var orderedFolders =
            new List<MailFolderItemViewModel>(
                folders.Count);

        var visitedFolderIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        void AppendFolderAndChildren(
            MailFolderItemViewModel folder)
        {
            if (!visitedFolderIds.Add(
                    folder.FolderId))
            {
                return;
            }

            orderedFolders.Add(
                folder);

            if (!childrenByParentId.TryGetValue(
                    folder.FolderId,
                    out var children))
            {
                return;
            }

            foreach (var child in
                     children)
            {
                AppendFolderAndChildren(
                    child);
            }
        }

        foreach (var rootFolder in
                 rootFolders)
        {
            AppendFolderAndChildren(
                rootFolder);
        }

        foreach (var folder in
                 folders)
        {
            AppendFolderAndChildren(
                folder);
        }

        return orderedFolders;
    }

    private static string?
        GetVisibleParentFolderId(
            string folderId,
            char separator)
    {
        var normalizedPath =
            NormalizeFolderHierarchyPath(
                folderId,
                separator);

        if (string.IsNullOrWhiteSpace(
                normalizedPath))
        {
            return null;
        }

        var lastSeparatorIndex =
            normalizedPath.LastIndexOf(
                separator);

        if (lastSeparatorIndex < 0)
        {
            return null;
        }

        var normalizedParentPath =
            normalizedPath[
                ..lastSeparatorIndex];

        if (string.IsNullOrWhiteSpace(
                normalizedParentPath))
        {
            return null;
        }

        var inboxPrefix =
            "INBOX" +
            separator;

        if (folderId.StartsWith(
                inboxPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return
                inboxPrefix +
                normalizedParentPath;
        }

        return normalizedParentPath;
    }

    private static char?
        DetectFolderHierarchySeparator(
            IReadOnlyList<
                MailFolderItemViewModel> folders)
    {
        var candidates =
            new[]
            {
                '/',
                '.',
                '\\'
            };

        char? bestCandidate =
            null;

        var bestMatchCount =
            0;

        foreach (var candidate in
                 candidates)
        {
            var matchCount =
                0;

            foreach (var possibleParent in
                     folders)
            {
                var prefix =
                    possibleParent.FolderId +
                    candidate;

                matchCount +=
                    folders.Count(
                        possibleChild =>
                            !ReferenceEquals(
                                possibleChild,
                                possibleParent) &&
                            possibleChild
                                .FolderId
                                .StartsWith(
                                    prefix,
                                    StringComparison.OrdinalIgnoreCase));
            }

            if (matchCount <=
                bestMatchCount)
            {
                continue;
            }

            bestMatchCount =
                matchCount;

            bestCandidate =
                candidate;
        }

        return bestMatchCount > 0
            ? bestCandidate
            : null;
    }

    private static int CalculateFolderHierarchyDepth(
        string folderId,
        char separator)
    {
        var normalizedPath =
            NormalizeFolderHierarchyPath(
                folderId,
                separator);

        if (string.IsNullOrWhiteSpace(
                normalizedPath))
        {
            return 0;
        }

        var depth =
            normalizedPath.Count(
                character =>
                    character == separator);

        return Math.Clamp(
            depth,
            0,
            10);
    }

    private static string NormalizeFolderHierarchyPath(
        string folderId,
        char separator)
    {
        if (string.Equals(
                folderId,
                "INBOX",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var inboxPrefix =
            "INBOX" +
            separator;

        if (folderId.StartsWith(
                inboxPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return folderId[
                inboxPrefix.Length..];
        }

        return folderId;
    }

    private void ApplyFolderNavigationBindings()
    {
        foreach (var folder in
                 _viewModel.MailFolders)
        {
            if (FolderListBox
                    .ItemContainerGenerator
                    .ContainerFromItem(
                        folder)
                is not ListBoxItem item)
            {
                continue;
            }

            var folderNameText =
                FindVisualDescendantByName<
                    TextBlock>(
                    item,
                    "FolderNameText");

            if (folderNameText is null)
            {
                continue;
            }

            folderNameText.SetBinding(
                TextBlock.TextProperty,
                new Binding(
                    nameof(
                        MailFolderItemViewModel
                            .NavigationDisplayName)));
        }
    }

    private static T?
        FindVisualDescendantByName<T>(
            DependencyObject root,
            string name)
        where T : FrameworkElement
    {
        var childCount =
            VisualTreeHelper
                .GetChildrenCount(
                    root);

        for (var index = 0;
             index < childCount;
             index++)
        {
            var child =
                VisualTreeHelper
                    .GetChild(
                        root,
                        index);

            if (child is T element &&
                string.Equals(
                    element.Name,
                    name,
                    StringComparison.Ordinal))
            {
                return element;
            }

            var nested =
                FindVisualDescendantByName<T>(
                    child,
                    name);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    /*
     * =========================================================
     * Kontextmenü
     * =========================================================
     */

    private void
        FolderListBox_OnPreviewMouseRightButtonDownForManagement(
            object sender,
            MouseButtonEventArgs e)
    {
        var source =
            e.OriginalSource
            as DependencyObject;

        var item =
            FindVisualParent<ListBoxItem>(
                source);

        if (item?.DataContext
            is not MailFolderItemViewModel folder)
        {
            return;
        }

        FolderListBox.SelectedItem =
            folder;

        SetFolderCreationParentCandidate(
            folder);

        item.ContextMenu =
            CreateFolderContextMenu(
                folder);
    }

    private ContextMenu CreateFolderContextMenu(
        MailFolderItemViewModel folder)
    {
        var menu =
            new ContextMenu();

        var createSubfolderItem =
            new MenuItem
            {
                Header =
                    "Unterordner anlegen"
            };

        createSubfolderItem.Click +=
            async (_, _) =>
            {
                await CreateSubfolderFromUiAsync(
                    folder);
            };

        menu.Items.Add(
            createSubfolderItem);

        if (!IsProtectedFolderForUi(
                folder))
        {
            menu.Items.Add(
                new Separator());

            var deleteItem =
                new MenuItem
                {
                    Header =
                        "Ordner löschen"
                };

            deleteItem.Click +=
                async (_, _) =>
                {
                    await DeleteFolderFromUiAsync(
                        folder);
                };

            menu.Items.Add(
                deleteItem);
        }

        return menu;
    }

    /*
     * =========================================================
     * Ordner erstellen
     * =========================================================
     */

    private async void CreateFolderButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (_folderManagementOperationRunning)
        {
            return;
        }

        var parentFolder =
            GetCurrentFolderCreationParentCandidate();

        if (parentFolder is not null)
        {
            await CreateSubfolderFromUiAsync(
                parentFolder);

            return;
        }

        await CreateRootFolderFromUiAsync();
    }

    private async Task CreateRootFolderFromUiAsync()
    {
        var folderName =
            ShowFolderNameDialog(
                windowTitle:
                    "Neuen Ordner anlegen",

                heading:
                    "Neuer Mailordner",

                description:
                    "Der neue Ordner wird auf der obersten Ebene angelegt.",

                confirmationText:
                    "Anlegen");

        if (string.IsNullOrWhiteSpace(
                folderName))
        {
            return;
        }

        var existingFolder =
            _viewModel
                .MailFolders
                .FirstOrDefault(
                    folder =>
                        folder.HierarchyDepth == 0 &&
                        string.Equals(
                            folder.DisplayName,
                            folderName,
                            StringComparison.OrdinalIgnoreCase));

        if (existingFolder is not null)
        {
            MessageBox.Show(
                this,
                $"Der Ordner „{folderName}“ existiert bereits.",
                "Ordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _viewModel.SelectedFolder =
                existingFolder;

            return;
        }

        _folderManagementOperationRunning =
            true;

        SetFolderManagementControlsEnabled(
            false);

        try
        {
            var folderManagementService =
                CreateFolderManagementService();

            var createdFolderId =
                await folderManagementService
                    .CreateFolderAsync(
                        folderName);

            var createdFolder =
                await ReloadFolderListFromServerAsync(
                    preferredFolderId:
                        createdFolderId,

                    preferredDisplayName:
                        folderName);

            if (createdFolder is null)
            {
                MessageBox.Show(
                    this,
                    "Der Ordner wurde auf dem Mailserver angelegt, " +
                    "konnte aber noch nicht eindeutig in der " +
                    "Ordnerliste gefunden werden.\n\n" +
                    "Bitte aktualisieren Sie das Postfach erneut.",
                    "Ordner angelegt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            SetFolderCreationParentCandidate(
                null);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Ordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                "Der Ordner konnte nicht angelegt werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen " +
                "Sie es erneut.\n\n" +
                "Möglicherweise existiert auf dem Mailserver " +
                "bereits ein Ordner mit diesem Namen.",
                "Ordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _folderManagementOperationRunning =
                false;

            SetFolderManagementControlsEnabled(
                true);
        }
    }

    private async Task CreateSubfolderFromUiAsync(
        MailFolderItemViewModel parentFolder)
    {
        if (_folderManagementOperationRunning)
        {
            return;
        }

        var folderName =
            ShowFolderNameDialog(
                windowTitle:
                    "Unterordner anlegen",

                heading:
                    "Neuer Unterordner",

                description:
                    $"Der neue Ordner wird unter " +
                    $"„{parentFolder.DisplayName}“ angelegt.",

                confirmationText:
                    "Anlegen");

        if (string.IsNullOrWhiteSpace(
                folderName))
        {
            return;
        }

        _folderManagementOperationRunning =
            true;

        SetFolderManagementControlsEnabled(
            false);

        try
        {
            var folderManagementService =
                CreateFolderManagementService();

            var createdFolderId =
                await folderManagementService
                    .CreateSubfolderAsync(
                        parentFolder.FolderId,
                        folderName);

            TryRememberHierarchySeparator(
                parentFolder.FolderId,
                createdFolderId);

            var createdFolder =
                await ReloadFolderListFromServerAsync(
                    preferredFolderId:
                        createdFolderId,

                    preferredDisplayName:
                        folderName);

            if (createdFolder is null)
            {
                MessageBox.Show(
                    this,
                    "Der Unterordner wurde auf dem Mailserver angelegt, " +
                    "konnte aber noch nicht eindeutig in der " +
                    "Ordnerliste gefunden werden.\n\n" +
                    "Bitte aktualisieren Sie das Postfach erneut.",
                    "Unterordner angelegt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            SetFolderCreationParentCandidate(
                null);
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Unterordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Unterordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                "Der Unterordner konnte nicht angelegt werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen " +
                "Sie es erneut.\n\n" +
                "Möglicherweise existiert unter diesem Ordner " +
                "bereits ein Unterordner mit diesem Namen.",
                "Unterordner anlegen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _folderManagementOperationRunning =
                false;

            SetFolderManagementControlsEnabled(
                true);
        }
    }

    private void TryRememberHierarchySeparator(
        string parentFolderId,
        string childFolderId)
    {
        if (!childFolderId.StartsWith(
                parentFolderId,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (childFolderId.Length <=
            parentFolderId.Length)
        {
            return;
        }

        var candidate =
            childFolderId[
                parentFolderId.Length];

        if (candidate is '/' or '.' or '\\')
        {
            _folderHierarchySeparator =
                candidate;
        }
    }

    /*
     * =========================================================
     * Löschen
     * =========================================================
     */

    private async Task DeleteFolderFromUiAsync(
        MailFolderItemViewModel folder)
    {
        if (_folderManagementOperationRunning ||
            IsProtectedFolderForUi(
                folder))
        {
            return;
        }

        if (HasChildFolders(
                folder))
        {
            MessageBox.Show(
                this,
                $"Der Ordner „{folder.DisplayName}“ enthält noch " +
                "Unterordner.\n\n" +
                "Bitte löschen Sie zuerst alle Unterordner und " +
                "versuchen Sie es anschließend erneut.",
                "Ordner kann nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var confirmationText =
            folder.MessageCount > 0
                ? $"Der Ordner „{folder.DisplayName}“ enthält " +
                  $"{folder.MessageCount} Nachricht(en).\n\n" +
                  "Beim Löschen werden der Ordner und alle darin " +
                  "enthaltenen Nachrichten endgültig vom Mailserver " +
                  "entfernt.\n\n" +
                  "Dieser Vorgang kann nicht rückgängig gemacht werden.\n\n" +
                  "Ordner wirklich löschen?"
                : $"Möchten Sie den Ordner „{folder.DisplayName}“ " +
                  "wirklich löschen?\n\n" +
                  "Der Ordner wird vom Mailserver entfernt. " +
                  "Dieser Vorgang kann nicht rückgängig gemacht werden.";

        var confirmation =
            MessageBox.Show(
                this,
                confirmationText,
                "Ordner löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

        if (confirmation !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _folderManagementOperationRunning =
            true;

        SetFolderManagementControlsEnabled(
            false);

        try
        {
            var folderManagementService =
                CreateFolderManagementService();

            await folderManagementService
                .DeleteFolderAsync(
                    folder.FolderId);

            SetFolderCreationParentCandidate(
                null);

            await ReloadFolderListFromServerAsync(
                preferredFolderId:
                    "INBOX",

                preferredDisplayName:
                    "Posteingang");
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Ordner kann nicht gelöscht werden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception)
        {
            MessageBox.Show(
                this,
                "Der Ordner konnte nicht gelöscht werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen " +
                "Sie es erneut.",
                "Ordner löschen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _folderManagementOperationRunning =
                false;

            SetFolderManagementControlsEnabled(
                true);
        }
    }

    private bool HasChildFolders(
        MailFolderItemViewModel folder)
    {
        var separator =
            _folderHierarchySeparator;

        if (!separator.HasValue)
        {
            separator =
                DetectFolderHierarchySeparator(
                    _viewModel
                        .MailFolders
                        .ToList());
        }

        if (!separator.HasValue)
        {
            return false;
        }

        var childPrefix =
            folder.FolderId +
            separator.Value;

        return _viewModel
            .MailFolders
            .Any(
                candidate =>
                    !ReferenceEquals(
                        candidate,
                        folder) &&
                    candidate
                        .FolderId
                        .StartsWith(
                            childPrefix,
                            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsProtectedFolderForUi(
        MailFolderItemViewModel folder)
    {
        if (string.Equals(
                folder.FolderId,
                "INBOX",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name =
            folder.DisplayName
                .Trim()
                .ToLowerInvariant();

        return name switch
        {
            "posteingang" => true,
            "inbox" => true,

            "gesendet" => true,
            "sent" => true,
            "sent items" => true,
            "sent messages" => true,

            "entwürfe" => true,
            "entwuerfe" => true,
            "drafts" => true,

            "spam" => true,
            "junk" => true,

            "papierkorb" => true,
            "trash" => true,
            "deleted items" => true,
            "deleted messages" => true,
            "gelöschte elemente" => true,
            "geloeschte elemente" => true,

            "archiv" => true,
            "archive" => true,

            "wichtig" => true,
            "important" => true,

            "markiert" => true,
            "flagged" => true,

            _ => false
        };
    }

    /*
     * =========================================================
     * Gemeinsame Hilfsfunktionen
     * =========================================================
     */

    private IMailFolderManagementService
        CreateFolderManagementService()
    {
        return ActivatorUtilities
            .CreateInstance<
                MailKitFolderManagementService>(
                _serviceProvider);
    }

    private async Task<MailFolderItemViewModel?>
        ReloadFolderListFromServerAsync(
            string? preferredFolderId,
            string? preferredDisplayName)
    {
        var mailDataSource =
            _serviceProvider
                .GetRequiredService<
                    IMailDataSource>();

        var serverFolders =
            await mailDataSource
                .GetFoldersAsync();

        _viewModel.SelectedFolder =
            null;

        _viewModel.MailFolders.Clear();

        MailFolderItemViewModel?
            preferredFolder = null;

        foreach (var folder in
                 serverFolders)
        {
            var folderViewModel =
                new MailFolderItemViewModel(
                    folderId:
                        folder.FolderId,

                    displayName:
                        folder.DisplayName,

                    headerSubtitle:
                        folder.HeaderSubtitle,

                    unreadCount:
                        folder.UnreadCount,

                    hasSeparatorAfter:
                        folder.HasSeparatorAfter,

                    messageCount:
                        folder.MessageCount);

            _viewModel.MailFolders.Add(
                folderViewModel);

            if (!string.IsNullOrWhiteSpace(
                    preferredFolderId) &&
                string.Equals(
                    folder.FolderId,
                    preferredFolderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                preferredFolder =
                    folderViewModel;
            }
        }

        UpdateFolderHierarchy();

        if (preferredFolder is null &&
            !string.IsNullOrWhiteSpace(
                preferredDisplayName))
        {
            preferredFolder =
                _viewModel
                    .MailFolders
                    .FirstOrDefault(
                        folder =>
                            string.Equals(
                                folder.DisplayName,
                                preferredDisplayName,
                                StringComparison.OrdinalIgnoreCase));
        }

        preferredFolder ??=
            _viewModel
                .MailFolders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            "INBOX",
                            StringComparison.OrdinalIgnoreCase));

        preferredFolder ??=
            _viewModel
                .MailFolders
                .FirstOrDefault();

        _viewModel.SelectedFolder =
            preferredFolder;

        return preferredFolder;
    }

    private void SetFolderManagementControlsEnabled(
        bool enabled)
    {
        if (_createFolderButton is not null)
        {
            _createFolderButton.IsEnabled =
                enabled;
        }
    }

    private static T?
        FindVisualParent<T>(
            DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current =
                VisualTreeHelper.GetParent(
                    current);
        }

        return null;
    }

    private string? ShowFolderNameDialog(
        string windowTitle,
        string heading,
        string description,
        string confirmationText)
    {
        var dialog =
            new Window
            {
                Owner =
                    this,

                Title =
                    windowTitle,

                Width =
                    420,

                SizeToContent =
                    SizeToContent.Height,

                MinHeight =
                    235,

                ResizeMode =
                    ResizeMode.NoResize,

                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,

                ShowInTaskbar =
                    false,

                Background =
                    TryFindResource(
                        "Surface.Background")
                    as Brush
                    ?? Brushes.White,

                Foreground =
                    TryFindResource(
                        "Text.Primary")
                    as Brush
                    ?? Brushes.Black,

                FontFamily =
                    TryFindResource(
                        "Typography.FontFamily")
                    as FontFamily
                    ?? new FontFamily(
                        "Segoe UI")
            };

        var root =
            new Grid
            {
                Margin =
                    new Thickness(
                        24)
            };

        for (var index = 0;
             index < 4;
             index++)
        {
            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height =
                        GridLength.Auto
                });
        }

        var title =
            new TextBlock
            {
                Text =
                    heading,

                FontSize =
                    18,

                FontWeight =
                    FontWeights.SemiBold
            };

        Grid.SetRow(
            title,
            0);

        root.Children.Add(
            title);

        var descriptionText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        14),

                Text =
                    description,

                FontSize =
                    12,

                TextWrapping =
                    TextWrapping.Wrap
            };

        descriptionText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetRow(
            descriptionText,
            1);

        root.Children.Add(
            descriptionText);

        var input =
            new TextBox
            {
                Height =
                    38,

                Padding =
                    new Thickness(
                        10,
                        7,
                        10,
                        7),

                FontSize =
                    13,

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                Background =
                    TryFindResource(
                        "Surface.Card")
                    as Brush
                    ?? Brushes.White,

                Foreground =
                    TryFindResource(
                        "Text.Primary")
                    as Brush
                    ?? Brushes.Black,

                BorderBrush =
                    TryFindResource(
                        "Border.Default")
                    as Brush
                    ?? Brushes.Gray,

                BorderThickness =
                    new Thickness(1)
            };

        Grid.SetRow(
            input,
            2);

        root.Children.Add(
            input);

        var buttonPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        18,
                        0,
                        0),

                Orientation =
                    Orientation.Horizontal,

                HorizontalAlignment =
                    HorizontalAlignment.Right
            };

        var cancelButton =
            new Button
            {
                Content =
                    "Abbrechen",

                Width =
                    100,

                Height =
                    36,

                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),

                IsCancel =
                    true,

                Cursor =
                    Cursors.Hand,

                Background =
                    TryFindResource(
                        "Surface.Card")
                    as Brush
                    ?? Brushes.Transparent,

                Foreground =
                    TryFindResource(
                        "Text.Primary")
                    as Brush
                    ?? Brushes.Black,

                BorderBrush =
                    TryFindResource(
                        "Border.Default")
                    as Brush
                    ?? Brushes.Gray,

                BorderThickness =
                    new Thickness(1)
            };

        buttonPanel.Children.Add(
            cancelButton);

        var confirmButton =
            new Button
            {
                Content =
                    confirmationText,

                Width =
                    100,

                Height =
                    36,

                IsDefault =
                    true,

                Cursor =
                    Cursors.Hand
            };

        confirmButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        confirmButton.Click +=
            (_, _) =>
            {
                var normalizedName =
                    input.Text.Trim();

                if (string.IsNullOrWhiteSpace(
                        normalizedName))
                {
                    MessageBox.Show(
                        dialog,
                        "Bitte geben Sie einen Ordnernamen ein.",
                        windowTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    input.Focus();

                    return;
                }

                dialog.Tag =
                    normalizedName;

                dialog.DialogResult =
                    true;
            };

        buttonPanel.Children.Add(
            confirmButton);

        Grid.SetRow(
            buttonPanel,
            3);

        root.Children.Add(
            buttonPanel);

        dialog.Content =
            root;

        dialog.ContentRendered +=
            (_, _) =>
            {
                input.Focus();

                Keyboard.Focus(
                    input);
            };

        var result =
            dialog.ShowDialog();

        if (result != true)
        {
            return null;
        }

        return dialog.Tag
            as string;
    }

    private void MainWindowFolderManagement_OnClosed(
        object? sender,
        EventArgs e)
    {
        _viewModel
            .MailFolders
            .CollectionChanged -=
                MailFolders_OnCollectionChangedForHierarchy;

        FolderListBox
            .ItemContainerGenerator
            .StatusChanged -=
                FolderListBoxItemContainerGenerator_OnStatusChanged;

        FolderListBox.PreviewMouseLeftButtonDown -=
            FolderListBox_OnPreviewMouseLeftButtonDownForCreationTarget;

        FolderListBox.PreviewMouseRightButtonDown -=
            FolderListBox_OnPreviewMouseRightButtonDownForManagement;

        PreviewMouseLeftButtonDown -=
            MainWindow_OnPreviewMouseLeftButtonDownForFolderCreationTarget;

        Closed -=
            MainWindowFolderManagement_OnClosed;
    }
}