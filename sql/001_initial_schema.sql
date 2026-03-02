CREATE TABLE email_headers (
    message_id       TEXT PRIMARY KEY,
    account_id       TEXT NOT NULL,
    folder_id        TEXT NOT NULL,
    subject          TEXT,
    from_address     TEXT,
    from_display     TEXT,
    to_addresses     TEXT,       -- JSON array
    cc_addresses     TEXT,       -- JSON array
    date             TEXT NOT NULL, -- ISO 8601
    is_read          INTEGER NOT NULL DEFAULT 0,
    is_flagged       INTEGER NOT NULL DEFAULT 0,
    snippet          TEXT,
    attachments      TEXT,       -- JSON array of {filename, content_type, size_bytes}
    synced_at        TEXT NOT NULL
);

CREATE INDEX idx_email_account_date ON email_headers(account_id, date DESC);
CREATE INDEX idx_email_unread ON email_headers(is_read, date DESC);
CREATE INDEX idx_email_flagged ON email_headers(is_flagged, date DESC);

CREATE TABLE email_bodies (
    message_id       TEXT PRIMARY KEY REFERENCES email_headers(message_id) ON DELETE CASCADE,
    plain_text       TEXT NOT NULL,
    synced_at        TEXT NOT NULL
);

CREATE TABLE calendar_events (
    event_id         TEXT PRIMARY KEY,
    account_id       TEXT NOT NULL,
    calendar_id      TEXT NOT NULL,
    summary          TEXT,
    description      TEXT,
    start_time       TEXT NOT NULL,
    end_time         TEXT NOT NULL,
    is_all_day       INTEGER NOT NULL DEFAULT 0,
    location         TEXT,
    invitees         TEXT,       -- JSON array
    recurrence_rule  TEXT,
    status           TEXT NOT NULL DEFAULT 'Confirmed',
    synced_at        TEXT NOT NULL
);

CREATE INDEX idx_cal_account_start ON calendar_events(account_id, start_time);
CREATE INDEX idx_cal_range ON calendar_events(start_time, end_time);

CREATE TABLE oauth_tokens (
    account_id       TEXT PRIMARY KEY,
    access_token     TEXT NOT NULL,
    refresh_token    TEXT NOT NULL,
    expires_at       TEXT NOT NULL
);

CREATE TABLE imap_credentials (
    account_id       TEXT PRIMARY KEY,
    password         TEXT NOT NULL
);

CREATE TABLE sync_state (
    account_id       TEXT NOT NULL,
    resource_type    TEXT NOT NULL,  -- 'email' | 'calendar'
    last_sync        TEXT,           -- ISO 8601
    sync_token       TEXT,           -- provider-specific delta token
    PRIMARY KEY (account_id, resource_type)
);
