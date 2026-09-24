using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Services.Storage;

public sealed class MailRuleStore
{
    private const int CurrentDocumentVersion =
        1;

    private const int MaximumRuleNameLength =
        120;

    private const int MaximumConditionValueLength =
        500;

    private const int MaximumFolderIdLength =
        1024;

    private static readonly JsonSerializerOptions
        SerializerOptions =
            CreateSerializerOptions();

    private readonly ISettingsStore
        _settingsStore;

    public MailRuleStore(
        ISettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(
            settingsStore);

        _settingsStore =
            settingsStore;
    }

    public async Task<IReadOnlyList<MailRuleDefinition>>
        GetRulesAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        var serializedDocument =
            await _settingsStore
                .GetAccountSettingAsync(
                    accountId,
                    SettingsKeys.MailRulesDefinitions,
                    cancellationToken);

        if (string.IsNullOrWhiteSpace(
                serializedDocument))
        {
            return Array.Empty<
                MailRuleDefinition>();
        }

        MailRuleDocument?
            document;

        try
        {
            document =
                JsonSerializer
                    .Deserialize<MailRuleDocument>(
                        serializedDocument,
                        SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Die gespeicherten Mailregeln sind beschädigt oder besitzen ein ungültiges Format.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidDataException(
                "Die gespeicherten Mailregeln konnten nicht gelesen werden.");
        }

        if (document.Version <= 0 ||
            document.Version >
                CurrentDocumentVersion)
        {
            throw new InvalidDataException(
                $"Die gespeicherten Mailregeln verwenden Version " +
                $"{document.Version}. Unterstützt wird aktuell maximal " +
                $"Version {CurrentDocumentVersion}.");
        }

        var rules =
            document.Rules
            ?? new List<
                MailRuleDefinition>();

        ValidateRules(
            rules);

        return rules
            .OrderBy(
                rule =>
                    rule.SortOrder)
            .ThenBy(
                rule =>
                    rule.Name,
                StringComparer
                    .CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task SaveRulesAsync(
        Guid accountId,
        IReadOnlyCollection<MailRuleDefinition> rules,
        CancellationToken cancellationToken = default)
    {
        ValidateAccountId(
            accountId);

        ArgumentNullException.ThrowIfNull(
            rules);

        var normalizedRules =
            rules
                .Select(
                    NormalizeRule)
                .OrderBy(
                    rule =>
                        rule.SortOrder)
                .ThenBy(
                    rule =>
                        rule.Name,
                    StringComparer
                        .CurrentCultureIgnoreCase)
                .ToList();

        ValidateRules(
            normalizedRules);

        var document =
            new MailRuleDocument
            {
                Version =
                    CurrentDocumentVersion,

                Rules =
                    normalizedRules
            };

        var serializedDocument =
            JsonSerializer.Serialize(
                document,
                SerializerOptions);

        await _settingsStore
            .SetAccountSettingAsync(
                accountId,
                SettingsKeys.MailRulesDefinitions,
                serializedDocument,
                cancellationToken);
    }

    private static MailRuleDefinition NormalizeRule(
        MailRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(
            rule);

        return rule with
        {
            Name =
                rule.Name.Trim(),

            ConditionValue =
                rule.ConditionValue.Trim(),

            TargetFolderId =
                rule.TargetFolderId.Trim()
        };
    }

    private static void ValidateRules(
        IReadOnlyCollection<MailRuleDefinition> rules)
    {
        var knownRuleIds =
            new HashSet<Guid>();

        foreach (var rule in rules)
        {
            ValidateRule(
                rule);

            if (!knownRuleIds.Add(
                    rule.RuleId))
            {
                throw new InvalidDataException(
                    $"Die Regel-ID {rule.RuleId:D} ist mehrfach vorhanden.");
            }
        }
    }

    private static void ValidateRule(
        MailRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(
            rule);

        if (rule.RuleId ==
            Guid.Empty)
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt keine gültige Regel-ID.");
        }

        if (string.IsNullOrWhiteSpace(
                rule.Name))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt keinen Namen.");
        }

        if (rule.Name.Length >
            MaximumRuleNameLength)
        {
            throw new InvalidDataException(
                $"Der Name einer Mailregel darf maximal " +
                $"{MaximumRuleNameLength} Zeichen lang sein.");
        }

        if (!Enum.IsDefined(
                rule.ConditionField))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt ein unbekanntes Bedingungsfeld.");
        }

        if (!Enum.IsDefined(
                rule.MatchType))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt einen unbekannten Vergleichstyp.");
        }

        if (string.IsNullOrWhiteSpace(
                rule.ConditionValue))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt keinen Vergleichswert.");
        }

        if (rule.ConditionValue.Length >
            MaximumConditionValueLength)
        {
            throw new InvalidDataException(
                $"Der Vergleichswert einer Mailregel darf maximal " +
                $"{MaximumConditionValueLength} Zeichen lang sein.");
        }

        if (!Enum.IsDefined(
                rule.ActionType))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt eine unbekannte Aktion.");
        }

        if (string.IsNullOrWhiteSpace(
                rule.TargetFolderId))
        {
            throw new InvalidDataException(
                "Eine Mailregel besitzt keinen Zielordner.");
        }

        if (rule.TargetFolderId.Length >
            MaximumFolderIdLength)
        {
            throw new InvalidDataException(
                $"Die Ordner-ID einer Mailregel darf maximal " +
                $"{MaximumFolderIdLength} Zeichen lang sein.");
        }

        if (rule.SortOrder < 0)
        {
            throw new InvalidDataException(
                "Die Sortierreihenfolge einer Mailregel darf nicht negativ sein.");
        }
    }

    private static void ValidateAccountId(
        Guid accountId)
    {
        if (accountId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Die Account-ID darf nicht leer sein.",
                nameof(accountId));
        }
    }

    private static JsonSerializerOptions
        CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                PropertyNameCaseInsensitive =
                    true,

                WriteIndented =
                    false
            };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase));

        return options;
    }

    private sealed class MailRuleDocument
    {
        public int Version
        { get; set; }

        public List<MailRuleDefinition> Rules
        { get; set; } =
            new();
    }
}