namespace Telenec.Mail.App.Models;

public enum MailRuleConditionField
{
    Sender,
    Subject,
    Recipient
}

public enum MailRuleMatchType
{
    Contains
}

public enum MailRuleActionType
{
    MoveToFolder
}

public sealed record MailRuleDefinition(
    Guid RuleId,
    string Name,
    bool IsEnabled,
    MailRuleConditionField ConditionField,
    MailRuleMatchType MatchType,
    string ConditionValue,
    MailRuleActionType ActionType,
    string TargetFolderId,
    int SortOrder);