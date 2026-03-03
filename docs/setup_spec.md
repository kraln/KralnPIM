# PIM.Setup — Configuration Manager Specification

## 0. Purpose

PIM.Setup is a standalone Terminal.Gui v2 TUI that manages KralnPIM configuration. It replaces manual YAML editing and handles the complete first-run experience: creating `config.yaml`, running OAuth flows, storing passwords, testing connections, and initializing the SQLite database. It also supports editing existing configurations.

This spec also introduces CalDAV as a first-class account type, independent from IMAP. CalDAV accounts have their own username, password, and calendar list — they are not nested under IMAP accounts. This requires changes to PIM.Core's config model, validation, and PIM.Server's provider registry (see Section 14).

Run via:
```sh
dotnet run --project src/PIM.Setup [-- --config-path /path/to/config.yaml]
```

Default config path: `~/.pim/config.yaml`

---

## 1. Project Structure

```
src/PIM.Setup/
    PIM.Setup.csproj
    Program.cs                  # Entry point, CLI args, Application lifecycle
    SetupApp.cs                 # Window subclass, main menu, view swapping
    Config/
        ConfigSerializer.cs     # YAML round-trip (load via PIM.Core, serialize via YamlDotNet)
    Auth/
        GoogleAuthFlow.cs       # Loopback HTTP listener OAuth flow
        GraphAuthFlow.cs        # MSAL device code flow
    Views/
        MainMenuView.cs         # Top-level navigation
        AccountListView.cs      # List accounts with status, add/edit/remove/test
        AccountWizardView.cs    # Multi-step account creation and editing
        SettingsView.cs         # UI, System, Storage, Server settings forms
        ConnectionTestView.cs   # Progress display and results for connection tests
tests/PIM.Setup.Tests/
    PIM.Setup.Tests.csproj
    Config/
        ConfigSerializerTests.cs
```

### Dependencies

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="PIM.Setup.Tests" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Terminal.Gui" Version="2.0.0-develop.5027" />
    <PackageReference Include="YamlDotNet" Version="16.3.0" />
    <PackageReference Include="MailKit" Version="4.12.0" />
    <PackageReference Include="Google.Apis.Auth" Version="1.69.0" />
    <PackageReference Include="Microsoft.Identity.Client" Version="4.82.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\PIM.Core\PIM.Core.csproj" />
  </ItemGroup>
</Project>
```

PIM.Setup does **not** reference `PIM.Sync.*` projects. The OAuth flow code (~50 lines for Google, ~30 lines for O365) is duplicated to avoid pulling in the full sync provider dependency trees (Google.Apis.Gmail, Microsoft.Graph, Ical.Net, etc.). The setup tool never syncs — it only authenticates and tests connectivity.

### Solution File

Add to `PIM.slnx`:
```xml
<Project Path="src/PIM.Setup/PIM.Setup.csproj" />
<Project Path="tests/PIM.Setup.Tests/PIM.Setup.Tests.csproj" />
```

---

## 2. Entry Point

`Program.cs` follows the same pattern as `PIM.Tui/Program.cs`:

```csharp
// Parse --config-path arg (default: ~/.pim/config.yaml)
// Create ILoggerFactory
// Application.Init()
// try { Application.Run(new SetupApp(configPath, loggerFactory)); }
// finally { Application.Shutdown(); }
```

On startup, `SetupApp` calls `ConfigSerializer.LoadOrDefault(configPath)`:
- If `config.yaml` exists: load it, populate in-memory `PimConfig` for editing
- If it doesn't: start with defaults (empty accounts, default storage/server settings)

---

## 3. UI Layout

### 3.1 SetupApp (Window)

`SetupApp` extends `Window` with a swappable content area and a status bar. Views are swapped by removing the current view and adding the new one — no `TabView` (setup is menu-driven, not tabbed).

```
+---------------------------------------------------------------------+
| KralnPIM Setup                                                      |
|                                                                     |
|  [content area — one view at a time]                                |
|                                                                     |
| Status: Ready                                                       |
+---------------------------------------------------------------------+
```

The status bar uses the same `Label` at `Pos.AnchorEnd(1)` with auto-clear pattern from `TuiApp`.

### 3.2 MainMenuView

```
  Config: ~/.pim/config.yaml         [exists]
  Database: ~/.pim/pim.db            [not found]
  Accounts: 2 configured

  1. Accounts          Add, edit, remove, and test accounts
  2. UI Settings       Timezones
  3. System Settings   Weather location and provider
  4. Storage Settings  Database path, attachments, buffer window
  5. Server Settings   Listen address and ports
  ─────────────────────────────────────
  6. Test All          Run connection tests for all accounts
  7. Save & Exit       Write config and exit
  8. Exit              Exit without saving
