using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace Telenec.Mail.App.Services.Account;

public sealed class AccountPasswordChangeApiClient :
    IAccountPasswordChangeService,
    IDisposable
{
    private static readonly Uri BaseAddress =
        new(
            "https://webmail.necnet.de/telenec-mail-api/",
            UriKind.Absolute);

    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(20);

    private readonly HttpClient
        _httpClient;

    private bool
        _disposed;

    public AccountPasswordChangeApiClient()
    {
        _httpClient =
            new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = RequestTimeout
            };
    }

    public async Task<AccountPasswordChangeResult> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException(
                "Der Benutzername darf nicht leer sein.",
                nameof(userName));
        }

        ArgumentNullException.ThrowIfNull(
            currentPassword);

        ArgumentNullException.ThrowIfNull(
            newPassword);

        var request =
            new ChangePasswordRequest(
                userName,
                currentPassword,
                newPassword);

        try
        {
            using var response =
                await _httpClient.PostAsJsonAsync(
                    "api/v1/account/password",
                    request,
                    cancellationToken);

            return response.StatusCode switch
            {
                HttpStatusCode.NoContent =>
                    AccountPasswordChangeResult.Success(),

                HttpStatusCode.Unauthorized =>
                    AccountPasswordChangeResult
                        .InvalidCredentials(),

                HttpStatusCode.BadRequest =>
                    AccountPasswordChangeResult
                        .PasswordRejected(),

                HttpStatusCode.TooManyRequests =>
                    AccountPasswordChangeResult
                        .RateLimited(),

                HttpStatusCode.ServiceUnavailable =>
                    AccountPasswordChangeResult
                        .ServiceUnavailable(),

                HttpStatusCode.BadGateway =>
                    AccountPasswordChangeResult
                        .ServiceUnavailable(),

                HttpStatusCode.GatewayTimeout =>
                    AccountPasswordChangeResult
                        .ServiceUnavailable(),

                _ =>
                    AccountPasswordChangeResult.Failed()
            };
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return AccountPasswordChangeResult
                .ServiceUnavailable();
        }
        catch (HttpRequestException)
        {
            return AccountPasswordChangeResult
                .ServiceUnavailable();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed =
            true;

        _httpClient.Dispose();

        GC.SuppressFinalize(
            this);
    }

    private sealed record ChangePasswordRequest(
        string UserName,
        string CurrentPassword,
        string NewPassword);
}