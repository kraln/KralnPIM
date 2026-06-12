# Unified Personal Information Manager (PIM) — Implementation Specification

## 0. How to Read This Document

This document decomposes the PIM system into **modules** that can be assigned to independent agents. Each module defines its **public interface**, **dependencies**, and **acceptance criteria**. Modules within the same phase can be developed in parallel; phases must be completed sequentially.

---

## 1. Technology Decisions (Binding)

| Concern | Decision |
|---|---|
| Language / Runtime | C# / .NET 10 with Native AOT publish |
| Database | SQLite via `Microsoft.Data.Sqlite` |
| Schema Migrations | Hand-written SQL in numbered `.sql` files, applied at startup |
| Config | `config.yaml` parsed via `YamlDotNet` |
| TUI Framework | `Terminal.Gui` (v2) |
| HTTP/REST | ASP.NET Core Minimal APIs (Kestrel) |
| WebSocket | ASP.NET Core native WebSocket middleware |
| Logging | `Microsoft.Extensions.Logging` → stdout only |
| Testing | xUnit + NSubstitute for mocks |
| Build | Single `dotnet` solution, one project per module below |

---

## 2. Solution Structure

```
PIM.slnx
├── src/
│   ├── PIM.Core/              # Shared models, interfaces, config, DB schema
│   ├── PIM.Sync.Imap/         # IMAP/SMTP provider
│   ├── PIM.Sync.Google/       # Gmail + Google Calendar provider
│   ├── PIM.Sync.Graph/        # Office365 Mail + Calendar provider
│   ├── PIM.Sync.CalDav/       # CalDAV (Radicale) provider
│   ├── PIM.Search/            # Local + remote search orchestration
│   ├── PIM.Server/            # Daemon host: REST + WebSocket + sync scheduler
│   ├── PIM.Tui/               # Terminal.Gui client
│   └── PIM.SystemInfo/        # Power metrics, weather, clock
├── tests/
│   ├── PIM.Core.Tests/
│   ├── PIM.Sync.Imap.Tests/
│   ├── ... (mirrors src/)
├── config.example.yaml
└── sql/
    ├── 001_initial_schema.sql
    ├── 002_fts_indexes.sql
    └── ...
```

---

## 3. Module Specifications

---

### Module A: `PIM.Core` — Shared Kernel

**Phase:** 1 (build first — everything depends on this)
**Dependencies:** None
**Owner:** 1 agent

#### A.1 Configuration Model

Parse `config.yaml` into strongly-typed C# records. The config file is the single source of truth for all runtime settings.

```yaml
# config.example.yaml
accounts:
  - id: "personal-imap"
    type: imap                # imap | google | office365 | caldav
    display_name: "Personal"
    imap_host: "mail.example.com"
    imap_port: 993
    imap_tls: true
    smtp_host: "mail.example.com"
    smtp_port: 587
    username: "user@example.com"
    # password stored in DB after first setup

  - id: "work-google"
    type: google
    display_name: "Work (Google)"
    client_id: "xxx.apps.googleusercontent.com"
    client_secret: "GOCSPX-xxx"
    # OAuth tokens stored in DB after auth flow
    calendars: []             # selected during setup from discovered calendars

  - id: "work-o365"
    type: office365
    display_name: "Work (O365)"
    tenant_id: "xxxx-xxxx"
    client_id: "xxxx-xxxx"
    # OAuth tokens stored in DB after auth flow
    calendars: []             # selected during setup from discovered calendars

  - id: "my-radicale"
    type: caldav
    display_name: "Radicale"
    username: "user@example.com"
    # password stored in DB after setup
    calendars:
      - id: "personal"
        type: caldav
        url: "https://radicale.example.com/user/personal.ics"

ui:
  timezone_primary: "America/New_York"
  timezone_secondary: "Europe/London"

system:
  weather_location: "40.7128,-74.0060"     # lat,lon
  weather_provider: "open-meteo"           # only supported provider for v1

storage:
  db_path: "~/.pim/pim.db"
  attachment_download_dir: "~/Downloads/pim-attachments"
  buffer_months_back: 6
  buffer_months_forward: 6                 # calendar only

server:
  listen_address: "127.0.0.1"
  rest_port: 9400
  ws_port: 9401
```

**C# representation:**

```csharp
// PIM.Core/Config/PimConfig.cs
public sealed record PimConfig(
    List<AccountConfig> Accounts,
    UiConfig Ui,
    SystemConfig System,
    StorageConfig Storage,
    ServerConfig Server
);

public sealed record AccountConfig(
    string Id,
    AccountType Type,          // enum: Imap, Google, Office365
    string DisplayName,
    // IMAP-specific (nullable)
    string? ImapHost, int? ImapPort, bool? ImapTls,
    string? SmtpHost, int? SmtpPort,
    string? Username,
    // Google-specific (nullable)
    string? ClientId, string? ClientSecret,
    // O365-specific (nullable)
    string? TenantId,
    List<CalendarSourceConfig>? Calendars
);

public enum AccountType { Imap, Google, Office365, CalDav }

public sealed record CalendarSourceConfig(
    string Id,
    CalendarType Type,         // enum: CalDav, Google, Office365
    string? Url,
    string? Color = null,
    bool? FreebusySink = null  // true → daemon overwrites this calendar with an aggregated busy/free view (see H.7)
);
```

Provide a static `ConfigLoader.Load(string yamlPath) -> PimConfig` method that validates required fields per account type and throws descriptive errors.

#### A.2 Domain Models