```

`ListView` with 8 items. Navigate with arrow keys + `Enter`, or press the number key directly. `Esc` from any sub-view returns here.

Status line at top shows:
- Config file path and whether it exists
- DB path and whether it exists
- Number of configured accounts

"Save & Exit" validates the config (same rules as `ConfigLoader.Validate`), writes YAML, ensures DB is initialized, then exits. If validation fails, show errors and stay.

"Exit" without saving uses a two-press confirmation if there are unsaved changes: first press shows a status bar warning, second press exits.

### 3.3 AccountListView

```
  Accounts                                            [A]dd   [Esc] Back

  Type       Display Name         Auth Status       Actions
  ─────────────────────────────────────────────────────────────────────
  IMAP       Personal             Has password      [E]dit [T]est [D]el
  Google     Work (Google)        Has token          [E]dit [T]est [D]el
  O365       Work (O365)          No token           [E]dit [T]est [D]el
  CalDAV     Radicale             Has password      [E]dit [T]est [D]el
```

`ListView` of configured accounts. Auth status column queries the DB:
- IMAP: `IAuthRepository.GetImapPasswordAsync` → "Has password" / "No password"
- CalDAV: `IAuthRepository.GetCalDavPasswordAsync` → "Has password" / "No password"
- Google: `IAuthRepository.GetOAuthTokenAsync` → "Has token" / "No token"
- O365: `IAuthRepository.GetOAuthTokenAsync` → "Has token" / "No token"

Key bindings:
- `A` — Add new account (opens AccountWizardView)
- `E` — Edit selected account (opens AccountWizardView pre-filled)
- `T` — Test selected account (opens ConnectionTestView)
- `D` — Delete selected account (two-press confirmation: first press shows status bar warning, second press deletes)
- `Esc` — Return to main menu

On deletion, removes account from in-memory config and deletes stored credentials from `oauth_tokens` and `imap_credentials` tables (best-effort). Does NOT delete synced email/calendar data (separate concern).

---

## 4. Account Wizard

Multi-step form within `AccountWizardView`. Each step validates before allowing "Next". "Back" returns to the previous step. "Cancel" returns to account list without changes.

### 4.1 Step 1: Account Type

```
  Add Account                                         Step 1 of N

  Choose account type:

    ( ) IMAP / SMTP        Standard email server
    (o) Google              Gmail + Google Calendar via OAuth
    ( ) Office 365          O365 Mail + Calendar via device code auth
    ( ) CalDAV              Standalone calendar server (Radicale, Nextcloud, etc.)

                                             [Next]   [Cancel]
```

`ListView` with 4 options (RadioGroup does not exist in Terminal.Gui v2). Selection determines subsequent steps:
- IMAP: 4 steps (type → fields → password → auth+test)
- Google: 3 steps (type → fields → auth+test)
- O365: 3 steps (type → fields → auth+test)
- CalDAV: 4 steps (type → fields → password → calendars+test)

When editing an existing account, account type is read-only (displayed but not changeable).

### 4.2 Step 2: Account Details

Fields vary by account type. All use `TextField` with validation on "Next".

**IMAP:**
```
  IMAP Account Details                                Step 2 of 4

  Account ID:     [personal-imap_________]    unique slug, a-z 0-9 hyphens
  Display Name:   [Personal________________]
  IMAP Host:      [mail.example.com________]
  IMAP Port:      [993____]
  Use TLS:        [X]
  SMTP Host:      [mail.example.com________]
  SMTP Port:      [587____]
  Username:       [user@example.com________]

                                    [Back]   [Next]   [Cancel]
