using System.Net.Http;
using System.Net.Http.Json;

namespace Telenec.Mail.App.Services.UsageStatistics;

public sealed class UsageStatisticsApiClient
{
    private const string HeartbeatEndpoint =
        "https://dav.necnet.de/statistics/api/v1/usage/heartbeat";

    private const string RevokeEndpoint =
        "https://dav.necnet.de/statistics/api/v1/usage/revoke";

    private static readonly HttpClient HttpClient =
        new()
        {
            Timeout =
                TimeSpan.FromSeconds(10)
        };

    public async Task SendHeartbeatAsync(
        Guid installationId,
        string applicationVersion,
        int consentVersion,
        CancellationToken cancellationToken = default)
    {
        var request =
            new HeartbeatRequest(
                installationId.ToString("D"),
                applicationVersion,
                consentVersion);

        using var response =
            await HttpClient.PostAsJsonAsync(
                HeartbeatEndpoint,
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task SendRevokeAsync(
        Guid installationId,
        CancellationToken cancellationToken = default)
    {
        var request =
            new RevokeRequest(
                installationId.ToString("D"));

        using var response =
            await HttpClient.PostAsJsonAsync(
                RevokeEndpoint,
                request,
                cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private sealed record HeartbeatRequest(
        string InstallationId,
        string ApplicationVersion,
        int ConsentVersion);

    private sealed record RevokeRequest(
        string InstallationId);
}