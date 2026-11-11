using Npgsql;

namespace AspireNotifications.Tests.Shared;

/// <summary>What a test reads back from the customer_notifications table.</summary>
public sealed record StoredNotification(
    string ExternalId,
    string CustomerEmail,
    string Title,
    string Severity,
    string SourceType);

/// <summary>
/// Query Postgres DIRECTLY with plain SQL.
///
/// Why not call the CustomerInterface's GET endpoint instead? Because an integration test
/// should check the REAL side effect (the row in the database), not ask the service under
/// test to grade its own homework. The E2E test uses the HTTP endpoint — that's the
/// customer's point of view, which is what E2E is about.
/// </summary>
public sealed class PostgresTestClient(string connectionString)
{
    public async Task<StoredNotification?> FindByExternalIdAsync(string externalId)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var cmd = dataSource.CreateCommand(
            """
            SELECT "ExternalId", "CustomerEmail", "Title", "Severity", "SourceType"
            FROM customer_notifications
            WHERE "ExternalId" = $1
            """);
        cmd.Parameters.AddWithValue(externalId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new StoredNotification(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    public async Task<long> CountByExternalIdAsync(string externalId)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var cmd = dataSource.CreateCommand(
            """SELECT COUNT(*) FROM customer_notifications WHERE "ExternalId" = $1""");
        cmd.Parameters.AddWithValue(externalId);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}