```

Validation:
- Account ID: required, `^[a-z0-9-]+$`, unique among all accounts
- Display Name: required
- IMAP Host: required
- IMAP Port: required, integer 1–65535
- SMTP Host: required
- SMTP Port: required, integer 1–65535
- Username: required

**Google:**
```
  Google Account Details                              Step 2 of 3

  Account ID:      [work-google____________]
  Display Name:    [Work (Google)___________]
  Client ID:       [xxx.apps.googleusercontent.com___]
  Client Secret:   [GOCSPX-xxx_____________]

                                    [Back]   [Next]   [Cancel]
```

Validation:
- Account ID: required, `^[a-z0-9-]+$`, unique
- Display Name: required
- Client ID: required
- Client Secret: required

**Office 365:**
```
  Office 365 Account Details                          Step 2 of 3

  Account ID:      [work-o365______________]
  Display Name:    [Work (O365)_____________]
  Tenant ID:       [xxxxxxxx-xxxx-xxxx____]
  Client ID:       [xxxxxxxx-xxxx-xxxx____]

                                    [Back]   [Next]   [Cancel]
```

Validation:
- Account ID: required, `^[a-z0-9-]+$`, unique
- Display Name: required
- Tenant ID: required
- Client ID: required

### 4.3 Step 3 (IMAP and CalDAV): Password

```
  Password                                            Step 3 of 4

  Password for user@example.com:
  [**************************]

  Confirm password:
  [**************************]

  Password is stored in the local database, not in config.yaml.

                                    [Back]   [Next]   [Cancel]
```

`TextField` with `Secret = true`. Passwords must match. Held in memory until the final step persists to DB. Used by both IMAP accounts (for IMAP/SMTP auth) and CalDAV accounts (for Basic Auth).

### 4.4 Step 2b (CalDAV only): Account Details

```
  CalDAV Account Details                              Step 2 of 4

  Account ID:     [my-radicale___________]    unique slug, a-z 0-9 hyphens
  Display Name:   [Radicale________________]
  Username:       [user@example.com________]

                                    [Back]   [Next]   [Cancel]
```

Validation:
- Account ID: required, `^[a-z0-9-]+$`, unique among all accounts
- Display Name: required
- Username: required

### 4.5 Step 4 (CalDAV only): Calendars & Test

```
  CalDAV Calendars                                    Step 4 of 4

  ID                    URL
  ────────────────────────────────────────────────────────────
  personal              https://radicale.example.com/user/personal.ics
  family                https://radicale.example.com/user/family.ics

  [A]dd   [R]emove selected

  ── Add Calendar ─────────────────
  Calendar ID:  [__________________]
  CalDAV URL:   [__________________]
                            [OK]   [Cancel]

                [Run Tests]   [Skip]   [Back]   [Cancel]
```

`ListView` of calendars. "Add" opens an inline form. At least one calendar is required.

Validation:
- Calendar ID: required, non-empty
- CalDAV URL: required, valid URI

"Run Tests" initializes the DB, saves the password, and runs PROPFIND against each calendar URL.

### 4.6 Final Step (IMAP): Authenticate & Test

```
  Authenticate & Test Connection                      Step 4 of 4

  [ ] Initialize database
  [ ] Save credentials
  [ ] Test IMAP connection
  [ ] Test SMTP connection

                         [Run All]   [Skip]   [Back]   [Cancel]
```

### 4.7 Final Step (Google / O365): Authenticate & Test

```
  Authenticate & Test Connection                      Step 3 of 3

  [ ] Initialize database
  [ ] Authenticate (OAuth)
  [ ] Verify token

                         [Run All]   [Skip]   [Back]   [Cancel]
```

When "Run All" is pressed, each check runs sequentially with status updates:

```
  [OK]   Initialize database
  [OK]   Authenticate (token acquired, expires in 3600s)
  [OK]   Verify token

                          [Retry]   [Done]   [Back]
