using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Telenec.Mail.App;

internal static class SelectableMailTextBootstrapper
{
    private static readonly HashSet<string>
        SelectablePreviewBindings =
            new(
                StringComparer.Ordinal)
            {
                "SelectedMessage.Subject",
                "SelectedMessage.Sender",
                "SelectedMessage.AddressLine",
                "SelectedMessage.DisplayDateTime"
            };

    [ModuleInitializer]
    internal static void Initialize()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MainWindow_OnLoaded),
            handledEventsToo:
                true);
    }

    private static void MainWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
        {
            return;
        }

        /*
         * Wir warten bewusst, bis das komplette MainWindow
         * einschließlich Mailvorschau fertig aufgebaut ist.
         *
         * Dadurch arbeiten wir nicht mehr mitten in einzelnen
         * TextBlock-Loaded-Ereignissen.
         */
        _ =
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(
                    () =>
                    {
                        MakePreviewHeaderSelectable(
                            window);
                    }));
    }

    private static void MakePreviewHeaderSelectable(
        MainWindow window)
    {
        var textBlocks =
            FindVisualChildren<TextBlock>(
                window)
            .ToArray();

        foreach (var textBlock in
                 textBlocks)
        {
            var bindingPath =
                GetBindingPath(
                    textBlock);

            if (string.IsNullOrWhiteSpace(
                    bindingPath) ||
                !SelectablePreviewBindings.Contains(
                    bindingPath))
            {
                continue;
            }

            ReplaceWithSelectableTextBox(
                textBlock,
                bindingPath);
        }
    }

    private static string? GetBindingPath(
        TextBlock textBlock)
    {
        var bindingExpression =
            BindingOperations
                .GetBindingExpression(
                    textBlock,
                    TextBlock.TextProperty);

        return bindingExpression?
            .ParentBinding
            .Path?
            .Path;
    }

    private static void ReplaceWithSelectableTextBox(
        TextBlock textBlock,
        string bindingPath)
    {
        if (VisualTreeHelper.GetParent(
                textBlock)
            is not Panel parent)
        {
            return;
        }

        var index =
            parent.Children.IndexOf(
                textBlock);

        if (index < 0)
        {
            return;
        }

        var textBox =
            new TextBox
            {
                IsReadOnly =
                    true,

                IsReadOnlyCaretVisible =
                    true,

                IsTabStop =
                    false,

                AcceptsReturn =
                    true,

                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(0),

                Background =
                    Brushes.Transparent,

                Cursor =
                    Cursors.IBeam,

                Margin =
                    textBlock.Margin,

                HorizontalAlignment =
                    textBlock.HorizontalAlignment,

                VerticalAlignment =
                    textBlock.VerticalAlignment,

                FontFamily =
                    textBlock.FontFamily,

                FontSize =
                    textBlock.FontSize,

                FontStyle =
                    textBlock.FontStyle,

                FontStretch =
                    textBlock.FontStretch,

                FontWeight =
                    textBlock.FontWeight,

                Foreground =
                    textBlock.Foreground,

                TextAlignment =
                    textBlock.TextAlignment,

                TextWrapping =
                    textBlock.TextWrapping,

                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Disabled,

                ToolTip =
                    textBlock.ToolTip
            };

        /*
         * Die Kopfzeile ist reine Anzeige.
         * Deshalb zwingend OneWay.
         */
        BindingOperations.SetBinding(
            textBox,
            TextBox.TextProperty,
            new Binding(
                bindingPath)
            {
                Mode =
                    BindingMode.OneWay
            });

        /*
         * Position innerhalb eines Grid übernehmen.
         * Bei StackPanel-Eltern sind diese Werte unschädlich.
         */
        Grid.SetRow(
            textBox,
            Grid.GetRow(
                textBlock));

        Grid.SetRowSpan(
            textBox,
            Grid.GetRowSpan(
                textBlock));

        Grid.SetColumn(
            textBox,
            Grid.GetColumn(
                textBlock));

        Grid.SetColumnSpan(
            textBox,
            Grid.GetColumnSpan(
                textBlock));

        Panel.SetZIndex(
            textBox,
            Panel.GetZIndex(
                textBlock));

        parent.Children.RemoveAt(
            index);

        parent.Children.Insert(
            index,
            textBox);
    }

    private static IEnumerable<T>
        FindVisualChildren<T>(
            DependencyObject root)
        where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

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

            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in
                     FindVisualChildren<T>(
                         child))
            {
                yield return descendant;
            }
        }
    }
}