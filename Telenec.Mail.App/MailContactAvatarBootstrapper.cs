using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Telenec.Mail.App.Models;
using Telenec.Mail.App.Services.Contacts;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

internal static class MailContactAvatarBootstrapper
{
    private static readonly ConditionalWeakTable<
        Window,
        WindowAvatarState>
        WindowStates =
            new();

    private static readonly ConditionalWeakTable<
        Border,
        AvatarVisualState>
        AvatarVisualStates =
            new();

    private static readonly SemaphoreSlim
        ContactLoadGate =
            new(
                1,
                1);

    private static IReadOnlyList<ContactData>
        _cachedContacts =
            Array.Empty<ContactData>();

    private static DateTimeOffset
        _contactsLoadedAtUtc =
            DateTimeOffset.MinValue;

    private static bool
        _contactLoadAttempted;

    private static readonly TimeSpan
        ContactCacheLifetime =
            TimeSpan.FromSeconds(30);

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

        EventManager.RegisterClassHandler(
            typeof(MailMessageWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(
                MailMessageWindow_OnLoaded),
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

        var state =
            WindowStates.GetOrCreateValue(
                window);

        if (!state.IsAttached)
        {
            state.IsAttached =
                true;

            if (window.DataContext
                is MainViewModel viewModel)
            {
                PropertyChangedEventHandler handler =
                    (_, args) =>
                    {
                        if (args.PropertyName !=
                            nameof(
                                MainViewModel
                                    .SelectedMessage))
                        {
                            return;
                        }

                        _ =
                            RefreshMainWindowAvatarAsync(
                                window,
                                viewModel,
                                state);
                    };

                state.MainViewModel =
                    viewModel;

                state.PropertyChangedHandler =
                    handler;

                viewModel.PropertyChanged +=
                    handler;
            }

            window.Closed +=
                Window_OnClosed;
        }

        if (window.DataContext
            is MainViewModel currentViewModel)
        {
            _ =
                RefreshMainWindowAvatarAsync(
                    window,
                    currentViewModel,
                    state);
        }
    }

    private static void MailMessageWindow_OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not MailMessageWindow window ||
            window.DataContext
                is not MailMessageItemViewModel message)
        {
            return;
        }

        var state =
            WindowStates.GetOrCreateValue(
                window);

        if (!state.IsAttached)
        {
            state.IsAttached =
                true;

            window.Closed +=
                Window_OnClosed;
        }