```

For Google, the authenticate step runs the loopback OAuth flow (see Section 5.1). For O365, it runs the device code flow (see Section 5.2). "Skip" saves the account config without testing. "Done" returns to account list.

Checks per account type:
- **IMAP**: Save password to DB → IMAP connect+auth → SMTP connect+auth
- **Google**: Google OAuth loopback flow → verify token refresh
- **O365**: MSAL device code flow → verify token acquisition
- **CalDAV**: Save password to DB → PROPFIND each calendar URL

---

## 5. OAuth Integration

### 5.1 Google OAuth (GoogleAuthFlow)

Adapted from `GoogleOAuthHelper.AuthorizeAsync` in `src/PIM.Sync.Google/GoogleOAuthHelper.cs`.

**Scopes:**
```csharp
"https://www.googleapis.com/auth/gmail.modify",
"https://www.googleapis.com/auth/calendar.events",
"https://www.googleapis.com/auth/calendar.readonly"
```

**Flow:**
1. Create `GoogleAuthorizationCodeFlow` with client ID, client secret, scopes
2. Check for existing valid token via `IAuthRepository.GetOAuthTokenAsync`
3. If token exists and has a refresh token, return success (token is already stored)
4. Allocate random loopback port via `TcpListener(IPAddress.Loopback, 0)`
5. Start `HttpListener` on `http://127.0.0.1:{port}/`
6. Build authorization URL via `flow.CreateAuthorizationCodeRequest(redirectUri).Build()`
7. Open browser: `Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })`
8. Update TUI: "Opening browser... If it doesn't open, visit: {url}"
9. Wait for callback asynchronously (with cancellation support)
10. Extract `code` from query string
11. Exchange code via `flow.ExchangeCodeForTokenAsync(accountId, code, redirectUri, ct)`
12. Persist token: `IAuthRepository.SaveOAuthTokenAsync(new OAuthToken(...))`
13. Return HTML response to browser: "Authorization successful! You can close this window."

**TUI coordination:** The `HttpListener` runs on `Task.Run` to avoid blocking the Terminal.Gui event loop. Status updates via `Application.Invoke()`. A "Cancel" button cancels the `CancellationTokenSource`, which stops the listener.

### 5.2 Office 365 Device Code (GraphAuthFlow)

Adapted from `GraphAuthProvider` in `src/PIM.Sync.Graph/GraphAuthProvider.cs`.

**Scopes:**
```csharp
"Mail.ReadWrite", "Mail.Send", "Calendars.ReadWrite"
```

**Flow:**
1. Build MSAL app: `PublicClientApplicationBuilder.Create(clientId).WithAuthority($"https://login.microsoftonline.com/{tenantId}").Build()`
2. Wire token cache serialization to `IAuthRepository` (same base64 MSAL cache blob pattern as `GraphAuthProvider.EnsureMsalApp`)
3. Try silent acquisition from existing cache (via `IAuthRepository.GetOAuthTokenAsync`)
4. If no cached token: call `AcquireTokenWithDeviceCode` with callback
5. The callback receives verification URL and user code — display in TUI:

```
  To sign in, visit:  https://microsoft.com/devicelogin
  Enter this code:    ABCD-EFGH

  Waiting for authentication...           [Cancel]
```

6. MSAL handles polling. On success, the `SetAfterAccessAsync` hook persists the cache blob
7. Update TUI with success

**Token storage:** MSAL cache blob is stored as base64 in `oauth_tokens.access_token`. The `refresh_token` field is set to `"msal-managed"`. Expiry is set to `DateTimeOffset.UtcNow.AddDays(90)`.

---

## 6. Connection Testing

`ConnectionTestView` runs tests for a single account and displays results. "Test All" from the main menu runs tests for every account sequentially in the same view.

All tests use a 15-second timeout via `CancellationTokenSource.CreateLinkedTokenSource`.

### 6.1 IMAP

```csharp
using var client = new ImapClient();
await client.ConnectAsync(host, port, useTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
await client.AuthenticateAsync(username, password, ct);
// Report: server banner, CONDSTORE capability, mailbox count
await client.DisconnectAsync(true, ct);
```

### 6.2 SMTP

```csharp
using var client = new SmtpClient();
await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);
await client.AuthenticateAsync(username, password, ct);
// Report: server greeting
await client.DisconnectAsync(true, ct);
```

