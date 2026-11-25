-- PostgreSQL design draft, not applied. Use a migration role; grant a separate
-- backend role only the required DML. Unity clients receive no DB credentials.
BEGIN;

CREATE TABLE game_accounts (
    account_id uuid PRIMARY KEY,
    identity_issuer text NOT NULL,
    identity_subject text NOT NULL,
    display_name text NOT NULL CHECK (length(display_name) BETWEEN 1 AND 80),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (identity_issuer, identity_subject)
);

CREATE TABLE game_rule_versions (
    rule_version text PRIMARY KEY,
    content_sha256 text NOT NULL CHECK (content_sha256 ~ '^[0-9a-f]{64}$'),
    rules jsonb NOT NULL CHECK (jsonb_typeof(rules) = 'object'),
    published_at timestamptz NOT NULL DEFAULT now()
    -- Published rows are immutable by application policy/role privileges.
);

CREATE TABLE game_worlds (
    world_id uuid PRIMARY KEY,
    schema_version integer NOT NULL CHECK (schema_version > 0),
    rule_version text NOT NULL REFERENCES game_rule_versions(rule_version),
    revision bigint NOT NULL DEFAULT 0 CHECK (revision >= 0),
    simulation_tick bigint NOT NULL DEFAULT 0 CHECK (simulation_tick >= 0),
    state jsonb NOT NULL CHECK (jsonb_typeof(state) = 'object'),
    writer_id uuid,
    writer_epoch bigint NOT NULL DEFAULT 0 CHECK (writer_epoch >= 0),
    lease_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CHECK ((writer_id IS NULL) = (lease_until IS NULL))
);

CREATE TABLE world_members (
    world_id uuid NOT NULL REFERENCES game_worlds(world_id),
    account_id uuid NOT NULL REFERENCES game_accounts(account_id),
    role text NOT NULL CHECK (role IN ('owner', 'controller', 'viewer')),
    joined_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, account_id)
);
CREATE UNIQUE INDEX one_owner_per_world ON world_members(world_id) WHERE role = 'owner';
CREATE INDEX memberships_by_account ON world_members(account_id, world_id);

CREATE TABLE command_receipts (
    world_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    command_id uuid NOT NULL,
    request_sha256 text NOT NULL CHECK (request_sha256 ~ '^[0-9a-f]{64}$'),
    committed_revision bigint NOT NULL CHECK (committed_revision > 0),
    accepted boolean NOT NULL,
    result jsonb NOT NULL CHECK (jsonb_typeof(result) = 'object'),
    committed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, actor_id, command_id),
    FOREIGN KEY (world_id, actor_id) REFERENCES world_members(world_id, account_id)
);
CREATE INDEX command_receipts_by_revision ON command_receipts(world_id, committed_revision);

CREATE TABLE resource_transfers (
    world_id uuid NOT NULL REFERENCES game_worlds(world_id),
    transfer_id uuid NOT NULL,
    committed_revision bigint NOT NULL CHECK (committed_revision > 0),
    ordinal integer NOT NULL CHECK (ordinal >= 0),
    simulation_tick bigint NOT NULL CHECK (simulation_tick >= 0),
    resource_code text NOT NULL CHECK (resource_code IN ('wood', 'ore', 'food')),
    from_type text NOT NULL CHECK (from_type IN ('node', 'worker', 'depot')),
    from_id uuid NOT NULL,
    to_type text NOT NULL CHECK (to_type IN ('worker', 'depot', 'consumed')),
    to_id uuid NOT NULL,
    item_count bigint NOT NULL CHECK (item_count > 0),
    unit_weight_grams bigint NOT NULL CHECK (unit_weight_grams > 0),
    committed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (world_id, transfer_id),
    UNIQUE (world_id, committed_revision, ordinal),
    CHECK (from_type <> to_type OR from_id <> to_id),
    CHECK ((from_type = 'node' AND to_type = 'worker')
        OR (from_type = 'worker' AND to_type = 'depot')
        OR (from_type = 'depot' AND to_type = 'consumed'))
    -- Entity IDs refer to the versioned checkpoint. Validate references, ownership,
    -- counts, locked weights and balances in the trusted checkpoint transaction.
);
CREATE INDEX transfers_by_revision ON resource_transfers(world_id, committed_revision, ordinal);

COMMIT;

-- Checkpoint writes, receipts and transfers MUST commit together. Example CAS:
-- UPDATE game_worlds SET state=:state, simulation_tick=:tick,
--   revision=revision+1, updated_at=now()
-- WHERE world_id=:world AND revision=:expected_revision
--   AND writer_id=:writer AND writer_epoch=:epoch AND lease_until>now()
-- RETURNING revision;
-- Require exactly one row. On a duplicate command key, rollback and read the
-- existing receipt, comparing its request hash. Never continue a failed txn.
