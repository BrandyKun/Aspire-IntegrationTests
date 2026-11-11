using Microsoft.EntityFrameworkCore;

namespace AspireNotifications.CustomerInterface.Data;

/// <summary>
/// EF Core DbContext = the C# view of the database. One DbSet per table.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    /// <summary>Must match the AppHost: postgres.AddDatabase("notificationsdb").</summary>
    public const string ConnectionName = "notificationsdb";

    public DbSet<CustomerNotification> Notifications => Set<CustomerNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<CustomerNotification>();

        entity.ToTable("customer_notifications");
        entity.HasKey(n => n.Id);

        // The duplicate shield: the database itself refuses a second row with the same ExternalId.
        entity.HasIndex(n => n.ExternalId).IsUnique();

        entity.Property(n => n.ExternalId).HasMaxLength(100);
        entity.Property(n => n.CustomerEmail).HasMaxLength(320);
        entity.Property(n => n.Title).HasMaxLength(200);
        entity.Property(n => n.SourceType).HasMaxLength(100);

        // Store the enum as text ("High") instead of a number (2) → readable in pgweb / SQL.
        entity.Property(n => n.Severity).HasConversion<string>().HasMaxLength(20);
    }
}