### 6.3 Google Token Verification

```csharp
var token = await authRepo.GetOAuthTokenAsync(accountId, ct);
// If no token: "No token — run authentication first"
// Build GoogleAuthorizationCodeFlow, load token, attempt refresh
// If refresh succeeds: "Token valid, expires at {time}"
// If refresh fails: "Token expired/revoked — re-authentication needed"
```

### 6.4 O365 Token Verification

```csharp
var stored = await authRepo.GetOAuthTokenAsync(accountId, ct);
// If no token: "No token — run authentication first"
// Build MSAL app, deserialize cache, try AcquireTokenSilent
// If succeeds: "Token valid, expires at {time}"
// If fails: "Token expired — re-authentication needed"
```

### 6.5 CalDAV

Tests each calendar URL in the CalDAV account. Uses the account's own username and password (stored via `IAuthRepository.GetCalDavPasswordAsync`).

```csharp
var password = await authRepo.GetCalDavPasswordAsync(accountId, ct);
using var client = new HttpClient();
var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

// Test each calendar URL
foreach (var cal in account.Calendars)
{
    var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), cal.Url);
    request.Headers.Add("Depth", "0");
    request.Content = new StringContent(
        "<?xml version=\"1.0\"?><propfind xmlns=\"DAV:\"><prop><getctag xmlns=\"http://calendarserver.org/ns/\"/></prop></propfind>",
        Encoding.UTF8, "application/xml");

    var response = await client.SendAsync(request, ct);
    // 207 Multi-Status: "{cal.Id}: Connected, ctag={value}"
    // 401: "{cal.Id}: Authentication failed"
    // Other: "{cal.Id}: HTTP {statusCode}: {reasonPhrase}"
}
```

---

## 7. Config Serialization

### 7.1 Loading

Delegate to `ConfigLoader.Load(path)` from PIM.Core for deserialization. If the file doesn't exist, return a default config:

```csharp
public static PimConfig CreateDefault() => new(
    Accounts: [],
    Ui: new UiConfig("UTC", null),
    System: new SystemConfig(null, "open-meteo"),
    Storage: new StorageConfig("~/.pim/pim.db", "~/Downloads/pim-attachments", 6, 6),
    Server: new ServerConfig("127.0.0.1", 9400, 9401)
);
```

### 7.2 Serialization

PIM.Setup defines its own public DTO classes that mirror the private DTOs in `ConfigLoader.cs` (lines 179–237). This avoids modifying PIM.Core for a setup-only concern.

```csharp
public static void Save(PimConfig config, string path)
{
    var dto = MapToDto(config);
    var serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

    File.WriteAllText(path, serializer.Serialize(dto));
}
```

DTO property ordering must match `config.example.yaml`: accounts → ui → system → storage → server.

`AccountType` and `CalendarType` enums serialize as lowercase strings (`"imap"`, `"google"`, `"office365"`, `"caldav"`). Use a `IYamlTypeConverter` or serialize via the DTO using string fields.

**Known limitation:** YAML comments are not preserved on round-trip. Editing an existing config that had comments will lose them.

### 7.3 Validation

Before saving, validate the in-memory config using the updated rules (extending `ConfigLoader.Validate`):

- At least one account
- Account IDs non-empty and unique
- Display name required per account
- IMAP: host, port (1–65535), smtp host, smtp port (1–65535), username all required
- Google: client_id, client_secret required
- O365: tenant_id, client_id required
- CalDAV: username required, at least one calendar with non-empty id and url
- Storage db_path non-empty
- Server ports 1–65535

Display all validation errors in the status bar. Do not write the file until validation passes.

---

## 8. Database Initialization

Reuse `DbConnectionFactory` and `MigrationRunner` from PIM.Core.

```csharp
// 1. Resolve ~ in db_path
var dbPath = config.Storage.DbPath.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

// 2. Ensure directory exists
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

// 3. Create factory and run migrations
var factory = new DbConnectionFactory(dbPath);
var runner = new MigrationRunner(factory, logger);
await runner.RunAsync(FindSqlDirectory(), ct);
```