All modules communicate via these shared models. They are **plain data records** — no behavior, no dependencies.

```csharp
// --- Email ---
public sealed record EmailHeader(
    string MessageId,           // globally unique (Message-ID header)
    string AccountId,           // FK to config account
    string FolderId,            // e.g., "INBOX", "Sent", "[Gmail]/All Mail"
    string Subject,
    string FromAddress,
    string FromDisplayName,
    List<string> ToAddresses,
    List<string> CcAddresses,
    DateTimeOffset Date,
    bool IsRead,
    bool IsFlagged,
    string? PlainTextSnippet,   // first ~200 chars, for list display
    List<AttachmentInfo> Attachments
);

public sealed record AttachmentInfo(
    string Filename,
    string ContentType,
    long SizeBytes
);

public sealed record EmailBody(
    string MessageId,
    string PlainTextContent     // full plain text body
);

// --- Calendar ---
public sealed record CalendarEvent(
    string EventId,             // provider-specific unique ID
    string AccountId,
    string CalendarId,
    string Summary,
    string? Description,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    List<string> Invitees,
    string? RecurrenceRule,     // RRULE string or null
    EventStatus Status,         // enum: Confirmed, Tentative, Cancelled
    Transparency Transparency = Transparency.Busy  // free/busy state; see H.7
);

public enum EventStatus { Confirmed, Tentative, Cancelled }

public enum Transparency { Busy, Free }

// --- Auth ---
public sealed record OAuthToken(
    string AccountId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt
);
```

#### A.3 Database Schema & Repository Interface

Database is SQLite. The schema below is applied via migration files in `/sql/`.

```sql
-- sql/001_initial_schema.sql

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
    message_id       TEXT PRIMARY KEY REFERENCES email_headers(message_id),
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
    transparency     TEXT NOT NULL DEFAULT 'Busy',
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
    password         TEXT NOT NULL   -- plaintext for v1; encrypt in v2
);

CREATE TABLE sync_state (
    account_id       TEXT NOT NULL,
    resource_type    TEXT NOT NULL,  -- 'email' | 'calendar'
    last_sync        TEXT,           -- ISO 8601
    sync_token       TEXT,           -- provider-specific delta token
    PRIMARY KEY (account_id, resource_type)
);
```

```sql
-- sql/002_fts_indexes.sql

CREATE VIRTUAL TABLE email_fts USING fts5(
    subject, from_address, from_display, snippet, plain_text,
    content='email_headers',
    content_rowid='rowid'
);

-- Triggers to keep FTS in sync:
CREATE TRIGGER email_fts_insert AFTER INSERT ON email_headers
BEGIN
    INSERT INTO email_fts(rowid, subject, from_address, from_display, snippet, plain_text)
    SELECT NEW.rowid, NEW.subject, NEW.from_address, NEW.from_display, NEW.snippet,
           (SELECT plain_text FROM email_bodies WHERE message_id = NEW.message_id);
END;
```

**Repository interfaces** (implemented in `PIM.Core`, backed by SQLite):

```csharp
public interface IEmailRepository
{
    Task UpsertHeadersAsync(IEnumerable<EmailHeader> headers);
    Task UpsertBodyAsync(string messageId, string plainText);
    Task<EmailHeader?> GetHeaderAsync(string messageId);
    Task<EmailBody?> GetBodyAsync(string messageId);
    Task<List<EmailHeader>> ListAsync(EmailListQuery query);
    Task SetReadAsync(string messageId, bool isRead);
    Task SetFlaggedAsync(string messageId, bool isFlagged);
    Task PurgeOlderThanAsync(DateTimeOffset cutoff);
}

public sealed record EmailListQuery(
    string? AccountId = null,
    bool? IsRead = null,
    bool? IsFlagged = null,
    int Offset = 0,
    int Limit = 50
);

public interface ICalendarRepository
{
    Task UpsertEventsAsync(IEnumerable<CalendarEvent> events);
    Task<List<CalendarEvent>> GetEventsInRangeAsync(
        DateTimeOffset start, DateTimeOffset end, string? accountId = null);
    Task DeleteEventAsync(string eventId);
    Task PurgeOlderThanAsync(DateTimeOffset cutoff);
}

public interface IAuthRepository
{
    Task SaveOAuthTokenAsync(OAuthToken token);
    Task<OAuthToken?> GetOAuthTokenAsync(string accountId);
    Task SaveImapPasswordAsync(string accountId, string password);
    Task<string?> GetImapPasswordAsync(string accountId);
}

public interface ISyncStateRepository
{
    Task<(DateTimeOffset? LastSync, string? SyncToken)> GetAsync(
        string accountId, string resourceType);
    Task SetAsync(string accountId, string resourceType,
        DateTimeOffset lastSync, string? syncToken);
}
```

#### A.4 Provider Interface (Contract for All Sync Modules)

Every sync provider must implement these interfaces. The daemon orchestrator calls them generically.

