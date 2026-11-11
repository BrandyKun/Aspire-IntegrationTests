using System.Net.Http.Json;

namespace AspireNotifications.NotificationReader;

/// <summary>
/// What the EXTERNAL API returns. This is OUR copy of their shape.
/// We deliberately do NOT share a class with the FakeExternalApi project: in real life
/// you don't get to reference a third party's code, you only know their JSON.
/// </summary>
public sealed record ExternalNotificationDto(
    long Sequence,
    string Id,
    string Type,
    string CustomerEmail,
    string Message,
    DateTimeOffset OccurredAt);

/// <summary>
/// Typed HttpClient: one small class that knows how to talk to the external API.
/// Registered in Program.cs with AddHttpClient&lt;ExternalApiClient&gt;(...).
/// </summary>
public sealed class ExternalApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<ExternalNotificationDto>> GetNewerThanAsync(long afterSequence, CancellationToken ct)
    {
        var result = await http.GetFromJsonAsync<List<ExternalNotificationDto>>(
            $"/notifications?afterSequence={afterSequence}", ct);

        return result ?? [];
    }
}
