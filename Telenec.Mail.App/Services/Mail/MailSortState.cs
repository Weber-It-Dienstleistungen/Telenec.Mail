using MailKit;
using System.Threading;
using MailKitOrderBy = MailKit.Search.OrderBy;

namespace Telenec.Mail.App.Services.Mail;

public enum MailSortField
{
    Sender,
    Subject,
    Date
}

public readonly record struct MailSortDescriptor(
    MailSortField Field,
    bool Descending);

public static class MailSortState
{
    private static readonly object SyncRoot =
        new();

    /*
     * Temporäre Overrides werden ausschließlich innerhalb
     * des jeweiligen asynchronen Aufrufkontexts verwendet.
     *
     * Das ist beispielsweise für den New-Mail-Monitor wichtig:
     * Die sichtbare Nachrichtenliste darf nach Absender
     * sortiert sein, während die Eingangserkennung weiterhin
     * zuverlässig die neuesten UIDs betrachtet.
     */
    private static readonly AsyncLocal<
        MailSortDescriptor?>
        TemporarySort =
            new();

    private static MailSortDescriptor
        _current =
            DefaultSort;

    public static MailSortDescriptor DefaultSort =>
        new(
            MailSortField.Date,
            Descending:
                true);

    public static MailSortDescriptor Current
    {
        get
        {
            lock (SyncRoot)
            {
                return _current;
            }
        }
    }

    public static MailSortDescriptor Effective =>
        TemporarySort.Value
        ?? Current;

    /*
     * Der Offline-Cache darf nur sichtbare, reguläre
     * Benutzerabrufe als Ordner-Snapshot speichern.
     *
     * Hintergrunddienste wie der New-Mail-Monitor arbeiten
     * bewusst mit einem temporären Sortier-Override.
     *
     * Dadurch kann die Cache-Schicht diese Abrufe erkennen,
     * ohne den Monitor selbst kennen zu müssen.
     */
    public static bool HasTemporaryOverride =>
        TemporarySort.Value.HasValue;

    public static MailSortDescriptor Toggle(
        MailSortField field)
    {
        lock (SyncRoot)
        {
            if (_current.Field ==
                field)
            {
                _current =
                    _current with
                    {
                        Descending =
                            !_current.Descending
                    };

                return _current;
            }

            /*
             * Beim Wechsel des Kriteriums verwenden wir
             * die jeweils natürliche erste Richtung:
             *
             * Von      -> A bis Z
             * Betreff  -> A bis Z
             * Datum    -> Neu nach Alt
             */
            _current =
                new MailSortDescriptor(
                    field,
                    Descending:
                        field ==
                        MailSortField.Date);

            return _current;
        }
    }

    public static IDisposable UseTemporarySort(
        MailSortDescriptor descriptor)
    {
        var previous =
            TemporarySort.Value;

        TemporarySort.Value =
            descriptor;

        return new TemporarySortScope(
            previous);
    }

    private sealed class TemporarySortScope :
        IDisposable
    {
        private readonly MailSortDescriptor?
            _previous;

        private bool
            _disposed;

        public TemporarySortScope(
            MailSortDescriptor? previous)
        {
            _previous =
                previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;

            TemporarySort.Value =
                _previous;
        }
    }
}

internal static class MailSortOrder
{
    /*
     * MailKit.SortAsync erwartet IList<OrderBy>.
     *
     * Deshalb liefern wir bewusst IList und nicht
     * IReadOnlyList zurück.
     */
    public static IList<
        MailKitOrderBy> CreateServerOrder(
        MailSortDescriptor descriptor)
    {
        return descriptor.Field switch
        {
            MailSortField.Sender =>
                new List<MailKitOrderBy>
                {
                    descriptor.Descending
                        ? MailKitOrderBy.ReverseDisplayFrom
                        : MailKitOrderBy.DisplayFrom,

                    MailKitOrderBy.ReverseDate
                },

            MailSortField.Subject =>
                new List<MailKitOrderBy>
                {
                    descriptor.Descending
                        ? MailKitOrderBy.ReverseSubject
                        : MailKitOrderBy.Subject,

                    MailKitOrderBy.ReverseDate
                },

            MailSortField.Date =>
                new List<MailKitOrderBy>
                {
                    descriptor.Descending
                        ? MailKitOrderBy.ReverseDate
                        : MailKitOrderBy.Date
                },

            _ =>
                new List<MailKitOrderBy>
                {
                    MailKitOrderBy.ReverseDate
                }
        };
    }

