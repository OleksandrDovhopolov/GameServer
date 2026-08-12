CREATE TABLE IF NOT EXISTS player_saves (
    player_id TEXT PRIMARY KEY,
    root_document TEXT,
    last_modified BIGINT,
    resources JSONB,
    resources_version BIGINT,
    inventory JSONB,
    inventory_version BIGINT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_player_saves_updated_at ON player_saves (updated_at);
