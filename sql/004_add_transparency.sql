-- Add free/busy transparency to calendar events so the free/busy sink can
-- exclude events marked "free" (birthdays, working-location markers, etc.).
-- Existing rows default to Busy; providers backfill the real value on re-sync.

ALTER TABLE calendar_events ADD COLUMN transparency TEXT NOT NULL DEFAULT 'Busy';
