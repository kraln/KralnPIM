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
- OAuth tokens persisted via `IAuthRepository`; CalDAV uses `IAuthRepository.GetImapPasswordAsync` for Basic Auth

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
- `PIM.Tui` duplicates server API model records (MailDetail, AccountOverview, etc.) to avoid referencing PIM.Server/ASP.NET Core
- Terminal.Gui v2 (`2.0.0-develop.5027`): use `Key.*` (not `KeyCode`), `Initialized` (not `Ready`), `SetSource(ObservableCollection<T>)`, `DateField.Value`/`TimeField.Value`/`CheckBox.Value`, `ListView.ValueChanged` with `e.NewValue`
- `ComboBox` is deprecated in Terminal.Gui v2 but still functional; `Application.Invoke()` is marked obsolete — both produce warnings only
