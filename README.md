# AspireNotifications — a hands-on Aspire + RabbitMQ + Postgres playground

A small but complete pipeline you can **run, break, and watch**:

```
 ┌──────────────┐   HTTP poll    ┌─────────────────────┐   publish    ┌───────────────┐
 │  externalapi │ ◄──────────────│ notification-reader │ ────────────►│   rabbitmq    │
 │  (fake 3rd   │   every 2s     │  "the fetcher"      │              │  exchange:    │
 │   party API) │                └─────────────────────┘              │ notifications │
 └──────────────┘                                                     └───────┬───────┘
        ▲                                                                     │ queue:
        │ POST /notifications                                                 │ customer-interface.notifications
        │ (you, curl, or a test)                                              ▼
                                   ┌─────────────────────┐   EF Core   ┌───────────────┐
                                   │ customer-interface  │ ───────────►│   postgres    │
                                   │  "the consumer"     │             │ notificationsdb│
                                   └──────────┬──────────┘             └───────────────┘
                                              │ GET /notifications
                                              ▼
                                        you / the E2E test
```

| Project | Role | Where to look first |
|---|---|---|
| `src/AspireNotifications.AppHost` | **The recipe.** Says what runs and how it's wired. Starts everything + the dashboard. | `Program.cs` |
| `src/AspireNotifications.FakeExternalApi` | Pretends to be the third-party system. Has a `POST` so you can inject notifications. | `Program.cs` |
| `src/AspireNotifications.NotificationReader` | Polls the external API, publishes each notification to RabbitMQ. | `NotificationReaderWorker.cs`, `NotificationPublisher.cs` |
| `src/AspireNotifications.CustomerInterface` | Listens on its queue, **transforms**, saves to Postgres, exposes `GET /notifications`. | `NotificationConsumer.cs`, `NotificationTransformer.cs` |
| `src/AspireNotifications.Contracts` | The message shape + RabbitMQ names + topology, shared by both services. | `Messaging.cs`, `RabbitTopology.cs` |
| `src/AspireNotifications.ServiceDefaults` | Telemetry / health / service discovery. Same in every Aspire solution. | `Extensions.cs` |
| `tests/…UnitTests` | Pure logic. No Docker. Milliseconds. | |
| `tests/…IntegrationTests` | One service + its real RabbitMQ/Postgres. | |
| `tests/…E2ETests` | The whole journey through the two public HTTP doors. | |
| `tests/…Tests.Shared` | Starts the AppHost once for the test run + RabbitMQ/Postgres helpers. | `AppHostFixture.cs` |

---

## 1. What you need on your Mac

| Tool | Check | Install |
|---|---|---|
| .NET 10 SDK | `dotnet --version` → `10.0.x` | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Docker Desktop (**running**) | `docker ps` works | https://www.docker.com/products/docker-desktop/ |
| Trusted dev HTTPS cert (for the dashboard) | — | `dotnet dev-certs https --trust` |
| Azure CLI (only for the ACR part) | `az --version` | `brew install azure-cli` |

The first run pulls the Postgres, RabbitMQ and pgweb images — give it a few minutes.

---

## 2. Run it and LOOK at it

```bash
cd ~/Desktop/AspireNotifications
dotnet run --project src/AspireNotifications.AppHost
```

The console prints a line like `Login to the dashboard at https://localhost:17150/login?t=...` — click it.

**Things to click, in this order:**

1. **Resources** page — wait until every row is green/"Running". The fake API invents a notification every 8 s, so the pipeline is already busy.
2. **Console logs** → pick `notification-reader`, then `customer-interface`. You'll see `Published notification …` on one side and `Stored … as 'Your order is on the way'` on the other.
3. **Traces** → open a trace named `poll external api`. One timeline, four services:
   `notification-reader → GET externalapi → notifications publish → notifications consume → SQL INSERT`.
   That is the trace id travelling *inside the RabbitMQ message header* (`traceparent`).
4. On the Resources page, **`rabbitmq` has a link to the Management UI** (login `guest` / `guest`). Go to **Queues** → `customer-interface.notifications`. Also notice `notifications.dead` (empty for now).
5. **`postgres-pgweb`** link → table `customer_notifications` → the stored rows.
6. **`customer-interface`** link → add `/notifications` to the URL → the same rows as JSON.

### Inject one yourself