```csharp
public interface IMailProvider
{
    string AccountId { get; }

    /// Perform initial auth. Log the auth URL to stdout; block until token is received.
    Task AuthenticateAsync(CancellationToken ct);

    /// Sync recent mail (last N months). Return new/updated headers.
    /// Use sync_state.sync_token for delta sync where supported.
    Task<SyncResult<EmailHeader>> SyncMailAsync(
        DateTimeOffset since, CancellationToken ct);

    /// Fetch full plain-text body for a single message (on-demand).
    Task<string> FetchBodyAsync(string messageId, CancellationToken ct);

    /// Download attachment to disk. Return the local file path.
    Task<string> DownloadAttachmentAsync(
        string messageId, string filename, string targetDir, CancellationToken ct);

    /// Send an email.
    Task SendAsync(OutboundEmail email, CancellationToken ct);

    /// Server-side search (Stage 2 deep search).
    Task<List<EmailHeader>> RemoteSearchAsync(string query, CancellationToken ct);

    /// Mark read/flagged on the remote server.
    Task SetFlagsAsync(string messageId, bool? isRead, bool? isFlagged, CancellationToken ct);
}

public interface ICalendarProvider
{
    string AccountId { get; }
    Task AuthenticateAsync(CancellationToken ct);
    Task<SyncResult<CalendarEvent>> SyncEventsAsync(
        DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct);
    Task CreateEventAsync(CalendarEvent evt, CancellationToken ct);
    Task UpdateEventAsync(CalendarEvent evt, CancellationToken ct);
    Task DeleteEventAsync(string eventId, CancellationToken ct);
}

public sealed record SyncResult<T>(
    List<T> Upserted,
    List<string> DeletedIds,
    string? NewSyncToken
);

public sealed record OutboundEmail(
    string FromAccountId,
    List<string> To,
    List<string> Cc,
    List<string> Bcc,
    string Subject,
    string PlainTextBody,
    string? InReplyToMessageId
);
```

#### A.5 Acceptance Criteria for Module A

- [ ] `ConfigLoader.Load()` correctly parses `config.example.yaml` and rejects invalid configs with clear error messages.
- [ ] All domain records compile and serialize to/from JSON round-trip cleanly.
- [ ] SQLite schema applies cleanly on a fresh database via migration runner.
- [ ] All repository implementations pass unit tests for CRUD + edge cases (empty results, duplicates, purge).
- [ ] FTS5 index returns results when querying inserted email data.

---

### Module B: `PIM.Sync.Imap` — IMAP/SMTP Provider

**Phase:** 2 (parallel with C, D, E)
**Dependencies:** Module A (interfaces + models)
**Owner:** 1 agent
**Key Library:** `MailKit` (NuGet)

#### B.1 Responsibilities

Implement `IMailProvider` for standard IMAP accounts + `ICalendarProvider` delegating to a `PIM.Sync.CalDav` sub-module.

#### B.2 Implementation Notes

- Use `MailKit.Net.Imap.ImapClient` for IMAP operations.
- Use `MailKit.Net.Smtp.SmtpClient` for sending.
- **Delta sync:** Use IMAP `CONDSTORE`/`HIGHESTMODSEQ` if the server supports it (Dovecot does). Fall back to `UID`-based comparison otherwise. Store the highest UID or MODSEQ in `sync_state.sync_token`.
- **HTML stripping:** Use the `HtmlAgilityPack` library to strip HTML to plain text. Strip all `<img>`, `<style>`, `<script>` tags. Extract `alt` text from images. Preserve link `href` as `[text](url)`.
- **Snippet generation:** First 200 characters of the plain text body.
- **Remote search:** Use IMAP `SEARCH` command with `SUBJECT`, `FROM`, `BODY` criteria.
- **Connection management:** Maintain a persistent connection; reconnect on failure with exponential backoff (1s, 2s, 4s, max 60s).

#### B.3 Acceptance Criteria

- [ ] Can authenticate against a Dovecot IMAP server using stored credentials.
- [ ] `SyncMailAsync` retrieves headers for the configured time window and correctly delta-syncs on subsequent calls.
- [ ] HTML emails are converted to readable plain text with no HTML artifacts.
- [ ] `SendAsync` sends a plain text email via SMTP and it arrives at the destination.
- [ ] `RemoteSearchAsync` returns results matching the query from the IMAP server.
- [ ] `DownloadAttachmentAsync` saves the correct file to the specified directory.
- [ ] Connection drops are handled gracefully with automatic reconnection.

---

### Module C: `PIM.Sync.Google` — Google Workspace Provider

**Phase:** 2 (parallel with B, D, E)
**Dependencies:** Module A
**Owner:** 1 agent
**Key Libraries:** `Google.Apis.Gmail.v1`, `Google.Apis.Calendar.v3`

#### C.1 Responsibilities

Implement `IMailProvider` and `ICalendarProvider` for Google accounts.

#### C.2 Implementation Notes

- **OAuth flow:** Use `GoogleAuthorizationCodeFlow`. Print the auth URL to stdout. Use a local loopback redirect (`http://127.0.0.1:{random_port}`) to capture the callback automatically. Store tokens via `IAuthRepository`.
- **Delta sync (Mail):** Use Gmail API's `history.list` with `historyId` stored in `sync_state.sync_token`. On first sync, use `messages.list` with `after:{date}` query. After full sync, save the current history ID from `users.getProfile` (not stale per-message history IDs). If `history.list` returns 404 (NotFound) or 410 (Gone), fall back to full sync.
- **Delta sync (Calendar):** Use Calendar API's `events.list` with `syncToken`. On first sync, use `timeMin`/`timeMax` parameters. When no calendars are configured (`allowedCalendarIds` is null or empty), skip sync entirely to avoid syncing unwanted auto-discovered calendars.
- **All-day events:** Store dates with `TimeZoneInfo.Local.GetUtcOffset()` so midnight stays in the local timezone, not UTC.
- **Body fetch:** `messages.get` with `format=full`. Extract `text/plain` part; if absent, extract `text/html` and strip (same logic as Module B).
- **Sending:** `messages.send` via Gmail API (not SMTP).
- **Remote search:** `messages.list` with Gmail's native query syntax (passthrough).
- **Calendar discovery:** `calendarList.list` to discover calendars, then filter to the configured `allowedCalendarIds` set. If no calendars are configured, skip calendar sync entirely.
- **Rate limiting:** Respect Gmail's 250 quota units/sec. Implement a simple token-bucket rate limiter.

