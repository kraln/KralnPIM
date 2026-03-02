CREATE VIRTUAL TABLE email_fts USING fts5(
    subject, from_address, from_display, snippet, plain_text,
    content='email_headers',
    content_rowid='rowid'
);

-- Trigger on header insert (body may not exist yet)
CREATE TRIGGER email_fts_insert AFTER INSERT ON email_headers
BEGIN
    INSERT INTO email_fts(rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES (NEW.rowid, NEW.subject, NEW.from_address, NEW.from_display, NEW.snippet, NULL);
END;

-- Trigger on header update
CREATE TRIGGER email_fts_update AFTER UPDATE ON email_headers
BEGIN
    INSERT INTO email_fts(email_fts, rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES ('delete', OLD.rowid, OLD.subject, OLD.from_address, OLD.from_display, OLD.snippet,
            (SELECT plain_text FROM email_bodies WHERE message_id = OLD.message_id));
    INSERT INTO email_fts(rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES (NEW.rowid, NEW.subject, NEW.from_address, NEW.from_display, NEW.snippet,
            (SELECT plain_text FROM email_bodies WHERE message_id = NEW.message_id));
END;

-- Trigger on header delete
CREATE TRIGGER email_fts_delete AFTER DELETE ON email_headers
BEGIN
    INSERT INTO email_fts(email_fts, rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES ('delete', OLD.rowid, OLD.subject, OLD.from_address, OLD.from_display, OLD.snippet,
            (SELECT plain_text FROM email_bodies WHERE message_id = OLD.message_id));
END;

-- Trigger on body insert to refresh FTS with body text
CREATE TRIGGER email_fts_body_insert AFTER INSERT ON email_bodies
BEGIN
    INSERT INTO email_fts(email_fts, rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES ('delete',
            (SELECT rowid FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT subject FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_address FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_display FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT snippet FROM email_headers WHERE message_id = NEW.message_id),
            NULL);
    INSERT INTO email_fts(rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES (
            (SELECT rowid FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT subject FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_address FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_display FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT snippet FROM email_headers WHERE message_id = NEW.message_id),
            NEW.plain_text);
END;

-- Trigger on body update to refresh FTS with updated body text
CREATE TRIGGER email_fts_body_update AFTER UPDATE ON email_bodies
BEGIN
    INSERT INTO email_fts(email_fts, rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES ('delete',
            (SELECT rowid FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT subject FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_address FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_display FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT snippet FROM email_headers WHERE message_id = NEW.message_id),
            OLD.plain_text);
    INSERT INTO email_fts(rowid, subject, from_address, from_display, snippet, plain_text)
    VALUES (
            (SELECT rowid FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT subject FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_address FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT from_display FROM email_headers WHERE message_id = NEW.message_id),
            (SELECT snippet FROM email_headers WHERE message_id = NEW.message_id),
            NEW.plain_text);
END;
