using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;

namespace Telenec.Mail.App;

public partial class ComposeWindow
{
    private const int MaximumContactSuggestions =
        8;

    private bool
        _contactAutocompleteInitialized;

    private bool
        _suppressContactAutocomplete;

    private IReadOnlyList<ContactEmailSuggestion>
        _contactEmailSuggestions =
            Array.Empty<ContactEmailSuggestion>();

    private Popup?
        _contactSuggestionPopup;

    private Border?
        _contactSuggestionBorder;

    private ListBox?
        _contactSuggestionListBox;

    private TextBox?
        _activeContactSuggestionTextBox;

    private CancellationTokenSource?
        _contactAutocompleteCancellationTokenSource;

    protected override void OnContentRendered(
        EventArgs e)
    {
        base.OnContentRendered(
            e);

        if (_contactAutocompleteInitialized)
        {
            return;
        }

        _contactAutocompleteInitialized =
            true;

        InitializeContactAutocomplete();

        _contactAutocompleteCancellationTokenSource =
            new CancellationTokenSource();

        Closed +=
            ComposeWindowContactAutocomplete_OnClosed;

        _ =
            LoadContactAutocompleteAsync(
                _contactAutocompleteCancellationTokenSource.Token);
    }

    private void InitializeContactAutocomplete()
    {
        InitializeContactAutocompleteTextBox(
            RecipientTextBox);

        InitializeContactAutocompleteTextBox(
            CcTextBox);

        InitializeContactAutocompleteTextBox(
            BccTextBox);

        var suggestionListBox =
            new ListBox
            {
                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(0),

                MaxHeight =
                    250,

                MinWidth =
                    280,

                DisplayMemberPath =
                    nameof(
                        ContactEmailSuggestion.DisplayText)
            };

        ScrollViewer.SetVerticalScrollBarVisibility(
            suggestionListBox,
            ScrollBarVisibility.Auto);

        ScrollViewer.SetHorizontalScrollBarVisibility(
            suggestionListBox,
            ScrollBarVisibility.Disabled);

        suggestionListBox.SetResourceReference(
            Control.BackgroundProperty,
            "Surface.Card");

        suggestionListBox.SetResourceReference(
            Control.ForegroundProperty,
            "Text.Primary");

        var itemStyle =
            new Style(
                typeof(ListBoxItem));

        itemStyle.Setters.Add(
            new Setter(
                Control.PaddingProperty,
                new Thickness(
                    10,
                    8,
                    10,
                    8)));

        itemStyle.Setters.Add(
            new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));

        suggestionListBox.ItemContainerStyle =
            itemStyle;

        suggestionListBox
            .PreviewMouseLeftButtonUp +=
                ContactSuggestionListBox_OnPreviewMouseLeftButtonUp;

        var suggestionBorder =
            new Border
            {
                Padding =
                    new Thickness(4),

                CornerRadius =
                    new CornerRadius(6),

                BorderThickness =
                    new Thickness(1),

                Child =
                    suggestionListBox
            };

        suggestionBorder.SetResourceReference(
            Border.BackgroundProperty,
            "Surface.Card");

        suggestionBorder.SetResourceReference(
            Border.BorderBrushProperty,
            "Border.Default");

        var popup =
            new Popup
            {
                AllowsTransparency =
                    true,

                Placement =
                    PlacementMode.Bottom,

                StaysOpen =
                    false,

                VerticalOffset =
                    2,

                Child =
                    suggestionBorder
            };

        _contactSuggestionListBox =
            suggestionListBox;

        _contactSuggestionBorder =
            suggestionBorder;