`FindSqlDirectory()` walks up from `AppContext.BaseDirectory` looking for the `sql/` directory (same pattern as `PIM.Server/Program.cs` lines 107–118).

DB initialization runs:
- During the account wizard's final auth+test step (before any credential storage)
- Automatically before any operation that touches `IAuthRepository`
- On "Save & Exit" if the DB doesn't exist yet

---

## 9. Settings Forms

Each settings section is a single-screen form with `TextField` inputs and a `[Save]` / `[Cancel]` footer. "Save" writes to the in-memory config and returns to the main menu. "Cancel" discards changes and returns.

### 9.1 UI Settings

| Field | Widget | Default | Validation |
|---|---|---|---|
| Primary Timezone | TextField | `"UTC"` | Must be valid `TimeZoneInfo.FindSystemTimeZoneById` |
| Secondary Timezone | TextField | (empty) | If non-empty, must be valid timezone ID |

### 9.2 System Settings

| Field | Widget | Default | Validation |
|---|---|---|---|
| Weather Location | TextField | (empty) | If non-empty, must match `lat,lon` with valid numeric ranges |
| Weather Provider | Label (read-only) | `"open-meteo"` | Only supported provider |

### 9.3 Storage Settings

| Field | Widget | Default | Validation |
|---|---|---|---|
| Database Path | TextField | `~/.pim/pim.db` | Non-empty |
| Attachment Download Dir | TextField | `~/Downloads/pim-attachments` | Non-empty |
| Buffer Months Back | TextField | `6` | Positive integer |
| Buffer Months Forward | TextField | `6` | Positive integer |

If DB path changes, show warning: "Changing the database path will not migrate existing data."

### 9.4 Server Settings

| Field | Widget | Default | Validation |
|---|---|---|---|
| Listen Address | TextField | `127.0.0.1` | Valid IP address |
| REST Port | TextField | `9400` | Integer 1–65535 |
| WebSocket Port | TextField | `9401` | Integer 1–65535, must differ from REST port |

---

## 10. Edge Cases

### Editing accounts with existing tokens
When editing a Google account's Client ID/Secret or an O365 account's Tenant ID/Client ID, display a warning: "Changing these credentials will invalidate existing tokens. You will need to re-authenticate after saving."

### Re-authentication
Connection test detects expired/revoked tokens and offers: "Token expired. Re-authenticate now? [Yes] [No]". "Yes" runs the OAuth flow. The new token overwrites the old via `INSERT OR REPLACE` in `oauth_tokens`.

### Removing the last account
Warn: "At least one account is required. The config will not pass validation until you add an account." Allow removal but block "Save & Exit" until at least one account exists.

### No existing config (first run)
Start with `CreateDefault()`. Main menu shows "Config: ~/.pim/config.yaml [new]". Guide user to add at least one account before saving.

### Config parse errors
If `ConfigLoader.Load` throws `ConfigValidationException`, display errors and offer: "Start fresh with defaults? [Yes] [No]". "No" exits the application (user must fix YAML manually).

### Server currently running
On startup, attempt `HTTP GET http://{listenAddress}:{restPort}/api/system/clock` (lightweight endpoint). If it responds, show in status: "PIM Server is running. Restart it after saving for changes to take effect."

---

## 11. Error Handling

All errors are caught, displayed in the status bar, and never crash the application. Pattern matches `TuiApp.ShowError` (src/PIM.Tui/TuiApp.cs line 96).

### Validation errors
Displayed in the status bar when "Next" or "Save" is pressed. The form stays on the current step until errors are resolved.

### OAuth failures
| Scenario | Message |
|---|---|
| Timeout waiting for Google callback | "Authorization timed out. Try again." |
| User denied Google access | "Authorization denied by user." |
| O365 device code expired | "Device code expired. Try again." |
| Network error during token exchange | "{exception message}" with "Retry" option |

### Connection test failures
| Scenario | Message |
|---|---|
| DNS resolution failure | "Could not resolve hostname '{host}'" |
| Connection refused | "Connection refused to {host}:{port}" |
| TLS handshake failure | "TLS handshake failed: {detail}" |
| Authentication failure | "Authentication failed: invalid username or password" |
| Timeout (15s) | "Connection timed out after 15 seconds" |
| CalDAV 401 | "CalDAV authentication failed (HTTP 401)" |
| CalDAV 404 | "Calendar URL not found (HTTP 404)" |

