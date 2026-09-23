using System.Windows.Media;

namespace Telenec.Mail.App.ViewModels;

public sealed record MailCategoryDefinition(
    string Key,
    string DisplayName,
    string Keyword,
    Brush Background,
    Brush Foreground);

public static class MailCategoryCatalog
{
    public static MailCategoryDefinition Red { get; } =
        new(
            Key:
                "Red",

            DisplayName:
                "Rot",

            Keyword:
                "TelenecCategory-Red",

            Background:
                CreateBrush(
                    "#CF222E"),

            Foreground:
                Brushes.White);

    public static IReadOnlyList<MailCategoryDefinition>
        All
    { get; } =
        [
            Red
        ];

    private static Brush CreateBrush(
        string colorValue)
    {
        var color =
            (Color)ColorConverter.ConvertFromString(
                colorValue);

        var brush =
            new SolidColorBrush(
                color);

        brush.Freeze();

        return brush;
    }
}