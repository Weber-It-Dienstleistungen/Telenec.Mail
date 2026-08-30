using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using System.IO;
using System.Net.Sockets;

namespace Telenec.Mail.App.Services.Mail;

public sealed class MailKitAuthenticationService :
    IMailAuthenticationService
{
    private const string ImapHost =
        "mail.necnet.de";

    private const int ImapPort =
        993;

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(15);

    private static readonly TimeSpan AuthenticationTimeout =
        TimeSpan.FromSeconds(30);

    public async Task<MailAuthenticationResult> AuthenticateAsync(
        string emailAddress,
        string password,
        CancellationToken cancellationToken = default)
    {
        using var client =
            new ImapClient();

        try
        {
            using (var connectionTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                connectionTimeoutSource.CancelAfter(
                    ConnectionTimeout);

                await client.ConnectAsync(
                    ImapHost,
                    ImapPort,
                    SecureSocketOptions.SslOnConnect,
                    connectionTimeoutSource.Token);
            }

            using (var authenticationTimeoutSource =
                   CancellationTokenSource.CreateLinkedTokenSource(
                       cancellationToken))
            {
                authenticationTimeoutSource.CancelAfter(
                    AuthenticationTimeout);

                await client.AuthenticateAsync(
                    emailAddress,
                    password,
                    authenticationTimeoutSource.Token);
            }

            var capabilities =
                client.Capabilities.ToString();

            return MailAuthenticationResult.Success(
                capabilities);
        }
        catch (MailKit.Security.AuthenticationException)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.InvalidCredentials);
        }
        catch (SslHandshakeException)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.CertificateError);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.Timeout);
        }
        catch (SocketException)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.ServerUnavailable);
        }
        catch (IOException)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.ServerUnavailable);
        }
        catch (ProtocolException)
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.ServerUnavailable);
        }
        catch
        {
            return MailAuthenticationResult.FromStatus(
                MailAuthenticationStatus.Failed);
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(
                        true,
                        CancellationToken.None);
                }
                catch
                {
                    // Ein Fehler beim Disconnect darf das Ergebnis
                    // des eigentlichen Loginversuchs nicht verändern.
                }
            }
        }
    }
}