### File system errors
| Scenario | Message |
|---|---|
| Permission denied writing config | "Cannot write to {path}: permission denied" |
| Cannot create ~/.pim directory | "Cannot create directory {path}: {reason}" |

---

## 12. Testing Strategy

### Unit tests (PIM.Setup.Tests)

- **ConfigSerializer round-trip**: serialize → deserialize → compare for all account types, optional fields, empty calendars
- **ConfigSerializer defaults**: `CreateDefault()` produces valid structure
- **ConfigSerializer null handling**: `OmitNull` omits optional fields, doesn't omit required fields
- **DTO mapping**: all `PimConfig` fields survive the config → DTO → YAML → DTO → config round-trip
- **Account ID validation**: regex accepts `a-z0-9-`, rejects spaces/uppercase/special chars
- **Port validation**: accepts 1–65535, rejects 0, negative, >65535, non-numeric
- **Timezone validation**: accepts "America/New_York", rejects "Not/A/Timezone"

### Manual testing

- OAuth flows (require real credentials, cannot be automated in CI)
- Terminal.Gui view layout and navigation
- Full end-to-end: fresh setup → add Google account → OAuth → test → save → run server

---

## 13. Acceptance Criteria

- [ ] `dotnet build PIM.slnx` compiles PIM.Setup with zero errors and zero warnings (except known Terminal.Gui CS0618)
- [ ] `dotnet test PIM.slnx` passes all PIM.Setup.Tests
- [ ] First-run: creates `~/.pim/config.yaml` and `~/.pim/pim.db` from scratch
- [ ] Existing config loads, edits round-trip, saves without data loss (except YAML comments)
- [ ] IMAP wizard: collects all fields, stores password in DB, tests IMAP + SMTP connection
- [ ] Google wizard: collects Client ID/Secret, completes loopback OAuth, token in DB
- [ ] O365 wizard: collects Tenant/Client ID, completes device code flow, token in DB
- [ ] CalDAV wizard: collects username, password, calendar URLs, tests PROPFIND per calendar
- [ ] All settings forms (UI, System, Storage, Server) load, edit, save correctly
- [ ] Account removal cleans up DB credentials
- [ ] Connection tests show clear pass/fail with specific error messages
- [ ] Config validation matches `ConfigLoader.Validate` rules exactly
- [ ] DB migrations run correctly on fresh database
- [ ] Unsaved changes prompt on exit
- [ ] All errors caught and displayed, never crash
- [ ] PIM.Core config model updated: CalDAV is a first-class account type
- [ ] PIM.Server ProviderRegistry handles CalDAV accounts independently

---

## 14. Prerequisite Changes (PIM.Core and PIM.Server)

CalDAV becoming a first-class account type requires changes outside PIM.Setup. These must be implemented before or alongside PIM.Setup.

### 14.1 PIM.Core Config Model

**`PimConfig.cs`:**

Add `CalDav` to the `AccountType` enum:
```csharp
public enum AccountType { Imap, Google, Office365, CalDav }
```

CalDAV accounts use the existing `AccountConfig` record. The relevant fields are:
- `Id`, `Type` (`CalDav`), `DisplayName` — common fields
- `Username` — reuses the existing nullable field (currently IMAP-only)
- `Calendars` — list of `CalendarSourceConfig` with `Type = CalDav` and `Url` set

No new fields on `AccountConfig` are needed. CalDAV accounts simply don't populate IMAP/SMTP/OAuth fields.

**`ConfigLoader.cs`:**

Add parsing for `"caldav"` in `ParseAccountType`:
```csharp
"caldav" => AccountType.CalDav,
```

Add validation case in `Validate`:
```csharp
case AccountType.CalDav:
    if (string.IsNullOrWhiteSpace(account.Username))
        errors.Add($"Account '{account.Id}': 'username' is required for CalDAV accounts.");
    if (account.Calendars is null || account.Calendars.Count == 0)
        errors.Add($"Account '{account.Id}': at least one calendar is required for CalDAV accounts.");
    break;
```

