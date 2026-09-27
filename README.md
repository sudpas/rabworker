# rabworker

A .NET 10 pipeline for per-person form data:

- **Test client** (`src/RabWorker.TestClient`) publishes partial form updates to the RabbitMQ queue `form-updates`.
- **Worker** (`src/RabWorker.Worker`) consumes them and upserts into the Postgres table `person_form`.
- Invalid messages are dead-lettered to `form-updates.dlq`.

## Running it

```bash
docker compose up -d                        # RabbitMQ + Postgres
dotnet run --project src/RabWorker.Worker   # leave running; creates the table on startup
```

In another terminal:

```bash
dotnet run --project src/RabWorker.TestClient -- --people 20 --updates 500 --shuffle --duplicate-rate 0.1 --seed 7
```

| Option | Default | Meaning |
|---|---|---|
| `--people N` | 10 | Number of people. Each gets a full form at version 1. |
| `--updates N` | 100 | Random 1–3-field patches spread across those people. |
| `--seed N` | 42 | RNG seed. Determines person IDs and data. |
| `--shuffle` | off | Publish in random order to exercise out-of-order handling. |
| `--duplicate-rate P` | 0 | Probability (0–1) that each message is published twice. |
| `--delay-ms N` | 0 | Pause between messages. |
| `--rabbit URI` | `amqp://guest:guest@localhost:5672/` | Broker URI. |

> **Rerunning with the same seed changes nothing.** The same seed produces the same people with the same
> version numbers, and the worker ignores versions it has already applied. Use a new `--seed` for fresh data.

## Message format

```json
{
  "messageId": "11111111-1111-1111-1111-111111111111",
  "personId": "aaaaaaaa-0000-0000-0000-000000000001",
  "version": 1,
  "occurredAt": "2026-09-25T12:00:00Z",
  "fields": { "first_name": "Test", "email": "test@example.com", "phone": null }
}
```

- Only the fields present in `fields` are changed; `null` clears a field.
- Allowed fields: `first_name`, `last_name`, `email`, `phone`, `date_of_birth` (`yyyy-MM-dd`), `address_line1`, `city`, `state`, `postal_code`.
- `version` must increase per person. Each field keeps the value from the highest version that wrote it (tracked in `field_versions`), so duplicates and out-of-order messages are safe.

## Manual testing

Management UI: <http://localhost:15672> (guest / guest). Queues → `form-updates` also has a **Publish message** form.

### Inspect queues

```bash
# Message counts and consumers (consumers = 1 means the worker is attached)
docker compose exec rabbitmq rabbitmqctl list_queues name messages messages_unacknowledged consumers

# Queue details via the HTTP API
curl -s -u guest:guest http://localhost:15672/api/queues/%2F/form-updates | python3 -m json.tool
```

### Publish a message by hand

```bash
curl -s -u guest:guest -H 'content-type: application/json' \
  -X POST http://localhost:15672/api/exchanges/%2F/amq.default/publish -d '{
    "routing_key": "form-updates",
    "properties": {"delivery_mode": 2, "content_type": "application/json"},
    "payload_encoding": "string",
    "payload": "{\"messageId\":\"11111111-1111-1111-1111-111111111111\",\"personId\":\"aaaaaaaa-0000-0000-0000-000000000001\",\"version\":1,\"occurredAt\":\"2026-09-25T12:00:00Z\",\"fields\":{\"first_name\":\"Test\",\"last_name\":\"User\",\"email\":\"test@example.com\"}}"
  }'
# → {"routed":true}
```

Things to try by changing `version` and `fields`:

- **Update:** version 2 with `{"email":"new@example.com"}` changes only the email.
- **Out of order:** send version 5 with `{"phone":"555"}`, then version 4 with `{"phone":"444","city":"Austin"}`. Phone stays `555`, but city becomes `Austin`.
- **Duplicate:** resend any message; nothing changes.
- **Invalid:** an unknown field such as `{"favorite_color":"red"}` lands in `form-updates.dlq`.

### Check the result in Postgres

```bash
docker compose exec postgres psql -U postgres -d forms -c \
  "SELECT person_id, first_name, last_name, email, phone, city, version, field_versions
   FROM person_form WHERE person_id = 'aaaaaaaa-0000-0000-0000-000000000001';"

# Overall
docker compose exec postgres psql -U postgres -d forms -c \
  "SELECT count(*) AS people, max(version) AS max_version, max(updated_at) AS last_update FROM person_form;"
```

### Dead-letter queue

```bash
# Peek at dead-lettered messages (left in the queue); reason is usually "rejected"
curl -s -u guest:guest -H 'content-type: application/json' \
  -X POST http://localhost:15672/api/queues/%2F/form-updates.dlq/get \
  -d '{"count":10,"ackmode":"ack_requeue_true","encoding":"auto"}' | python3 -m json.tool

# Empty it
docker compose exec rabbitmq rabbitmqctl purge_queue form-updates.dlq
```

### See messages pile up

Stop the worker (Ctrl+C), run the test client, and `list_queues` shows the backlog. Start the worker again and it drains.

### Reset everything

```bash
docker compose exec postgres psql -U postgres -d forms -c "TRUNCATE person_form;"
docker compose exec rabbitmq rabbitmqctl purge_queue form-updates
docker compose exec rabbitmq rabbitmqctl purge_queue form-updates.dlq
```

## Tests

```bash
dotnet test --filter "Category!=Integration"   # unit tests only
dotnet test                                    # plus integration tests (needs Docker; starts its own containers)
```

## Troubleshooting

**Worker logs `Failed to connect to 127.0.0.1:5432 ... Connection refused`** while `docker compose ps` shows Postgres
as `5432/tcp` with no `0.0.0.0:5432->` mapping: the host port wasn't bound, usually because something else was on
5432 when the container started. Free the port, then run `docker compose up -d --force-recreate postgres`.
