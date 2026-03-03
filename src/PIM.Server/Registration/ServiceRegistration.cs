using PIM.Core.Config;
using PIM.Core.Data;
using PIM.Core.Providers;
using PIM.Search;
using PIM.Server.Services;
using PIM.Server.Sync;
using PIM.Server.WebSocket;
using PIM.SystemInfo;

namespace PIM.Server.Registration;

internal static class ServiceRegistration
{
    internal static IServiceCollection AddPimCore(this IServiceCollection services, PimConfig config)
    {
        var dbFactory = new DbConnectionFactory(config.Storage.DbPath);
        services.AddSingleton(dbFactory);
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<IAuthRepository>(new SqliteAuthRepository(dbFactory));
        services.AddSingleton<ISyncStateRepository>(new SqliteSyncStateRepository(dbFactory));
        services.AddSingleton<IEmailRepository>(new SqliteEmailRepository(dbFactory));
        services.AddSingleton<ICalendarRepository>(new SqliteCalendarRepository(dbFactory));
        return services;
    }

    internal static IServiceCollection AddPimProviders(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<IPowerInfoProvider>(sp =>
            PowerInfoProviderFactory.Create(sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IWeatherProvider>(sp =>
            new OpenMeteoWeatherProvider(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("weather"),
                sp.GetRequiredService<ILogger<OpenMeteoWeatherProvider>>()));
        services.AddSingleton<IClockProvider>(sp =>
            new ClockProvider(sp.GetRequiredService<ILogger<ClockProvider>>()));
        return services;
    }

    internal static IServiceCollection AddPimSearch(this IServiceCollection services)
    {
        services.AddSingleton<ISearchService>(sp =>
        {
            var emailRepo = sp.GetRequiredService<IEmailRepository>();
            var calRepo = sp.GetRequiredService<ICalendarRepository>();
            var registry = sp.GetRequiredService<ProviderRegistry>();
            var logger = sp.GetRequiredService<ILogger<SearchService>>();
            return new SearchService(emailRepo, calRepo, registry.AllMailProviders, logger);
        });
        return services;
    }

    internal static IServiceCollection AddPimSync(this IServiceCollection services)
    {
        services.AddSingleton<AccountStatusTracker>();
        services.AddSingleton<WebSocketBroadcaster>();
        services.AddHostedService<SyncScheduler>();
        return services;
    }
}
