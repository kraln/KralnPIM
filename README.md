# KralnPIM

A unified Personal Information Manager that aggregates email and calendar data from multiple providers (IMAP, Gmail, Office 365, CalDAV) into a local SQLite database, exposed via REST API and WebSocket, with a Terminal.Gui TUI client.

## Architecture

```
PIM.slnx
├── src/
│   ├── PIM.Core/              # Shared models, interfaces, config, DB schema
│   ├── PIM.Sync.Google/       # Gmail + Google Calendar provider
│   ├── PIM.Sync.Imap/         # IMAP/SMTP provider (planned)
│   ├── PIM.Sync.Graph/        # Office 365 Mail + Calendar (planned)
│   ├── PIM.Sync.CalDav/       # CalDAV provider (planned)
│   ├── PIM.Search/            # Local + remote search (planned)
│   ├── PIM.Server/            # REST + WebSocket daemon (planned)
│   ├── PIM.SystemInfo/        # Weather, system metrics (planned)
│   └── PIM.Tui/               # Terminal.Gui client (planned)
├── tests/                     # Mirrors src/ with xUnit tests
├── sql/                       # SQLite schema migrations
├── config.example.yaml        # Sample configuration
└── docs/design_spec.md        # Full implementation specification
```

## Requirements

- .NET 10 SDK

## Build & Test

```sh
dotnet build PIM.slnx
dotnet test PIM.slnx
```

## Configuration

Copy `config.example.yaml` to `~/.pim/config.yaml` and fill in your account details. See the file for supported account types (IMAP, Google, Office 365) and options.

## Status

| Phase | Module | Status |
|---|---|---|
| 1 | PIM.Core | Done |
| 2 | PIM.Sync.Google | Done |
| 2 | PIM.Sync.Imap | Planned |
| 2 | PIM.Sync.Graph | Planned |
| 2 | PIM.Sync.CalDav | Planned |
| 2 | PIM.SystemInfo | Planned |
| 3 | PIM.Search | Planned |
| 3 | PIM.Server | Planned |
| 4 | PIM.Tui | Planned |
