using MailKit;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Telenec.Mail.App.Services.Security;
using Telenec.Mail.App.Services.Storage;

namespace Telenec.Mail.App.Services.Migration;

internal sealed class RawImapAppendService
{
    private const string TargetImapHost =
        "mail.necnet.de";

    private const int TargetImapPort =
        993;

    private static readonly Regex AppendUidRegex =
        new(
            @"\[APPENDUID\s+\d+\s+(?<uid>\d+)\]",
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant);

    private readonly IMailAccountStore
        _mailAccountStore;

    private readonly ICredentialStore
        _credentialStore;

    public RawImapAppendService(
        IMailAccountStore mailAccountStore,
        ICredentialStore credentialStore)
    {
        ArgumentNullException.ThrowIfNull(
            mailAccountStore);

        ArgumentNullException.ThrowIfNull(
            credentialStore);

        _mailAccountStore =
            mailAccountStore;

        _credentialStore =
            credentialStore;
    }

    public async Task<UniqueId>
        AppendAsync(
            string folderFullName,
            byte[] rawMessage,
            MessageFlags flags,
            IReadOnlyCollection<string> keywords,
            DateTimeOffset internalDate,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            folderFullName);

        ArgumentNullException.ThrowIfNull(
            rawMessage);

        ArgumentNullException.ThrowIfNull(
            keywords);

        var account =
            await _mailAccountStore
                .GetActiveAccountAsync(
                    cancellationToken);

        if (account is null ||
            string.IsNullOrWhiteSpace(
                account.EmailAddress))
        {
            throw new InvalidOperationException(
                "Das aktive Zielkonto konnte für den RAW-IMAP-Upload nicht ermittelt werden.");
        }

        var credential =
            await _credentialStore
                .ReadAsync(
                    account.AccountId,
                    cancellationToken);

        if (credential is null ||
            string.IsNullOrEmpty(
                credential.Password))
        {
            throw new InvalidOperationException(
                "Für das neue Telenec-Mail-Konto sind keine gültigen Zugangsdaten gespeichert.");
        }

        using var tcpClient =
            new TcpClient();

        await tcpClient.ConnectAsync(
            TargetImapHost,
            TargetImapPort,
            cancellationToken);