        _ =
            RefreshReaderAvatarAsync(
                window,
                message,
                state);
    }

    private static void Window_OnClosed(
        object? sender,
        EventArgs e)
    {
        if (sender is not Window window ||
            !WindowStates.TryGetValue(
                window,
                out var state))
        {
            return;
        }

        if (state.MainViewModel is not null &&
            state.PropertyChangedHandler is not null)
        {
            state.MainViewModel.PropertyChanged -=
                state.PropertyChangedHandler;
        }

        window.Closed -=
            Window_OnClosed;

        WindowStates.Remove(
            window);
    }

    private static async Task
        RefreshMainWindowAvatarAsync(
            MainWindow window,
            MainViewModel viewModel,
            WindowAvatarState state)
    {
        var message =
            viewModel.SelectedMessage;

        var generation =
            ++state.Generation;

        if (message is null)
        {
            ApplyAvatar(
                window,
                targetBindingPath:
                    "SelectedMessage.SenderInitial",
                photoData:
                    null);

            return;
        }

        var photoData =
            await FindContactPhotoAsync(
                message.SenderAddress);

        /*
         * Während CardDAV geladen wurde, kann der Benutzer
         * längst eine andere Mail ausgewählt haben.
         *
         * In diesem Fall darf das alte Foto nicht mehr
         * eingeblendet werden.
         */
        if (generation !=
                state.Generation ||
            !ReferenceEquals(
                viewModel.SelectedMessage,
                message))
        {
            return;
        }

        ApplyAvatar(
            window,
            targetBindingPath:
                "SelectedMessage.SenderInitial",
            photoData);
    }

    private static async Task
        RefreshReaderAvatarAsync(
            MailMessageWindow window,
            MailMessageItemViewModel message,
            WindowAvatarState state)
    {
        var generation =
            ++state.Generation;

        var photoData =
            await FindContactPhotoAsync(
                message.SenderAddress);

        if (generation !=
            state.Generation)
        {
            return;
        }

        ApplyAvatar(
            window,
            targetBindingPath:
                "SenderInitial",
            photoData);
    }

    private static async Task<byte[]?>
        FindContactPhotoAsync(
            string? senderAddress)
    {
        var normalizedAddress =
            NormalizeEmailAddress(
                senderAddress);

        if (normalizedAddress is null)
        {
            return null;
        }

        try
        {
            var contacts =
                await GetContactsAsync();

            var contact =
                contacts
                    .FirstOrDefault(
                        candidate =>
                            ContactContainsEmail(
                                candidate,
                                normalizedAddress));

            if (contact?.PhotoData is not
                { Length: > 0 })
            {
                return null;
            }

            return contact.PhotoData;
        }
        catch
        {
            /*
             * Ein CardDAV-Problem darf niemals verhindern,
             * dass eine Mail gelesen wird.
             *
             * Im Fehlerfall bleibt deshalb einfach der
             * bisherige Initial-Avatar sichtbar.
             */
            return null;
        }
    }

    private static async Task<
        IReadOnlyList<ContactData>>
        GetContactsAsync()
    {
        var now =
            DateTimeOffset.UtcNow;

        if (_contactLoadAttempted &&
            now -
            _contactsLoadedAtUtc <
            ContactCacheLifetime)
        {
            return _cachedContacts;
        }

        await ContactLoadGate
            .WaitAsync();

        try
        {
            now =
                DateTimeOffset.UtcNow;

            if (_contactLoadAttempted &&
                now -
                _contactsLoadedAtUtc <
                ContactCacheLifetime)
            {
                return _cachedContacts;
            }

            _contactLoadAttempted =
                true;

            _contactsLoadedAtUtc =
                now;

            try
            {
                if (Application.Current
                    is not App application)
                {
                    return _cachedContacts;
                }

                var contactService =
                    ActivatorUtilities
                        .CreateInstance<
                            CardDavContactService>(
                            application.Services);

                _cachedContacts =
                    await contactService
                        .GetAllAsync();
            }
            catch
            {
                /*
                 * Vorhandenen Cache behalten.
                 *
                 * Ist noch keiner vorhanden, bleibt das
                 * Ergebnis einfach leer.
                 */
            }

            return _cachedContacts;
        }
        finally
        {
            ContactLoadGate.Release();
        }
    }

    private static bool ContactContainsEmail(
        ContactData contact,
        string normalizedAddress)
    {
        if (EmailMatches(
                contact.EmailAddress,
                normalizedAddress) ||
            EmailMatches(
                contact.BusinessEmailAddress,
                normalizedAddress) ||
            EmailMatches(
                contact.PrivateEmailAddress,
                normalizedAddress))
        {
            return true;
        }

        return contact
            .AdditionalEmailAddresses
            .Any(
                address =>
                    EmailMatches(
                        address,
                        normalizedAddress));
    }

    private static bool EmailMatches(
        string? candidate,
        string normalizedAddress)
    {
        var normalizedCandidate =
            NormalizeEmailAddress(
                candidate);

        return normalizedCandidate is not null &&
               string.Equals(
                   normalizedCandidate,
                   normalizedAddress,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string?
        NormalizeEmailAddress(
            string? address)
    {
        if (string.IsNullOrWhiteSpace(
                address))
        {
            return null;
        }

        return address.Trim();
    }

    private static void ApplyAvatar(
        DependencyObject root,
        string targetBindingPath,
        byte[]? photoData)
    {
        var avatar =
            FindAvatar(
                root,
                targetBindingPath);

        if (avatar is null)
        {
            return;
        }

        var border =
            avatar.Value.Border;

        var initialText =
            avatar.Value.InitialText;

        var visualState =
            AvatarVisualStates
                .GetOrCreateValue(
                    border);

        if (!visualState.HasOriginalState)
        {
            visualState.HasOriginalState =
                true;

            visualState.OriginalBackground =
                border.Background;

            visualState.OriginalTextVisibility =
                initialText.Visibility;
        }

        if (photoData is not
            { Length: > 0 })
        {
            RestoreInitialAvatar(
                border,
                initialText,
                visualState);

            return;
        }

        try
        {
            var image =
                CreateBitmapImage(
                    photoData);

            border.Background =
                new ImageBrush(
                    image)
                {
                    Stretch =
                        Stretch.UniformToFill,

                    AlignmentX =
                        AlignmentX.Center,

                    AlignmentY =
                        AlignmentY.Center
                };

            initialText.Visibility =
                Visibility.Collapsed;
        }
        catch
        {
            RestoreInitialAvatar(
                border,
                initialText,
                visualState);
        }
    }

    private static void RestoreInitialAvatar(
        Border border,
        TextBlock initialText,
        AvatarVisualState visualState)
    {
        border.Background =
            visualState.OriginalBackground;

        initialText.Visibility =
            visualState.OriginalTextVisibility;
    }

    private static (
        Border Border,
        TextBlock InitialText)?
        FindAvatar(
            DependencyObject root,
            string targetBindingPath)
    {
        foreach (var textBlock in
                 FindVisualChildren<TextBlock>(
                     root))
        {
            var bindingExpression =
                BindingOperations
                    .GetBindingExpression(
                        textBlock,
                        TextBlock.TextProperty);

            var bindingPath =
                bindingExpression?
                    .ParentBinding
                    .Path?
                    .Path;

            if (!string.Equals(
                    bindingPath,
                    targetBindingPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (VisualTreeHelper.GetParent(
                    textBlock)
                is Border border)
            {
                return (
                    border,
                    textBlock);
            }
        }

        return null;
    }

    private static BitmapImage
        CreateBitmapImage(
            byte[] data)
    {
        using var stream =
            new MemoryStream(
                data,
                writable:
                    false);

        var image =
            new BitmapImage();

        image.BeginInit();

        image.CacheOption =
            BitmapCacheOption.OnLoad;

        image.StreamSource =
            stream;

        image.EndInit();

        image.Freeze();

        return image;
    }

    private static IEnumerable<T>
        FindVisualChildren<T>(
            DependencyObject root)
        where T : DependencyObject
    {
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

    private sealed class WindowAvatarState
    {
        public bool IsAttached { get; set; }

        public int Generation { get; set; }

        public MainViewModel?
            MainViewModel
        { get; set; }

        public PropertyChangedEventHandler?
            PropertyChangedHandler
        { get; set; }
    }

    private sealed class AvatarVisualState
    {
        public bool HasOriginalState { get; set; }

        public Brush?
            OriginalBackground
        { get; set; }

        public Visibility
            OriginalTextVisibility
        { get; set; } =
            Visibility.Visible;
    }
}