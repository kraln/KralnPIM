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

Application.Init();

try
{
    var app = new SetupApp(configPath, loggerFactory);
    Application.Run(app);
}
finally
{
    Application.Shutdown();
}
