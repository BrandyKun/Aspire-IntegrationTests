using System.Diagnostics;

namespace AspireNotifications.Contracts;

/// <summary>
/// One shared ActivitySource for our own custom spans ("activities").
/// ServiceDefaults tells OpenTelemetry to listen to this name, so anything we start
/// with <c>Telemetry.Source.StartActivity(...)</c> shows up in the Aspire dashboard → Traces.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "AspireNotifications";
    public static readonly ActivitySource Source = new(SourceName);
}
