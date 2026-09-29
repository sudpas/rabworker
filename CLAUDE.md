# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A .NET 10 pipeline: a **test client** publishes partial updates to per-person form data into RabbitMQ; a **worker** consumes them and upserts into Postgres (`person_form` table).

- `src/RabWorker.Contracts` — message shape (`FormUpdateMessage`), field whitelist (`FormFields`), and queue topology (`RabbitTopology`), shared by both sides.
- `src/RabWorker.Worker` — `FormWorker` (single hosted service: schema init, consume, retry) + `FormRepository` (upsert) + `FormUpdateParser` (validation).
- `src/RabWorker.TestClient` — console app that generates and publishes random updates.
- `tests/RabWorker.Tests` — xUnit; unit tests plus Testcontainers-based integration tests.

## Commands

```bash
docker compose up -d                                    # RabbitMQ (mgmt UI :15672 guest/guest) + Postgres (forms db)
dotnet build
dotnet run --project src/RabWorker.Worker               # applies schema, then consumes forever
dotnet run --project src/RabWorker.TestClient -- --people 20 --updates 500 --shuffle --duplicate-rate 0.1

dotnet test --filter "Category!=Integration"            # unit tests only, no Docker needed
dotnet test                                             # includes integration tests (needs Docker running)
dotnet test --filter "FullyQualifiedName~FormRepositoryTests.Out_of_order_patches_converge_per_field"  # single test
```

Worker config lives under the `Worker` section (`appsettings.json`; `PrefetchCount`/`Concurrency` default to 32/4 if absent); override via env vars like `Worker__PostgresConnectionString`, `Worker__RabbitMqUri`, `Worker__Concurrency`.

## Design invariants (read before changing the write path)

**Messages are partial patches.** Only keys present in `fields` change; an explicit JSON `null` clears the field. Field names are identical across JSON keys, `FormFields` constants, and SQL column names — adding a field means updating `FormFields`, `Sql/schema.sql`, the `date_of_birth`-style special cases in `FormUpdateParser` / `FormRepository.ApplyAsync` (non-text types), and the client generator.

**Ordering/idempotency is per-field versioning, not per-row.** `person_form.field_versions` (JSONB) records which message version last wrote each column. `FormRepository.ApplyAsync`'s upsert writes a column only if the incoming version is newer than that field's recorded version. A single row-level version check would silently lose data when patches touching *different* fields arrive out of order. Consequences:
- Replays/duplicates are no-ops by construction — there is no processed-message table.
- Concurrent processing (`Concurrency` > 1, multiple worker instances) is safe; the upsert's row lock serializes per person.
- `first_name`/`last_name` are nullable because a later patch may create the row before the person's full v1 form arrives.
- `version` is supplied by the publisher and must be monotonically increasing per person.

The upsert SQL interpolates column names; that is only safe because `FormUpdateParser` rejects any field not in `FormFields.All`. Keep that validation in front of the repository.

**Failure handling (`FormWorker`):**
- Invalid messages (`InvalidMessageException`, bad JSON, unknown field, bad type) and any other non-transient exception → rejected without requeue → dead-lettered to `form-updates.dlq`.
- Transient `NpgsqlException` → retried in-process with exponential backoff (max 30s) until success or shutdown. This is deliberate: requeueing would count against the quorum queue's `x-delivery-limit` and dead-letter good messages during a DB outage.
- Shutdown mid-message → nack with requeue.
- Ack happens only after the DB write commits (at-least-once; idempotency covers redelivery).

**Topology** (`RabbitTopology.DeclareAsync`) is declared idempotently by both client and worker. Queue arguments must stay identical on both sides — RabbitMQ refuses a redeclare with different args (`PRECONDITION_FAILED`), which also means changing args requires deleting the existing queue.

**Schema** is `src/RabWorker.Worker/Sql/schema.sql`, embedded into the assembly and applied at startup by `FormRepository.EnsureSchemaAsync` (guarded by an advisory lock). There is no migration tool; the script uses `IF NOT EXISTS`, so changes to an existing table need explicit `ALTER` statements.
