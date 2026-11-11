using AspireNotifications.Contracts;
using AspireNotifications.CustomerInterface;
using AspireNotifications.CustomerInterface.Data;

namespace AspireNotifications.UnitTests;

/// <summary>
/// WHAT TO UNIT TEST: the decisions your code makes.
/// "Type X becomes title Y", "emails are normalised", "missing data is rejected".
/// No RabbitMQ, no Postgres, no Aspire — just a function in, a value out.
/// </summary>
public class NotificationTransformerTests
{
    private static RawNotification Raw(string type = "ORDER_SHIPPED", string email = "anna@example.com",
        string message = "Parcel 123 left the warehouse", string id = "ext-1") =>
        new(id, type, email, message, new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("ORDER_SHIPPED",  "Your order is on the way",               Severity.Info)]
    [InlineData("PAYMENT_FAILED", "There was a problem with your payment",  Severity.High)]
    [InlineData("ACCOUNT_LOCKED", "Your account has been locked",           Severity.Critical)]
    [InlineData("PROMO",          "A new offer for you",                    Severity.Low)]
    [InlineData("SOMETHING_NEW",  "You have a new notification",            Severity.Low)] // unknown → safe default
    public void Maps_external_type_to_title_and_severity(string type, string expectedTitle, Severity expectedSeverity)
    {
        var result = NotificationTransformer.Transform(Raw(type: type), DateTimeOffset.UtcNow);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedSeverity, result.Severity);
        Assert.Equal(type, result.SourceType); // original code is kept
    }

    [Theory]
    [InlineData("order_shipped")]
    [InlineData("  ORDER_SHIPPED  ")]
    public void Type_matching_ignores_case_and_whitespace(string type)
    {
        var result = NotificationTransformer.Transform(Raw(type: type), DateTimeOffset.UtcNow);

        Assert.Equal("Your order is on the way", result.Title);
    }

    [Fact]
    public void Normalises_customer_email()
    {
        var result = NotificationTransformer.Transform(Raw(email: "  Anna@Example.COM "), DateTimeOffset.UtcNow);

        Assert.Equal("anna@example.com", result.CustomerEmail);
    }

    [Fact]
    public void Keeps_external_id_and_timestamps()
    {
        var receivedAt = new DateTimeOffset(2026, 1, 15, 9, 31, 0, TimeSpan.Zero);

        var result = NotificationTransformer.Transform(Raw(id: " ext-42 "), receivedAt);

        Assert.Equal("ext-42", result.ExternalId);
        Assert.Equal(Raw().OccurredAt, result.OccurredAt);
        Assert.Equal(receivedAt, result.ReceivedAt);
    }

    [Fact]
    public void Empty_message_gets_a_placeholder_body()
    {
        var result = NotificationTransformer.Transform(Raw(message: "   "), DateTimeOffset.UtcNow);

        Assert.Equal("(no details provided)", result.Body);
    }

    [Theory]
    [InlineData("", "anna@example.com")]
    [InlineData("ext-1", "")]
    [InlineData("ext-1", "   ")]
    public void Rejects_notifications_missing_required_fields(string id, string email)
    {
        var raw = Raw(id: id, email: email);

        // An ArgumentException is what the consumer treats as a POISON message → dead-letter queue.
        Assert.Throws<ArgumentException>(() => NotificationTransformer.Transform(raw, DateTimeOffset.UtcNow));
    }
}
