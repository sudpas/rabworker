# RabWorker Code Review: Bugs & Security

_Date: 2026-09-26 · Scope: all of `src/` plus `docker-compose.yml` and config_

This review comes from reading the code only. The code was not built, the tests were not run, and none of the findings were reproduced. The core design holds up: per-field versioning, idempotent upserts, and retrying transient errors in-process instead of requeueing.

**Suggested fix order:** B1 → B2 → S1/S2 → S4 → the rest.

---

## Bugs

### B1. A long database outage leaves the worker idle and dead-letters good messages (High)
**Where:** `src/RabWorker.Worker/FormUpdateHandler.cs:19`, `src/RabWorker.Worker/FormUpdateConsumer.cs:37`

`FormUpdateHandler` keeps retrying transient errors without a time limit, and the message stays unacked the whole time. RabbitMQ closes the channel when a delivery stays unacked longer than `consumer_timeout`, which defaults to 30 minutes. Then:

- The client's automatic recovery only handles lost **connections**, not channels the broker closed. The consumer never resumes, and `ExecuteAsync` sits in `Task.Delay(Timeout.Infinite)`, so the worker looks alive but does nothing.
- The prefetched messages (up to 32) go back to the queue with their delivery count raised. That is the `x-delivery-limit` problem the in-process retry was meant to avoid.
- Handlers that are still retrying later try to ack on a closed channel.

Any failed ack or nack that closes the channel causes the same silent stall.

**Fix:** subscribe to `channel.ChannelShutdownAsync` or the consumer being cancelled, then rebuild the channel or stop the host so something restarts it. Cap the in-process retry time below `consumer_timeout`, or raise `consumer_timeout` for this queue.

### B2. Some non-transient database errors dead-letter every message (Medium)
**Where:** `src/RabWorker.Worker/FormUpdateConsumer.cs:61`

Every non-transient exception is rejected without requeue. A dropped table, revoked permissions or a wrong search path are setup problems, not bad messages, but any of them would send the whole queue to the DLQ within seconds.

**Fix:** treat `PostgresException` in SQLSTATE classes `42` (e.g. `42P01` undefined table, `42501` permission denied) and `28` (auth) as fatal: nack with requeue and stop the consumer or host. Keep dead-lettering for data errors (class `22`).

### B3. Re-running the test client with the default seed does nothing (Medium, test tool only)
**Where:** `src/RabWorker.TestClient/Program.cs:9`, `Options.Seed = 42`

Person IDs come from a seeded `Random`, so they are the same on every run, and each run's version counters restart at 1. On a second run, every message has a version the worker has already applied, so it skips them all without any error. It looks like the worker is broken.

**Fix:** use a random seed by default (and print it), or print a warning when the seed is fixed.

### B4. Minor issues
- **Shutdown:** `ExecuteAsync` disposes the channel as soon as shutdown starts, before in-flight handlers can nack. The comment at `FormUpdateConsumer.cs:46` says messages still need settling, but they can't be. The broker requeues them anyway, so nothing is lost.
- **Leak:** the `StreamReader` in `SchemaInitializer.cs:12` is never disposed.
- **Config validation:** `WorkerOptions` isn't validated. `Concurrency = 0` is invalid, and `Concurrency > PrefetchCount` gains nothing.

---

## Security

| # | Issue | Where | Recommendation |
|---|---|---|---|
| S1 | Default credentials (`guest/guest`, `postgres/postgres`) are hardcoded as defaults. If the env override is forgotten in a deployment, the worker quietly connects to localhost with them. | `WorkerOptions.cs:7-9`, `appsettings.json` | No defaults in code: fail at startup if unset. Load secrets from env or user-secrets (`UserSecretsId` is already set). |
| S2 | docker-compose publishes Postgres (5432), AMQP (5672) and the management UI (15672) on **all network interfaces**. The official RabbitMQ image lets `guest` log in remotely. | `docker-compose.yml` | Bind to localhost: `"127.0.0.1:5432:5432"`, etc. |
| S3 | Any publisher can "freeze" a person's record: one message with `version = long.MaxValue` blocks all later updates to those fields for good. There is no authentication or authorization on who may publish. | `FormPatch.cs:33`, broker config | Give the publisher and the worker separate RabbitMQ users with scoped permissions. Consider rejecting versions implausibly far ahead of the current one. |
| S4 | Only the parser guards against SQL injection. `FormPatch` and `FormRepository.ApplyAsync` are public, so any other caller can pass arbitrary field names into the SQL built at `FormRepository.cs:40-56`. | `FormRepository.cs:17` | Add a second check against `FormFields.All` inside `ApplyAsync`. |
| S5 | Personal data (name, date of birth, email, phone, address) travels without TLS on both AMQP and Postgres. | config | Use `amqps://` and `SSL Mode=Require` outside local dev. |
| S6 | Field values have no length or format limits, so a message can store a multi-MB "email". | `FormPatch.cs:53` | Add per-field max lengths and basic format checks, and throw `InvalidMessageException` on failure. |
| S7 | The worker runs DDL at startup, so its database role needs create privileges. | `SchemaInitializer.cs` | Low priority: use a separate migration role, or a restricted runtime role after init. |

---

## Operational gaps (lower priority)
- **DLQ:** it has no consumer, alerting or length limit, so failures pile up unnoticed.
- **Health checks:** there is no liveness or readiness signal, which is what makes the stall in B1 invisible.
- **Tests:** nothing covers transient-error retry, shutdown requeue, or the broker-closed channel case.
