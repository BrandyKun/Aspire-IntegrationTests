using System.Net;
using System.Net.Http.Json;

namespace AspireNotifications.E2ETests;

/// <summary>
/// END-TO-END: "a notification appears in the external system → the customer can see it".
///
///   POST externalapi/notifications
///        └─► notification-reader ─► RabbitMQ ─► customer-interface ─► Postgres
///                                                     └─► GET customer-interface/notifications/{id}  ← we assert here
///
/// Notice what we DON'T do: no RabbitMQ client, no SQL. We only use the two public HTTP doors,
/// like a real integration partner and a real customer would. If this fails, open the Aspire
/// dashboard (see README → "Debugging a failing test") and follow the trace to see which hop broke.
/// </summary>
[Collection(AppHostCollection.Name)]
public class CustomerNotificationJourneyTests(AppHostFixture fixture)
{
    private static readonly TimeSpan JourneyTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Shipped_order_notification_reaches_the_customer_view()
    {
        var externalApi = fixture.ExternalApiClient();
        var customerInterface = fixture.CustomerInterfaceClient();
        var externalId = $"e2e-{Guid.NewGuid():N}";

        // Before: the customer view knows nothing about it.
        var before = await customerInterface.GetAsync($"/notifications/{externalId}");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        // Act: the external system produces a notification.
        var created = await externalApi.PostAsJsonAsync("/notifications", new
        {
            id = externalId,
            type = "ORDER_SHIPPED",
            customerEmail = "Anna@Example.com",
            message = "Your parcel left the warehouse"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // Assert: eventually the customer view shows the transformed notification.
        var shown = await Wait.UntilAsync(async () =>
        {
            var response = await customerInterface.GetAsync($"/notifications/{externalId}");
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<CustomerNotificationView>()
                : null;
        }, JourneyTimeout, $"GET /notifications/{externalId} to return 200");

        Assert.Equal("Your order is on the way", shown.Title);
        Assert.Equal("anna@example.com", shown.CustomerEmail);
        Assert.Equal("Info", shown.Severity);
        Assert.Equal("Your parcel left the warehouse", shown.Body);
    }

    [Fact]
    public async Task Several_notifications_all_arrive_in_the_list()
    {
        var externalApi = fixture.ExternalApiClient();
        var customerInterface = fixture.CustomerInterfaceClient();
        var batch = Enumerable.Range(0, 5).Select(i => $"e2e-batch-{i}-{Guid.NewGuid():N}").ToArray();

        foreach (var id in batch)
        {
            var r = await externalApi.PostAsJsonAsync("/notifications",
                new { id, type = "PROMO", customerEmail = "mia@example.com", message = "offer" });
            r.EnsureSuccessStatusCode();
        }

        await Wait.UntilTrueAsync(async () =>
        {
            var list = await customerInterface.GetFromJsonAsync<List<CustomerNotificationView>>("/notifications") ?? [];
            var seen = list.Select(n => n.ExternalId).ToHashSet();
            return batch.All(seen.Contains);
        }, JourneyTimeout, "all 5 notifications to appear in GET /notifications");
    }

    /// <summary>Only the fields we care about; the JSON may contain more.</summary>
    private sealed record CustomerNotificationView(string ExternalId, string CustomerEmail, string Title, string Body, string Severity);
}
