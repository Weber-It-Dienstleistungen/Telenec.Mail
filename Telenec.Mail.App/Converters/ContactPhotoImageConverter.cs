using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Telenec.Mail.App.Converters;

public sealed class ContactPhotoImageConverter :
    IValueConverter
{
    public object? Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not byte[] data ||
            data.Length == 0)
        {
            return null;
        }

        try
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
        catch
        {
            /*
             * Ein beschädigtes oder unbekanntes Bild darf
             * niemals die komplette Kontaktansicht stören.
             *
             * null bedeutet für die Oberfläche:
             * Standardsymbol anzeigen.
             */
            return null;
        }
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}