    public static List<IMessageSummary>
        SortSummaries(
            IEnumerable<IMessageSummary> summaries,
            MailSortDescriptor descriptor)
    {
        var validSummaries =
            summaries
                .Where(
                    summary =>
                        summary.UniqueId.IsValid);

        return descriptor.Field switch
        {
            MailSortField.Sender
                when descriptor.Descending =>

                validSummaries
                    .OrderByDescending(
                        GetSenderSortValue,
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList(),

            MailSortField.Sender =>

                validSummaries
                    .OrderBy(
                        GetSenderSortValue,
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList(),

            MailSortField.Subject
                when descriptor.Descending =>

                validSummaries
                    .OrderByDescending(
                        GetSubjectSortValue,
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList(),

            MailSortField.Subject =>

                validSummaries
                    .OrderBy(
                        GetSubjectSortValue,
                        StringComparer
                            .CurrentCultureIgnoreCase)
                    .ThenByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList(),

            MailSortField.Date
                when !descriptor.Descending =>

                validSummaries
                    .OrderBy(
                        GetMessageSortDate)
                    .ThenBy(
                        summary =>
                            summary.Index)
                    .ToList(),

            _ =>

                validSummaries
                    .OrderByDescending(
                        GetMessageSortDate)
                    .ThenByDescending(
                        summary =>
                            summary.Index)
                    .ToList()
        };
    }

    /*
     * Ein UID-Fetch muss nicht in derselben Reihenfolge
     * zurückkommen, in der die UIDs angefordert wurden.
     *
     * Deshalb stellen wir nach dem Fetch exakt die Reihenfolge
     * wieder her, die IMAP SORT bzw. unser lokaler Fallback
     * zuvor festgelegt hat.
     *
     * IList wird bewusst verwendet, weil sowohl MailKit als
     * auch unsere Datenquellen an dieser Stelle IList liefern.
     */
    public static List<IMessageSummary>
        RestoreRequestedUniqueIdOrder(
            IEnumerable<IMessageSummary> summaries,
            IList<UniqueId> requestedUniqueIds)
    {
        var positions =
            requestedUniqueIds
                .Where(
                    uniqueId =>
                        uniqueId.IsValid)
                .Select(
                    (
                        uniqueId,
                        index) =>
                        new
                        {
                            UniqueId =
                                uniqueId.Id,

                            Index =
                                index
                        })
                .GroupBy(
                    item =>
                        item.UniqueId)
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group
                            .First()
                            .Index);

        return summaries
            .Where(
                summary =>
                    summary.UniqueId.IsValid &&
                    positions.ContainsKey(
                        summary.UniqueId.Id))
            .OrderBy(
                summary =>
                    positions[
                        summary.UniqueId.Id])
            .ToList();
    }

    private static string GetSenderSortValue(
        IMessageSummary summary)
    {
        var sender =
            summary.Envelope?
                .From?
                .Mailboxes
                .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(
                sender?.Name))
        {
            return sender.Name.Trim();
        }

        return sender?
                   .Address?
                   .Trim()
               ?? string.Empty;
    }

    private static string GetSubjectSortValue(
        IMessageSummary summary)
    {
        return summary
                   .Envelope?
                   .Subject?
                   .Trim()
               ?? string.Empty;
    }

    private static DateTimeOffset
        GetMessageSortDate(
            IMessageSummary summary)
    {
        return summary
                   .Envelope?
                   .Date
               ?? DateTimeOffset.MinValue;
    }
}