```bash
# find the externalapi port in the dashboard (Resources → externalapi → endpoint), then:
curl -X POST http://localhost:<port>/notifications \
  -H 'Content-Type: application/json' \
  -d '{"id":"manual-1","type":"PAYMENT_FAILED","customerEmail":"You@Example.com","message":"Card ending 1234 declined"}'
```

Within ~2 s: new log lines, a new trace, a new row with title *"There was a problem with your payment"*, severity *High*, email lower-cased.

---

## 3. RabbitMQ in 5 minutes (the bits you actually need)

```
   NotificationReader                     RabbitMQ                          CustomerInterface
   ──────────────────      ┌─────────────────────────────────────┐          ─────────────────
                           │                                     │
   publish(message) ──────►│ EXCHANGE "notifications" (fanout)   │
                           │        │ binding                    │
                           │        ▼                            │
                           │ QUEUE "customer-interface.          │
                           │        notifications"  ▒▒▒▒▒▒ ──────┼────────► consume → ack ✔
                           │        │ rejected (nack)            │
                           │        ▼                            │
                           │ EXCHANGE "notifications.dead"       │
                           │        ▼                            │
                           │ QUEUE "notifications.dead" ▒        │          (a human looks here)
                           └─────────────────────────────────────┘
```

| Word | Meaning here | Where in the code |
|---|---|---|
| **Connection** | The TCP link to RabbitMQ. One per app. Aspire gives it to you (`AddRabbitMQClient`). | both `Program.cs` |
| **Channel** | A lightweight session on the connection. All work happens on a channel. Not thread-safe. | `NotificationPublisher`, `NotificationConsumer` |
| **Exchange** | Where publishers send to. **Never** publish to a queue directly. *Fanout* = copy to every bound queue. | `RabbitTopology.cs` |
| **Queue** | Where messages wait. Each consuming *service* has its own queue. | `RabbitTopology.cs` |
| **Binding** | "Queue Q wants messages from exchange E." | `RabbitTopology.cs` |
| **Declare** | "Make sure it exists." Safe to call every start-up — whoever's first creates it. | everywhere via `RabbitTopology.DeclareAsync` |
| **Durable / Persistent** | Queue and message survive a RabbitMQ restart. | `RabbitTopology.cs`, `NotificationPublisher.cs` |
| **Ack** | Consumer says "done" → RabbitMQ deletes the message. No ack → RabbitMQ re-delivers. | `NotificationConsumer.cs` |
| **Nack (requeue=false)** + **dead-letter** | "This message is poison, put it in the failed tray." | `NotificationConsumer.cs` |
| **At-least-once** | Because of re-delivery you can get a message **twice** → consumers must handle duplicates. | `NotificationConsumer.StoreAsync` + unique index |

### Why a queue at all? Why not have the reader call the CustomerInterface over HTTP?

Try this experiment, it's the whole argument in 30 seconds:

1. In the dashboard, **Stop** `customer-interface` (⋮ menu on the row).
2. Watch the RabbitMQ UI: `customer-interface.notifications` grows — 1, 2, 3 messages… Nothing is lost.
3. **Start** `customer-interface` again. The queue drains to 0 and the rows appear in Postgres.

With a direct HTTP call, step 1 would have made the reader fail and those notifications would be gone.

---

## 4. The tests, and what each one proves

```
            ┌───────────────┐
  many  →   │   UNIT        │  NotificationTransformer: type → title/severity, email normalising, rejects bad input
            │   (ms, no     │  NotificationStore: the cursor logic the reader depends on
            │    Docker)    │
            ├───────────────┤
  some  →   │ INTEGRATION   │  Reader:   POST to fake API  ⇒ message appears on the exchange (spy queue)
            │ (one service  │  Consumer: publish to exchange ⇒ TRANSFORMED row in Postgres (checked with SQL)
            │  + real deps) │            same message twice ⇒ ONE row
            │               │            broken message     ⇒ dead-letter queue, next message still processed
            ├───────────────┤
  few   →   │   E2E         │  POST to fake API ⇒ GET customer-interface/notifications/{id} returns the transformed item
            │ (whole system,│  5 notifications  ⇒ all 5 in GET /notifications
            │  HTTP only)   │
            └───────────────┘
```

```bash
dotnet test tests/AspireNotifications.UnitTests          # instant
dotnet test tests/AspireNotifications.IntegrationTests   # starts the whole system once, ~1 min
dotnet test tests/AspireNotifications.E2ETests           # same
dotnet test                                              # everything
```

