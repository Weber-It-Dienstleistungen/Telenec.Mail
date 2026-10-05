using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MainWindow
{
    [ModuleInitializer]
    internal static void InitializeFolderRenameUi()
    {
        /*
         * Die bestehende Ordnerverwaltung erstellt ihr
         * Kontextmenü bereits beim rechten Mausklick.
         *
         * ContextMenuOpening kommt danach. Dadurch können wir
         * das vorhandene Menü gezielt um "Ordner umbenennen"
         * ergänzen, ohne MainWindow.FolderManagement.cs
         * verändern zu müssen.
         */
        EventManager.RegisterClassHandler(
            typeof(ListBoxItem),
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(
                FolderListBoxItem_OnContextMenuOpeningForRename),
            handledEventsToo:
                true);
    }

    private static void
        FolderListBoxItem_OnContextMenuOpeningForRename(
            object sender,
            ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem item ||
            item.DataContext
                is not MailFolderItemViewModel folder)
        {
            return;
        }

        var listBox =
            FindVisualParent<ListBox>(
                item);

        if (listBox is null ||
            !string.Equals(
                listBox.Name,
                "FolderListBox",
                StringComparison.Ordinal))
        {
            return;
        }

        if (Window.GetWindow(
                item)
            is not MainWindow mainWindow)
        {
            return;
        }

        mainWindow
            .EnsureFolderRenameMenuItem(
                item,
                folder);
    }

    private void EnsureFolderRenameMenuItem(
        ListBoxItem item,
        MailFolderItemViewModel folder)
    {
        /*
         * Systemordner wie Posteingang, Gesendet,
         * Entwürfe, Papierkorb usw. bleiben geschützt.
         */
        if (IsProtectedFolderForUi(
                folder))
        {
            return;
        }

        var menu =
            item.ContextMenu;

        if (menu is null)
        {
            return;
        }

        /*
         * ContextMenuOpening kann mehrmals auftreten.
         * Deshalb niemals einen zweiten Rename-Eintrag
         * hinzufügen.
         */
        if (menu.Items
            .OfType<MenuItem>()
            .Any(
                menuItem =>
                    string.Equals(
                        menuItem.Tag
                            as string,
                        "RenameFolder",
                        StringComparison.Ordinal)))
        {
            return;
        }

        var renameItem =
            new MenuItem
            {
                Header =
                    "Ordner umbenennen",

                Tag =
                    "RenameFolder"
            };

        renameItem.Click +=
            async (_, _) =>
            {
                await RenameFolderFromUiAsync(
                    folder);
            };

        /*
         * Bestehendes Menü:
         *
         * Unterordner anlegen
         * -------------------
         * Ordner löschen
         *
         * Danach:
         *
         * Unterordner anlegen
         * Ordner umbenennen
         * -------------------
         * Ordner löschen
         */
        var insertIndex =
            Math.Min(
                1,
                menu.Items.Count);

        menu.Items.Insert(
            insertIndex,
            renameItem);
    }

    private async Task RenameFolderFromUiAsync(
        MailFolderItemViewModel folder)
    {
        if (_folderManagementOperationRunning ||
            IsProtectedFolderForUi(
                folder))
        {
            return;
        }

        var newFolderName =
            ShowRenameFolderDialog(
                folder.DisplayName);

        if (string.IsNullOrWhiteSpace(
                newFolderName))
        {
            return;
        }

        newFolderName =
            newFolderName.Trim();

        /*
         * Exakt gleicher Name:
         * kein Serverzugriff erforderlich.
         */
        if (string.Equals(
                folder.DisplayName,
                newFolderName,
                StringComparison.Ordinal))
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

            var renamedFolderId =
                await folderManagementService
                    .RenameFolderAsync(
                        folder.FolderId,
                        newFolderName);

            /*
             * Wird ein Elternordner umbenannt, ändern sich
             * je nach IMAP-Server auch die FullNames seiner
             * Unterordner.
             *
             * Alte IDs im Ein-/Ausklappzustand wären danach
             * wertlos. Deshalb einmal sauber zurücksetzen.
             */
            _collapsedFolderIds.Clear();

            SetFolderCreationParentCandidate(
                null);

            var renamedFolder =
                await ReloadFolderListFromServerAsync(
                    preferredFolderId:
                        renamedFolderId,

                    preferredDisplayName:
                        newFolderName);

            if (renamedFolder is null)
            {
                MessageBox.Show(
                    this,
                    "Der Ordner wurde auf dem Mailserver umbenannt, " +
                    "konnte anschließend aber nicht eindeutig in der " +
                    "Ordnerliste gefunden werden.\n\n" +
                    "Bitte aktualisieren Sie das Postfach erneut.",
                    "Ordner umbenannt",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Ordner umbenennen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Ordner umbenennen",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            MessageBox.Show(
                this,
                "Der Ordner konnte nicht umbenannt werden.\n\n" +
                "Bitte prüfen Sie die Verbindung und versuchen Sie " +
                "es erneut.\n\n" +
                "Möglicherweise existiert auf dem Mailserver bereits " +
                "ein Ordner mit diesem Namen.",
                "Ordner umbenennen",
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

    private string? ShowRenameFolderDialog(
        string currentFolderName)
    {
        var dialog =
            new Window
            {
                Owner =
                    this,

                Title =
                    "Ordner umbenennen",

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
                    new Thickness(24)
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

        var heading =
            new TextBlock
            {
                Text =
                    "Ordner umbenennen",

                FontSize =
                    18,

                FontWeight =
                    FontWeights.SemiBold
            };

        Grid.SetRow(
            heading,
            0);

        root.Children.Add(
            heading);

        var description =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        14),

                Text =
                    $"Geben Sie einen neuen Namen für " +
                    $"„{currentFolderName}“ ein.",

                FontSize =
                    12,

                TextWrapping =
                    TextWrapping.Wrap
            };

        description.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetRow(
            description,
            1);

        root.Children.Add(
            description);

        var input =
            new TextBox
            {
                Height =
                    36,

                Padding =
                    new Thickness(
                        10,
                        7,
                        10,
                        7),

                FontSize =
                    13,

                Text =
                    currentFolderName,

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

        var renameButton =
            new Button
            {
                Content =
                    "Umbenennen",

                Width =
                    110,

                Height =
                    36,

                IsDefault =
                    true,

                Cursor =
                    Cursors.Hand
            };

        renameButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        renameButton.Click +=
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
                        "Ordner umbenennen",
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
            renameButton);

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

                /*
                 * Der vorhandene Name ist direkt markiert.
                 * Der Benutzer kann also sofort lostippen.
                 */
                input.SelectAll();
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
}