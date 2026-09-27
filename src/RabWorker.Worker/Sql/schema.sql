BEGIN;
-- Serialize concurrent worker startups so CREATE ... IF NOT EXISTS can't race.
SELECT pg_advisory_xact_lock(727274);

CREATE TABLE IF NOT EXISTS person_form (
    person_id      UUID PRIMARY KEY,
    -- Nullable: with out-of-order delivery a patch can create the row before the full v1 form arrives.
    first_name     TEXT,
    last_name      TEXT,
    email          TEXT,
    phone          TEXT,
    date_of_birth  DATE,
    address_line1  TEXT,
    city           TEXT,
    state          TEXT,
    postal_code    TEXT,
    -- Highest message version seen for this person.
    version        BIGINT NOT NULL,
    -- Version that last wrote each field, e.g. {"email": 7}. A field is only overwritten by a newer version.
    field_versions JSONB NOT NULL DEFAULT '{}'::jsonb,
    -- occurred_at of the message with the highest version.
    updated_at     TIMESTAMPTZ NOT NULL
);

COMMIT;