> ⚠️ **Do not have `dotnet run` (the AppHost) running while you run tests.** The tests start their *own* copy of the AppHost. Two copies = locked build files and port clashes. Stop one, run the other.

**How the tests differ, in one sentence each:**

- **Unit** tests call a C# function. If it fails, the bug is in that function.
- **Integration** tests put the real RabbitMQ/Postgres under *one* service and check the real side effect (a message, a row). If it fails, the bug is in that service or its wiring.
- **E2E** tests only use the two public HTTP doors. If it fails, *something* in the chain is broken — go to the dashboard and follow the trace to find which hop.

### Debugging a failing integration/E2E test with the dashboard

1. In `tests/AspireNotifications.Tests.Shared/AppHostFixture.cs`, set `ShowDashboard = true`.
2. Put a breakpoint at the top of the failing test, run it in **Debug**.
3. While paused, open the `Login to the dashboard at …` URL from the test output → the dashboard of *the test's* system. Send requests, read logs, follow traces.
4. Set it back to `false` before committing (CI doesn't need a dashboard).

---

## 5. Local source vs images from Azure Container Registry

The AppHost has one switch, `Images:Source`:

```
 Images:Source = Local  (default)            Images:Source = Acr
 ───────────────────────────────             ───────────────────────────────────────
 AddProject<…NotificationReader>()           AddContainer("notification-reader",
   → compiles the csproj, runs the exe           "myacr.azurecr.io/notification-reader", "v1")
   → breakpoints work, instant rebuilds       → pulls the image Docker-style
   → "does MY CODE work?"                     → "does the ARTEFACT we're about to deploy work?"
```

Everything else (RabbitMQ, Postgres, wiring, health checks, tests) is **identical**, which is exactly what makes the second mode useful for a release pipeline.

```bash
# one-time: a registry
az login
az group create --name rg-aspire-notifications --location uksouth
az acr create --name <yourAcrName> --resource-group rg-aspire-notifications --sku Basic

# build both images IN Azure and push them (no local docker build needed)
./scripts/push-to-acr.sh <yourAcrName> v1

# run the pipeline with those images
./scripts/run-acr.sh <yourAcrName> v1
# …which is just:
#   az acr login --name <yourAcrName>
#   dotnet run --project src/AspireNotifications.AppHost -- --Images:Source=Acr --Images:Registry=<yourAcrName>.azurecr.io --Images:Tag=v1
```

The tests use whatever mode the AppHost configuration says, so in CI you can run the **same** test projects against the ACR images by setting the environment variable `Images__Source=Acr` (plus `Images__Registry` and `Images__Tag`).

---

## 6. Exercises (do these — this is how it sticks)

1. **Break the transform on purpose.** In `NotificationTransformer.Describe` change `"ORDER_SHIPPED"` to `"ORDER_SHIPED"`. Run the unit tests → red. Run the E2E test → red, but notice how much *slower* and *vaguer* the failure is. Fix it.
2. **Add a new type** `REFUND_ISSUED` → title *"Your refund is on its way"*, severity Info. Write the unit test **first**, watch it fail, then add the case.
3. **Poison a message by hand.** RabbitMQ UI → Exchanges → `notifications` → *Publish message* → payload `hello`. Watch `customer-interface` log an error and the message land in `notifications.dead`. Nothing crashed, the next good message still flows.
4. **Prove at-least-once.** Publish the same JSON twice from the RabbitMQ UI (copy one from the `customer_notifications` table and wrap it as a `RawNotification`). Count the rows.
5. **Persist the reader's cursor.** Right now restarting `notification-reader` republishes everything (harmless thanks to duplicate handling, but wasteful). Store `lastSequence` somewhere that survives a restart. Which test would you add?

---

## 7. Where things are named — the "must match" list

If you rename something, these pairs must stay in sync:

| AppHost | Service code |
|---|---|
| `AddRabbitMQ("rabbitmq")` | `AddRabbitMQClient("rabbitmq")` / `Messaging.RabbitConnectionName` |
| `postgres.AddDatabase("notificationsdb")` | `AddNpgsqlDbContext<…>("notificationsdb")` / `NotificationsDbContext.ConnectionName` |
| `AddProject<…>("externalapi")` | `https+http://externalapi` in NotificationReader `Program.cs` |
| `WithHttpHealthCheck("/health")` | `app.MapDefaultEndpoints()` in every service |
| resource names in `AppHostFixture` constants | the names above |
# Aspire-IntegrationTests
