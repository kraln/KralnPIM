using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PIM.Sync.EventKit;

public sealed class EventKitException : Exception
{
    public string Code { get; }

    public EventKitException(string code, string message) : base($"eventkit-cli error [{code}]: {message}")
    {
        Code = code;
    }
}

internal sealed class EventKitClient
{
    private const int SupportedSchemaVersion = 1;
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:sszzz";

    private readonly string _binaryPath;
    private readonly ILogger<EventKitClient> _logger;

    public EventKitClient(string binaryPath, ILogger<EventKitClient> logger)
    {
        _binaryPath = binaryPath;
        _logger = logger;
    }

    public async Task<List<EventKitCalendarDto>> ListCalendarsAsync(CancellationToken ct)
    {
        var stdout = await RunAsync(["list-calendars"], ct);
        var response = JsonSerializer.Deserialize(stdout, EventKitJsonContext.Default.EventKitCalendarListResponse)
            ?? throw new EventKitException("unknown", "Empty response from list-calendars.");
        EnsureSchema(response.SchemaVersion);
        if (response.Error is not null)
            throw new EventKitException(response.Error.Code, response.Error.Message);
        return response.Data?.Calendars ?? [];
    }

    public async Task<List<EventKitEventDto>> FetchEventsAsync(
        DateTimeOffset start, DateTimeOffset end, IEnumerable<string>? calendarIds, CancellationToken ct)
    {
        var args = new List<string>
        {
            "fetch-events",
            "--start", start.ToString(IsoFormat, CultureInfo.InvariantCulture),
            "--end", end.ToString(IsoFormat, CultureInfo.InvariantCulture),
        };
        if (calendarIds is not null)
        {
            foreach (var id in calendarIds)
            {
                args.Add("--calendar");
                args.Add(id);
            }
        }

        var stdout = await RunAsync(args, ct);
        var response = JsonSerializer.Deserialize(stdout, EventKitJsonContext.Default.EventKitEventListResponse)
            ?? throw new EventKitException("unknown", "Empty response from fetch-events.");
        EnsureSchema(response.SchemaVersion);
        if (response.Error is not null)
            throw new EventKitException(response.Error.Code, response.Error.Message);
        return response.Data?.Events ?? [];
    }

    private static void EnsureSchema(int version)
    {
        if (version != SupportedSchemaVersion)
            throw new EventKitException(
                "unknown",
                $"Unsupported eventkit-cli schemaVersion {version} (expected {SupportedSchemaVersion}).");
    }

    private async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var stdoutBuf = new StringBuilder();
        var stderrBuf = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdoutBuf.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderrBuf.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new EventKitException("unknown", $"Failed to launch '{_binaryPath}': {ex.Message}");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);

        var stdout = stdoutBuf.ToString();
        var stderr = stderrBuf.ToString();
        _logger.LogDebug("eventkit-cli {Args} exited {Code}", string.Join(" ", args), proc.ExitCode);

        // Non-zero exit: prefer the JSON error envelope on stdout, fall back to stderr.
        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
        {
            throw new EventKitException(
                "unknown",
                $"eventkit-cli exited {proc.ExitCode} with no stdout. stderr: {stderr.Trim()}");
        }

        return stdout;
    }
}