#### C.3 Acceptance Criteria

- [ ] OAuth flow completes end-to-end: auth URL printed, browser auth, token captured and stored.
- [ ] Token refresh works transparently before expiry.
- [ ] `SyncMailAsync` uses `history.list` for delta sync after initial load.
- [ ] Calendar events sync with proper timezone handling (all-day events, recurring events).
- [ ] `SendAsync` sends via Gmail API and the email appears in the sender's "Sent" folder.
- [ ] Rate limits are respected; no 429 errors under normal operation.

---

### Module D: `PIM.Sync.Graph` — Office 365 Provider

**Phase:** 2 (parallel with B, C, E)
**Dependencies:** Module A
**Owner:** 1 agent
**Key Library:** `Microsoft.Graph` SDK

#### D.1 Responsibilities

Implement `IMailProvider` and `ICalendarProvider` for Office 365 accounts.

#### D.2 Implementation Notes

- **OAuth flow:** Use MSAL (`Microsoft.Identity.Client`) with device code flow (`AcquireTokenWithDeviceCode`). This prints a URL + code to stdout — ideal for a headless daemon.
- **Delta sync (Mail):** Use Graph's `delta` endpoint for messages (`/me/mailFolders/inbox/messages/delta`). Store `deltaLink` in `sync_state.sync_token`.
- **Delta sync (Calendar):** Use `/me/calendarView/delta`.
- **Body fetch:** `GET /me/messages/{id}` with `$select=body`. Request `Prefer: outlook.body-content-type="text"` header to get plain text directly from Graph.
- **Sending:** `POST /me/sendMail`.
- **Remote search:** `POST /search/query` with `entityTypes: ["message"]`.
- **Calendar discovery:** `GET /me/calendars` to list calendars, then filter to the configured `allowedCalendarIds` set. If no calendars are configured, skip calendar sync entirely.
- **All-day events:** Store dates with `TimeZoneInfo.Local.GetUtcOffset()` so midnight stays in the local timezone, not UTC.
- **Throttling:** Respect `Retry-After` headers on 429 responses. The Graph SDK handles this partially; ensure it's configured.

#### D.3 Acceptance Criteria

- [ ] Device code flow completes: code printed to stdout, user authenticates in browser, token acquired.
- [ ] Delta sync correctly tracks changes using `deltaLink`.
- [ ] Calendar events with timezones, recurrence, and all-day flags are correctly mapped to `CalendarEvent`.
- [ ] `SendAsync` sends mail and the message appears in Sent Items.
- [ ] `RemoteSearchAsync` returns results from the Office 365 search index.

---

### Module E: `PIM.Sync.CalDav` — CalDAV Provider

**Phase:** 2 (parallel with B, C, D)
**Dependencies:** Module A
**Owner:** 1 agent (can be same agent as Module B)
**Key Library:** Hand-rolled HTTP + `Ical.Net` for iCalendar parsing

#### E.1 Responsibilities

Implement `ICalendarProvider` for CalDAV servers (Radicale, etc.).

#### E.2 Implementation Notes

- **Protocol:** Use `REPORT` requests with `calendar-query` or `calendar-multiget` (RFC 4791). Use `ctag` (collection tag) or `ETag` per-resource for delta sync.
- **Auth:** HTTP Basic Auth using credentials from `IAuthRepository`.
- **Parsing:** Parse iCalendar (`.ics`) responses with `Ical.Net`. Map `VEVENT` components to `CalendarEvent` records. All-day events (no time component) store dates with `TimeZoneInfo.Local.GetUtcOffset()` to keep midnight in the local timezone.
- **Write operations:** Use `PUT` to create/update events (send full `.ics`), `DELETE` to remove.
- **Sync strategy:** On each sync, fetch the `ctag` for the calendar collection. If it changed, fetch individual event `ETags` and diff against stored state. Only download changed/new events.

#### E.3 Acceptance Criteria

- [ ] Connects to a Radicale CalDAV server and lists events within the configured time range.
- [ ] Delta sync using `ctag`/`ETag` avoids re-downloading unchanged events.
- [ ] Recurring events (RRULE) are correctly parsed into `CalendarEvent` records.
- [ ] `CreateEventAsync` and `UpdateEventAsync` produce valid iCalendar data accepted by the server.
- [ ] `DeleteEventAsync` removes the event from the server.

---

### Module F: `PIM.Search` — Search Orchestration

**Phase:** 3 (needs Module A + at least one sync provider working)
**Dependencies:** Module A (repositories), Modules B/C/D (for remote search)
**Owner:** 1 agent

#### F.1 Responsibilities

Provide a unified search interface that abstracts local FTS and remote server-side search.

#### F.2 Interface

```csharp
public interface ISearchService
{
    /// Stage 1: Search local SQLite FTS index.
    Task<SearchResults> LocalSearchAsync(string query, SearchScope scope, int limit = 50);

    /// Stage 2: Fan out to all configured providers' RemoteSearchAsync.
    Task<SearchResults> DeepSearchAsync(string query, SearchScope scope, CancellationToken ct);
}

public enum SearchScope { All, EmailOnly, CalendarOnly }

public sealed record SearchResults(
    List<EmailHeader> Emails,
    List<CalendarEvent> Events,
    bool IsFromRemote
);
```

