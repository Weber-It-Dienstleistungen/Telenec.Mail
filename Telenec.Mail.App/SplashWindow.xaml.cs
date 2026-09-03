using System.Windows;
using System.Windows.Media.Animation;

namespace Telenec.Mail.App;

public partial class SplashWindow : Window
{
    private static readonly TimeSpan FadeInDuration =
        TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan FadeOutDuration =
        TimeSpan.FromMilliseconds(300);

    public SplashWindow()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(
        object sender,
        RoutedEventArgs e)
    {
        var fadeInAnimation =
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = FadeInDuration,
                FillBehavior = FillBehavior.HoldEnd
            };

        BeginAnimation(
            OpacityProperty,
            fadeInAnimation);
    }

    public void SetStatus(
        string? status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () => SetStatus(status));

            return;
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            StatusTextBlock.Text =
                string.Empty;

            StatusTextBlock.Visibility =
                Visibility.Collapsed;

            return;
        }

        StatusTextBlock.Text =
            status;

        StatusTextBlock.Visibility =
            Visibility.Visible;
    }

    public Task FadeOutAsync()
    {
        var completionSource =
            new TaskCompletionSource<bool>(
                TaskCreationOptions
                    .RunContinuationsAsynchronously);

        var fadeOutAnimation =
            new DoubleAnimation
            {
                From = Opacity,
                To = 0,
                Duration = FadeOutDuration,
                FillBehavior = FillBehavior.HoldEnd
            };

        fadeOutAnimation.Completed +=
            (_, _) =>
                completionSource.TrySetResult(
                    true);

        BeginAnimation(
            OpacityProperty,
            fadeOutAnimation);

        return completionSource.Task;
    }
}