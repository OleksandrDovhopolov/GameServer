CREATE TABLE IF NOT EXISTS players (
    player_id    TEXT PRIMARY KEY,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_seen_at TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS player_identities (
    provider   TEXT NOT NULL,
    subject    TEXT NOT NULL,
    player_id  TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (provider, subject)
);
CREATE INDEX IF NOT EXISTS idx_player_identities_player ON player_identities (player_id);

CREATE TABLE IF NOT EXISTS refresh_tokens (
    token_hash TEXT PRIMARY KEY,
    player_id  TEXT NOT NULL REFERENCES players(player_id) ON DELETE CASCADE,
    issued_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_player ON refresh_tokens (player_id);

-- Future save-storage hardening, intentionally not applied in Player Auth V1.
-- Apply only after save storage has moved to Postgres and existing saves have
-- been backfilled into players/player_identities.
--
-- INSERT INTO players (player_id)
-- SELECT player_id FROM player_saves
-- ON CONFLICT DO NOTHING;
--
-- INSERT INTO player_identities (provider, subject, player_id)
-- SELECT 'legacy', player_id, player_id FROM player_saves
-- ON CONFLICT DO NOTHING;
--
-- ALTER TABLE player_saves ADD CONSTRAINT fk_player_saves_player
--     FOREIGN KEY (player_id) REFERENCES players(player_id) ON DELETE CASCADE;
