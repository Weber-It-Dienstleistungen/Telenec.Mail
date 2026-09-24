using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App;

public partial class SettingsWindow
{
    private readonly MailRuleStore
        _mailRuleStore;

    private readonly IMailDataSource
        _ruleMailDataSource;

    private Grid?
        _ruleSettingsPanel;

    private FrameworkElement?
        _ruleSettingsControls;

    private TextBlock?
        _ruleAccountText;

    private ListBox?
        _ruleListBox;

    private TextBlock?
        _ruleEmptyText;

    private TextBlock?
        _ruleEditorTitle;

    private TextBox?
        _ruleNameTextBox;

    private CheckBox?
        _ruleEnabledCheckBox;

    private ComboBox?
        _ruleConditionFieldComboBox;

    private TextBox?
        _ruleConditionValueTextBox;

    private ComboBox?
        _ruleTargetFolderComboBox;

    private Button?
        _deleteRuleButton;

    private TextBlock?
        _ruleStatusText;

    private bool
        _ruleSettingsLoaded;

    private bool
        _ruleSettingsLoading;

    private bool
        _isSavingRules;

    private bool
        _suppressRuleSelectionChanged;

    private Guid?
        _ruleAccountId;

    private Guid?
        _editingRuleId;

    private List<MailRuleDefinition>
        _mailRules =
            new();

    private List<RuleFolderOption>
        _ruleFolders =
            new();

    private void InitializeRuleSettingsUi()
    {
        if (_ruleSettingsPanel is not null)
        {
            return;
        }

        if (GeneralSettingsPanel.Parent
            is not Grid contentGrid)
        {
            return;
        }

        var panel =
            CreateRuleSettingsPanel();

        Grid.SetRow(
            panel,
            0);

        panel.Visibility =
            Visibility.Collapsed;

        contentGrid.Children.Add(
            panel);

        _ruleSettingsPanel =
            panel;

        var navigationItem =
            CreateRuleNavigationItem();

        SettingsNavigation.Items.Add(
            navigationItem);

        SettingsNavigation.SelectionChanged +=
            RuleSettingsNavigation_OnSelectionChanged;
    }

    private ListBoxItem CreateRuleNavigationItem()
    {
        var content =
            new StackPanel
            {
                Orientation =
                    Orientation.Horizontal
            };

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "\uE71C",

                Width =
                    24,

                FontFamily =
                    new System.Windows.Media.FontFamily(
                        "Segoe MDL2 Assets"),

                FontSize =
                    15,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

        content.Children.Add(
            new TextBlock
            {
                Text =
                    "Regeln",

                FontSize =
                    13,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            });

        return new ListBoxItem
        {
            Content =
                content
        };
    }

