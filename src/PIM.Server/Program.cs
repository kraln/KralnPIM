using System.Net;
using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Serialization;
using PIM.Server.Models;
using PIM.Server.Api;
using PIM.Server.Registration;
using PIM.Server.Services;
using PIM.Server.WebSocket;

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pim", "config.yaml");

// 1. Load config
var config = ConfigLoader.Load(configPath);

// 2. Build host
var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Parse(config.Server.ListenAddress), config.Server.RestPort);
    kestrel.Listen(IPAddress.Parse(config.Server.ListenAddress), config.Server.WsPort);
});

// 3. Register services
builder.Services.AddSingleton(config);
builder.Services.AddPimCore(config);
builder.Services.AddPimProviders();
builder.Services.AddPimSearch();
builder.Services.AddPimSync();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, PimJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, ServerJsonContext.Default);
});

var app = builder.Build();

// 4. Run DB migrations
var sqlDir = FindSqlDirectory();
var migrationRunner = app.Services.GetRequiredService<MigrationRunner>();
await migrationRunner.RunAsync(sqlDir);

// 5. Initialize and authenticate providers
var registry = app.Services.GetRequiredService<ProviderRegistry>();
await registry.InitializeAsync(
    config,
    app.Services.GetRequiredService<IAuthRepository>(),
    app.Services.GetRequiredService<ISyncStateRepository>(),
    app.Services.GetRequiredService<ILoggerFactory>(),
    app.Services.GetRequiredService<IHttpClientFactory>(),
    CancellationToken.None);

await registry.AuthenticateAllAsync(
    app.Services.GetRequiredService<AccountStatusTracker>(),
    CancellationToken.None);

// 6. WebSocket middleware on WS port
app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort == config.Server.WsPort)
    {
        if (context.WebSockets.IsWebSocketRequest && context.Request.Path == "/ws")
        {
            var ws = await context.WebSockets.AcceptWebSocketAsync();
            var broadcaster = context.RequestServices.GetRequiredService<WebSocketBroadcaster>();
            await broadcaster.HandleConnectionAsync(ws, context.RequestAborted);
            return;
        }

        context.Response.StatusCode = 400;
        return;
    }

    await next();
});

// 7. Global error handler (inline — UseExceptionHandler needs Diagnostics.Abstractions which is
//    missing in .NET 10 preview builds)
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException)
    {
        // Client disconnected — don't log noise
        if (!context.Response.HasStarted)
            context.Response.StatusCode = 499; // nginx-style "client closed request"
    }
    catch (Exception ex)
    {
        var log = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("PIM.Server");
        log.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse("An unexpected error occurred."),
                ServerJsonContext.Default.ErrorResponse);
        }
    }
});

// 8. Map API endpoints
app.MapGet("/api/health", () => Results.Ok("ok"));
app.MapMailEndpoints();
app.MapCalendarEndpoints();
app.MapSearchEndpoints();
app.MapSystemEndpoints();

// 9. Start
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PIM.Server");
logger.LogInformation("PIM daemon ready — REST on :{RestPort}, WS on :{WsPort}",
    config.Server.RestPort, config.Server.WsPort);

await app.RunAsync();

static string FindSqlDirectory()
{
    var dir = AppContext.BaseDirectory;
    while (dir != null)
    {
        var sqlDir = Path.Combine(dir, "sql");
        if (Directory.Exists(sqlDir))
            return sqlDir;
        dir = Directory.GetParent(dir)?.FullName;
    }
    throw new DirectoryNotFoundException("Could not find sql/ directory.");
}
