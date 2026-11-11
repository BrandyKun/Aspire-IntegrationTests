using AspireNotifications.FakeExternalApi;

namespace AspireNotifications.UnitTests;

/// <summary>
/// The fake API's cursor logic is small, but the reader DEPENDS on it being right:
/// "give me everything after sequence N" must never skip or repeat items.
/// </summary>
public class NotificationStoreTests
{
    [Fact]
    public void Sequence_numbers_grow_by_one()
    {
        var store = new NotificationStore();

        var first = store.Add("PROMO", "a@example.com", "one");
        var second = store.Add("PROMO", "a@example.com", "two");

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
    }

    [Fact]
    public void GetAfter_returns_only_newer_items()
    {
        var store = new NotificationStore();
        store.Add("PROMO", "a@example.com", "one");
        var second = store.Add("PROMO", "a@example.com", "two");
        var third = store.Add("PROMO", "a@example.com", "three");

        var newer = store.GetAfter(afterSequence: 1);

        Assert.Equal(new[] { second.Id, third.Id }, newer.Select(n => n.Id).ToArray());
    }

    [Fact]
    public void GetAfter_with_latest_sequence_returns_nothing()
    {
        var store = new NotificationStore();
        var only = store.Add("PROMO", "a@example.com", "one");

        Assert.Empty(store.GetAfter(only.Sequence));
    }

    [Fact]
    public void Caller_supplied_id_is_kept()
    {
        var store = new NotificationStore();

        var item = store.Add("PROMO", "a@example.com", "one", id: "my-own-id");

        Assert.Equal("my-own-id", item.Id);
    }
}
