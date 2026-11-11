// =====================================================================================
//  AppHost / Program.cs  —  THE RECIPE
//
//     ┌──────────────┐  HTTP poll   ┌─────────────────────┐  publish  ┌──────────┐
//     │ externalapi  │◄─────────────│ notification-reader │──────────►│ rabbitmq │
//     │ (fake 3rd    │              └─────────────────────┘           └────┬─────┘
//     │  party)      │                                                     │ consume
//     └──────────────┘              ┌─────────────────────┐  EF Core  ┌────▼─────┐
//                                   │ customer-interface  │──────────►│ postgres │
//                                   └─────────────────────┘           └──────────┘
//
//  Two ways to run the two services (pick with configuration, see appsettings.json):
//     Images:Source = "Local"  → compile & run the projects from this repo   (dev loop)
//     Images:Source = "Acr"    → pull pre-built images from Azure Container Registry
//                                (what a "test the real artefacts" pipeline does)
// =====================================================================================

using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// ---- Read the switches (appsettings.json, env vars, or `-- --Images:Source=Acr` on the command line)
var imageSource = builder.Configuration["Images:Source"] ?? "Local";
var registry    = builder.Configuration["Images:Registry"] ?? "myregistry.azurecr.io";
var imageTag    = builder.Configuration["Images:Tag"] ?? "latest";
var generatorOn = builder.Configuration["Generator:Enabled"] ?? "true";
var useAcr      = string.Equals(imageSource, "Acr", StringComparison.OrdinalIgnoreCase);

// -------------------------------------------------------------------------------------
//  1) INFRASTRUCTURE  (containers Aspire pulls and starts for you)
// -------------------------------------------------------------------------------------

// Fixed passwords so you can log in to the admin UIs without hunting for generated secrets.
// (Only for local dev! In real environments let Aspire generate them.)
var postgresPassword = builder.AddParameter("postgres-password", "postgres", secret: true);
var rabbitPassword   = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    // pgweb = small web UI to browse tables. Click its link in the dashboard.
    .WithPgWeb();

// The DATABASE is a separate resource from the SERVER. Services reference the database.
// Name "notificationsdb" is what the CustomerInterface looks up (ConnectionStrings__notificationsdb).
var notificationsDb = postgres.AddDatabase("notificationsdb");

var rabbitmq = builder.AddRabbitMQ("rabbitmq", password: rabbitPassword)
    // Management plugin = the RabbitMQ web UI (queues, messages, rates). Login: guest / guest.
    .WithManagementPlugin();

// -------------------------------------------------------------------------------------
//  2) THE FAKE EXTERNAL API  (always runs from source — it stands in for a 3rd party)
// -------------------------------------------------------------------------------------
var externalApi = builder.AddProject<Projects.AspireNotifications_FakeExternalApi>("externalapi")
    .WithEnvironment("Generator__Enabled", generatorOn)   // tests pass --Generator:Enabled=false
    .WithHttpHealthCheck("/health");

// -------------------------------------------------------------------------------------
//  3) OUR TWO SERVICES  — from source (Local) or from ACR images (Acr)
// -------------------------------------------------------------------------------------
if (!useAcr)
{
    // LOCAL: Aspire builds the csproj and runs it as a process. F5-able, breakpoints work.
    var reader = builder.AddProject<Projects.AspireNotifications_NotificationReader>("notification-reader")
        .WithReference(externalApi)        // → service discovery: "https+http://externalapi" resolves
        .WaitFor(externalApi);
    WireCommonDependencies(reader);

    var customerInterface = builder.AddProject<Projects.AspireNotifications_CustomerInterface>("customer-interface")
        .WithReference(notificationsDb)    // → ConnectionStrings__notificationsdb
        .WaitFor(notificationsDb);
    WireCommonDependencies(customerInterface);
}
else
{
    // ACR: run the SAME services as containers pulled from your registry.
    // Prerequisites:  az acr login --name <registry>   (so Docker can pull)
    //                 scripts/push-to-acr.sh           (to build & push the images)
    var reader = builder.AddContainer("notification-reader", $"{registry}/notification-reader", imageTag)
        .WithHttpEndpoint(targetPort: 8080)          // the aspnet image listens on 8080
        .WithReference(externalApi)                  // Aspire rewrites localhost → host.docker.internal for containers
        .WaitFor(externalApi);
    WireCommonDependencies(reader);

    var customerInterface = builder.AddContainer("customer-interface", $"{registry}/customer-interface", imageTag)
        .WithHttpEndpoint(targetPort: 8080)
        .WithReference(notificationsDb)
        .WaitFor(notificationsDb);
    WireCommonDependencies(customerInterface);
}

builder.Build().Run();

// -------------------------------------------------------------------------------------
//  Both services, in both modes, need RabbitMQ and a health check. One helper, no copy-paste.
//  The generic constraint just means "anything that has env vars, endpoints and can wait",
//  which is true for both a project (Local) and a container (Acr).
// -------------------------------------------------------------------------------------
IResourceBuilder<T> WireCommonDependencies<T>(IResourceBuilder<T> service)
    where T : IResourceWithEnvironment, IResourceWithEndpoints, IResourceWithWaitSupport
    => service
        .WithReference(rabbitmq)          // → ConnectionStrings__rabbitmq
        .WaitFor(rabbitmq)                // don't start until RabbitMQ answers its health check
        .WithHttpHealthCheck("/health");  // Aspire polls this → green tick + WaitForResourceHealthyAsync in tests
