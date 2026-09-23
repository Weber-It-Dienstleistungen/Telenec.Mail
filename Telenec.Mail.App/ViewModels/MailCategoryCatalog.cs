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

    public static MailCategoryDefinition Orange { get; } =
        new(
            Key:
                "Orange",

            DisplayName:
                "Orange",

            Keyword:
                "TelenecCategory-Orange",

            Background:
                CreateBrush(
                    "#BC4C00"),

            Foreground:
                Brushes.White);

    public static MailCategoryDefinition Yellow { get; } =
        new(
            Key:
                "Yellow",

            DisplayName:
                "Gelb",

            Keyword:
                "TelenecCategory-Yellow",

            Background:
                CreateBrush(
                    "#D4A72C"),

            Foreground:
                CreateBrush(
                    "#1F2328"));

    public static MailCategoryDefinition Green { get; } =
        new(
            Key:
                "Green",

            DisplayName:
                "Grün",

            Keyword:
                "TelenecCategory-Green",

            Background:
                CreateBrush(
                    "#1A7F37"),

            Foreground:
                Brushes.White);

    public static MailCategoryDefinition Blue { get; } =
        new(
            Key:
                "Blue",

            DisplayName:
                "Blau",

            Keyword:
                "TelenecCategory-Blue",

            Background:
                CreateBrush(
                    "#0969DA"),

            Foreground:
                Brushes.White);

    public static MailCategoryDefinition Purple { get; } =
        new(
            Key:
                "Purple",

            DisplayName:
                "Violett",

            Keyword:
                "TelenecCategory-Purple",

            Background:
                CreateBrush(
                    "#8250DF"),

            Foreground:
                Brushes.White);

    public static IReadOnlyList<MailCategoryDefinition>
        All
    { get; } =
        [
            Red,
            Orange,
            Yellow,
            Green,
            Blue,
            Purple
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