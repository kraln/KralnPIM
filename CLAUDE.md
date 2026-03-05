# CLAUDE.md

## Build & Test

```sh
dotnet build PIM.slnx
dotnet test PIM.slnx
```

All code must compile with zero errors and zero warnings (except the known IL3050 AOT warning in PIM.Core/Config/ConfigLoader.cs from YamlDotNet reflection).

## Project Conventions

- **.NET 10**, solution file is `PIM.slnx` (not `.sln`)
- One project per module under `src/`, mirrored test project under `tests/`
- Target framework: `net10.0` for all projects
- Tests use **xUnit** + **NSubstitute** for mocks
- All async methods accept `CancellationToken` as the last parameter
- Records for models, sealed where possible
- Nullable reference types enabled everywhere
- Provider interfaces defined in `PIM.Core/Providers/` — implementations in separate `PIM.Sync.*` / `PIM.SystemInfo` projects

## Architecture

- `PIM.Core` — shared kernel: models, interfaces, config, repositories, serialization
- `PIM.Sync.*` — provider implementations (Google, IMAP, O365, CalDAV), each references PIM.Core
- `PIM.SystemInfo` — system providers: power (Linux `/sys/`), weather (Open-Meteo), clock (TimeZoneInfo)
- `PIM.Search` — unified search: local FTS5 via `IEmailRepository.SearchAsync`, deep search fans out to all `IMailProvider.RemoteSearchAsync` in parallel with MessageId dedup
- `PIM.Server` — ASP.NET Core Minimal API daemon: REST (port 9400) + WebSocket (port 9401) on a single Kestrel host with dual listeners
- `PIM.Tui` — Terminal.Gui v2 TUI client: pure REST/WebSocket client referencing only PIM.Core (not PIM.Server)
- Providers implement `IMailProvider`, `ICalendarProvider`, `IPowerInfoProvider`, `IWeatherProvider`, or `IClockProvider`
- `ISearchService` defined in `PIM.Core/Providers/`, implemented in `PIM.Search`
- Sync uses delta tokens where the API supports them (`ISyncStateRepository`)
- CalDAV sync uses ctag/etag diffing — no WebDAV library, hand-rolled HTTP with `HttpClient`
- OAuth tokens persisted via `IAuthRepository`; CalDAV uses `IAuthRepository.GetCalDavPasswordAsync` for Basic Auth

## Key Patterns

