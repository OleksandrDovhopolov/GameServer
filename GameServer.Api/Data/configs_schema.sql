CREATE TABLE IF NOT EXISTS configs (
    name        TEXT        NOT NULL,
    environment TEXT        NOT NULL,
    version     BIGINT      NOT NULL,
    etag        TEXT        NOT NULL,
    json        JSONB       NOT NULL,
    updated_by  TEXT        NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL,
    comment     TEXT,
    PRIMARY KEY (name, environment, version)
);

CREATE INDEX IF NOT EXISTS idx_configs_latest
    ON configs (name, environment, version DESC);
