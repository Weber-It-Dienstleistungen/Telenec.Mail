using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Telenec.Mail.App.Models;

namespace Telenec.Mail.App.Converters;

public sealed class ContactVisibilityConverter :
    IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        if (value is ContactData contact &&
            parameter is string section)
        {
            return HasSectionData(
                    contact,
                    section)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return HasValue(
                value)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static bool HasSectionData(
        ContactData contact,
        string section)
    {
        return section
            .Trim()
            .ToUpperInvariant()
        switch
        {
            "NAME" =>
                HasAnyString(
                    contact.Salutation,
                    contact.AcademicTitle,
                    contact.FirstName,
                    contact.MiddleName,
                    contact.LastName,
                    contact.NameSuffix,
                    contact.Nickname),

            "EMAIL" =>
                HasAnyString(
                    contact.EmailAddress,
                    contact.BusinessEmailAddress,
                    contact.PrivateEmailAddress) ||
                contact.AdditionalEmailAddresses.Count > 0,

            "PHONE" =>
                HasAnyString(
                    contact.PhoneNumber,
                    contact.MobilePhoneNumber,
                    contact.BusinessPhoneNumber,
                    contact.PrivatePhoneNumber,
                    contact.FaxNumber) ||
                contact.AdditionalPhoneNumbers.Count > 0,

            "WORK" =>
                HasAnyString(
                    contact.Company,
                    contact.Department,
                    contact.JobTitle),

            "MORE" =>
                HasAnyString(
                    contact.Website,
                    contact.Birthday,
                    contact.Notes) ||
                contact.Categories.Count > 0,

            _ =>
                true
        };
    }

    private static bool HasValue(
        object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string text)
        {
            return !string.IsNullOrWhiteSpace(
                text);
        }

        if (value is ContactPostalAddress address)
        {
            return HasAnyString(
                address.PostOfficeBox,
                address.ExtendedAddress,
                address.Street,
                address.City,
                address.Region,
                address.PostalCode,
                address.Country);
        }

        if (value is IEnumerable enumerable)
        {
            var enumerator =
                enumerable
                    .GetEnumerator();

            try
            {
                return enumerator
                    .MoveNext();
            }
            finally
            {
                if (enumerator is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        return true;
    }

    private static bool HasAnyString(
        params string?[] values)
    {
        return values.Any(
            value =>
                !string.IsNullOrWhiteSpace(
                    value));
    }
}