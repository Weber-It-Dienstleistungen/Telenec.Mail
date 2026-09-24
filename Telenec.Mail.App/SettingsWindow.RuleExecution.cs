using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Mail;

namespace Telenec.Mail.App;

public partial class SettingsWindow
{
    private readonly MailRuleExecutionService
        _mailRuleExecutionService;

    private Button?
        _applyRulesNowButton;

    private bool
        _ruleExecutionUiInitialized;

    private bool
        _isExecutingRules;

    private void InitializeRuleExecutionUi()
    {
        if (_ruleExecutionUiInitialized ||
            _ruleSettingsControls
                is not StackPanel controls)
        {
            return;
        }

        var executionCard =
            CreateRuleExecutionCard();

        /*
         * Die bestehende Regeloberfläche besteht aus:
         *
         * 0 = gespeicherte Regeln
         * 1 = Regeleditor
         *
         * Der Ausführungsbereich wird bewusst dazwischen
         * eingefügt.
         */
        var insertionIndex =
            Math.Min(
                1,
                controls.Children.Count);

        controls.Children.Insert(
            insertionIndex,
            executionCard);

        _ruleExecutionUiInitialized =
            true;
    }

    private Border CreateRuleExecutionCard()
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

        var title =
            new TextBlock
            {
                Text =
                    "Bestehende Nachrichten",

                FontSize =
                    14,

                FontWeight =
                    FontWeights.SemiBold
            };

        title.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Primary");

        content.Children.Add(
            title);

        var description =
            new TextBlock
            {
                Text =
                    "Aktive Regeln können manuell auf alle aktuell im Posteingang vorhandenen E-Mails angewendet werden. " +
                    "Telenec Mail prüft zuerst nur die Header und zeigt vor jeder Änderung eine Vorschau an.",

                Margin =
                    new Thickness(
                        0,
                        7,
                        0,
                        0),

                FontSize =
                    12,

                TextWrapping =
                    TextWrapping.Wrap
            };

        description.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Secondary");

        content.Children.Add(
            description);

        var note =
            new TextBlock
            {
                Text =
                    "Trifft eine Nachricht auf mehrere Regeln zu, wird nur die erste passende aktive Regel ausgeführt.",

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                FontSize =
                    11,

                TextWrapping =
                    TextWrapping.Wrap
            };

        note.SetResourceReference(
            TextBlock.ForegroundProperty,
            "Text.Muted");

        content.Children.Add(
            note);

        var button =
            new Button
            {
                Width =
                    190,

                Height =
                    38,

                Margin =
                    new Thickness(
                        0,
                        18,
                        0,
                        0),

                HorizontalAlignment =
                    HorizontalAlignment.Left,

                Content =
                    "Regeln jetzt anwenden",

                Cursor =
                    Cursors.Hand
            };

        button.SetResourceReference(
            FrameworkElement.StyleProperty,
            "Button.Primary");

        button.Click +=
            ApplyRulesNowButton_OnClick;

        _applyRulesNowButton =
            button;

        content.Children.Add(
            button);

        card.Child =
            content;