        _contactSuggestionPopup =
            popup;
    }

    private void InitializeContactAutocompleteTextBox(
        TextBox textBox)
    {
        textBox.TextChanged +=
            ContactAutocompleteTextBox_OnTextChanged;

        textBox.PreviewKeyDown +=
            ContactAutocompleteTextBox_OnPreviewKeyDown;

        textBox.GotKeyboardFocus +=
            ContactAutocompleteTextBox_OnGotKeyboardFocus;
    }

    private async Task LoadContactAutocompleteAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (Application.Current
                is not App application)
            {
                return;
            }

            var contactService =
                ActivatorUtilities
                    .CreateInstance<
                        CardDavContactService>(
                        application.Services);

            var contacts =
                await contactService
                    .GetAllAsync(
                        cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            _contactEmailSuggestions =
                CreateContactEmailSuggestions(
                    contacts);

            if (Keyboard.FocusedElement
                is TextBox textBox &&
                IsContactAutocompleteTextBox(
                    textBox))
            {
                UpdateContactSuggestions(
                    textBox);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            /*
             * Autocomplete ist eine Komfortfunktion.
             *
             * Ein CardDAV-Problem darf weder das
             * Verfassen noch das Versenden einer E-Mail
             * beeinträchtigen.
             */
            _contactEmailSuggestions =
                Array.Empty<ContactEmailSuggestion>();

            CloseContactSuggestions();
        }
    }

    private static IReadOnlyList<ContactEmailSuggestion>
        CreateContactEmailSuggestions(
            IEnumerable<ContactData> contacts)
    {
        var result =
            new List<ContactEmailSuggestion>();

        var knownEmailAddresses =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var contact in
                 contacts.OrderBy(
                     contact =>
                         contact.DisplayName,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            AddContactEmailSuggestion(
                result,
                knownEmailAddresses,
                contact,
                contact.EmailAddress,
                "Bevorzugt");

            AddContactEmailSuggestion(
                result,
                knownEmailAddresses,
                contact,
                contact.BusinessEmailAddress,
                "Geschäftlich");

            AddContactEmailSuggestion(
                result,
                knownEmailAddresses,
                contact,
                contact.PrivateEmailAddress,
                "Privat");

            foreach (var emailAddress in
                     contact.AdditionalEmailAddresses)
            {
                AddContactEmailSuggestion(
                    result,
                    knownEmailAddresses,
                    contact,
                    emailAddress,
                    "Weitere");
            }
        }

        return result;
    }

    private static void AddContactEmailSuggestion(
        ICollection<ContactEmailSuggestion> result,
        ISet<string> knownEmailAddresses,
        ContactData contact,
        string? emailAddress,
        string emailType)
    {
        var normalizedEmailAddress =
            emailAddress?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                normalizedEmailAddress))
        {
            return;
        }

        /*
         * Bewusst nur:
         *
         * - Whitespace entfernen
         * - Groß-/Kleinschreibung ignorieren
         *
         * Plus-Aliase oder Punkte werden nicht
         * normalisiert.
         */
        if (!knownEmailAddresses.Add(
                normalizedEmailAddress))
        {
            return;
        }

        var displayName =
            contact.DisplayName?
                .Trim();

        if (string.IsNullOrWhiteSpace(
                displayName))
        {
            displayName =
                normalizedEmailAddress;
        }

        var searchValues =
            new[]
            {
                displayName,
                contact.FirstName,
                contact.MiddleName,
                contact.LastName,
                contact.Nickname,
                contact.Company,
                contact.Department,
                contact.JobTitle,
                normalizedEmailAddress
            }
            .Where(
                value =>
                    !string.IsNullOrWhiteSpace(
                        value))
            .Select(
                value =>
                    value!.Trim())
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        result.Add(
            new ContactEmailSuggestion(
                DisplayName:
                    displayName,

                EmailAddress:
                    normalizedEmailAddress,

                EmailType:
                    emailType,

                SearchValues:
                    searchValues));
    }

    private void ContactAutocompleteTextBox_OnGotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        UpdateContactSuggestions(
            textBox);
    }

    private void ContactAutocompleteTextBox_OnTextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        if (_suppressContactAutocomplete ||
            sender is not TextBox textBox)
        {
            return;
        }

        UpdateContactSuggestions(
            textBox);
    }

    private void UpdateContactSuggestions(
        TextBox textBox)
    {
        if (_contactEmailSuggestions.Count ==
            0)
        {
            CloseContactSuggestions();

            return;
        }

        var token =
            GetRecipientToken(
                textBox);

        if (string.IsNullOrWhiteSpace(
                token.Query))
        {
            CloseContactSuggestions();

            return;
        }

        var suggestions =
            _contactEmailSuggestions
                .Select(
                    suggestion =>
                        new
                        {
                            Suggestion =
                                suggestion,

                            Rank =
                                GetSuggestionRank(
                                    suggestion,
                                    token.Query)
                        })
                .Where(
                    item =>
                        item.Rank <
                        int.MaxValue)
                .OrderBy(
                    item =>
                        item.Rank)
                .ThenBy(
                    item =>
                        item.Suggestion.DisplayName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    item =>
                        item.Suggestion.EmailAddress,
                    StringComparer.OrdinalIgnoreCase)
                .Take(
                    MaximumContactSuggestions)
                .Select(
                    item =>
                        item.Suggestion)
                .ToArray();

        if (suggestions.Length ==
            0)
        {
            CloseContactSuggestions();

            return;
        }

        ShowContactSuggestions(
            textBox,
            suggestions);
    }

    private static int GetSuggestionRank(
        ContactEmailSuggestion suggestion,
        string query)
    {
        if (suggestion.EmailAddress.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (suggestion.DisplayName.StartsWith(
                query,
                StringComparison.CurrentCultureIgnoreCase))
        {
            return 1;
        }

        if (suggestion.SearchValues.Any(
                value =>
                    value.StartsWith(
                        query,
                        StringComparison.CurrentCultureIgnoreCase)))
        {
            return 2;
        }

        if (suggestion.EmailAddress.Contains(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (suggestion.SearchValues.Any(
                value =>
                    value.Contains(
                        query,
                        StringComparison.CurrentCultureIgnoreCase)))
        {
            return 4;
        }

        return int.MaxValue;
    }

    private void ShowContactSuggestions(
        TextBox textBox,
        IReadOnlyList<ContactEmailSuggestion> suggestions)
    {
        if (_contactSuggestionPopup is null ||
            _contactSuggestionListBox is null ||
            _contactSuggestionBorder is null)
        {
            return;
        }

        _activeContactSuggestionTextBox =
            textBox;

        _contactSuggestionListBox
            .ItemsSource =
                suggestions;

        _contactSuggestionListBox
            .SelectedIndex =
                0;

        _contactSuggestionPopup
            .PlacementTarget =
                textBox;

        var popupWidth =
            Math.Max(
                320,
                textBox.ActualWidth);

        _contactSuggestionBorder.Width =
            popupWidth;

        _contactSuggestionPopup
            .IsOpen =
                true;
    }

    private void CloseContactSuggestions()
    {
        if (_contactSuggestionPopup is not null)
        {
            _contactSuggestionPopup.IsOpen =
                false;
        }

        if (_contactSuggestionListBox is not null)
        {
            _contactSuggestionListBox.ItemsSource =
                null;

            _contactSuggestionListBox.SelectedIndex =
                -1;
        }

        _activeContactSuggestionTextBox =
            null;
    }

    private void ContactAutocompleteTextBox_OnPreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (sender is not TextBox textBox ||
            _contactSuggestionPopup is null ||
            _contactSuggestionListBox is null ||
            !_contactSuggestionPopup.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                MoveContactSuggestionSelection(
                    1);

                e.Handled =
                    true;

                break;

            case Key.Up:
                MoveContactSuggestionSelection(
                    -1);

                e.Handled =
                    true;

                break;

            case Key.Enter:
                if (_contactSuggestionListBox
                        .SelectedItem
                    is ContactEmailSuggestion suggestion)
                {
                    ApplyContactSuggestion(
                        textBox,
                        suggestion);

                    e.Handled =
                        true;
                }

                break;

            case Key.Escape:
                CloseContactSuggestions();

                e.Handled =
                    true;

                break;
        }
    }

    private void MoveContactSuggestionSelection(
        int direction)
    {
        if (_contactSuggestionListBox is null ||
            _contactSuggestionListBox.Items.Count ==
            0)
        {
            return;
        }

        var currentIndex =
            _contactSuggestionListBox
                .SelectedIndex;

        var nextIndex =
            currentIndex +
            direction;

        if (nextIndex < 0)
        {
            nextIndex =
                _contactSuggestionListBox
                    .Items.Count - 1;
        }
        else if (nextIndex >=
                 _contactSuggestionListBox
                     .Items.Count)
        {
            nextIndex =
                0;
        }

        _contactSuggestionListBox
            .SelectedIndex =
                nextIndex;

        _contactSuggestionListBox
            .ScrollIntoView(
                _contactSuggestionListBox
                    .SelectedItem);
    }

    private void ContactSuggestionListBox_OnPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (_activeContactSuggestionTextBox is null ||
            _contactSuggestionListBox is null)
        {
            return;
        }

        var source =
            e.OriginalSource
                as DependencyObject;

        var item =
            FindParentListBoxItem(
                source);

        if (item?.DataContext
            is not ContactEmailSuggestion suggestion)
        {
            return;
        }

        ApplyContactSuggestion(
            _activeContactSuggestionTextBox,
            suggestion);

        e.Handled =
            true;
    }

    private static ListBoxItem?
        FindParentListBoxItem(
            DependencyObject? source)
    {
        var current =
            source;

        while (current is not null)
        {
            if (current
                is ListBoxItem item)
            {
                return item;
            }

            current =
                VisualTreeHelper
                    .GetParent(
                        current);
        }

        return null;
    }

    private void ApplyContactSuggestion(
        TextBox textBox,
        ContactEmailSuggestion suggestion)
    {
        var token =
            GetRecipientToken(
                textBox);

        var currentText =
            textBox.Text
            ?? string.Empty;

        var before =
            currentText[
                ..token.ContentStart];

        var after =
            currentText[
                token.ContentEnd..];

        _suppressContactAutocomplete =
            true;

        try
        {
            textBox.Text =
                before +
                suggestion.EmailAddress +
                after;

            textBox.CaretIndex =
                before.Length +
                suggestion.EmailAddress.Length;
        }
        finally
        {
            _suppressContactAutocomplete =
                false;
        }

        CloseContactSuggestions();

        textBox.Focus();
    }

    private static RecipientToken GetRecipientToken(
        TextBox textBox)
    {
        var text =
            textBox.Text
            ?? string.Empty;

        var caretIndex =
            Math.Clamp(
                textBox.CaretIndex,
                0,
                text.Length);

        var tokenStart =
            caretIndex;

        while (tokenStart > 0)
        {
            var previousCharacter =
                text[tokenStart - 1];

            if (IsRecipientSeparator(
                    previousCharacter))
            {
                break;
            }

            tokenStart--;
        }

        var tokenEnd =
            caretIndex;

        while (tokenEnd <
               text.Length)
        {
            var character =
                text[tokenEnd];

            if (IsRecipientSeparator(
                    character))
            {
                break;
            }

            tokenEnd++;
        }

        var contentStart =
            tokenStart;

        while (contentStart <
               tokenEnd &&
               char.IsWhiteSpace(
                   text[contentStart]))
        {
            contentStart++;
        }

        var contentEnd =
            tokenEnd;

        while (contentEnd >
               contentStart &&
               char.IsWhiteSpace(
                   text[contentEnd - 1]))
        {
            contentEnd--;
        }

        var query =
            contentStart <
            contentEnd
                ? text[
                    contentStart..contentEnd]
                    .Trim()
                : string.Empty;

        return new RecipientToken(
            Query:
                query,

            ContentStart:
                contentStart,

            ContentEnd:
                contentEnd);
    }

    private static bool IsRecipientSeparator(
        char character)
    {
        return
            character == ';' ||
            character == ',';
    }

    private bool IsContactAutocompleteTextBox(
        TextBox textBox)
    {
        return
            ReferenceEquals(
                textBox,
                RecipientTextBox) ||
            ReferenceEquals(
                textBox,
                CcTextBox) ||
            ReferenceEquals(
                textBox,
                BccTextBox);
    }

    private void ComposeWindowContactAutocomplete_OnClosed(
        object? sender,
        EventArgs e)
    {
        Closed -=
            ComposeWindowContactAutocomplete_OnClosed;

        RecipientTextBox.TextChanged -=
            ContactAutocompleteTextBox_OnTextChanged;

        RecipientTextBox.PreviewKeyDown -=
            ContactAutocompleteTextBox_OnPreviewKeyDown;

        RecipientTextBox.GotKeyboardFocus -=
            ContactAutocompleteTextBox_OnGotKeyboardFocus;

        CcTextBox.TextChanged -=
            ContactAutocompleteTextBox_OnTextChanged;

        CcTextBox.PreviewKeyDown -=
            ContactAutocompleteTextBox_OnPreviewKeyDown;

        CcTextBox.GotKeyboardFocus -=
            ContactAutocompleteTextBox_OnGotKeyboardFocus;

        BccTextBox.TextChanged -=
            ContactAutocompleteTextBox_OnTextChanged;

        BccTextBox.PreviewKeyDown -=
            ContactAutocompleteTextBox_OnPreviewKeyDown;

        BccTextBox.GotKeyboardFocus -=
            ContactAutocompleteTextBox_OnGotKeyboardFocus;

        CloseContactSuggestions();

        var cancellationTokenSource =
            _contactAutocompleteCancellationTokenSource;

        _contactAutocompleteCancellationTokenSource =
            null;

        if (cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    private sealed record ContactEmailSuggestion(
        string DisplayName,
        string EmailAddress,
        string EmailType,
        IReadOnlyList<string> SearchValues)
    {
        public string DisplayText =>
            string.Equals(
                DisplayName,
                EmailAddress,
                StringComparison.OrdinalIgnoreCase)
                ? $"{EmailAddress} · {EmailType}"
                : $"{DisplayName} — {EmailAddress} · {EmailType}";
    }

    private sealed record RecipientToken(
        string Query,
        int ContentStart,
        int ContentEnd);
}