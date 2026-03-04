using Microsoft.Extensions.Logging;
using PIM.Setup;
using Terminal.Gui.App;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".pim", "config.yaml");

for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--config-path")
        configPath = args[++i];
}

using var loggerFactory = LoggerFactory.Create(b =>
    b.AddConsole().SetMinimumLevel(LogLevel.Warning));

using IApplication guiApp = Application.Create();
guiApp.Init();

using var app = new SetupApp(configPath, loggerFactory);

// Terminal.Gui can throw on mouse events (e.g. right-click clipboard access on Linux).
// Return true to swallow the exception and keep running.
guiApp.Run(app, errorHandler: _ => true);