        return card;
    }

    private async void ApplyRulesNowButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (!_ruleSettingsLoaded ||
            _isSavingRules ||
            _isExecutingRules ||
            !_ruleAccountId.HasValue ||
            _ruleSettingsControls is null ||
            _ruleStatusText is null)
        {
            return;
        }

        var activeRules =
            _mailRules
                .Where(
                    rule =>
                        rule.IsEnabled)
                .OrderBy(
                    rule =>
                        rule.SortOrder)
                .ThenBy(
                    rule =>
                        rule.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        if (activeRules.Count == 0)
        {
            MessageBox.Show(
                this,
                "Es gibt derzeit keine aktive Regel.",
                "Regeln anwenden",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        _isExecutingRules =
            true;

        _ruleSettingsControls.IsEnabled =
            false;

        _ruleStatusText.Text =
            "Posteingang wird auf passende Regeln geprüft …";

        try
        {
            /*
             * Analyse und Mutation sind bewusst zwei getrennte
             * Schritte.
             *
             * Vor dieser Stelle wird keine einzige Nachricht
             * verändert.
             */
            var plan =
                await _mailRuleExecutionService
                    .AnalyzeInboxAsync(
                        _ruleAccountId.Value,
                        activeRules);

            if (plan.Items.Count == 0)
            {
                _ruleStatusText.Text =
                    "Keine passende Nachricht gefunden.";

                var message =
                    CreateNoRuleMatchesMessage(
                        plan);

                MessageBox.Show(
                    this,
                    message,
                    "Regeln anwenden",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var previewMessage =
                CreateRuleExecutionPreview(
                    plan);

            var confirmation =
                MessageBox.Show(
                    this,
                    previewMessage,
                    "Regeln auf bestehende Nachrichten anwenden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

            if (confirmation !=
                MessageBoxResult.Yes)
            {
                _ruleStatusText.Text =
                    "Keine Änderungen vorgenommen.";

                return;
            }

            _ruleStatusText.Text =
                "Regeln werden angewendet …";

            var result =
                await _mailRuleExecutionService
                    .ApplyAsync(
                        _ruleAccountId.Value,
                        plan);

            var resultMessage =
                CreateRuleExecutionResultMessage(
                    result);

            if (result.FailedMessageCount >
                0)
            {
                _ruleStatusText.Text =
                    $"{result.MovedMessageCount} verschoben, " +
                    $"{result.FailedMessageCount} fehlgeschlagen.";

                MessageBox.Show(
                    this,
                    resultMessage,
                    "Regeln angewendet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                _ruleStatusText.Text =
                    result.MovedMessageCount == 1
                        ? "1 Nachricht verschoben."
                        : $"{result.MovedMessageCount} Nachrichten verschoben.";

                MessageBox.Show(
                    this,
                    resultMessage,
                    "Regeln angewendet",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception)
        {
            _ruleStatusText.Text =
                "Regelausführung fehlgeschlagen.";

            MessageBox.Show(
                this,
                "Die Regeln konnten nicht auf den Posteingang angewendet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isExecutingRules =
                false;

            _ruleSettingsControls.IsEnabled =
                true;
        }
    }

    private static string CreateNoRuleMatchesMessage(
        MailRuleExecutionPlan plan)
    {
        var builder =
            new StringBuilder();

        builder.Append(
            "Es wurde keine Nachricht gefunden, auf die eine aktive Regel zutrifft.");

        if (plan.ScannedMessageCount >
            0)
        {
            builder.AppendLine();
            builder.AppendLine();

            builder.Append(
                $"Geprüfte Nachrichten im Posteingang: {plan.ScannedMessageCount}");
        }

        AppendUnavailableRules(
            builder,
            plan.UnavailableRuleNames);

        return builder.ToString();
    }

    private static string CreateRuleExecutionPreview(
        MailRuleExecutionPlan plan)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            $"Im Posteingang wurden {plan.ScannedMessageCount} Nachrichten geprüft.");

        builder.AppendLine();

        builder.AppendLine(
            plan.Items.Count == 1
                ? "1 Nachricht würde durch die aktiven Regeln verschoben:"
                : $"{plan.Items.Count} Nachrichten würden durch die aktiven Regeln verschoben:");

        builder.AppendLine();

        var ruleGroups =
            plan.Items
                .GroupBy(
                    item =>
                        item.RuleId)
                .Select(
                    group =>
                        new
                        {
                            RuleName =
                                group
                                    .First()
                                    .RuleName,

                            Count =
                                group.Count()
                        })
                .ToList();

        foreach (var group in ruleGroups)
        {
            builder.AppendLine(
                $"• {group.RuleName}: {group.Count}");
        }

        AppendUnavailableRules(
            builder,
            plan.UnavailableRuleNames);

        builder.AppendLine();
        builder.AppendLine();

        builder.Append(
            "Die betroffenen Nachrichten werden aus dem Posteingang in die jeweiligen Zielordner verschoben. Fortfahren?");

        return builder.ToString();
    }

    private static void AppendUnavailableRules(
        StringBuilder builder,
        IReadOnlyList<string> unavailableRuleNames)
    {
        if (unavailableRuleNames.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine();

        builder.AppendLine(
            unavailableRuleNames.Count == 1
                ? "Eine aktive Regel konnte nicht ausgeführt werden:"
                : $"{unavailableRuleNames.Count} aktive Regeln konnten nicht ausgeführt werden:");

        foreach (var ruleName in
                 unavailableRuleNames)
        {
            builder.AppendLine(
                $"• {ruleName}");
        }

        builder.Append(
            "Bitte prüfen Sie bei diesen Regeln den Zielordner.");
    }

    private static string
        CreateRuleExecutionResultMessage(
            MailRuleExecutionResult result)
    {
        var builder =
            new StringBuilder();

        if (result.MovedMessageCount == 1)
        {
            builder.AppendLine(
                "1 Nachricht wurde erfolgreich verschoben.");
        }
        else
        {
            builder.AppendLine(
                $"{result.MovedMessageCount} Nachrichten wurden erfolgreich verschoben.");
        }

        if (result.SkippedMessageCount >
            0)
        {
            builder.AppendLine();
            builder.AppendLine(
                result.SkippedMessageCount == 1
                    ? "1 Nachricht wurde übersprungen, weil sie sich seit der Vorschau verändert hat."
                    : $"{result.SkippedMessageCount} Nachrichten wurden übersprungen, weil sie sich seit der Vorschau verändert haben.");
        }

        if (result.FailedMessageCount >
            0)
        {
            builder.AppendLine();
            builder.AppendLine(
                result.FailedMessageCount == 1
                    ? "1 Nachricht konnte nicht verschoben werden."
                    : $"{result.FailedMessageCount} Nachrichten konnten nicht verschoben werden.");
        }

        return builder
            .ToString()
            .TrimEnd();
    }
}