### 14.2 IAuthRepository

Add CalDAV password storage (separate from IMAP passwords, since they're independent accounts):

```csharp
Task SaveCalDavPasswordAsync(string accountId, string password, CancellationToken ct = default);
Task<string?> GetCalDavPasswordAsync(string accountId, CancellationToken ct = default);
```

Implementation in `SqliteAuthRepository` uses a new `caldav_credentials` table, or reuses `imap_credentials` with a type discriminator. The simplest approach: rename `imap_credentials` to `credentials` with an `account_id` primary key (it already works for any account type), and add a migration. Alternatively, just reuse `imap_credentials` as-is — the table name is an implementation detail, and the column is just `account_id` + `password`.

**Recommended approach:** Reuse `imap_credentials` table as-is. Both IMAP and CalDAV store `(account_id, password)`. Add `SaveCalDavPasswordAsync` / `GetCalDavPasswordAsync` as aliases to the same table operations. The account ID uniqueness (IMAP vs CalDAV accounts have different IDs) prevents collisions.

### 14.3 SQL Migration

If renaming the table (optional, for clarity):
```sql
-- sql/003_rename_credentials.sql
ALTER TABLE imap_credentials RENAME TO credentials;
```

If reusing `imap_credentials` as-is, no migration is needed.

### 14.4 PIM.Server ProviderRegistry

Add a `BuildCalDavProvidersAsync` method alongside the existing `BuildImapProvidersAsync`, `BuildGoogleProvidersAsync`, `BuildGraphProvidersAsync`:

```csharp
private async Task BuildCalDavProvidersAsync(
    AccountConfig account,
    IAuthRepository authRepo,
    ISyncStateRepository syncStateRepo,
    ILoggerFactory loggerFactory,
    IHttpClientFactory httpClientFactory,
    CancellationToken ct)
{
    var password = await authRepo.GetCalDavPasswordAsync(account.Id, ct);
    if (password is null)
    {
        _logger.LogWarning("No password for CalDAV account {AccountId}, skipping", account.Id);
        return;
    }

    var calendars = new List<ICalendarProvider>();
    foreach (var calConfig in account.Calendars ?? [])
    {
        var httpClient = httpClientFactory.CreateClient($"caldav-{calConfig.Id}");
        calendars.Add(new CalDavCalendarProvider(
            account.Id, calConfig.Id, calConfig.Url!,
            account.Username!, authRepo, syncStateRepo,
            httpClient, loggerFactory.CreateLogger<CalDavCalendarProvider>()));
    }

    if (calendars.Count > 0)
        _calendarProviders[account.Id] = calendars;

    // CalDAV accounts have no mail provider — _mailProviders is not populated
}
```

Wire it into the account type switch in `BuildProvidersForAccountAsync`:
```csharp
AccountType.CalDav => BuildCalDavProvidersAsync(account, ...),
```

### 14.5 CalDavCalendarProvider

Update `CalDavCalendarProvider` to accept credentials from either source. Currently it calls `_authRepo.GetImapPasswordAsync(AccountId, ct)`. Change to `_authRepo.GetCalDavPasswordAsync(AccountId, ct)` when the parent account is CalDAV type, or keep a constructor parameter for the password source.

**Simplest approach:** If `imap_credentials` is reused as-is, `GetImapPasswordAsync` and `GetCalDavPasswordAsync` are the same operation. No change to `CalDavCalendarProvider` is needed — it already works.

### 14.6 config.example.yaml

Add a CalDAV account example:

```yaml
  - id: "my-radicale"
    type: caldav
    display_name: "Radicale"
    username: "user@example.com"
    # password stored in DB after setup
    calendars:
      - id: "personal"
        type: caldav
        url: "https://radicale.example.com/user/personal.ics"
      - id: "family"
        type: caldav
        url: "https://radicale.example.com/user/family.ics"
```

Remove the CalDAV calendar from the IMAP account example (it's now a separate account).

### 14.7 Existing Tests

Update tests that reference `AccountType` enum or config validation to include the new `CalDav` type. Add test cases for CalDAV account validation in `ConfigLoaderTests`.
