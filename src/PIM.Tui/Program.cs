using Microsoft.Extensions.Logging;
using PIM.Tui;
using PIM.Tui.Client;
using Terminal.Gui.App;

var restUrl = "http://127.0.0.1:9400";
var wsUrl = "ws://127.0.0.1:9401/ws";

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
    }
}

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using var apiClient = new PimApiClient(restUrl);
var wsClient = new PimWsClient(new Uri(wsUrl), loggerFactory.CreateLogger<PimWsClient>());

using IApplication guiApp = Application.Create();
guiApp.Init();

using var tuiApp = new TuiApp(apiClient, wsClient);
guiApp.Run(tuiApp, errorHandler: _ => true);
await wsClient.DisposeAsync();
