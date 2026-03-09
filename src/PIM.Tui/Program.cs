using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PIM.Tui;
using PIM.Tui.Client;
using Terminal.Gui.App;

var restUrl = "http://127.0.0.1:9400";
var wsUrl = "ws://127.0.0.1:9401/ws";
string? tihUrl = null;
var tihPort = 8080;
var noTih = false;

for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--rest-url":
            restUrl = args[++i];
            break;
        case "--ws-url":
            wsUrl = args[++i];
            break;
        case "--tih-url":
            tihUrl = args[++i];
            break;
        case "--tih-port":
            tihPort = int.Parse(args[++i]);
            break;
    }
}

// Check last arg (no value) for --no-tih
if (args.Length > 0 && args[^1] == "--no-tih")
    noTih = true;

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// If no explicit --tih-url and tih isn't disabled, try to auto-launch
Process? tihProcess = null;
if (tihUrl is null && !noTih)
{
    tihUrl = $"http://127.0.0.1:{tihPort}";

    if (!IsPortListening(tihPort))
    {
        var tihPath = FindOnPath("tih");
        if (tihPath is not null)
        {
            tihProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = tihPath,
                    ArgumentList = { "-modern", "-no-browser", "-port", tihPort.ToString() },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            try
            {
                tihProcess.Start();
                // Give the server a moment to bind
                Thread.Sleep(500);
            }
            catch
            {
                tihProcess.Dispose();
                tihProcess = null;
                tihUrl = null;
            }
        }
        else
        {
            tihUrl = null;
        }
    }
}

try
{
    using var apiClient = new PimApiClient(restUrl);
    var wsClient = new PimWsClient(new Uri(wsUrl), loggerFactory.CreateLogger<PimWsClient>());
    using var tihClient = tihUrl is not null ? new TihApiClient(tihUrl) : null;

    using IApplication guiApp = Application.Create();
    guiApp.Init();

    using var tuiApp = new TuiApp(apiClient, wsClient, tihClient);
    guiApp.Run(tuiApp, errorHandler: _ => true);
    await wsClient.DisposeAsync();
}
finally
{
    if (tihProcess is not null)
    {
        try
        {
            tihProcess.Kill(entireProcessTree: true);
            tihProcess.WaitForExit(3000);
        }
        catch
        {
            // Best effort
        }
        tihProcess.Dispose();
    }
}

static bool IsPortListening(int port)
{
    try
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect("127.0.0.1", port);
        return true;
    }
    catch
    {
        return false;
    }
}

static string? FindOnPath(string executable)
{
    var pathVar = Environment.GetEnvironmentVariable("PATH");
    if (pathVar is null) return null;
    foreach (var dir in pathVar.Split(':'))
    {
        var candidate = Path.Combine(dir, executable);
        if (File.Exists(candidate)) return candidate;
    }
    return null;
}