        await using var sslStream =
            new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false);

        await sslStream.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost =
                    TargetImapHost
            },
            cancellationToken);

        var greeting =
            await ReadLineAsync(
                sslStream,
                cancellationToken);

        if (!greeting.StartsWith(
                "* OK",
                StringComparison.OrdinalIgnoreCase) &&
            !greeting.StartsWith(
                "* PREAUTH",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "Der neue IMAP-Server hat keine gültige Begrüßung geliefert.");
        }

        var commandNumber =
            1;

        string NextTag()
        {
            return
                $"TM{commandNumber++:0000}";
        }

        var preAuthenticated =
            greeting.StartsWith(
                "* PREAUTH",
                StringComparison.OrdinalIgnoreCase);

        if (!preAuthenticated)
        {
            var capabilityTag =
                NextTag();

            await WriteLineAsync(
                sslStream,
                $"{capabilityTag} CAPABILITY",
                cancellationToken);

            var capabilityResponse =
                await ReadTaggedResponseAsync(
                    sslStream,
                    capabilityTag,
                    cancellationToken);

            EnsureOk(
                capabilityTag,
                capabilityResponse.TaggedLine,
                "CAPABILITY");

            var supportsPlainAuthentication =
                capabilityResponse
                    .Lines
                    .Any(
                        line =>
                            line.Contains(
                                "AUTH=PLAIN",
                                StringComparison.OrdinalIgnoreCase));

            if (supportsPlainAuthentication)
            {
                await AuthenticatePlainAsync(
                    sslStream,
                    account.EmailAddress,
                    credential.Password,
                    NextTag(),
                    cancellationToken);
            }
            else
            {
                await AuthenticateLoginAsync(
                    sslStream,
                    account.EmailAddress,
                    credential.Password,
                    NextTag(),
                    cancellationToken);
            }
        }

        var encodedFolderName =
            EncodeModifiedUtf7(
                folderFullName);

        var flagList =
            BuildFlagList(
                flags,
                keywords);

        var appendTag =
            NextTag();

        var appendCommand =
            new StringBuilder();

        appendCommand.Append(
            appendTag);

        appendCommand.Append(
            " APPEND ");

        appendCommand.Append(
            QuoteString(
                encodedFolderName));

        if (flagList.Count > 0)
        {
            appendCommand.Append(
                " (");

            appendCommand.Append(
                string.Join(
                    ' ',
                    flagList));

            appendCommand.Append(
                ')');
        }

        appendCommand.Append(
            " \"");

        appendCommand.Append(
            FormatInternalDate(
                internalDate));

        appendCommand.Append(
            "\" {");

        appendCommand.Append(
            rawMessage.Length.ToString(
                CultureInfo.InvariantCulture));

        appendCommand.Append(
            '}');

        await WriteLineAsync(
            sslStream,
            appendCommand.ToString(),
            cancellationToken);

        /*
         * Wir verwenden absichtlich ein synchronisierendes
         * IMAP-Literal.
         *
         * Der Server muss mit "+" bestätigen, bevor auch
         * nur ein Byte der Nachricht gesendet wird.
         */
        while (true)
        {
            var continuation =
                await ReadLineAsync(
                    sslStream,
                    cancellationToken);

            if (continuation.StartsWith(
                    "+",
                    StringComparison.Ordinal))
            {
                break;
            }

            if (continuation.StartsWith(
                    appendTag + " ",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Der neue IMAP-Server hat den RAW-APPEND vor Übertragung des Literals abgelehnt.\n" +
                    continuation);
            }
        }

        /*
         * ENTSCHEIDEND:
         *
         * Es werden exakt rawMessage.Length Bytes als
         * IMAP-Literal übertragen.
         *
         * Weder MimeKit noch MailKit können hier
         * Zeilenenden verändern oder ein CRLF ergänzen.
         */
        await sslStream.WriteAsync(
            rawMessage,
            cancellationToken);

        /*
         * Dieses CRLF gehört zum IMAP-Kommando NACH dem
         * Literal und ist ausdrücklich NICHT Bestandteil
         * der zuvor angekündigten Literal-Länge.
         */
        await sslStream.WriteAsync(
            "\r\n"u8.ToArray(),
            cancellationToken);

        await sslStream.FlushAsync(
            cancellationToken);

        var appendResponse =
            await ReadTaggedResponseAsync(
                sslStream,
                appendTag,
                cancellationToken);

        EnsureOk(
            appendTag,
            appendResponse.TaggedLine,
            "APPEND");

        var appendUidMatch =
            AppendUidRegex.Match(
                appendResponse.TaggedLine);

        if (!appendUidMatch.Success ||
            !uint.TryParse(
                appendUidMatch
                    .Groups["uid"]
                    .Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var appendedUid) ||
            appendedUid == 0)
        {
            throw new IOException(
                "Der neue IMAP-Server hat den RAW-APPEND bestätigt, aber keine eindeutige APPENDUID geliefert. " +
                "Die Quellmail bleibt deshalb erhalten.");
        }

        /*
         * LOGOUT ist nur Best-Effort.
         *
         * Der erfolgreiche APPEND ist bereits bestätigt.
         */
        try
        {
            var logoutTag =
                NextTag();

            await WriteLineAsync(
                sslStream,
                $"{logoutTag} LOGOUT",
                CancellationToken.None);

            await ReadTaggedResponseAsync(
                sslStream,
                logoutTag,
                CancellationToken.None);
        }
        catch
        {
        }

        return new UniqueId(
            appendedUid);
    }

    private static async Task
        AuthenticatePlainAsync(
            Stream stream,
            string userName,
            string password,
            string tag,
            CancellationToken cancellationToken)
    {
        await WriteLineAsync(
            stream,
            $"{tag} AUTHENTICATE PLAIN",
            cancellationToken);

        while (true)
        {
            var line =
                await ReadLineAsync(
                    stream,
                    cancellationToken);

            if (line.StartsWith(
                    "+",
                    StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith(
                    tag + " ",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    "Der neue IMAP-Server hat AUTHENTICATE PLAIN abgelehnt.\n" +
                    line);
            }
        }

        var authenticationBytes =
            Encoding.UTF8.GetBytes(
                "\0" +
                userName +
                "\0" +
                password);

        var authenticationValue =
            Convert.ToBase64String(
                authenticationBytes);

        await WriteLineAsync(
            stream,
            authenticationValue,
            cancellationToken);

        var response =
            await ReadTaggedResponseAsync(
                stream,
                tag,
                cancellationToken);

        EnsureOk(
            tag,
            response.TaggedLine,
            "AUTHENTICATE PLAIN");
    }

    private static async Task
        AuthenticateLoginAsync(
            Stream stream,
            string userName,
            string password,
            string tag,
            CancellationToken cancellationToken)
    {
        if (userName.Contains(
                '\r') ||
            userName.Contains(
                '\n') ||
            password.Contains(
                '\r') ||
            password.Contains(
                '\n'))
        {
            throw new InvalidOperationException(
                "Die Zugangsdaten können nicht sicher per IMAP LOGIN übertragen werden.");
        }

        await WriteLineAsync(
            stream,
            $"{tag} LOGIN {QuoteString(userName)} {QuoteString(password)}",
            cancellationToken);

        var response =
            await ReadTaggedResponseAsync(
                stream,
                tag,
                cancellationToken);

        EnsureOk(
            tag,
            response.TaggedLine,
            "LOGIN");
    }

    private static List<string>
        BuildFlagList(
            MessageFlags flags,
            IReadOnlyCollection<string> keywords)
    {
        var result =
            new List<string>();

        if (flags.HasFlag(
                MessageFlags.Seen))
        {
            result.Add(
                @"\Seen");
        }

        if (flags.HasFlag(
                MessageFlags.Answered))
        {
            result.Add(
                @"\Answered");
        }

        if (flags.HasFlag(
                MessageFlags.Flagged))
        {
            result.Add(
                @"\Flagged");
        }

        if (flags.HasFlag(
                MessageFlags.Draft))
        {
            result.Add(
                @"\Draft");
        }

        foreach (var keyword in
                 keywords)
        {
            if (string.IsNullOrWhiteSpace(
                    keyword))
            {
                continue;
            }

            if (!IsSafeImapAtom(
                    keyword))
            {
                throw new InvalidOperationException(
                    $"Das IMAP-Keyword „{keyword}“ kann im RAW-APPEND nicht sicher als IMAP-Atom übertragen werden.");
            }

            result.Add(
                keyword);
        }

        return result;
    }

    private static bool
        IsSafeImapAtom(
            string value)
    {
        foreach (var character in
                 value)
        {
            if (character <= 0x20 ||
                character >= 0x7f)
            {
                return false;
            }

            if (character is
                '(' or
                ')' or
                '{' or
                '}' or
                '%' or
                '*' or
                '"' or
                '\\' or
                ']')
            {
                return false;
            }
        }

        return true;
    }

    private static string
        FormatInternalDate(
            DateTimeOffset value)
    {
        var offset =
            value.Offset;

        var sign =
            offset < TimeSpan.Zero
                ? "-"
                : "+";

        var absoluteOffset =
            offset.Duration();

        var offsetHours =
            (int)absoluteOffset.TotalHours;

        var offsetMinutes =
            absoluteOffset.Minutes;

        return
            value.ToString(
                "dd-MMM-yyyy HH:mm:ss",
                CultureInfo.InvariantCulture) +
            " " +
            sign +
            offsetHours.ToString(
                "00",
                CultureInfo.InvariantCulture) +
            offsetMinutes.ToString(
                "00",
                CultureInfo.InvariantCulture);
    }

    private static string
        QuoteString(
            string value)
    {
        if (value.Contains(
                '\r') ||
            value.Contains(
                '\n'))
        {
            throw new InvalidOperationException(
                "Ein IMAP-String enthält unzulässige Zeilenumbrüche.");
        }

        return
            "\"" +
            value
                .Replace(
                    "\\",
                    "\\\\",
                    StringComparison.Ordinal)
                .Replace(
                    "\"",
                    "\\\"",
                    StringComparison.Ordinal) +
            "\"";
    }

    private static string
        EncodeModifiedUtf7(
            string value)
    {
        var result =
            new StringBuilder();

        var unicodeBuffer =
            new StringBuilder();

        void FlushUnicodeBuffer()
        {
            if (unicodeBuffer.Length == 0)
            {
                return;
            }

            var bytes =
                Encoding.BigEndianUnicode.GetBytes(
                    unicodeBuffer.ToString());

            var base64 =
                Convert
                    .ToBase64String(
                        bytes)
                    .TrimEnd(
                        '=')
                    .Replace(
                        '/',
                        ',');

            result.Append(
                '&');

            result.Append(
                base64);

            result.Append(
                '-');

            unicodeBuffer.Clear();
        }

        foreach (var character in
                 value)
        {
            if (character >= 0x20 &&
                character <= 0x7e)
            {
                FlushUnicodeBuffer();

                if (character ==
                    '&')
                {
                    result.Append(
                        "&-");
                }
                else
                {
                    result.Append(
                        character);
                }

                continue;
            }

            unicodeBuffer.Append(
                character);
        }

        FlushUnicodeBuffer();

        return result.ToString();
    }

    private static async Task
        WriteLineAsync(
            Stream stream,
            string value,
            CancellationToken cancellationToken)
    {
        var bytes =
            Encoding.UTF8.GetBytes(
                value +
                "\r\n");

        await stream.WriteAsync(
            bytes,
            cancellationToken);

        await stream.FlushAsync(
            cancellationToken);
    }

    private static async Task<string>
        ReadLineAsync(
            Stream stream,
            CancellationToken cancellationToken)
    {
        using var buffer =
            new MemoryStream();

        var singleByte =
            new byte[1];

        while (true)
        {
            var bytesRead =
                await stream.ReadAsync(
                    singleByte,
                    cancellationToken);

            if (bytesRead == 0)
            {
                throw new EndOfStreamException(
                    "Die IMAP-Verbindung wurde unerwartet beendet.");
            }

            if (singleByte[0] ==
                (byte)'\n')
            {
                break;
            }

            if (singleByte[0] !=
                (byte)'\r')
            {
                buffer.WriteByte(
                    singleByte[0]);
            }
        }

        return Encoding.UTF8.GetString(
            buffer.ToArray());
    }

    private static async Task<TaggedResponse>
        ReadTaggedResponseAsync(
            Stream stream,
            string tag,
            CancellationToken cancellationToken)
    {
        var lines =
            new List<string>();

        while (true)
        {
            var line =
                await ReadLineAsync(
                    stream,
                    cancellationToken);

            lines.Add(
                line);

            if (line.StartsWith(
                    tag + " ",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new TaggedResponse(
                    line,
                    lines);
            }
        }
    }

    private static void
        EnsureOk(
            string tag,
            string taggedLine,
            string commandName)
    {
        if (taggedLine.StartsWith(
                tag + " OK",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new IOException(
            $"Der IMAP-Befehl {commandName} wurde vom neuen Server nicht bestätigt.\n" +
            taggedLine);
    }

    private sealed record TaggedResponse(
        string TaggedLine,
        IReadOnlyList<string> Lines);
}