#### F.3 Implementation Notes

- **Local search:** Query `email_fts` using FTS5 syntax. For calendar, use SQL `LIKE` against `summary` and `description` (calendar volume is low enough that FTS isn't needed).
- **Deep search:** Call `RemoteSearchAsync` on each registered `IMailProvider` in parallel. Merge results, deduplicate by `messageId`.
- **Calendar deep search not needed for v1** — the 12-month local window is sufficient.

#### F.4 Acceptance Criteria

- [ ] Local search returns relevant emails within <100ms for a database with 10,000+ headers.
- [ ] Deep search fans out to all providers concurrently and merges results correctly.
- [ ] Duplicate results (same `messageId` from local + remote) are deduplicated.

---

### Module G: `PIM.SystemInfo` — System Data Collectors

**Phase:** 2 (parallel, no dependencies on other sync modules)
**Dependencies:** Module A (config for location/timezone)
**Owner:** 1 agent

#### G.1 Responsibilities

Provide system metrics and environmental data for the Dashboard.

#### G.2 Interface

```csharp
public interface IPowerInfoProvider
{
    Task<PowerInfo> GetAsync();
}

public sealed record PowerInfo(
    int BatteryPercent,         // 0-100, or -1 if no battery
    string? TimeRemaining,      // "2h 15m" or null
    double? DrainWatts           // current power draw or null
);

public interface IWeatherProvider
{
    Task<WeatherInfo> GetCurrentAsync(double lat, double lon);
}

public sealed record WeatherInfo(
    double TemperatureCelsius,
    string Condition,           // "Clear", "Cloudy", "Rain", etc.
    int HumidityPercent,
    double WindSpeedKmh
);

public interface IClockProvider
{
    ClockInfo GetCurrent(List<string> timezoneIds);
}

public sealed record ClockInfo(
    List<TimeZoneDisplay> Zones
);

public sealed record TimeZoneDisplay(
    string TimezoneId,         // e.g., "America/New_York"
    string Label,              // e.g., "EST"
    DateTimeOffset CurrentTime
);
```

#### G.3 Implementation Notes

- **Power (Linux):** Read `/sys/class/power_supply/BAT0/capacity`, `/sys/class/power_supply/BAT0/status`, etc.
- **Power (macOS):** Parse output of `ioreg -l -w0 | grep -E "CurrentCapacity|MaxCapacity|InstantAmperage"`.
- **Power (Windows):** Use WMI via `System.Management` — query `Win32_Battery`.
- **Weather:** HTTP GET to Open-Meteo API (`https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current_weather=true`). No API key needed.
- **Clock:** Pure .NET `TimeZoneInfo.ConvertTime()`. No external dependencies.
- **Use a factory pattern** to select the correct `IPowerInfoProvider` implementation at startup based on `RuntimeInformation.IsOSPlatform()`.

#### G.4 Acceptance Criteria

- [ ] Power info returns valid data on at least one target OS (Linux or macOS or Windows).
- [ ] Weather provider returns current conditions from Open-Meteo.
- [ ] Clock provider correctly formats times in multiple timezones.
- [ ] All providers gracefully return fallback values (not exceptions) when data is unavailable.

---

### Module H: `PIM.Server` — Daemon Host

**Phase:** 3 (needs A + at least B or C or D)
**Dependencies:** Modules A, B, C, D, E, F, G
**Owner:** 1 agent

#### H.1 Responsibilities

The daemon process that wires everything together: hosts the REST API, WebSocket server, and sync scheduler.

#### H.2 REST API Specification

Base URL: `http://127.0.0.1:9400`

**Email Endpoints:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/mail` | List email headers. Query params: `accountId`, `isRead`, `isFlagged`, `offset`, `limit` |
| `GET` | `/api/mail/{messageId}` | Get full email (header + body). Fetches body on-demand if not cached. |
| `PATCH` | `/api/mail/{messageId}` | Update flags. Body: `{ "isRead": bool?, "isFlagged": bool? }` |
| `POST` | `/api/mail/send` | Send email. Body: `OutboundEmail` JSON. |
| `GET` | `/api/mail/attachment/{messageId}/{filename}` | Download attachment. Returns file or triggers download + returns path. |
| `GET` | `/api/mail/accounts` | List configured email accounts with unread/flagged counts. |

**Calendar Endpoints:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/calendar/events` | List events. Query params: `start`, `end`, `accountId`, `calendarId` |
| `POST` | `/api/calendar/events` | Create event. Body: `CalendarEvent` JSON. |
| `PUT` | `/api/calendar/events/{eventId}` | Update event. |
| `DELETE` | `/api/calendar/events/{eventId}` | Delete event. |

**Search Endpoints:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/search?q={query}&scope={all\|email\|calendar}` | Local search (Stage 1). |
| `POST` | `/api/search/deep` | Deep search (Stage 2). Body: `{ "query": "...", "scope": "..." }` |

**System Endpoints:**

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/system/power` | Current power metrics. |
| `GET` | `/api/system/weather` | Current weather. |
| `GET` | `/api/system/clock` | Current times in configured timezones. |
| `GET` | `/api/system/status` | Daemon health: online/offline per account, last sync times. |

#### H.3 WebSocket Events

Connect to `ws://127.0.0.1:9401/ws`. Server pushes JSON events:

```jsonc
// New/updated emails arrived
{
  "type": "mail.sync",
  "accountId": "work-google",
  "newCount": 3,
  "updatedIds": ["msg-123", "msg-456"]
}

// Calendar events changed
{
  "type": "calendar.sync",
  "accountId": "work-o365",
  "updatedCount": 1
}

// Connectivity change
{
  "type": "status.change",
  "accountId": "personal-imap",
  "online": false
}
```

#### H.4 Sync Scheduler

- Run sync for each account on a configurable interval (default: every 5 minutes).
- Use `System.Threading.PeriodicTimer`.
- On each tick per account:
  1. Call `IMailProvider.SyncMailAsync()` → upsert results to DB → push WebSocket event.
  2. Call `ICalendarProvider.SyncEventsAsync()` → upsert results to DB → push WebSocket event.
  3. Refresh any free/busy sinks (see H.7).
  4. Call `PurgeOlderThanAsync()` on repos to trim the buffer window.
- Handle auth failures by logging an error and marking the account as offline (push `status.change`).
- Handle network failures with retry (3 attempts with exponential backoff), then mark offline.

#### H.5 Startup Sequence

1. Load config.yaml.
2. Run database migrations.
3. For each account, check if auth tokens exist. If not, initiate the auth flow (print URL/code to stdout, wait for completion).
4. Start the sync scheduler.
5. Start the REST API and WebSocket servers.
6. Log `PIM daemon ready on http://127.0.0.1:9400` to stdout.

#### H.6 Offline Mode Handling

- Each account has an `online: bool` status tracked in-memory.
- When an account goes offline, the REST API continues serving local data but returns `503` for write operations targeting that account.
- The TUI reads the `status.change` WebSocket events to display `[OFFLINE]` indicators.

#### H.7 Free/Busy Sink

A **sink** is a calendar flagged with `freebusy_sink: true` in config. The daemon treats it not as a source of events to read, but as a destination it overwrites with an anonymized busy/free view aggregated from every other calendar.

On each sink refresh:

1. **Gather.** Read all events in the buffer window (`buffer_months_back` … `buffer_months_forward`) across every account.
2. **Filter.** Drop cancelled events, events that are themselves on a sink calendar (so sinks never feed each other), and events whose `Transparency` is `Free`.
3. **Coalesce.** Sort the remaining intervals and merge them: overlapping intervals combine, and intervals separated by no more than a 5-minute bridge gap join into one block. All-day events expand to full local days in the primary timezone.
4. **Diff.** Compute a SHA-256 hash over the block boundaries. A shadow record — the hash plus the IDs of the events last created on the sink — is persisted via `ISyncStateRepository` under `freebusy-sink:{calendarId}`. If the hash is unchanged, the refresh is a no-op (no provider calls).
5. **Rewrite.** On a hash change, delete the previously created events (by the IDs in the shadow), create one opaque `"Busy"` event per block (no title detail, description, location, or attendees), and store the new shadow.

The sink calendar must exist and be dedicated to this purpose — the daemon owns its contents and deletes the events it previously created. Created-event UIDs are not stable across refreshes.

**Transparency source.** `CalendarEvent.Transparency` (`Busy`/`Free`) is populated by each provider's mapper from its native free/busy field — iCal `TRANSP` (CalDAV), Google `transparency`, Graph `showAs`, EventKit availability. Only an explicitly-free value maps to `Free`; anything else (opaque, tentative, out-of-office, unknown, or absent) maps to `Busy`.

#### H.8 Acceptance Criteria

- [ ] Daemon starts, runs migrations, completes auth flows, and begins syncing.
- [ ] REST API returns correct data for all endpoints.
- [ ] WebSocket pushes events when new data arrives.
- [ ] Sync scheduler runs at the configured interval without drift or memory leaks.
- [ ] An account going offline is detected, logged, and communicated via WebSocket within 30 seconds.
- [ ] Write operations return 503 when the target account is offline.
- [ ] A free/busy sink reflects busy blocks from all source calendars, excludes free events, and is a no-op when nothing changed.

---

### Module I: `PIM.Tui` — Terminal User Interface

**Phase:** 4 (needs Module H running)
**Dependencies:** Module H (REST API + WebSocket)
**Owner:** 1-2 agents (can split by Window)

#### I.1 Architecture

The TUI is a **pure client**. It has zero direct database or provider access. It communicates exclusively via the REST API and WebSocket.

```
TUI  ──REST──▶  Daemon
TUI  ◀──WS───  Daemon
```

#### I.2 HTTP Client Service

Create a `PimApiClient` class that wraps all REST calls:

```csharp
public class PimApiClient
{
    private readonly HttpClient _http;

    public PimApiClient(string baseUrl) { ... }

    // Mail
    public Task<List<EmailHeader>> ListMailAsync(EmailListQuery query);
    public Task<EmailBody> GetMailBodyAsync(string messageId);
    public Task SetMailFlagsAsync(string messageId, bool? isRead, bool? isFlagged);
    public Task SendMailAsync(OutboundEmail email);
    public Task<string> DownloadAttachmentAsync(string messageId, string filename);
    public Task<List<AccountOverview>> GetAccountOverviewsAsync();

    // Calendar
    public Task<List<CalendarEvent>> GetEventsAsync(DateTimeOffset start, DateTimeOffset end);
    public Task CreateEventAsync(CalendarEvent evt);
    public Task UpdateEventAsync(CalendarEvent evt);
    public Task DeleteEventAsync(string eventId);

    // Search
    public Task<SearchResults> SearchLocalAsync(string query, SearchScope scope);
    public Task<SearchResults> SearchDeepAsync(string query, SearchScope scope);

    // System
    public Task<PowerInfo> GetPowerAsync();
    public Task<WeatherInfo> GetWeatherAsync();
    public Task<ClockInfo> GetClockAsync();
    public Task<SystemStatus> GetStatusAsync();
}
```

#### I.3 Window Specifications

**Tab 1: Dashboard**

```
┌─ Agenda ──────────┬─ System ─────────────┬─ Mail Overview ────────────┐
│ Today - Mon Jan 6  │ Monday, 6 January    │ Accounts:                  │
│   09:00  Standup   │    2025  (W2)        │ ● Personal   3 unread  1 ★│
│   10:30  Design Rev│                      │ ● Work (G)  12 unread  0 ★│
│   12:00  Lunch     │ Weather: 22.2°C Clear│ ● Work (O)   0 unread  2 ★│
│   14:00  Sprint    │ Power: 87% (3h 12m)  │──────────────────────────  │
│   16:30  1:1 w/ Mgr│                      │ ✉ [Work G] Design specs.. │
│                    │ Clocks:              │ ✉ [Person] Re: Weekend..  │
│ Wed Jan 8          │   New York: 10:42    │ ✉ [Work O] Q4 Budget ap.. │
│   09:00  Review    │   London: 15:42      │ ★ [Person] Flight conf..  │
└────────────────────┴──────────────────────┴────────────────────────────┘
```

- Agenda: Fetch `GET /api/calendar/events?start={today}&end={today+14d}`, grouped by day with headers.
- System: Date/week at top; poll `GET /api/system/power`, `/weather`, `/clock` every 60 seconds.
- Mail Overview: Fetch `GET /api/mail/accounts` (excluding CalDAV) and `GET /api/mail?limit=10`.

**Tab 2: Calendar**

```
┌─ Upcoming ────────┬─ Timeline: Jan 6 - Jan 9 ─────────────────────────┐
│ 14-day agenda     │  Mon 6     Tue 7     Wed 8     Thu 9              │
│                   │ ┌───────┐                                         │
│                   │ │09 Stup│ ┌───────┐                               │
│                   │ └───────┘ │10 Dsgn│            ┌───────┐          │
│                   │ ┌───────┐ └───────┘            │09 Retro│         │
│                   │ │10:30  │                      └───────┘          │
│                   │ │Design │                                         │
│                   │ └───────┘                                         │
│                   │                                                   │
│ [N]ew  [←][→]    │                                                   │
└───────────────────┴───────────────────────────────────────────────────┘
```

- Timeline: Fetch `GET /api/calendar/events?start={day1}&end={day4+1}`. All-day events render in a dedicated banner row between the weather forecast header and the hourly time slots.
- Paginate with `←`/`→` keys (shift the 4-day window by 1 day).
- `N` key opens the **Event Editor** in place of the timeline:

```
┌─ Agenda ──────────┬─ New Event ────────────────────────────────────────┐
│ ...               │ Summary:  [________________________]              │
│                   │ Start:    [2025-01-06] [10:00] [America/New_York]  │
│                   │ End:      [2025-01-06] [11:00] [America/New_York]  │
│                   │ All Day:  [ ]                                      │
│                   │ Calendar: [▼ Work (Google) > Primary ]             │
│                   │ Location: [________________________]              │
│                   │ Invitees: [________________________]              │
│                   │ Description:                                       │
│                   │ [                                                 ]│
│                   │ [                                                 ]│
│                   │                                                   │
│                   │ [Save]  [Cancel]                                   │
└───────────────────┴───────────────────────────────────────────────────┘
```

**Tab 3: Email**

```
┌─ Inbox ───────────┬─ Reader ───────────────────────────────────────────┐
│ ● Design specs    │ From: alice@work.com                               │
│   alice@work 10:42│ To: me@work.com                                    │
│ ● Re: Weekend     │ Date: Mon, 6 Jan 2025 10:42 AM                    │
│   bob@me.com 09:15│ Subject: Design specs for Project X                │
│ ★ Flight confirm  │ ───────────────────────────────────────────────    │
│   airline  Jan 3  │ Hey,                                               │
│   Budget approval │                                                    │
│   cfo@work  Jan 2 │ Here are the updated design specs. Please review   │
│                   │ and let me know if you have any questions.          │
│                   │                                                    │
│ [U]nread [F]lag   │ [🔗 design-specs-v2.pdf (2.4 MB)]                 │
│ [/] Search        │ [🔗 wireframes.fig (890 KB)]                      │
│                   │                                                    │
│                   │ [R]eply  [N]ew  [D]ownload Attachment              │
└───────────────────┴────────────────────────────────────────────────────┘
```

- Inbox list: `GET /api/mail?limit=50`. Paginate with `PageUp`/`PageDown`.
- Reader: `GET /api/mail/{messageId}` when an item is selected.
- `R` opens the **Composer** (replaces the Reader pane):

```
┌─ Inbox ───────────┬─ Compose ──────────────────────────────────────────┐
│ (same)            │ From: [▼ me@work.com (Work Google)              ]  │
│                   │ To:   [alice@work.com                           ]  │
│                   │ CC:   [                                         ]  │
│                   │ BCC:  [                                         ]  │
│                   │ Subj: [Re: Design specs for Project X           ]  │
│                   │ ────────────────────────────────────────────────── │
│                   │ [                                                 ]│
│                   │ [                                                 ]│
│                   │ [                                                 ]│
│                   │ [                                                 ]│
│                   │                                                    │
│                   │ [Send]  [Cancel]                                    │
└───────────────────┴────────────────────────────────────────────────────┘
```

#### I.4 WebSocket Handling

On receiving WebSocket events:
- `mail.sync`: Refresh the inbox list if on Tab 1 or Tab 3. Update unread counts on Tab 1.
- `calendar.sync`: Refresh agenda and timeline if on Tab 1 or Tab 2.
- `status.change`: Show/hide `[OFFLINE]` indicator per account. If offline, disable send/reply/create buttons and show a status bar message.

#### I.5 Keybinding Summary

| Key | Context | Action |
|---|---|---|
| `Tab` | Global | Cycle between tabs (Dashboard → Calendar → Email) |
| `↑`/`↓` | List pane | Navigate items |
| `Enter` | List pane | Select / open item |
| `←`/`→` | Calendar | Paginate the 4-day window |
| `N` | Calendar / Email | New event / New email |
| `R` | Email reader | Reply |
| `U` | Email list | Toggle unread filter |
| `F` | Email list | Toggle flagged filter |
| `D` | Email reader | Download selected attachment |
| `/` | Email list | Focus search bar |
| `Esc` | Editor/Composer | Cancel and return to previous view |
| `Ctrl+S` | Editor/Composer | Save / Send |
| `q` | Global | Quit TUI |

#### I.6 Acceptance Criteria

- [ ] All three tabs render correctly with data from the REST API.
- [ ] WebSocket events trigger live UI updates without manual refresh.
- [ ] Email compose/reply flow works end-to-end (compose → send → appears in inbox).
- [ ] Calendar event creation flow works end-to-end (fill form → save → appears on timeline).
- [ ] Offline state is visually indicated and write operations are gracefully blocked.
- [ ] TUI handles terminal resize without crashing.
- [ ] All keybindings work as specified.

---

## 4. Phased Delivery Plan

```
Phase 1: Foundation                              [Module A]
   │      Core models, DB schema, config,
   │      provider interfaces, repository impl
   │
Phase 2: Providers (parallel)                    [Modules B, C, D, E, G]
   │      Each sync provider + system info
   │      Each can be tested independently
   │      with integration tests against
   │      real servers
   │
Phase 3: Orchestration                           [Modules F, H]
   │      Search service, daemon host,
   │      REST API, WebSocket, scheduler
   │      Integration test: daemon syncs
   │      and serves data via API
   │
Phase 4: User Interface                          [Module I]
          TUI client consuming the REST API
          End-to-end testing
```

### Milestone Checkpoints

**M1 (end of Phase 1):** `dotnet build` succeeds. All repository unit tests pass. A test program can insert and query emails/events from SQLite.

**M2 (end of Phase 2):** Each provider can be run in isolation with a test harness that: authenticates, syncs data, prints results to stdout. System info providers return data on at least one OS.

**M3 (end of Phase 3):** The daemon starts, syncs all configured accounts, and serves data via REST. A `curl` session can list emails, read a message, and create a calendar event.

**M4 (end of Phase 4):** The TUI connects to the daemon and all three windows are functional. A user can read email, compose a reply, view their calendar, and create an event entirely from the terminal.

---

## 5. Cross-Cutting Concerns

### 5.1 Error Handling Strategy

- **Providers:** Never throw on transient network errors. Return partial results and log warnings. Throw only on auth failures or configuration errors.
- **REST API:** Return appropriate HTTP status codes (400 for bad input, 404 for not found, 503 for offline accounts, 500 for unexpected errors). Always return a JSON error body: `{ "error": "description" }`.
- **TUI:** Display errors in a status bar at the bottom of the screen. Never crash on API errors.

### 5.2 Cancellation

All async methods accept `CancellationToken`. The daemon uses a single `CancellationTokenSource` triggered on `SIGTERM`/`SIGINT` to cleanly shut down all sync operations and close connections.

### 5.3 Native AOT Compatibility

- Avoid reflection-based serialization. Use `System.Text.Json` source generators for all JSON models.
- Avoid `System.Reflection.Emit` and dynamic assemblies.
- Test AOT compilation early (Phase 1) with `dotnet publish -r <rid> /p:PublishAot=true` to catch issues before they compound.
- **Known risk:** Some NuGet libraries (e.g., `Microsoft.Graph`) may not be fully AOT-compatible. If blocking issues arise, fall back to self-contained single-file publish (still low memory with trimming) rather than blocking progress.

### 5.4 Testing Strategy

- **Unit tests:** Every repository method, every provider's data mapping logic, every HTML-to-text conversion.
- **Integration tests:** Each provider against a real server (use a dedicated test account). These are opt-in (require credentials in a `.env` file) and excluded from CI by default.
- **End-to-end test:** A scripted test that starts the daemon, waits for sync, calls REST endpoints, and verifies data.

### 5.5 Logging Convention

Use structured logging with `ILogger<T>`:

```csharp
_logger.LogInformation("Synced {Count} emails for {AccountId}", result.Upserted.Count, accountId);
_logger.LogWarning("IMAP connection lost for {AccountId}, retrying in {Delay}s", accountId, delay);
_logger.LogError(ex, "Failed to refresh OAuth token for {AccountId}", accountId);
```

Log levels: `Debug` for detailed sync progress, `Information` for normal operations, `Warning` for recoverable issues, `Error` for failures requiring attention.

