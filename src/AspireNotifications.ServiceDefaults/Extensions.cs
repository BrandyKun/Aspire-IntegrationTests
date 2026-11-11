using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// This is (almost) the standard file Aspire generates for every solution.
/// You rarely edit it. The one custom line is marked with "CUSTOM".
/// </summary>
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        // Service discovery: lets an HttpClient call "https+http://externalapi" and have the
        // name resolved from environment variables that the AppHost injects.
        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retries + timeouts + circuit breaker on EVERY HttpClient, for free.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddHttpClientInstrumentation()
                       .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                       // CUSTOM: listen to our own ActivitySource so the RabbitMQ publish/consume
                       // spans we create by hand appear in the dashboard traces.
                       .AddSource("AspireNotifications")
                       .AddAspNetCoreInstrumentation(o =>
                           // Don't create a trace for every health-check ping, it's noise.
                           o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")
                                          && !ctx.Request.Path.StartsWithSegments("/alive"))
                       .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // The AppHost sets OTEL_EXPORTER_OTLP_ENDPOINT to the dashboard's address.
        // If it's set we export; if not (e.g. plain `dotnet run` of one service) we stay quiet.
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // "self" = the process is up. Tagged "live" so /alive can report it.
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // NOTE: the Aspire template only maps these in Development. We map them ALWAYS
        // because the AppHost's WithHttpHealthCheck("/health") also needs them when the
        // service runs as a container image pulled from ACR (which runs as Production).
        // In a real system you'd protect these endpoints (network rules / auth).

        // /health  = everything is fine, including dependencies (RabbitMQ, Postgres…).
        app.MapHealthChecks("/health");

        // /alive   = the process is alive, even if a dependency is down.
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
