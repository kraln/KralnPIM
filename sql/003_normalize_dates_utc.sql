-- Normalize email_headers.date to UTC so ORDER BY date DESC sorts chronologically.
-- SQLite doesn't have native timezone conversion, but all dates are stored as ISO 8601
-- with offset (e.g. "2026-03-09T12:56:27.0000000+01:00"). We parse and reconstruct in UTC.
-- This uses a simple approach: extract the offset hours/minutes and adjust.

UPDATE email_headers
SET date = strftime('%Y-%m-%dT%H:%M:%S.0000000+00:00',
    datetime(
        substr(date, 1, 19),
        CASE
            WHEN substr(date, -6, 1) = '+' THEN '-' || substr(date, -5, 2) || ' hours'
            WHEN substr(date, -6, 1) = '-' THEN '+' || substr(date, -5, 2) || ' hours'
            ELSE '+0 hours'
        END,
        CASE
            WHEN substr(date, -6, 1) IN ('+', '-') THEN
                CASE WHEN substr(date, -6, 1) = '+' THEN '-' ELSE '+' END || substr(date, -2, 2) || ' minutes'
            ELSE '+0 minutes'
        END
    ))
WHERE date NOT LIKE '%+00:00';