    private Grid CreateRuleSettingsPanel()
    {
        var panel =
            new Grid();

        var scrollViewer =
            new ScrollViewer
            {
                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Auto,

                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled
            };

        var root =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        0,
                        0,
                        12,
                        0)
            };

        var title =
            new TextBlock
            {
                Text =
                    "Regeln",

                FontSize =
                    26,

                FontWeight =
                    FontWeights.SemiBold
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        root.Children.Add(
            title);

        var subtitle =
            new TextBlock
            {
                Text =
                    "E-Mails automatisch anhand von Absender, Betreff oder Empfänger organisieren.",

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        6),

                FontSize =
                    13,

                TextWrapping =
                    TextWrapping.Wrap
            };

        subtitle.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        root.Children.Add(
            subtitle);

        var accountText =
            new TextBlock
            {
                Text =
                    "Aktuelles Konto wird geladen …",

                Margin =
                    new Thickness(
                        0,
                        0,
                        0,
                        20),

                FontSize =
                    12,

                TextWrapping =
                    TextWrapping.Wrap
            };

        accountText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        _ruleAccountText =
            accountText;

        root.Children.Add(
            accountText);

        var controls =
            new StackPanel
            {
                IsEnabled =
                    false
            };

        _ruleSettingsControls =
            controls;

        controls.Children.Add(
            CreateRuleListCard());

        controls.Children.Add(
            CreateRuleEditorCard());

        root.Children.Add(
            controls);

        var statusText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0),

                FontSize =
                    12,

                TextWrapping =
                    TextWrapping.Wrap
            };

        statusText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        _ruleStatusText =
            statusText;

        root.Children.Add(
            statusText);

        scrollViewer.Content =
            root;

        panel.Children.Add(
            scrollViewer);

        return panel;
    }

    private Border CreateRuleListCard()
    {
        var card =
            CreateRuleCard();

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
                    "Gespeicherte Regeln",

                FontSize =
                    14,

                FontWeight =
                    FontWeights.SemiBold,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        Grid.SetColumn(
            title,
            0);

        header.Children.Add(
            title);

        var newRuleButton =
            new Button
            {
                Width =
                    110,

                Height =
                    34,

                Content =
                    "Neue Regel",

                Cursor =
                    Cursors.Hand
            };

        newRuleButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        newRuleButton.Click +=
            NewRuleButton_OnClick;

        Grid.SetColumn(
            newRuleButton,
            1);

        header.Children.Add(
            newRuleButton);

        content.Children.Add(
            header);

        var emptyText =
            new TextBlock
            {
                Margin =
                    new Thickness(
                        0,
                        20,
                        0,
                        0),

                Text =
                    "Noch keine Regeln angelegt.",

                FontSize =
                    12,

                Visibility =
                    Visibility.Collapsed
            };

        emptyText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        _ruleEmptyText =
            emptyText;

        content.Children.Add(
            emptyText);

        var listBox =
            new ListBox
            {
                Height =
                    170,

                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0),

                BorderThickness =
                    new Thickness(
                        1),

                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch
            };

        listBox.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Background");

        listBox.SetResourceReference(
            Control.BorderBrushProperty,
            "Border.Default");

        listBox.SelectionChanged +=
            RuleListBox_OnSelectionChanged;

        _ruleListBox =
            listBox;

        content.Children.Add(
            listBox);

        card.Child =
            content;

        return card;
    }

    private Border CreateRuleEditorCard()
    {
        var card =
            CreateRuleCard();

        card.Margin =
            new Thickness(
                0,
                16,
                0,
                0);

        var content =
            new StackPanel();

        var editorTitle =
            new TextBlock
            {
                Text =
                    "Neue Regel",

                FontSize =
                    14,

                FontWeight =
                    FontWeights.SemiBold
            };

        editorTitle.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        _ruleEditorTitle =
            editorTitle;

        content.Children.Add(
            editorTitle);

        content.Children.Add(
            CreateRuleFieldLabel(
                "Name der Regel",
                topMargin:
                    18));

        var nameTextBox =
            new TextBox
            {
                Height =
                    34,

                MaxLength =
                    120,

                Padding =
                    new Thickness(
                        8,
                        4,
                        8,
                        4),

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

        _ruleNameTextBox =
            nameTextBox;

        content.Children.Add(
            nameTextBox);

        var enabledCheckBox =
            new CheckBox
            {
                Margin =
                    new Thickness(
                        0,
                        16,
                        0,
                        0),

                Content =
                    "Regel aktiv",

                IsChecked =
                    true,

                FontSize =
                    13,

                FontWeight =
                    FontWeights.SemiBold
            };

        enabledCheckBox.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        _ruleEnabledCheckBox =
            enabledCheckBox;

        content.Children.Add(
            enabledCheckBox);

        content.Children.Add(
            CreateRuleFieldLabel(
                "Bedingung",
                topMargin:
                    20));

        var conditionGrid =
            new Grid();

        conditionGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        155)
            });

        conditionGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        conditionGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var conditionFieldComboBox =
            new ComboBox
            {
                Height =
                    34,

                Padding =
                    new Thickness(
                        7,
                        3,
                        7,
                        3),

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

        conditionFieldComboBox.Items.Add(
            CreateConditionComboBoxItem(
                "Absender",
                MailRuleConditionField.Sender));

        conditionFieldComboBox.Items.Add(
            CreateConditionComboBoxItem(
                "Betreff",
                MailRuleConditionField.Subject));

        conditionFieldComboBox.Items.Add(
            CreateConditionComboBoxItem(
                "Empfänger",
                MailRuleConditionField.Recipient));

        conditionFieldComboBox.SelectedIndex =
            0;

        _ruleConditionFieldComboBox =
            conditionFieldComboBox;

        Grid.SetColumn(
            conditionFieldComboBox,
            0);

        conditionGrid.Children.Add(
            conditionFieldComboBox);

        var containsText =
            new TextBlock
            {
                Text =
                    "enthält",

                Margin =
                    new Thickness(
                        12,
                        0,
                        12,
                        0),

                FontSize =
                    12,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        containsText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetColumn(
            containsText,
            1);

        conditionGrid.Children.Add(
            containsText);

        var conditionValueTextBox =
            new TextBox
            {
                Height =
                    34,

                MaxLength =
                    500,

                Padding =
                    new Thickness(
                        8,
                        4,
                        8,
                        4),

                VerticalContentAlignment =
                    VerticalAlignment.Center,

                ToolTip =
                    "Text, der im ausgewählten Feld enthalten sein muss"
            };

        _ruleConditionValueTextBox =
            conditionValueTextBox;

        Grid.SetColumn(
            conditionValueTextBox,
            2);

        conditionGrid.Children.Add(
            conditionValueTextBox);

        content.Children.Add(
            conditionGrid);

        content.Children.Add(
            CreateRuleFieldLabel(
                "Aktion",
                topMargin:
                    20));

        var actionGrid =
            new Grid();

        actionGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        actionGrid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        var actionText =
            new TextBlock
            {
                Text =
                    "In Ordner verschieben:",

                Margin =
                    new Thickness(
                        0,
                        0,
                        12,
                        0),

                FontSize =
                    12,

                VerticalAlignment =
                    VerticalAlignment.Center
            };

        actionText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        Grid.SetColumn(
            actionText,
            0);

        actionGrid.Children.Add(
            actionText);

        var targetFolderComboBox =
            new ComboBox
            {
                Height =
                    34,

                Padding =
                    new Thickness(
                        7,
                        3,
                        7,
                        3),

                DisplayMemberPath =
                    nameof(
                        RuleFolderOption.DisplayName),

                SelectedValuePath =
                    nameof(
                        RuleFolderOption.FolderId),

                IsTextSearchEnabled =
                    true,

                VerticalContentAlignment =
                    VerticalAlignment.Center
            };

        _ruleTargetFolderComboBox =
            targetFolderComboBox;

        Grid.SetColumn(
            targetFolderComboBox,
            1);

        actionGrid.Children.Add(
            targetFolderComboBox);

        content.Children.Add(
            actionGrid);

        var informationBorder =
            new Border
            {
                Margin =
                    new Thickness(
                        0,
                        20,
                        0,
                        0),

                Padding =
                    new Thickness(
                        12),

                CornerRadius =
                    new CornerRadius(
                        6),

                BorderThickness =
                    new Thickness(
                        1)
            };

        informationBorder.SetResourceReference(
            Border.BackgroundProperty,
            "Brand.PrimaryLight");

        informationBorder.SetResourceReference(
            Border.BorderBrushProperty,
            "Border.Default");

        var informationText =
            new TextBlock
            {
                Text =
                    "Die Regelverwaltung ist bereits vollständig persistent. " +
                    "Die automatische Anwendung auf eingehende E-Mails folgt im nächsten Entwicklungsschritt.",

                FontSize =
                    11,

                TextWrapping =
                    TextWrapping.Wrap
            };

        informationText.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        informationBorder.Child =
            informationText;

        content.Children.Add(
            informationBorder);

        var actionButtons =
            new Grid
            {
                Margin =
                    new Thickness(
                        0,
                        20,
                        0,
                        0)
            };

        actionButtons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        actionButtons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        actionButtons.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    GridLength.Auto
            });

        var deleteButton =
            new Button
            {
                Width =
                    110,

                Height =
                    38,

                Content =
                    "Löschen",

                IsEnabled =
                    false,

                Cursor =
                    Cursors.Hand
            };

        deleteButton.Click +=
            DeleteRuleButton_OnClick;

        _deleteRuleButton =
            deleteButton;

        Grid.SetColumn(
            deleteButton,
            0);

        actionButtons.Children.Add(
            deleteButton);

        var saveButton =
            new Button
            {
                Width =
                    120,

                Height =
                    38,

                Content =
                    "Speichern",

                Cursor =
                    Cursors.Hand
            };

        saveButton.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        saveButton.Click +=
            SaveRuleButton_OnClick;

        Grid.SetColumn(
            saveButton,
            2);

        actionButtons.Children.Add(
            saveButton);

        content.Children.Add(
            actionButtons);

        card.Child =
            content;

        return card;
    }

    private Border CreateRuleCard()
    {
        var card =
            new Border
            {
                Padding =
                    new Thickness(
                        22),

                BorderThickness =
                    new Thickness(
                        1),

                CornerRadius =
                    new CornerRadius(
                        8)
            };

        card.SetResourceReference(
            Border.BackgroundProperty,
            "Surface.Card");

        card.SetResourceReference(
            Border.BorderBrushProperty,
            "Border.Default");

        return card;
    }

    private TextBlock CreateRuleFieldLabel(
        string text,
        double topMargin)
    {
        var label =
            new TextBlock
            {
                Text =
                    text,

                Margin =
                    new Thickness(
                        0,
                        topMargin,
                        0,
                        6),

                FontSize =
                    12
            };

        label.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        return label;
    }

    private static ComboBoxItem
        CreateConditionComboBoxItem(
            string text,
            MailRuleConditionField field)
    {
        return new ComboBoxItem
        {
            Content =
                text,

            Tag =
                field
        };
    }

    private void RuleSettingsNavigation_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_ruleSettingsPanel is null)
        {
            return;
        }

        var rulePageSelected =
            SettingsNavigation.SelectedIndex ==
            3;

        _ruleSettingsPanel.Visibility =
            rulePageSelected
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (rulePageSelected)
        {
            _ =
                EnsureRuleSettingsLoadedAsync();
        }
    }

    private async Task EnsureRuleSettingsLoadedAsync()
    {
        if (_ruleSettingsLoaded ||
            _ruleSettingsLoading ||
            _ruleSettingsControls is null ||
            _ruleAccountText is null ||
            _ruleStatusText is null ||
            _ruleTargetFolderComboBox is null)
        {
            return;
        }

        _ruleSettingsLoading =
            true;

        _ruleSettingsControls.IsEnabled =
            false;

        _ruleStatusText.Text =
            "Regeln werden geladen …";

        try
        {
            var account =
                await _mailAccountStore
                    .GetActiveAccountAsync();

            if (account is null)
            {
                _ruleAccountText.Text =
                    "Das aktuell angemeldete Telenec-Mail-Konto konnte nicht ermittelt werden.";

                _ruleStatusText.Text =
                    "Die Regelverwaltung kann derzeit nicht verwendet werden.";

                return;
            }

            _ruleAccountId =
                account.AccountId;

            _ruleAccountText.Text =
                $"Regeln für {account.EmailAddress}";

            var rules =
                await _mailRuleStore
                    .GetRulesAsync(
                        account.AccountId);

            var folders =
                await _ruleMailDataSource
                    .GetFoldersAsync();

            _mailRules =
                rules
                    .OrderBy(
                        rule =>
                            rule.SortOrder)
                    .ToList();

            _ruleFolders =
                folders
                    .Select(
                        folder =>
                            new RuleFolderOption(
                                folder.FolderId,
                                folder.DisplayName))
                    .ToList();

            _ruleTargetFolderComboBox.ItemsSource =
                _ruleFolders;

            RefreshRuleList(
                selectedRuleId:
                    null);

            StartNewRuleEditor(
                clearStatus:
                    false);

            _ruleSettingsLoaded =
                true;

            _ruleSettingsControls.IsEnabled =
                true;

            _ruleStatusText.Text =
                string.Empty;
        }
        catch (Exception exception)
        {
            _ruleStatusText.Text =
                "Die Regeln konnten nicht geladen werden.";

            MessageBox.Show(
                this,
                "Die Mailregeln konnten nicht geladen werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _ruleSettingsLoading =
                false;
        }
    }

    private void RefreshRuleList(
        Guid? selectedRuleId)
    {
        if (_ruleListBox is null ||
            _ruleEmptyText is null)
        {
            return;
        }

        _suppressRuleSelectionChanged =
            true;

        try
        {
            _ruleListBox.Items.Clear();

            ListBoxItem?
                selectedItem =
                    null;

            foreach (var rule in
                     _mailRules
                         .OrderBy(
                             current =>
                                 current.SortOrder))
            {
                var item =
                    new ListBoxItem
                    {
                        Tag =
                            rule.RuleId,

                        Padding =
                            new Thickness(
                                10,
                                8,
                                10,
                                8),

                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch,

                        Content =
                            CreateRuleListItemContent(
                                rule)
                    };

                _ruleListBox.Items.Add(
                    item);

                if (selectedRuleId.HasValue &&
                    rule.RuleId ==
                        selectedRuleId.Value)
                {
                    selectedItem =
                        item;
                }
            }

            _ruleEmptyText.Visibility =
                _mailRules.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _ruleListBox.Visibility =
                _mailRules.Count == 0
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (selectedItem is not null)
            {
                _ruleListBox.SelectedItem =
                    selectedItem;

                selectedItem.BringIntoView();
            }
        }
        finally
        {
            _suppressRuleSelectionChanged =
                false;
        }
    }

    private FrameworkElement CreateRuleListItemContent(
        MailRuleDefinition rule)
    {
        var grid =
            new Grid();

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

        var details =
            new StackPanel();

        var name =
            new TextBlock
            {
                Text =
                    rule.Name,

                FontSize =
                    12,

                FontWeight =
                    FontWeights.SemiBold,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        name.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        details.Children.Add(
            name);

        var summary =
            new TextBlock
            {
                Text =
                    CreateRuleSummary(
                        rule),

                Margin =
                    new Thickness(
                        0,
                        3,
                        12,
                        0),

                FontSize =
                    11,

                TextTrimming =
                    TextTrimming.CharacterEllipsis
            };

        summary.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        details.Children.Add(
            summary);

        Grid.SetColumn(
            details,
            0);

        grid.Children.Add(
            details);

        var status =
            new TextBlock
            {
                Text =
                    rule.IsEnabled
                        ? "Aktiv"
                        : "Inaktiv",

                Margin =
                    new Thickness(
                        12,
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

        status.SetResourceReference(
            TextBlock.ForegroundProperty,
            rule.IsEnabled
                ? "Brand.Primary"
                : "Text.Muted");

        Grid.SetColumn(
            status,
            1);

        grid.Children.Add(
            status);

        return grid;
    }

    private string CreateRuleSummary(
        MailRuleDefinition rule)
    {
        var fieldName =
            rule.ConditionField switch
            {
                MailRuleConditionField.Sender =>
                    "Absender",

                MailRuleConditionField.Subject =>
                    "Betreff",

                MailRuleConditionField.Recipient =>
                    "Empfänger",

                _ =>
                    "Feld"
            };

        var targetFolderName =
            _ruleFolders
                .FirstOrDefault(
                    folder =>
                        string.Equals(
                            folder.FolderId,
                            rule.TargetFolderId,
                            StringComparison.OrdinalIgnoreCase))?
                .DisplayName
            ?? rule.TargetFolderId;

        var conditionValue =
            TruncateRuleListText(
                rule.ConditionValue,
                maximumLength:
                    45);

        return
            $"{fieldName} enthält „{conditionValue}“ → " +
            $"nach „{targetFolderName}“ verschieben";
    }

    private void RuleListBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_suppressRuleSelectionChanged ||
            !_ruleSettingsLoaded ||
            _ruleListBox?.SelectedItem
                is not ListBoxItem selectedItem ||
            selectedItem.Tag
                is not Guid ruleId)
        {
            return;
        }

        var rule =
            _mailRules
                .FirstOrDefault(
                    current =>
                        current.RuleId ==
                        ruleId);

        if (rule is null)
        {
            return;
        }

        LoadRuleIntoEditor(
            rule);
    }

    private void LoadRuleIntoEditor(
        MailRuleDefinition rule)
    {
        if (_ruleEditorTitle is null ||
            _ruleNameTextBox is null ||
            _ruleEnabledCheckBox is null ||
            _ruleConditionFieldComboBox is null ||
            _ruleConditionValueTextBox is null ||
            _ruleTargetFolderComboBox is null ||
            _deleteRuleButton is null ||
            _ruleStatusText is null)
        {
            return;
        }

        _editingRuleId =
            rule.RuleId;

        _ruleEditorTitle.Text =
            "Regel bearbeiten";

        _ruleNameTextBox.Text =
            rule.Name;

        _ruleEnabledCheckBox.IsChecked =
            rule.IsEnabled;

        SelectConditionField(
            rule.ConditionField);

        _ruleConditionValueTextBox.Text =
            rule.ConditionValue;

        _ruleTargetFolderComboBox.SelectedValue =
            rule.TargetFolderId;

        _deleteRuleButton.IsEnabled =
            true;

        if (_ruleTargetFolderComboBox.SelectedIndex <
            0)
        {
            _ruleStatusText.Text =
                "Der gespeicherte Zielordner ist auf dem Mailserver nicht mehr vorhanden.";
        }
        else
        {
            _ruleStatusText.Text =
                string.Empty;
        }
    }

    private void SelectConditionField(
        MailRuleConditionField field)
    {
        if (_ruleConditionFieldComboBox is null)
        {
            return;
        }

        foreach (var item in
                 _ruleConditionFieldComboBox
                     .Items
                     .OfType<ComboBoxItem>())
        {
            if (item.Tag
                    is MailRuleConditionField itemField &&
                itemField ==
                    field)
            {
                _ruleConditionFieldComboBox.SelectedItem =
                    item;

                return;
            }
        }

        _ruleConditionFieldComboBox.SelectedIndex =
            0;
    }

    private void NewRuleButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        StartNewRuleEditor(
            clearStatus:
                true);

        _ruleNameTextBox?
            .Focus();
    }

    private void StartNewRuleEditor(
        bool clearStatus)
    {
        if (_ruleEditorTitle is null ||
            _ruleNameTextBox is null ||
            _ruleEnabledCheckBox is null ||
            _ruleConditionFieldComboBox is null ||
            _ruleConditionValueTextBox is null ||
            _ruleTargetFolderComboBox is null ||
            _deleteRuleButton is null)
        {
            return;
        }

        _editingRuleId =
            null;

        _suppressRuleSelectionChanged =
            true;

        try
        {
            if (_ruleListBox is not null)
            {
                _ruleListBox.SelectedItem =
                    null;
            }
        }
        finally
        {
            _suppressRuleSelectionChanged =
                false;
        }

        _ruleEditorTitle.Text =
            "Neue Regel";

        _ruleNameTextBox.Clear();

        _ruleEnabledCheckBox.IsChecked =
            true;

        _ruleConditionFieldComboBox.SelectedIndex =
            0;

        _ruleConditionValueTextBox.Clear();

        _ruleTargetFolderComboBox.SelectedIndex =
            -1;

        _deleteRuleButton.IsEnabled =
            false;

        if (clearStatus &&
            _ruleStatusText is not null)
        {
            _ruleStatusText.Text =
                string.Empty;
        }
    }

    private async void SaveRuleButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_ruleSettingsLoaded ||
            _isSavingRules ||
            !_ruleAccountId.HasValue ||
            _ruleSettingsControls is null ||
            _ruleNameTextBox is null ||
            _ruleEnabledCheckBox is null ||
            _ruleConditionFieldComboBox is null ||
            _ruleConditionValueTextBox is null ||
            _ruleTargetFolderComboBox is null ||
            _ruleStatusText is null)
        {
            return;
        }

        var name =
            _ruleNameTextBox
                .Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                name))
        {
            MessageBox.Show(
                this,
                "Bitte geben Sie der Regel einen Namen.",
                "Regel unvollständig",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _ruleNameTextBox.Focus();

            return;
        }

        if (_ruleConditionFieldComboBox.SelectedItem
                is not ComboBoxItem conditionItem ||
            conditionItem.Tag
                is not MailRuleConditionField conditionField)
        {
            MessageBox.Show(
                this,
                "Bitte wählen Sie ein Bedingungsfeld aus.",
                "Regel unvollständig",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var conditionValue =
            _ruleConditionValueTextBox
                .Text
                .Trim();

        if (string.IsNullOrWhiteSpace(
                conditionValue))
        {
            MessageBox.Show(
                this,
                "Bitte geben Sie an, welcher Text enthalten sein soll.",
                "Regel unvollständig",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _ruleConditionValueTextBox.Focus();

            return;
        }

        if (_ruleTargetFolderComboBox.SelectedItem
                is not RuleFolderOption targetFolder)
        {
            MessageBox.Show(
                this,
                "Bitte wählen Sie einen Zielordner aus.",
                "Regel unvollständig",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _ruleTargetFolderComboBox.Focus();

            return;
        }

        var existingRule =
            _editingRuleId.HasValue
                ? _mailRules
                    .FirstOrDefault(
                        rule =>
                            rule.RuleId ==
                            _editingRuleId.Value)
                : null;

        var candidate =
            new MailRuleDefinition(
                RuleId:
                    existingRule?
                        .RuleId
                    ?? Guid.NewGuid(),

                Name:
                    name,

                IsEnabled:
                    _ruleEnabledCheckBox.IsChecked ==
                    true,

                ConditionField:
                    conditionField,

                MatchType:
                    MailRuleMatchType.Contains,

                ConditionValue:
                    conditionValue,

                ActionType:
                    MailRuleActionType.MoveToFolder,

                TargetFolderId:
                    targetFolder.FolderId,

                SortOrder:
                    existingRule?
                        .SortOrder
                    ??
                    (
                        _mailRules.Count == 0
                            ? 0
                            : _mailRules
                                .Max(
                                    rule =>
                                        rule.SortOrder)
                              + 1
                    ));

        var updatedRules =
            _mailRules
                .ToList();

        if (existingRule is null)
        {
            updatedRules.Add(
                candidate);
        }
        else
        {
            var existingIndex =
                updatedRules.FindIndex(
                    rule =>
                        rule.RuleId ==
                        existingRule.RuleId);

            if (existingIndex < 0)
            {
                return;
            }

            updatedRules[
                existingIndex] =
                    candidate;
        }

        updatedRules =
            NormalizeRuleSortOrder(
                updatedRules);

        _isSavingRules =
            true;

        _ruleSettingsControls.IsEnabled =
            false;

        _ruleStatusText.Text =
            "Regel wird gespeichert …";

        try
        {
            await _mailRuleStore
                .SaveRulesAsync(
                    _ruleAccountId.Value,
                    updatedRules);

            _mailRules =
                updatedRules;

            var savedRule =
                _mailRules
                    .First(
                        rule =>
                            rule.RuleId ==
                            candidate.RuleId);

            RefreshRuleList(
                savedRule.RuleId);

            LoadRuleIntoEditor(
                savedRule);

            _ruleStatusText.Text =
                "Regel gespeichert.";
        }
        catch (Exception exception)
        {
            _ruleStatusText.Text =
                "Speichern fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Regel konnte nicht gespeichert werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSavingRules =
                false;

            _ruleSettingsControls.IsEnabled =
                true;
        }
    }

    private async void DeleteRuleButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_ruleSettingsLoaded ||
            _isSavingRules ||
            !_ruleAccountId.HasValue ||
            !_editingRuleId.HasValue ||
            _ruleSettingsControls is null ||
            _ruleStatusText is null)
        {
            return;
        }

        var rule =
            _mailRules
                .FirstOrDefault(
                    current =>
                        current.RuleId ==
                        _editingRuleId.Value);

        if (rule is null)
        {
            return;
        }

        var result =
            MessageBox.Show(
                this,
                $"Soll die Regel „{rule.Name}“ wirklich gelöscht werden?",
                "Regel löschen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

        if (result !=
            MessageBoxResult.Yes)
        {
            return;
        }

        var updatedRules =
            NormalizeRuleSortOrder(
                _mailRules
                    .Where(
                        current =>
                            current.RuleId !=
                            rule.RuleId)
                    .ToList());

        _isSavingRules =
            true;

        _ruleSettingsControls.IsEnabled =
            false;

        _ruleStatusText.Text =
            "Regel wird gelöscht …";

        try
        {
            await _mailRuleStore
                .SaveRulesAsync(
                    _ruleAccountId.Value,
                    updatedRules);

            _mailRules =
                updatedRules;

            RefreshRuleList(
                selectedRuleId:
                    null);

            StartNewRuleEditor(
                clearStatus:
                    false);

            _ruleStatusText.Text =
                "Regel gelöscht.";
        }
        catch (Exception exception)
        {
            _ruleStatusText.Text =
                "Löschen fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Regel konnte nicht gelöscht werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isSavingRules =
                false;

            _ruleSettingsControls.IsEnabled =
                true;
        }
    }

    private static List<MailRuleDefinition>
        NormalizeRuleSortOrder(
            IEnumerable<MailRuleDefinition> rules)
    {
        return rules
            .OrderBy(
                rule =>
                    rule.SortOrder)
            .ThenBy(
                rule =>
                    rule.Name,
                StringComparer
                    .CurrentCultureIgnoreCase)
            .Select(
                (
                    rule,
                    index) =>
                    rule with
                    {
                        SortOrder =
                            index
                    })
            .ToList();
    }

    private static string TruncateRuleListText(
        string value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(
                value) ||
            value.Length <=
                maximumLength)
        {
            return value;
        }

        return
            value[
                ..(maximumLength - 1)]
            + "…";
    }

    private sealed record RuleFolderOption(
        string FolderId,
        string DisplayName);
}