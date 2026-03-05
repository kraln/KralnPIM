# KralnPIM

A unified Personal Information Manager that aggregates email and calendar data from multiple providers (IMAP, Gmail, Office 365, CalDAV) into a local SQLite database, exposed via REST API and WebSocket, with a Terminal.Gui TUI client.

## Architecture

```
PIM.slnx
├── src/
│   ├── PIM.Core/              # Shared models, interfaces, config, DB schema
│   ├── PIM.Sync.Google/       # Gmail + Google Calendar provider
│   ├── PIM.Sync.Imap/         # IMAP/SMTP provider
│   ├── PIM.Sync.Graph/        # Office 365 Mail + Calendar provider
│   ├── PIM.Sync.CalDav/       # CalDAV calendar provider
│   ├── PIM.Search/            # Local FTS + remote deep search
│   ├── PIM.Server/            # REST API + WebSocket daemon
│   ├── PIM.SystemInfo/        # Power, weather, clock providers
│   ├── PIM.Tui/               # Terminal.Gui TUI client
│   └── PIM.Setup/             # Interactive configuration TUI
├── tests/                     # Mirrors src/ with xUnit tests
├── sql/                       # SQLite schema migrations
├── config.example.yaml        # Sample configuration
└── docs/design_spec.md        # Full implementation specification
```

## Requirements

- .NET 10 SDK
- ASP.NET Core 10 runtime (for PIM.Server)

## Build & Test

```sh
dotnet build PIM.slnx
dotnet test PIM.slnx
```

## Running the Daemon

```sh
dotnet run --project src/PIM.Server -- ~/.pim/config.yaml
```

REST API on port 9400, WebSocket on port 9401 (configurable in `config.yaml`).

## Running the TUI

```sh
dotnet run --project src/PIM.Tui
```

Optional flags: `--rest-url http://host:port` and `--ws-url ws://host:port/ws` (defaults to localhost 9400/9401).

## Running Setup

```sh
dotnet run --project src/PIM.Setup
```

Interactive TUI for configuring accounts, settings, and running connection tests. Creates `~/.pim/config.yaml` and initializes the database. Optional flag: `--config-path /path/to/config.yaml`.

## Configuration

Copy `config.example.yaml` to `~/.pim/config.yaml` and fill in your account details, or use `PIM.Setup` for guided configuration. See the file for supported account types (IMAP, Google, Office 365, CalDAV) and options.

## Features

- **Multi-provider aggregation**: IMAP/SMTP, Gmail (OAuth2), Office 365 (MSAL), CalDAV — with delta sync and automatic fallback on expired tokens/history IDs
- **Unified search**: Local FTS5 + parallel remote deep search with dedup
- **Real-time sync**: WebSocket push for mail, calendar, and account status changes
- **Dashboard**: Upcoming agenda, weather, power, world clocks, mail overview
- **Calendar**: 4-day timeline with weather forecasts, now line, sunrise/sunset markers, all-day event banner row, event creation/editing, left/right day navigation
- **Email**: Inbox with read/unread/flagged filters, search, compose/reply, attachments
- **Setup wizard**: Guided account configuration with OAuth flows, connection testing, calendar discovery, SSL error bypass
- **System info**: Battery/power (Linux `/sys/`), weather (Open-Meteo), multi-timezone clocks

## Status

| Phase | Module | Status |
|---|---|---|
| 1 | PIM.Core | Done |
| 2 | PIM.Sync.Google | Done |
| 2 | PIM.Sync.Imap | Done |
| 2 | PIM.Sync.Graph | Done |
| 2 | PIM.Sync.CalDav | Done |
| 2 | PIM.SystemInfo | Done |
| 3 | PIM.Search | Done |
| 3 | PIM.Server | Done |
| 4 | PIM.Tui | Done |
| 5 | PIM.Setup | Done |
| 6 | Integration testing | In progress |
