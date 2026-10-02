using System.Net.Http;
using System.Net.Http.Json;

namespace Telenec.Mail.App.Services.UsageStatistics;

public sealed class UsageStatisticsApiClient
{
    private const string HeartbeatEndpoint =
        "https://dav.necnet.de/statistics/api/v1/usage/heartbeat";

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

    private sealed record HeartbeatRequest(
        string InstallationId,
        string ApplicationVersion,
        int ConsentVersion);
}