namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    public void PrepareFeedback(
        string recipientAddress,
        string feedbackType,
        string applicationVersion)
    {
        if (string.IsNullOrWhiteSpace(
                recipientAddress))
        {
            throw new ArgumentException(
                "Die Feedback-Empfängeradresse darf nicht leer sein.",
                nameof(recipientAddress));
        }

        if (string.IsNullOrWhiteSpace(
                feedbackType))
        {
            throw new ArgumentException(
                "Die Feedback-Art darf nicht leer sein.",
                nameof(feedbackType));
        }

        var normalizedVersion =
            string.IsNullOrWhiteSpace(
                applicationVersion)
                ? "unbekannt"
                : applicationVersion.Trim();

        /*
         * Der Betreff entspricht bewusst exakt der vom
         * Benutzer ausgewählten Feedback-Art:
         *
         * - Fehler
         * - Fehlende Funktion
         * - Lob
         */
        _viewModel.RecipientAddress =
            recipientAddress.Trim();

        _viewModel.Subject =
            feedbackType.Trim();

        /*
         * Keine automatischen Logs, IDs, Kontodaten oder
         * sonstigen Diagnosedaten.
         *
         * Lediglich die Programmversion wird als technische
         * Basisinformation ergänzt.
         */
        _viewModel.Body =
            "Bitte beschreiben Sie Ihr Feedback hier:\n\n\n\n" +
            "------------------------------\n" +
            $"Telenec Mail Version: {normalizedVersion}";

        _viewModel.HtmlBody =
            null;
    }
}