- Google API namespace collides with `PIM.Sync.Google` — use `global::Google.GoogleApiException`
- Graph SDK `AttachmentInfo` collides with `PIM.Core.Models.AttachmentInfo` — use `PimAttachmentInfo` alias
- Ical.Net `EventStatus` collides with `PIM.Core.Models.EventStatus` — use `PimEventStatus` alias
- Graph SDK uses `Microsoft.Graph.Me.SendMail` namespace (not `Users.Item.SendMail`)
- Graph SDK `DaysOfWeek` is `List<DayOfWeekObject?>` (nullable elements); `RecurrenceRange.EndDate` is `Microsoft.Kiota.Abstractions.Date`
- No custom rate limiter for Graph — SDK's built-in `RetryHandler` handles 429 throttling
- `InternalsVisibleTo` used for test access to internal members
- `HtmlToTextConverter` copied into PIM.Sync.Imap (avoids Google API transitive deps; 89-line trivial duplicate)
- IMAP sync token format: JSON `{"uidValidity":N,"maxUid":N,"modseq":N}` — CONDSTORE delta when supported, UID fallback otherwise
- IMAP uses MailKit `IMessageSummary.Envelope` for header mapping; `MimeMessage` for full body/attachment access
- MimeKit normalizes `InReplyTo` by stripping angle brackets from message IDs
- Gmail delta sync: `DeltaSyncAsync` catches both 404 (NotFound) and 410 (Gone) from history API — falls back to `FullSyncAsync` with 30-day window. `FullSyncAsync` saves the current history ID from `users.getProfile` (not stale per-message history IDs)
- Calendar providers skip sync entirely when `_allowedCalendarIds` is null or empty — prevents syncing unwanted calendars (e.g., auto-discovered holiday calendars)
- All-day events: all three mappers (Google, Graph, CalDAV) store dates with `TimeZoneInfo.Local.GetUtcOffset()` so midnight stays midnight in the local timezone. TUI `TimeGridView` renders all-day events in a dedicated banner row between the forecast header and time slots
- `GoogleAuthFlow.AuthorizeAsync` takes an `Action<string>? onAuthUrl` callback — callers get the auth URL for explicit browser/clipboard control instead of auto-launch. `TryOpenBrowser` and `TryCopyToClipboard` are `internal static`
- Account wizard type selection: `ListView.Accepting` + `KeyDown` for number keys `1`–`4` advance immediately; `NoTypeAheadMatcher` prevents letter-key consumption
- `TokenBucketRateLimiter` wraps Google API calls to respect quota limits
- JSON serialization uses source-generated `PimJsonContext` for AOT compatibility
- `PIM.Server` has its own `ServerJsonContext` for server-specific types (API models, WS events)
- `ProviderRegistry` builds provider instances per account from config — methods are `virtual` (not sealed) for NSubstitute mocking in tests
- `SyncScheduler` is a `BackgroundService` with `PeriodicTimer` (5 min default); `AccountSyncWorker` handles per-account sync with 3-retry exponential backoff
- `AccountStatusTracker` tracks online/offline per account in-memory; REST API returns 503 for writes to offline accounts
- `WebSocketBroadcaster` pushes `mail.sync`, `calendar.sync`, `status.change` events to connected clients
- Server dual-port routing: middleware checks `context.Connection.LocalPort` to distinguish REST vs WS traffic
- `PIM.Server.csproj` requires `AllowMissingPrunePackageData` for .NET 10 preview compatibility
- `PIM.Tui` has its own `TuiJsonContext` for TUI-local types, chained with `PimJsonContext` via `TypeInfoResolverChain`
- `PIM.Tui` JSON options must set `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` — the server sends camelCase, and bare `JsonSerializerOptions()` defaults to PascalCase, causing silent deserialization failures (all properties get default values)
- `PIM.Tui` duplicates server API model records (MailDetail, AccountOverview, etc.) to avoid referencing PIM.Server/ASP.NET Core
- Terminal.Gui v2 (`2.0.0-develop.5043`): use `Key.*` (not `KeyCode`), `Initialized` (not `Ready`), `SetSource(ObservableCollection<T>)`, `DateField.Value`/`TimeField.Value`, `CheckBox.Value` is `CheckState` enum (not bool), `ListView.ValueChanged` with `e.NewValue`, `ListView.SelectedItem` returns `int?` (not `int`), `Button.Accepting` (not `Button.Accept`), `ListView.Accepting` for item activation, `KeyDown` handler: `e == Key.X` (not `e.AsKey == Key.X`), number keys via `new Key('1')`, `ListWrapper<T>` takes `ObservableCollection<T>`, `RadioGroup` does not exist (use `ListView` instead), `MessageBox.Query` requires `IApplication` first param
- Terminal.Gui v2 lifecycle: `using IApplication app = Application.Create(); app.Init(); app.Run(window);` — not `Application.Init()/Run()/Shutdown()` (static API is obsolete)
- Terminal.Gui v2 `CanFocus` defaults to `false` on `View` — all View subclasses must set `CanFocus = true` in their constructor for keyboard navigation to work
- Terminal.Gui v2 `TabView` does not give default sizing to `Tab.View` — View subclasses used as tab content must set `X = 0; Y = 0; Width = Dim.Fill(); Height = Dim.Fill();` or they render at 0x0 (invisible)
- Terminal.Gui v2 instance API: use `View.App?.Invoke()`, `View.App?.RequestStop()`, `View.App?.AddTimeout()`, `View.App?.RemoveTimeout()` — `App` (`IApplication?`) is set when the view joins a running hierarchy, so calls in constructors must be deferred to `Initialized` handlers
- Terminal.Gui v2 ListView type-ahead search consumes letter keys in `OnKeyDown()` before `KeyDown` event fires — disable with `list.KeystrokeNavigator.Matcher = new NoTypeAheadMatcher()` (implements `ICollectionNavigatorMatcher`, returns `false` for `IsCompatibleKey`); `NoTypeAheadMatcher` defined in `PIM.Setup.Views` and `PIM.Tui` namespaces
- Terminal.Gui v2 async UI updates: after `await`, continuations run on thread pool — must marshal back with `App?.Invoke(() => { ... })` before modifying UI state (ObservableCollections, Labels, etc.)
- `ComboBox` is deprecated in Terminal.Gui v2 but still functional — produces warnings only
- Terminal.Gui v2 emoji/wide char rendering: `AddStr` uses `GetColumns()` (wcwidth port) for cursor advancement, but many emoji have wrong width — VS16 (`\ufe0f`) suffix is never counted, and some supplementary plane chars (U+1F324–1F328, U+1F32B–1F32C, U+1F321) report cols=1 but render as 2. Only use emoji verified in `sandbox/emoji_test/`. Safe emoji (str.Length matches terminal display width): 🌅 `\U0001f305`, 🌇 `\U0001f307`, 🌞 `\U0001f31e`, ⛅ `\u26c5`, ☁ `\u2601` (no VS16!), ⛈ `\u26c8`, ❄ `\u2744` (no VS16!), 💧 `\U0001f4a7`, 🧊 `\U0001f9ca`, 🌈 `\U0001f308`, ⚡ `\u26a1`, 🌙 `\U0001f319`, ⚑ `\u2691`. Never use `\ufe0f` suffix.
