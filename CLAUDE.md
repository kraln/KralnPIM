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
- Provider interfaces defined in `PIM.Core/Providers/` — implementations in separate `PIM.Sync.*` projects

## Architecture

- `PIM.Core` — shared kernel: models, interfaces, config, repositories, serialization
- `PIM.Sync.*` — provider implementations (Google, IMAP, O365, CalDAV), each references PIM.Core
- Providers implement `IMailProvider` and/or `ICalendarProvider`
- Sync uses delta tokens where the API supports them (`ISyncStateRepository`)
- OAuth tokens persisted via `IAuthRepository`

## Key Patterns

- Google API namespace collides with `PIM.Sync.Google` — use `global::Google.GoogleApiException`
- Graph SDK `AttachmentInfo` collides with `PIM.Core.Models.AttachmentInfo` — use `PimAttachmentInfo` alias
- Graph SDK uses `Microsoft.Graph.Me.SendMail` namespace (not `Users.Item.SendMail`)
- Graph SDK `DaysOfWeek` is `List<DayOfWeekObject?>` (nullable elements); `RecurrenceRange.EndDate` is `Microsoft.Kiota.Abstractions.Date`
- No custom rate limiter for Graph — SDK's built-in `RetryHandler` handles 429 throttling
- `InternalsVisibleTo` used for test access to internal members
- `HtmlToTextConverter` in PIM.Sync.Google is reusable by other sync modules
- `TokenBucketRateLimiter` wraps Google API calls to respect quota limits
- JSON serialization uses source-generated `PimJsonContext` for AOT compatibility
