using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Telenec.Mail.App.ViewModels;

namespace Telenec.Mail.App;

public partial class MailMessageWindow
{
    private MailMessageWindowActions?
        _actions;

    private bool
        _isExecutingMessageAction;

    internal MailMessageWindow(
        MailMessageItemViewModel message,
        MailMessageWindowActions actions)
        : this(
            message)
    {
        ArgumentNullException.ThrowIfNull(
            actions);

        _actions =
            actions;

        ConfigureMessageActions();
    }

    private void ConfigureMessageActions()
    {
        var actions =
            _actions;

        if (actions is null)
        {
            MessageActionsBar.Visibility =
                Visibility.Collapsed;

            return;
        }

        MessageActionsBar.Visibility =
            Visibility.Visible;

        ReplyButton.IsEnabled =
            actions.ReplyAsync is not null;

        ReplyAllButton.IsEnabled =
            actions.ReplyAllAsync is not null;

        ForwardButton.IsEnabled =
            actions.ForwardAsync is not null;

        PrimaryMessageActionButton.Content =
            actions.PrimaryActionText;

        PrimaryMessageActionButton.ToolTip =
            actions.PrimaryActionToolTip;

        PrimaryMessageActionButton.IsEnabled =
            actions.PrimaryActionAsync is not null;

        if (actions.PermanentDeleteAsync is null)
        {
            PermanentDeleteButton.Visibility =
                Visibility.Collapsed;
        }
        else
        {
            PermanentDeleteButton.Visibility =
                Visibility.Visible;

            PermanentDeleteButton.IsEnabled =
                true;
        }
    }

    private async void ReplyButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var action =
            _actions?
                .ReplyAsync;

        if (action is null)
        {
            return;
        }

        await ExecuteNonClosingActionAsync(
            action);
    }

    private async void ReplyAllButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var action =
            _actions?
                .ReplyAllAsync;

        if (action is null)
        {
            return;
        }

        await ExecuteNonClosingActionAsync(
            action);
    }

    private async void ForwardButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var action =
            _actions?
                .ForwardAsync;

        if (action is null)
        {
            return;
        }

        await ExecuteNonClosingActionAsync(
            action);
    }

    private async void PrimaryMessageActionButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var action =
            _actions?
                .PrimaryActionAsync;

        if (action is null ||
            _isExecutingMessageAction)
        {
            return;
        }

        _isExecutingMessageAction =
            true;

        SetMessageActionButtonsEnabled(
            false);

        Mouse.OverrideCursor =
            Cursors.Wait;

        try
        {
            var completed =
                await action();

            if (completed)
            {
                Close();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Nachrichtenaktion konnte nicht abgeschlossen werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isExecutingMessageAction =
                false;

            if (IsVisible)
            {
                SetMessageActionButtonsEnabled(
                    true);
            }
        }
    }

    private async void PermanentDeleteButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        var action =
            _actions?
                .PermanentDeleteAsync;

        if (action is null ||
            _isExecutingMessageAction)
        {
            return;
        }

        _isExecutingMessageAction =
            true;

        SetMessageActionButtonsEnabled(
            false);

        Mouse.OverrideCursor =
            Cursors.Wait;

        try
        {
            var completed =
                await action();

            if (completed)
            {
                Close();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Nachricht konnte nicht endgültig gelöscht werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isExecutingMessageAction =
                false;

            if (IsVisible)
            {
                SetMessageActionButtonsEnabled(
                    true);
            }
        }
    }

    private void PrintButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            var printWindow =
                new MailPrintWindow(
                    _message,
                    _allowExternalImages)
                {
                    Owner =
                        this
                };

            printWindow.ShowDialog();
        }
        catch
        {
            MessageBox.Show(
                this,
                "Die Druckansicht konnte nicht geöffnet werden.",
                "Drucken nicht möglich",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ExecuteNonClosingActionAsync(
        Func<Task> action)
    {
        if (_isExecutingMessageAction)
        {
            return;
        }

        _isExecutingMessageAction =
            true;

        SetMessageActionButtonsEnabled(
            false);

        Mouse.OverrideCursor =
            Cursors.Wait;

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Die Nachrichtenaktion konnte nicht geöffnet werden.\n\n" +
                exception.Message,
                "Telenec Mail",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Mouse.OverrideCursor =
                null;

            _isExecutingMessageAction =
                false;

            if (IsVisible)
            {
                SetMessageActionButtonsEnabled(
                    true);
            }
        }
    }

    private void SetMessageActionButtonsEnabled(
        bool enabled)
    {
        var actions =
            _actions;

        if (actions is null)
        {
            return;
        }

        ReplyButton.IsEnabled =
            enabled &&
            actions.ReplyAsync is not null;

        ReplyAllButton.IsEnabled =
            enabled &&
            actions.ReplyAllAsync is not null;

        ForwardButton.IsEnabled =
            enabled &&
            actions.ForwardAsync is not null;

        PrimaryMessageActionButton.IsEnabled =
            enabled &&
            actions.PrimaryActionAsync is not null;

        PermanentDeleteButton.IsEnabled =
            enabled &&
            actions.PermanentDeleteAsync is not null;

        PrintButton.IsEnabled =
            enabled;
    }
}

internal sealed record MailMessageWindowActions(
    Func<Task>? ReplyAsync,
    Func<Task>? ReplyAllAsync,
    Func<Task>? ForwardAsync,
    string PrimaryActionText,
    string PrimaryActionToolTip,
    Func<Task<bool>>? PrimaryActionAsync,
    Func<Task<bool>>? PermanentDeleteAsync);