using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    private bool _importanceSelectorInitialized;

    protected override void OnSourceInitialized(
        EventArgs e)
    {
        base.OnSourceInitialized(
            e);

        if (_importanceSelectorInitialized)
        {
            return;
        }

        _importanceSelectorInitialized =
            true;

        AddImportanceSelector();
    }

    private void AddImportanceSelector()
    {
        if (Content is not Grid rootGrid)
        {
            return;
        }

        /*
         * Grid.Row 3 enthält aktuell den Bereich
         *
         * Datei anhängen | Anhang-Zusammenfassung
         *
         * Dort ergänzen wir rechts eine dritte Spalte
         * für die Wichtigkeit.
         *
         * Dadurch bleibt die Funktion unabhängig vom
         * Rich-Text-Editor und funktioniert auch im
         * Plaintext-Fallback.
         */
        var attachmentBorder =
            rootGrid
                .Children
                .OfType<Border>()
                .FirstOrDefault(
                    border =>
                        Grid.GetRow(
                            border) == 3);

        if (attachmentBorder?.Child
            is not StackPanel attachmentPanel)
        {
            return;
        }

        var attachmentHeaderGrid =
            attachmentPanel
                .Children
                .OfType<Grid>()
                .FirstOrDefault();

        if (attachmentHeaderGrid is null)
        {
            return;
        }

        attachmentHeaderGrid
            .ColumnDefinitions
            .Add(
                new ColumnDefinition
                {
                    Width =
                        GridLength.Auto
                });

        var importancePanel =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal,

                VerticalAlignment =
                    VerticalAlignment.Center,

                Margin =
                    new Thickness(
                        16,
                        0,
                        0,
                        0)
            };

        Grid.SetColumn(
            importancePanel,
            2);

        var label =
            new TextBlock
            {
                Text =
                    "Wichtigkeit",

                Margin =
                    new Thickness(
                        0,
                        0,
                        8,
                        0),

                FontSize =
                    12,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        importancePanel
            .Children
            .Add(
                label);

        var selector =
            new ComboBox
            {
                Width =
                    120,

                Height =
                    34,

                Padding =
                    new Thickness(
                        8,
                        3,
                        8,
                        3),

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                ToolTip =
                    "Wichtigkeit / Priorität der E-Mail",

                ItemsSource =
                    new[]
                    {
                        new MailImportanceOption(
                            "Niedrig",
                            MailImportanceLevel.Low),

                        new MailImportanceOption(
                            "Normal",
                            MailImportanceLevel.Normal),

                        new MailImportanceOption(
                            "Hoch",
                            MailImportanceLevel.High)
                    },

                DisplayMemberPath =
                    nameof(
                        MailImportanceOption.DisplayName),

                SelectedValuePath =
                    nameof(
                        MailImportanceOption.Value)
            };

        selector.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Card");

        selector.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        selector.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        selector.SetBinding(
            ComboBox.SelectedValueProperty,
            new Binding(
                nameof(
                    ComposeMailViewModel.Importance))
            {
                Mode =
                    BindingMode.TwoWay,

                UpdateSourceTrigger =
                    UpdateSourceTrigger.PropertyChanged
            });

        /*
         * Während Versand oder Speicherung soll die
         * Wichtigkeit nicht mehr verändert werden.
         *
         * CanModifyAttachments entspricht aktuell
         * genau diesem Zustand.
         */
        selector.SetBinding(
            UIElement.IsEnabledProperty,
            new Binding(
                nameof(
                    ComposeMailViewModel
                        .CanModifyAttachments)));

        importancePanel
            .Children
            .Add(
                selector);

        attachmentHeaderGrid
            .Children
            .Add(
                importancePanel);
    }

    private sealed record MailImportanceOption(
        string DisplayName,
        MailImportanceLevel Value);
}